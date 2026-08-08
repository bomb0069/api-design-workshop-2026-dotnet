using System.Diagnostics;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");

var dataSource = NpgsqlDataSource.Create(Db.BuildConnectionString());
builder.Services.AddSingleton(dataSource);

var app = builder.Build();
Handlers.Logger = app.Logger;

// Ping the database and create the table on startup (fail fast, like the Go version).
await using (var conn = await dataSource.OpenConnectionAsync())
{
    await using var cmd = new NpgsqlCommand("""
        CREATE TABLE IF NOT EXISTS products (
            id SERIAL PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            price DECIMAL(10,2) NOT NULL,
            category TEXT NOT NULL
        )
        """, conn);
    await cmd.ExecuteNonQueryAsync();
}

// Middleware chain (same order as the Go version):
// Request -> Logger -> Recoverer -> JsonContentType -> Handler -> Response

// Logger: logs method, path, status code, and duration for every request.
app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    await next(context);
    stopwatch.Stop();
    app.Logger.LogInformation("{Method} {Path} {StatusCode} {ElapsedMs}ms",
        context.Request.Method, context.Request.Path, context.Response.StatusCode,
        stopwatch.Elapsed.TotalMilliseconds);
});

// Recoverer: catches unhandled exceptions and returns a 500 instead of crashing the server.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled exception");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = ApiError.NewInternalError() });
        }
    }
});

// JsonContentType: defaults Content-Type to application/json on all responses
// so individual handlers do not need to set it.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (string.IsNullOrEmpty(context.Response.ContentType)
            && context.Response.StatusCode != StatusCodes.Status204NoContent)
        {
            context.Response.ContentType = "application/json";
        }
        return Task.CompletedTask;
    });
    await next(context);
});

app.MapGet("/products", Handlers.ListProducts);
app.MapPost("/products", Handlers.CreateProduct);
app.MapGet("/products/{id}", Handlers.GetProduct);
app.MapPut("/products/{id}", Handlers.UpdateProduct);
app.MapDelete("/products/{id}", Handlers.DeleteProduct);

app.Logger.LogInformation("Server starting on :8080");
app.Run();

static class Db
{
    // Accepts the same DATABASE_URL URI format the Go lab uses,
    // e.g. postgres://postgres:postgres@db:5432/workshop?sslmode=disable
    public static string BuildConnectionString()
    {
        var url = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrEmpty(url))
        {
            url = "postgres://postgres:postgres@localhost:5432/workshop?sslmode=disable";
        }

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            Database = uri.AbsolutePath.TrimStart('/'),
        };
        if (uri.Query.Contains("sslmode=disable"))
        {
            csb.SslMode = SslMode.Disable;
        }
        return csb.ConnectionString;
    }
}
