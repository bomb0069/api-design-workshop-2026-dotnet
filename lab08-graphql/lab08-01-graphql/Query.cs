// Mirrors the query fields in schema.go and their resolvers in resolvers.go.
using Npgsql;

public class Query
{
    [GraphQLDescription("List all products")]
    public async Task<List<Product>> GetProducts(
        [Service] NpgsqlDataSource db,
        [GraphQLDescription("Filter by category")] string? category = null,
        [GraphQLDescription("Minimum price filter")] double? minPrice = null,
        [GraphQLDescription("Maximum price filter")] double? maxPrice = null)
    {
        var sql = "SELECT id, name, price, category FROM products WHERE 1=1";
        var values = new List<object>();

        if (category is not null)
        {
            sql += $" AND category = ${values.Count + 1}";
            values.Add(category);
        }
        if (minPrice is not null)
        {
            sql += $" AND price >= ${values.Count + 1}";
            values.Add(minPrice.Value);
        }
        if (maxPrice is not null)
        {
            sql += $" AND price <= ${values.Count + 1}";
            values.Add(maxPrice.Value);
        }

        sql += " ORDER BY id";

        await using var cmd = db.CreateCommand(sql);
        foreach (var value in values)
            cmd.Parameters.AddWithValue(value);

        var products = new List<Product>();
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
        return products;
    }

    [GraphQLDescription("Get a single product by ID")]
    public async Task<Product?> GetProduct([Service] NpgsqlDataSource db, int id)
    {
        await using var cmd = db.CreateCommand("SELECT id, name, price, category FROM products WHERE id = $1");
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Product
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Price = reader.GetDouble(2),
            Category = reader.GetString(3)
        };
    }

    [GraphQLDescription("List categories with product counts")]
    public async Task<List<Category>> GetCategories([Service] NpgsqlDataSource db)
    {
        await using var cmd = db.CreateCommand(
            "SELECT category, COUNT(*) FROM products GROUP BY category ORDER BY category");
        var categories = new List<Category>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            categories.Add(new Category
            {
                Name = reader.GetString(0),
                Count = (int)reader.GetInt64(1)
            });
        }
        return categories;
    }
}
