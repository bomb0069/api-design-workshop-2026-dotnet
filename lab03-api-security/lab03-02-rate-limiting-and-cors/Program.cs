// Mirrors main.go: CORS + per-IP token bucket rate limiting in front of a
// small products API backed by PostgreSQL.
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Npgsql;

const int MaxRequests = 10;                       // bucket capacity
var window = TimeSpan.FromMinutes(1);             // 10 requests per minute
var refillPeriod = window / MaxRequests;          // one token every 6 seconds

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=workshop;Username=postgres;Password=postgres";

var builder = WebApplication.CreateBuilder(args);

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);

// CORS middleware (same options as the Go chi/cors configuration)
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:3000", "http://localhost:8081")
    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
    .WithHeaders("Accept", "Authorization", "Content-Type")
    .WithExposedHeaders("X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset")
    .AllowCredentials()
    .SetPreflightMaxAge(TimeSpan.FromSeconds(300))));

// Per-IP token bucket built on System.Threading.RateLimiting.
// TokenLimit = 10, refilled 1 token every 6 seconds = 10 requests/minute,
// matching the Go TokenBucket (10 tokens, continuous refill at 10/minute).
var rateLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    RateLimitPartition.GetTokenBucketLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = MaxRequests,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = refillPeriod,
            QueueLimit = 0,
            AutoReplenishment = true
        }));

// GLOBAL limiter: ONE bucket shared by every caller, no matter who they are.
// This models total server capacity — "this box handles ~600 req/min" —
// expressed per second: 10 tokens refilled every 1 second = 10 req/s.
// Contrast with the per-IP limiter above, which is about fairness between
// clients; this one is about protecting the machine itself.
var globalCapacityLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
{
    TokenLimit = 10,                                  // burst: 10 at once
    TokensPerPeriod = 10,                             // refill 10 tokens...
    ReplenishmentPeriod = TimeSpan.FromSeconds(1),    // ...every second = 600/min
    QueueLimit = 0,
    AutoReplenishment = true
});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = rateLimiter;
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = ((int)refillPeriod.TotalSeconds).ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ErrorResponse("Rate limit exceeded. Try again later."), cancellationToken);
    };
});

var app = builder.Build();

Db.CreateTable(dataSource);

app.UseCors();

// Attach the X-RateLimit-* headers to every response (mirrors the Go
// middleware, which sets them on both allowed and rejected requests).
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        // /reports/* uses the global policy and writes its own headers.
        if (context.Response.Headers.ContainsKey("X-RateLimit-Policy"))
            return Task.CompletedTask;
        var stats = rateLimiter.GetStatistics(context);
        var remaining = (int)(stats?.CurrentAvailablePermits ?? 0);
        // Allowed requests: bucket takes a full window to refill from empty.
        // Rejected requests: the next token arrives after one refill period.
        var reset = context.Response.StatusCode == StatusCodes.Status429TooManyRequests
            ? DateTimeOffset.UtcNow.Add(refillPeriod)
            : DateTimeOffset.UtcNow.Add(window);
        context.Response.Headers["X-RateLimit-Limit"] = MaxRequests.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = reset.ToUnixTimeSeconds().ToString();
        return Task.CompletedTask;
    });
    await next();
});

app.UseRateLimiter();

app.MapGet("/products", ProductHandlers.List);
app.MapPost("/products", ProductHandlers.Create);
app.MapGet("/products/{id}", ProductHandlers.Get);

// An "expensive" aggregation endpoint guarded by the GLOBAL limiter: all
// callers together may make 10 req/s — the 11th request in the same second
// gets 429 even if every request comes from a different IP.
// .DisableRateLimiting() opts this endpoint out of the per-IP GlobalLimiter
// so the two policies don't stack.
app.MapGet("/reports/summary", async (HttpContext context, NpgsqlDataSource db) =>
{
    using var lease = globalCapacityLimiter.AttemptAcquire(1);
    context.Response.Headers["X-RateLimit-Policy"] = "global";
    context.Response.Headers["X-RateLimit-Limit"] = "10";
    context.Response.Headers["X-RateLimit-Remaining"] =
        ((int)(globalCapacityLimiter.GetStatistics()?.CurrentAvailablePermits ?? 0)).ToString();
    if (!lease.IsAcquired)
    {
        context.Response.Headers.RetryAfter = "1";
        return Results.Json(new ErrorResponse("Server is at capacity. Try again later."), statusCode: 429);
    }
    return await ProductHandlers.Summary(db);
}).DisableRateLimiting();

