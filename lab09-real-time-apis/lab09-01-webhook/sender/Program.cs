using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "postgres://postgres:postgres@localhost:5432/workshop?sslmode=disable";
var dataSource = NpgsqlDataSource.Create(Db.BuildConnectionString(databaseUrl));

var app = builder.Build();

await Db.CreateTablesAsync(dataSource);

// Order endpoints

app.MapPost("/orders", async (HttpRequest request) =>
{
    var input = await JsonBody.ReadAsync<CreateOrderInput>(request);
    if (string.IsNullOrEmpty(input?.Item))
        return Results.Json(new { error = "Item is required" }, statusCode: StatusCodes.Status400BadRequest);

    Order order;
    await using (var cmd = dataSource.CreateCommand(
        "INSERT INTO orders (item, amount) VALUES ($1, $2) RETURNING id, item, amount, status"))
    {
        cmd.Parameters.AddWithValue(input.Item);
        cmd.Parameters.AddWithValue(input.Amount);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        order = ReadOrder(reader);
    }

    // Fire-and-forget, like `go sendWebhooks(...)` in the Go version
    _ = WebhookDelivery.SendWebhooksAsync(dataSource, "order.created", order);
    return Results.Json(order, statusCode: StatusCodes.Status201Created);
});

app.MapGet("/orders", async () =>
{
    var orders = new List<Order>();
    await using var cmd = dataSource.CreateCommand("SELECT id, item, amount, status FROM orders ORDER BY id");
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        orders.Add(ReadOrder(reader));
    return Results.Json(orders);
});

app.MapPut("/orders/{id:int}/status", async (int id, HttpRequest request) =>
{
    var input = await JsonBody.ReadAsync<UpdateStatusInput>(request);

    var validStatuses = new HashSet<string> { "pending", "confirmed", "shipped", "delivered", "cancelled" };
    if (input?.Status is null || !validStatuses.Contains(input.Status))
        return Results.Json(new { error = "Invalid status" }, statusCode: StatusCodes.Status400BadRequest);

    await using var cmd = dataSource.CreateCommand(
        "UPDATE orders SET status=$1 WHERE id=$2 RETURNING id, item, amount, status");
    cmd.Parameters.AddWithValue(input.Status);
    cmd.Parameters.AddWithValue(id);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.Json(new { error = "Order not found" }, statusCode: StatusCodes.Status404NotFound);

    var order = ReadOrder(reader);
    _ = WebhookDelivery.SendWebhooksAsync(dataSource, "order." + input.Status, order);
    return Results.Json(order);
});

// Webhook registration

app.MapPost("/webhooks", async (HttpRequest request) =>
{
    var input = await JsonBody.ReadAsync<RegisterWebhookInput>(request);
    if (string.IsNullOrEmpty(input?.Url))
        return Results.Json(new { error = "URL is required" }, statusCode: StatusCodes.Status400BadRequest);

    var events = string.IsNullOrEmpty(input.Events) ? "*" : input.Events;

    await using var cmd = dataSource.CreateCommand(
        "INSERT INTO webhooks (url, events) VALUES ($1, $2) RETURNING id, url, events, active");
    cmd.Parameters.AddWithValue(input.Url);
    cmd.Parameters.AddWithValue(events);
    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    var webhook = new WebhookRegistration(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));
    return Results.Json(webhook, statusCode: StatusCodes.Status201Created);
});

app.MapGet("/webhooks", async () =>
{
    var webhooks = new List<WebhookRegistration>();
    await using var cmd = dataSource.CreateCommand("SELECT id, url, events, active FROM webhooks ORDER BY id");
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        webhooks.Add(new WebhookRegistration(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
    return Results.Json(webhooks);
});

app.MapDelete("/webhooks/{id:int}", async (int id) =>
{
    try
    {
        await using var cmd = dataSource.CreateCommand("DELETE FROM webhooks WHERE id = $1");
        cmd.Parameters.AddWithValue(id);
        await cmd.ExecuteNonQueryAsync();
    }
    catch (PostgresException)
    {
        // The Go version ignores db.Exec errors here (e.g. the FK constraint
        // from webhook_logs) and returns 204 regardless — mirror that.
    }
    return Results.NoContent();
});

app.Logger.LogInformation("Sender (Order Service) starting on :8080");
app.Run("http://0.0.0.0:8080");

static Order ReadOrder(NpgsqlDataReader reader) =>
    new(reader.GetInt32(0), reader.GetString(1), reader.GetDecimal(2), reader.GetString(3));

record Order(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("item")] string Item,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("status")] string Status);

record WebhookRegistration(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("events")] string Events,
    [property: JsonPropertyName("active")] bool Active);

