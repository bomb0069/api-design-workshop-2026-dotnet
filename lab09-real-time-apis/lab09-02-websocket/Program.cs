using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var hub = new Hub();
_ = hub.RunAsync();

app.UseWebSockets();

app.Map("/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var username = context.Request.Query["username"].ToString();
    if (string.IsNullOrEmpty(username))
        username = "Anonymous";

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var client = new Client(socket, username);

    hub.Register(client);

    var writeTask = WritePumpAsync(client);
    await ReadPumpAsync(hub, client);
    await writeTask;
});

app.MapGet("/stats", () =>
{
    var (onlineUsers, usernames, messageCount) = hub.GetStats();
    return Results.Json(new Dictionary<string, object>
    {
        ["online_users"] = onlineUsers,
        ["usernames"] = usernames,
        ["message_count"] = messageCount,
    });
});

// Serve the chat UI from ./static (like Go's http.FileServer(http.Dir("static")))
var staticFiles = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "static"));
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = staticFiles });
app.UseStaticFiles(new StaticFileOptions { FileProvider = staticFiles });

app.Logger.LogInformation("Server starting on :8080");
app.Logger.LogInformation("Open http://localhost:8080 in your browser");
app.Run("http://0.0.0.0:8080");

// Reads incoming messages from the WebSocket connection and forwards them to
// the Hub's broadcast channel — the equivalent of the Go client's readPump.
static async Task ReadPumpAsync(Hub hub, Client client)
{
    var buffer = new byte[4096];
    try
    {
        while (client.Socket.State == WebSocketState.Open)
        {
            using var messageStream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await client.Socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                messageStream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            string? content = null;
            try
            {
                using var doc = JsonDocument.Parse(messageStream.ToArray());
                if (doc.RootElement.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    content = c.GetString();
            }
            catch (JsonException)
            {
                // Ignore malformed messages, same as the Go version
            }

            if (string.IsNullOrEmpty(content))
                continue;

            hub.Broadcast(new Message("message", client.Username, content, Hub.Now()));
        }
    }
    catch (WebSocketException)
    {
        // Connection dropped
    }
    finally
    {
        hub.Unregister(client);
        try
        {
            await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch
        {
            // Socket already closed/aborted
        }
    }
}

// Reads from the client's send channel and writes messages to the WebSocket
// connection — the equivalent of the Go client's writePump.
static async Task WritePumpAsync(Client client)
{
    try
    {
        await foreach (var msg in client.Send.Reader.ReadAllAsync())
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(msg);
            await client.Socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        }
    }
    catch (WebSocketException)
    {
        // Connection dropped
    }
    catch (ObjectDisposedException)
    {
        // Socket closed while writing
    }
}