Console.WriteLine("Server starting on :8080");
app.Run("http://0.0.0.0:8080");

public record Product
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("price")] public double Price { get; init; }
    [JsonPropertyName("category")] public string Category { get; init; } = "";
}

public record ProductInput(string? Name, double Price, string? Category);

public record ErrorResponse([property: JsonPropertyName("error")] string Error);

public static class Db
{
    public static void CreateTable(NpgsqlDataSource db)
    {
        using (var cmd = db.CreateCommand(@"CREATE TABLE IF NOT EXISTS products (
            id SERIAL PRIMARY KEY,
            name TEXT NOT NULL,
            price DECIMAL(10,2) NOT NULL,
            category TEXT NOT NULL
        )"))
        {
            cmd.ExecuteNonQuery();
        }

        long count;
        using (var cmd = db.CreateCommand("SELECT COUNT(*) FROM products"))
        {
            count = (long)(cmd.ExecuteScalar() ?? 0L);
        }
        if (count == 0)
        {
            using var cmd = db.CreateCommand(
                "INSERT INTO products (name, price, category) VALUES " +
                "('Laptop', 999.99, 'electronics'), ('Go Book', 39.99, 'books'), ('T-Shirt', 19.99, 'clothing')");
            cmd.ExecuteNonQuery();
        }
    }
}

public static class ProductHandlers
{
    public static async Task<IResult> List(NpgsqlDataSource db)
    {
        var products = new List<Product>();
        await using var cmd = db.CreateCommand("SELECT id, name, price, category FROM products ORDER BY id");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            products.Add(new Product
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Price = reader.GetDouble(2),
                Category = reader.GetString(3)
            });
        }
        return Results.Json(products);
    }

    public static async Task<IResult> Create(HttpRequest request, NpgsqlDataSource db)
    {
        ProductInput? input;
        try
        {
            input = await request.ReadFromJsonAsync<ProductInput>();
        }
        catch (JsonException)
        {
            input = null;
        }
        if (input is null)
            return Results.Json(new ErrorResponse("Invalid request body"), statusCode: 400);

        await using var cmd = db.CreateCommand(
            "INSERT INTO products (name, price, category) VALUES ($1, $2, $3) RETURNING id, name, price, category");
        cmd.Parameters.AddWithValue(input.Name ?? "");
        cmd.Parameters.AddWithValue(input.Price);
        cmd.Parameters.AddWithValue(input.Category ?? "");

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var product = new Product
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Price = reader.GetDouble(2),
            Category = reader.GetString(3)
        };
        return Results.Json(product, statusCode: 201);
    }

    public static async Task<IResult> Summary(NpgsqlDataSource db)
    {
        var rows = new List<object>();
        await using var cmd = db.CreateCommand(
            "SELECT category, COUNT(*), SUM(price) FROM products GROUP BY category ORDER BY category");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new
            {
                category = reader.GetString(0),
                count = reader.GetInt64(1),
                total_value = reader.GetDouble(2)
            });
        }
        return Results.Json(rows);
    }

    public static async Task<IResult> Get(string id, NpgsqlDataSource db)
    {
        int.TryParse(id, out var productId);

        await using var cmd = db.CreateCommand("SELECT id, name, price, category FROM products WHERE id = $1");
        cmd.Parameters.AddWithValue(productId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.Json(new ErrorResponse("Product not found"), statusCode: 404);

        var product = new Product
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Price = reader.GetDouble(2),
            Category = reader.GetString(3)
        };
        return Results.Json(product);
    }
}
