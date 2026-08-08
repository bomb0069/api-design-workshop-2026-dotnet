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
            category TEXT NOT NULL,
            sku VARCHAR(8) UNIQUE NOT NULL
        )
        """, conn);
    await cmd.ExecuteNonQueryAsync();
}

app.MapGet("/products", async (NpgsqlDataSource db) =>
{
    try
    {
        var products = new List<Product>();
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT id, name, price, category, sku FROM products ORDER BY id", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            products.Add(Product.From(reader));
        }
        return Results.Json(products, statusCode: StatusCodes.Status200OK);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/products", async (HttpRequest request, NpgsqlDataSource db) =>
{
    var input = await RequestBody.Read(request);
    if (input is null)
    {
        return Results.Json(new { error = "Invalid request body" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var errors = Validation.Validate(input);
    if (errors.Count > 0)
    {
        return Results.Json(new { error = "Validation failed", details = errors },
            statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO products (name, price, category, sku) VALUES (@name, @price, @category, @sku) RETURNING id, name, price, category, sku",
            conn);
        cmd.Parameters.AddWithValue("name", input.Name);
        cmd.Parameters.AddWithValue("price", input.Price);
        cmd.Parameters.AddWithValue("category", input.Category);
        cmd.Parameters.AddWithValue("sku", input.Sku);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var product = Product.From(reader);
        return Results.Json(product, statusCode: StatusCodes.Status201Created);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Json(new { error = "Product with this SKU already exists" }, statusCode: StatusCodes.Status409Conflict);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/products/{id}", async (string id, NpgsqlDataSource db) =>
{
    if (!int.TryParse(id, out var productId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT id, name, price, category, sku FROM products WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", productId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Json(new { error = "Product not found" }, statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Json(Product.From(reader), statusCode: StatusCodes.Status200OK);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPut("/products/{id}", async (string id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!int.TryParse(id, out var productId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var input = await RequestBody.Read(request);
    if (input is null)
    {
        return Results.Json(new { error = "Invalid request body" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var errors = Validation.Validate(input);
    if (errors.Count > 0)
    {
        return Results.Json(new { error = "Validation failed", details = errors },
            statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE products SET name=@name, price=@price, category=@category, sku=@sku WHERE id=@id RETURNING id, name, price, category, sku",
            conn);
        cmd.Parameters.AddWithValue("name", input.Name);
        cmd.Parameters.AddWithValue("price", input.Price);
        cmd.Parameters.AddWithValue("category", input.Category);
        cmd.Parameters.AddWithValue("sku", input.Sku);
        cmd.Parameters.AddWithValue("id", productId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Json(new { error = "Product not found" }, statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Json(Product.From(reader), statusCode: StatusCodes.Status200OK);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapDelete("/products/{id}", async (string id, NpgsqlDataSource db) =>
{
    if (!int.TryParse(id, out var productId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM products WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", productId);
        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        if (rowsAffected == 0)
        {
            return Results.Json(new { error = "Product not found" }, statusCode: StatusCodes.Status404NotFound);
        }
        return Results.StatusCode(StatusCodes.Status204NoContent);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Logger.LogInformation("Server starting on :8080");
app.Run();

class Product
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("price")] public double Price { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("sku")] public string Sku { get; set; } = "";

    public static Product From(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        Price = reader.GetDouble(2),
        Category = reader.GetString(3),
        Sku = reader.GetString(4),
    };
}

class CreateProductInput
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("price")] public double Price { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("sku")] public string Sku { get; set; } = "";
}

record ValidationError(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("message")] string Message);

static class Validation
{
    static readonly string[] Categories = { "electronics", "books", "clothing", "food" };

    // Mirrors the Go lab's go-playground/validator rules. Rules run in tag order
    // per field and the first failing rule produces the error message:
    //   name:     required,min=2,max=100
    //   price:    required,gt=0        (required fails when the value is the zero value 0)
    //   category: required,oneof=electronics books clothing food
    //   sku:      required,len=8,alphanum
    public static List<ValidationError> Validate(CreateProductInput input)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrEmpty(input.Name))
            errors.Add(new("name", "name is required"));
        else if (input.Name.Length < 2)
            errors.Add(new("name", "name must be at least 2 characters"));
        else if (input.Name.Length > 100)
            errors.Add(new("name", "name must be at most 100 characters"));

        if (input.Price == 0)
            errors.Add(new("price", "price is required"));
        else if (input.Price <= 0)
            errors.Add(new("price", "price must be greater than 0"));

        if (string.IsNullOrEmpty(input.Category))
            errors.Add(new("category", "category is required"));
        else if (!Categories.Contains(input.Category))
            errors.Add(new("category", "category must be one of: electronics books clothing food"));

        if (string.IsNullOrEmpty(input.Sku))
            errors.Add(new("sku", "sku is required"));
        else if (input.Sku.Length != 8)
            errors.Add(new("sku", "sku must be exactly 8 characters"));
        else if (!input.Sku.All(IsAsciiAlphanumeric))
            errors.Add(new("sku", "sku must contain only alphanumeric characters"));

        return errors;
    }

    static bool IsAsciiAlphanumeric(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
}

static class RequestBody
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    // Returns null when the body is not valid JSON (-> 400 Invalid request body),
    // mirroring json.NewDecoder(...).Decode in the Go version.
    public static async Task<CreateProductInput?> Read(HttpRequest request)
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<CreateProductInput>(body, Options) ?? new CreateProductInput();
        }
        catch (JsonException)
        {
            return null;
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
