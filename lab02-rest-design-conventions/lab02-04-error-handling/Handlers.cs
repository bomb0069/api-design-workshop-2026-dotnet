using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

// Request handlers, mirroring handlers.go in the Go version.

public class Product
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

public class CreateProductInput
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("price")] public double Price { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
}

public static class Handlers
{
    public static ILogger Logger { get; set; } = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

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
        catch (Exception ex)
        {
            Logger.LogError("Database error: {Error}", ex.Message);
            return ApiError.NewInternalError().Send();
        }
    }

    public static async Task<IResult> CreateProduct(HttpRequest request, NpgsqlDataSource db)
    {
        var input = await ReadInput(request);
        if (input is null)
        {
            return ApiError.NewBadRequestError("Invalid request body").Send();
        }

        if (string.IsNullOrEmpty(input.Name))
        {
            return ApiError.NewBadRequestError("Name is required").Send();
        }
        if (input.Price <= 0)
        {
            return ApiError.NewBadRequestError("Price must be greater than 0").Send();
        }
        if (string.IsNullOrEmpty(input.Category))
        {
            return ApiError.NewBadRequestError("Category is required").Send();
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
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return ApiError.NewConflictError("A product with this name already exists").Send();
        }
        catch (Exception ex)
        {
            Logger.LogError("Database error: {Error}", ex.Message);
            return ApiError.NewInternalError().Send();
        }
    }

    public static async Task<IResult> GetProduct(string id, NpgsqlDataSource db)
    {
        if (!int.TryParse(id, out var productId))
        {
            return ApiError.NewBadRequestError("Invalid ID format").Send();
        }

        try
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, name, price, category FROM products WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", productId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return ApiError.NewNotFoundError("Product").Send();
            }
            return Results.Json(Product.From(reader));
        }
        catch (Exception ex)
        {
            Logger.LogError("Database error: {Error}", ex.Message);
            return ApiError.NewInternalError().Send();
        }
    }

    public static async Task<IResult> UpdateProduct(string id, HttpRequest request, NpgsqlDataSource db)
    {
        if (!int.TryParse(id, out var productId))
        {
            return ApiError.NewBadRequestError("Invalid ID format").Send();
        }

        var input = await ReadInput(request);
        if (input is null)
        {
            return ApiError.NewBadRequestError("Invalid request body").Send();
        }

        if (string.IsNullOrEmpty(input.Name))
        {
            return ApiError.NewBadRequestError("Name is required").Send();
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
                return ApiError.NewNotFoundError("Product").Send();
            }
            return Results.Json(Product.From(reader));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return ApiError.NewConflictError("A product with this name already exists").Send();
        }
        catch (Exception ex)
        {
            Logger.LogError("Database error: {Error}", ex.Message);
            return ApiError.NewInternalError().Send();
        }
    }

    public static async Task<IResult> DeleteProduct(string id, NpgsqlDataSource db)
    {
        if (!int.TryParse(id, out var productId))
        {
            return ApiError.NewBadRequestError("Invalid ID format").Send();
        }

        try
        {
            await using var conn = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM products WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", productId);
            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                return ApiError.NewNotFoundError("Product").Send();
            }
            return Results.StatusCode(StatusCodes.Status204NoContent);
        }
        catch (Exception ex)
        {
            Logger.LogError("Database error: {Error}", ex.Message);
            return ApiError.NewInternalError().Send();
        }
    }
}
