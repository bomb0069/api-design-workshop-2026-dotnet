// Mirrors handlers.go: table creation/seeding and the product handlers.
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

public record Product
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("price")] public double Price { get; init; }
    [JsonPropertyName("category")] public string Category { get; init; } = "";
}

public record ProductInput(string? Name, double Price, string? Category);

public static class Db
{
    public static void CreateTables(NpgsqlDataSource db)
    {
        using (var cmd = db.CreateCommand(@"CREATE TABLE IF NOT EXISTS users (
            id SERIAL PRIMARY KEY,
            username TEXT UNIQUE NOT NULL,
            email TEXT UNIQUE NOT NULL,
            password_hash TEXT NOT NULL
        )"))
        {
            cmd.ExecuteNonQuery();
        }

        using (var cmd = db.CreateCommand(@"CREATE TABLE IF NOT EXISTS products (
            id SERIAL PRIMARY KEY,
            name TEXT NOT NULL,
            price DECIMAL(10,2) NOT NULL,
            category TEXT NOT NULL
        )"))
        {
            cmd.ExecuteNonQuery();
        }

        // Seed sample products
        long count;
        using (var cmd = db.CreateCommand("SELECT COUNT(*) FROM products"))
        {
            count = (long)(cmd.ExecuteScalar() ?? 0L);
        }
        if (count == 0)
        {
            using var cmd = db.CreateCommand(
                "INSERT INTO products (name, price, category) VALUES " +
                "('Laptop', 999.99, 'electronics'), " +
                "('Go Book', 39.99, 'books'), " +
                "('T-Shirt', 19.99, 'clothing')");
            cmd.ExecuteNonQuery();
        }
    }
}

public static class ProductHandlers
{
    public static async Task<IResult> List(NpgsqlDataSource db)
    {
        var products = new List<Product>();
        try
        {
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
        }
        catch (Exception)
        {
            return Results.Json(new ErrorResponse("Internal server error"), statusCode: 500);
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

        if (string.IsNullOrEmpty(input.Name))
            return Results.Json(new ErrorResponse("Name is required"), statusCode: 400);

        try
        {
            await using var cmd = db.CreateCommand(
                "INSERT INTO products (name, price, category) VALUES ($1, $2, $3) RETURNING id, name, price, category");
            cmd.Parameters.AddWithValue(input.Name);
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
        catch (Exception)
        {
            return Results.Json(new ErrorResponse("Internal server error"), statusCode: 500);
        }
    }

    public static async Task<IResult> Get(string id, NpgsqlDataSource db)
    {
        if (!int.TryParse(id, out var productId))
            return Results.Json(new ErrorResponse("Invalid ID"), statusCode: 400);

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
