// Lab 03-04: API Key Lifecycle Management
//
// How API keys live and die: create -> use -> rotate -> revoke.
// Keys are stored as SHA-256 hashes, carry scopes, are rate limited
// per key, and every authenticated request is written to an audit trail.
using System.Text.Json;
using System.Threading.RateLimiting;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=workshop;Username=postgres;Password=postgres";

// Stand-in for real operator authentication (SSO/mTLS/IAM) on the admin API.
var adminToken = Environment.GetEnvironmentVariable("ADMIN_TOKEN") ?? "admin-secret";

// How long a rotated-out key keeps working. 24h is a realistic default;
// configurable so the grace behavior is easy to demonstrate (and test).
var graceHours = double.TryParse(Environment.GetEnvironmentVariable("KEY_ROTATION_GRACE_HOURS"), out var g)
    ? g : 24.0;

// Per-key token bucket: 10 requests burst, 1 token every 6 seconds =
// 10 requests/minute — the same budget as lab03-02, but partitioned by
// api_key id instead of caller IP. Rotating to a new key means a new bucket;
// that is another reason quotas belong to the key, not the client machine.
const int MaxRequests = 10;
var window = TimeSpan.FromMinutes(1);
var refillPeriod = window / MaxRequests;
var rateLimiter = PartitionedRateLimiter.Create<int, int>(apiKeyId =>
    RateLimitPartition.GetTokenBucketLimiter(apiKeyId, _ => new TokenBucketRateLimiterOptions
    {
        TokenLimit = MaxRequests,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = refillPeriod,
        QueueLimit = 0,
        AutoReplenishment = true
    }));

var builder = WebApplication.CreateBuilder(args);

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);

var app = builder.Build();

Db.CreateTables(dataSource);

// --- Admin auth -------------------------------------------------------------
// Everything under /admin requires the operator token. This is deliberately
// simple: the lab's lesson is the lifecycle of the keys being managed, not
// how operators log in.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/admin"))
    {
        await next();
        return;
    }
    if (context.Request.Headers["X-Admin-Token"].FirstOrDefault() != adminToken)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ErrorResponse("missing or invalid admin token"));
        return;
    }
    await next();
});

// --- API key authentication -------------------------------------------------
// Guards the business endpoints (/products). The full pipeline:
//   parse header -> hash -> lookup -> lifecycle checks -> rate limit ->
//   handler -> audit row.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/products"))
    {
        await next();
        return;
    }

    // Teaching guard: keys in query strings end up in access logs, browser
    // history, and Referer headers — reject them outright.
    if (new[] { "api_key", "apikey", "key", "token" }.Any(context.Request.Query.ContainsKey))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(
            "API keys must not be sent in the query string: query strings are written to server logs, " +
            "proxies, and browser history. Send the key in a header instead: 'Authorization: ApiKey <key>'"));
        return;
    }

    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
    if (authHeader is null || !authHeader.StartsWith("ApiKey ", StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(
            "missing API key. Send 'Authorization: ApiKey <key>'"));
        return;
    }

    // Hash the presented key and look the hash up — the server never needs
    // (and never stores) the raw key.
    var rawKey = authHeader["ApiKey ".Length..].Trim();
    var key = await Db.FindKeyByHash(dataSource, Keys.Sha256Hex(rawKey));

    // Three distinct failures, three distinct messages: an unknown key, a
    // revoked key, and an expired key mean different things to the caller.
    string? reason = key switch
    {
        null => "invalid API key",
        { RevokedAt: not null } => "API key revoked",
        { ExpiresAt: DateTime expires } when expires <= DateTime.UtcNow => "API key expired",
        _ => null
    };
    if (reason is not null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(reason));
        return;
    }

    context.Items["ApiKey"] = key;   // handlers read client + scopes from here

    // Per-key rate limit. The bucket belongs to the key id, so each issued
    // key gets its own 10/min budget.
    using var lease = rateLimiter.AttemptAcquire(key!.Id);
    var remaining = (int)(rateLimiter.GetStatistics(key.Id)?.CurrentAvailablePermits ?? 0);
    var reset = lease.IsAcquired
        ? DateTimeOffset.UtcNow.Add(window)
        : DateTimeOffset.UtcNow.Add(refillPeriod);
    context.Response.Headers["X-RateLimit-Limit"] = MaxRequests.ToString();
    context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
    context.Response.Headers["X-RateLimit-Reset"] = reset.ToUnixTimeSeconds().ToString();

    if (!lease.IsAcquired)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = ((int)refillPeriod.TotalSeconds).ToString();
        await context.Response.WriteAsJsonAsync(new ErrorResponse("Rate limit exceeded. Try again later."));
        // fall through to the audit write below — 429s are usage too
    }
    else
    {
        await next();
    }

    // Audit trail: every AUTHENTICATED request gets a row — success, 403
    // missing scope, 429 rate limited. Failed auth (401) is not attributable
    // to a key, so it cannot be recorded against one.
    await Db.RecordUsage(dataSource, key.Id, context.Request.Method,
        context.Request.Path.ToString(), context.Response.StatusCode);
});

// --- Admin endpoints (the control plane) ------------------------------------
app.MapPost("/admin/keys", AdminHandlers.CreateKey);
app.MapGet("/admin/keys", AdminHandlers.ListKeys);
app.MapPost("/admin/keys/{id:int}/rotate", (int id, NpgsqlDataSource db) =>
    AdminHandlers.RotateKey(id, db, graceHours));
app.MapDelete("/admin/keys/{id:int}", AdminHandlers.RevokeKey);
app.MapGet("/admin/keys/{id:int}/usage", AdminHandlers.Usage);

// --- Business endpoints (the data plane) ------------------------------------
// Tiny in-memory resource: the lesson is the key handling, not the products.
var products = new List<Product>
{
    new(1, "Laptop", 999.99),
    new(2, "Go Book", 39.99),
    new(3, "T-Shirt", 19.99)
};
var productsLock = new object();

app.MapGet("/products", (HttpContext context) =>
{
    if (RequireScope(context, "read:products") is IResult denied) return denied;
    lock (productsLock) return Results.Json(products.ToList());
});

app.MapPost("/products", async (HttpContext context) =>
{
    if (RequireScope(context, "write:products") is IResult denied) return denied;

    ProductInput? input;
    try
    {
        input = await context.Request.ReadFromJsonAsync<ProductInput>();
    }
    catch (JsonException)
    {
        input = null;
    }
    if (input is null || string.IsNullOrWhiteSpace(input.Name))
        return Results.Json(new ErrorResponse("name is required"), statusCode: 400);

    lock (productsLock)
    {
        var product = new Product(products.Count == 0 ? 1 : products.Max(p => p.Id) + 1,
            input.Name.Trim(), input.Price);
        products.Add(product);
        return Results.Json(product, statusCode: 201);
    }
});

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

Console.WriteLine("Server starting on :8080");
app.Run(Environment.GetEnvironmentVariable("APP_URL") ?? "http://0.0.0.0:8080");

// Scope check: the middleware authenticated the key; each endpoint declares
// which scope it needs. 403 (not 401): we know who you are — you just are
// not allowed to do this.
static IResult? RequireScope(HttpContext context, string scope)
{
    var key = (ApiKeyRecord)context.Items["ApiKey"]!;
    return key.Scopes.Contains(scope)
        ? null
        : Results.Json(new ErrorResponse($"missing scope: {scope}"), statusCode: 403);
}
