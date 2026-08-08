using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");

var dataSource = NpgsqlDataSource.Create(Db.BuildConnectionString());
builder.Services.AddSingleton(dataSource);

var app = builder.Build();

// Ping the database and create the table on startup (fail fast, like the Go version).
await using (var conn = await dataSource.OpenConnectionAsync())
{
    await using var cmd = new NpgsqlCommand("""
        CREATE TABLE IF NOT EXISTS products (
            id SERIAL PRIMARY KEY,
            name TEXT NOT NULL,
            price DECIMAL(10,2) NOT NULL,
            category TEXT NOT NULL
        )
        """, conn);
    await cmd.ExecuteNonQueryAsync();
}

// Logger middleware: logs method, path, status code, and duration.
app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    await next(context);
    stopwatch.Stop();
    app.Logger.LogInformation("{Method} {Path} {StatusCode} {ElapsedMs}ms",
        context.Request.Method, context.Request.Path, context.Response.StatusCode,
        stopwatch.Elapsed.TotalMilliseconds);
});

// Recoverer middleware: catches unhandled exceptions and returns a 500.
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
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                Error = new ErrorDetail { Code = "INTERNAL_ERROR", Message = "Internal server error" },
            });
        }
    }
});

// Serve the hand-crafted OpenAPI 3.0 specification, like the Go version.
app.MapGet("/swagger.json", () =>
    Results.File(Path.Combine(AppContext.BaseDirectory, "swagger.json"), "application/json"));

// Swagger UI (Swashbuckle) rendering the hand-crafted spec, available at /swagger.
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger.json", "Products API");
    options.RoutePrefix = "swagger";
});

app.MapGet("/products", Handlers.ListProducts);
app.MapPost("/products", Handlers.CreateProduct);
app.MapGet("/products/{id}", Handlers.GetProduct);
app.MapPut("/products/{id}", Handlers.UpdateProduct);
app.MapDelete("/products/{id}", Handlers.DeleteProduct);

app.Logger.LogInformation("Server starting on :8080");
app.Run();

// Product represents a product in the store
class Product
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("price")] public double Price { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";

    public static Product From(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        Price = reader.GetDouble(2),
        Category = reader.GetString(3),
    };
}

// CreateProductInput represents the input for creating a product
class CreateProductInput
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("price")] public double Price { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
}

// ErrorResponse represents an error response
class ErrorResponse
{
    [JsonPropertyName("error")] public ErrorDetail Error { get; set; } = new();
}

// ErrorDetail contains error details
class ErrorDetail
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

static class Handlers
{
    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    static IResult WriteError(int status, string code, string message) =>
        Results.Json(new ErrorResponse
        {
            Error = new ErrorDetail { Code = code, Message = message },
        }, statusCode: status);

    static async Task<CreateProductInput?> ReadInput(HttpRequest request)
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<CreateProductInput>(body, JsonOpts) ?? new CreateProductInput();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<IResult> ListProducts(NpgsqlDataSource db)
    {
        try
        {
            var products = new List<Product>();
            await using var conn = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, name, price, category FROM products ORDER BY id", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                products.Add(Product.From(reader));
            }
            return Results.Json(products);
        }
        catch (Exception)
        {
            return WriteError(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Internal server error");
        }
    }

    public static async Task<IResult> CreateProduct(HttpRequest request, NpgsqlDataSource db)
    {
        var input = await ReadInput(request);
        if (input is null)
        {
            return WriteError(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Invalid request body");
        }

        if (input.Name == "" || input.Price <= 0 || input.Category == "")
        {
            return WriteError(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Name, price (>0), and category are required");
        }

        try
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO products (name, price, category) VALUES (@name, @price, @category) RETURNING id, name, price, category",
                conn);
            cmd.Parameters.AddWithValue("name", input.Name);
            cmd.Parameters.AddWithValue("price", input.Price);
            cmd.Parameters.AddWithValue("category", input.Category);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var product = Product.From(reader);
            return Results.Json(product, statusCode: StatusCodes.Status201Created);
        }
        catch (Exception)
        {
            return WriteError(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Failed to create product");
        }
    }

    public static async Task<IResult> GetProduct(string id, NpgsqlDataSource db)
    {
        if (!int.TryParse(id, out var productId))
        {
            return WriteError(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Invalid ID");
        }

        try
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, name, price, category FROM products WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", productId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return WriteError(StatusCodes.Status404NotFound, "NOT_FOUND", "Product not found");
            }
            return Results.Json(Product.From(reader));
        }
        catch (Exception)
        {
            return WriteError(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Internal server error");
        }
    }

    public static async Task<IResult> UpdateProduct(string id, HttpRequest request, NpgsqlDataSource db)
    {
        if (!int.TryParse(id, out var productId))
        {
            return WriteError(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Invalid ID");
        }

        var input = await ReadInput(request);
        if (input is null)
        {
            return WriteError(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Invalid request body");
        }

        try
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE products SET name=@name, price=@price, category=@category WHERE id=@id RETURNING id, name, price, category",
                conn);
            cmd.Parameters.AddWithValue("name", input.Name);
            cmd.Parameters.AddWithValue("price", input.Price);
            cmd.Parameters.AddWithValue("category", input.Category);
            cmd.Parameters.AddWithValue("id", productId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return WriteError(StatusCodes.Status404NotFound, "NOT_FOUND", "Product not found");
            }
            return Results.Json(Product.From(reader));
        }
        catch (Exception)
        {
            return WriteError(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Internal server error");
        }
    }

    public static async Task<IResult> DeleteProduct(string id, NpgsqlDataSource db)
    {
        if (!int.TryParse(id, out var productId))
        {
            return WriteError(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Invalid ID");
        }

        try
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM products WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", productId);
            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                return WriteError(StatusCodes.Status404NotFound, "NOT_FOUND", "Product not found");
            }
            return Results.StatusCode(StatusCodes.Status204NoContent);
        }
        catch (Exception)
        {
            return WriteError(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Internal server error");
        }
    }
}

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
