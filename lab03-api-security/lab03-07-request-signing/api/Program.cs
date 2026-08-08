using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Static client registry: client id -> signing secret.
// Real systems store these the way lab03-04 stores API key hashes — in a
// database, hashed or in a secrets manager, with rotation. A dictionary keeps
// this lab focused on the signing scheme itself.
var clients = new Dictionary<string, string>
{
    ["mobile-app"] = "demo-signing-secret-1",
    ["partner-web"] = "demo-signing-secret-2",
};

var signingDebug = Environment.GetEnvironmentVariable("SIGNING_DEBUG") == "true";

const long AllowedSkewSeconds = 300;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var ordersLock = new object();
var orders = new List<Order>
{
    new(1, "Keyboard", 1290, "seed"),
};
var nextId = 2;

// ---------------------------------------------------------------------------
// Signature verification middleware
//
// String to sign:  {METHOD}\n{PATH}\n{X-Timestamp}\n{raw body}
// (empty string body for GET, so the string ends with the trailing \n)
//
// Headers:
//   X-Client-Id  — which secret to verify with
//   X-Timestamp  — unix seconds; rejected when |now - ts| > 300 s
//   X-Signature  — lowercase hex HMAC-SHA256 of the string-to-sign
// ---------------------------------------------------------------------------
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/health")
    {
        await next(context);
        return;
    }

    var clientId = context.Request.Headers["X-Client-Id"].ToString();
    var timestampHeader = context.Request.Headers["X-Timestamp"].ToString();
    var signatureHeader = context.Request.Headers["X-Signature"].ToString();

    if (clientId == "" || timestampHeader == "" || signatureHeader == "")
    {
        await Reject(context, "missing signature headers");
        return;
    }

    if (!clients.TryGetValue(clientId, out var secret))
    {
        await Reject(context, "unknown client");
        return;
    }

    if (!long.TryParse(timestampHeader, out var timestamp)
        || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > AllowedSkewSeconds)
    {
        await Reject(context, "timestamp outside allowed window");
        return;
    }

    // Read the raw body exactly as it arrived on the wire — the signature
    // covers the bytes, not the parsed JSON. EnableBuffering lets the
    // endpoint read the body again afterwards.
    context.Request.EnableBuffering();
    string rawBody;
    using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
    {
        rawBody = await reader.ReadToEndAsync();
    }
    context.Request.Body.Position = 0;

    var stringToSign = $"{context.Request.Method}\n{context.Request.Path}\n{timestampHeader}\n{rawBody}";

    // Debug aid ONLY for this lab (compose sets SIGNING_DEBUG=true): echo the
    // string the server reconstructed so learners can diff it against the
    // client's. Never ship this — it hands an attacker the exact input to
    // brute-force offline. Newlines are not allowed in header values, so they
    // are escaped as \n.
    if (signingDebug)
    {
        context.Response.Headers["X-Debug-String-To-Sign"] = stringToSign.Replace("\n", "\\n");
    }

    var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(stringToSign));

    byte[] provided;
    try
    {
        provided = Convert.FromHexString(signatureHeader);
    }
    catch (FormatException)
    {
        await Reject(context, "invalid signature");
        return;
    }

    // Fixed-time comparison on the raw bytes: a naive == compare leaks how
    // many leading bytes matched through response timing.
    if (!CryptographicOperations.FixedTimeEquals(provided, expected))
    {
        await Reject(context, "invalid signature");
        return;
    }

    context.Items["ClientId"] = clientId;
    app.Logger.LogInformation("Verified signature: {Method} {Path} client={ClientId}",
        context.Request.Method, context.Request.Path, clientId);

    await next(context);
});

app.MapGet("/orders", (HttpContext context) =>
{
    lock (ordersLock)
    {
        return Results.Json(orders.ToList());
    }
});

app.MapPost("/orders", async (HttpContext context) =>
{
    CreateOrderInput? input = null;
    try
    {
        input = await context.Request.ReadFromJsonAsync<CreateOrderInput>();
    }
    catch (JsonException)
    {
        // fall through to validation below
    }

    if (string.IsNullOrEmpty(input?.Item))
        return Results.Json(new { error = "item is required" }, statusCode: StatusCodes.Status400BadRequest);

    var clientId = (string)context.Items["ClientId"]!;
    Order order;
    lock (ordersLock)
    {
        order = new Order(nextId++, input.Item, input.Amount, clientId);
        orders.Add(order);
    }
    return Results.Json(order, statusCode: StatusCodes.Status201Created);
});

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.Logger.LogInformation("Request Signing API starting");
app.Run(Environment.GetEnvironmentVariable("APP_URL") ?? "http://0.0.0.0:8080");

static Task Reject(HttpContext context, string error)
{
    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    return context.Response.WriteAsJsonAsync(new { error });
}

record Order(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("item")] string Item,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("created_by")] string CreatedBy);

record CreateOrderInput(
    [property: JsonPropertyName("item")] string? Item,
    [property: JsonPropertyName("amount")] decimal Amount);