record WebhookPayload(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("data")] object Data);

record CreateOrderInput(
    [property: JsonPropertyName("item")] string? Item,
    [property: JsonPropertyName("amount")] decimal Amount);

record UpdateStatusInput([property: JsonPropertyName("status")] string? Status);

record RegisterWebhookInput(
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("events")] string? Events);

static class JsonBody
{
    // Mirrors Go's json.NewDecoder(r.Body).Decode(&input) which ignores
    // decode errors: a bad/empty body simply yields an empty input struct.
    public static async Task<T?> ReadAsync<T>(HttpRequest request)
    {
        try
        {
            return await request.ReadFromJsonAsync<T>();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

static class WebhookDelivery
{
    private const string WebhookSecret = "webhook-secret-key";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task SendWebhooksAsync(NpgsqlDataSource db, string evt, object data)
    {
        var targets = new List<(int Id, string Url)>();
        try
        {
            await using var cmd = db.CreateCommand("SELECT id, url FROM webhooks WHERE active = TRUE");
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                targets.Add((reader.GetInt32(0), reader.GetString(1)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load webhooks: {ex.Message}");
            return;
        }

        foreach (var (id, url) in targets)
        {
            // RFC 3339 timestamp (InvariantCulture keeps the Gregorian calendar
            // regardless of system locale)
            var payload = new WebhookPayload(evt,
                DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), data);
            _ = DeliverWebhookAsync(db, id, url, evt, payload);
        }
    }

    private static async Task DeliverWebhookAsync(NpgsqlDataSource db, int webhookId, string url, string evt, WebhookPayload payload)
    {
        var body = JsonSerializer.Serialize(payload);
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        // Create HMAC signature (HMAC-SHA256, lowercase hex — same as the Go sender)
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), bodyBytes)).ToLowerInvariant();

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            var statusCode = 0;
            var respBody = "";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new ByteArrayContent(bodyBytes)
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Headers.Add("X-Webhook-Event", evt);
                request.Headers.Add("X-Webhook-Signature", signature);

                using var response = await Http.SendAsync(request);
                statusCode = (int)response.StatusCode;
            }
            catch (Exception ex)
            {
                respBody = ex.Message;
            }

            try
            {
                await using var cmd = db.CreateCommand(
                    "INSERT INTO webhook_logs (webhook_id, event, payload, status_code, response, attempt) VALUES ($1, $2, $3, $4, $5, $6)");
                cmd.Parameters.AddWithValue(webhookId);
                cmd.Parameters.AddWithValue(evt);
                cmd.Parameters.AddWithValue(body);
                cmd.Parameters.AddWithValue(statusCode);
                cmd.Parameters.AddWithValue(respBody);
                cmd.Parameters.AddWithValue(attempt);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to log webhook delivery: {ex.Message}");
            }

            if (statusCode >= 200 && statusCode < 300)
            {
                Console.WriteLine($"Webhook delivered: {evt} -> {url} (attempt {attempt})");
                return;
            }

            Console.WriteLine($"Webhook failed: {evt} -> {url} (attempt {attempt}, status: {statusCode})");
            if (attempt < maxRetries)
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
        }
    }
}

static class Db
{
    // Converts a postgres:// URL (as used by the Go edition's DATABASE_URL)
    // into an Npgsql connection string.
    public static string BuildConnectionString(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
        };
        if (userInfo.Length > 1)
            csb.Password = Uri.UnescapeDataString(userInfo[1]);
        if (uri.Query.Contains("sslmode=disable"))
            csb.SslMode = SslMode.Disable;
        return csb.ConnectionString;
    }

    public static async Task CreateTablesAsync(NpgsqlDataSource db)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS orders (
                id SERIAL PRIMARY KEY,
                item TEXT NOT NULL,
                amount DECIMAL(10,2) NOT NULL,
                status TEXT DEFAULT 'pending'
            );
            CREATE TABLE IF NOT EXISTS webhooks (
                id SERIAL PRIMARY KEY,
                url TEXT NOT NULL,
                events TEXT NOT NULL DEFAULT '*',
                active BOOLEAN DEFAULT TRUE
            );
            CREATE TABLE IF NOT EXISTS webhook_logs (
                id SERIAL PRIMARY KEY,
                webhook_id INT REFERENCES webhooks(id),
                event TEXT NOT NULL,
                payload TEXT NOT NULL,
                status_code INT,
                response TEXT,
                attempt INT DEFAULT 1,
                sent_at TIMESTAMP DEFAULT NOW()
            );
            """;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var cmd = db.CreateCommand(ddl);
                await cmd.ExecuteNonQueryAsync();
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                Console.WriteLine($"Waiting for database ({ex.Message})...");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
