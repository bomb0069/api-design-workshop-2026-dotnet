// Mirrors main.go: connect to Postgres, seed data, serve GraphQL at /graphql
// plus a REST comparison endpoint at /api/products.
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=workshop;Username=postgres;Password=postgres";

var builder = WebApplication.CreateBuilder(args);

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();

var app = builder.Build();

Db.CreateTableAndSeed(dataSource);

// GraphQL endpoint (Banana Cake Pop / Nitro IDE is served here in the browser,
// replacing the Go lab's GraphQL Playground)
app.MapGraphQL("/graphql");

// REST endpoint for comparison
app.MapGet("/api/products", async (NpgsqlDataSource db) =>
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
});

Console.WriteLine("Server starting on :8080");
Console.WriteLine("GraphQL IDE: http://localhost:8080/graphql");
app.Run("http://0.0.0.0:8080");

public static class Db
{
    public static void CreateTableAndSeed(NpgsqlDataSource db)
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
        if (count > 0)
            return;

        var products = new (string Name, double Price, string Category)[]
        {
            ("Laptop Pro", 1299.99, "electronics"),
            ("Wireless Mouse", 29.99, "electronics"),
            ("Mechanical Keyboard", 89.99, "electronics"),
            ("Go Programming Language", 39.99, "books"),
            ("Clean Code", 34.99, "books"),
            ("Design Patterns", 44.99, "books"),
            ("Cotton T-Shirt", 19.99, "clothing"),
            ("Denim Jeans", 59.99, "clothing"),
            ("Running Shoes", 89.99, "clothing")
        };
        foreach (var p in products)
        {
            using var cmd = db.CreateCommand("INSERT INTO products (name, price, category) VALUES ($1, $2, $3)");
            cmd.Parameters.AddWithValue(p.Name);
            cmd.Parameters.AddWithValue(p.Price);
            cmd.Parameters.AddWithValue(p.Category);
            cmd.ExecuteNonQuery();
        }
    }
}
