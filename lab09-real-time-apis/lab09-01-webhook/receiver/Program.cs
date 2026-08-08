using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const string WebhookSecret = "webhook-secret-key";

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var eventsLock = new object();
var events = new List<ReceivedEvent>();

app.MapPost("/webhook", async (HttpRequest request) =>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    var body = ms.ToArray();

    // Verify signature (HMAC-SHA256 over the raw body, lowercase hex)
    var signature = request.Headers["X-Webhook-Signature"].ToString();
    var expectedSig = Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), body)).ToLowerInvariant();
    var valid = CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expectedSig));

    var eventName = request.Headers["X-Webhook-Event"].ToString();

    var timestamp = "";
    JsonElement? data = null;
    try
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String)
            timestamp = ts.GetString() ?? "";
        if (doc.RootElement.TryGetProperty("data", out var d))
            data = d.Clone();
    }
    catch (JsonException)
    {
        // Malformed payload: keep defaults, same as Go ignoring json.Unmarshal errors
    }

    var received = new ReceivedEvent(eventName, timestamp, data, signature, valid);

    lock (eventsLock)
    {
        events.Add(received);
    }

    if (valid)
    {
        app.Logger.LogInformation("Received valid webhook: {Event}", eventName);
        return Results.Text("""{"status":"accepted"}""");
    }

    app.Logger.LogWarning("Received webhook with INVALID signature: {Event}", eventName);
    return Results.Text("""{"status":"invalid signature"}""", statusCode: StatusCodes.Status401Unauthorized);
});

app.MapGet("/events", () =>
{
    lock (eventsLock)
    {
        return Results.Json(events.ToList());
    }
});

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.Logger.LogInformation("Receiver starting on :9090");
app.Run("http://0.0.0.0:9090");

record ReceivedEvent(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("data")] JsonElement? Data,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("valid")] bool Valid);
