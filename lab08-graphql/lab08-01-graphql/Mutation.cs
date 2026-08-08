// Mirrors the mutation fields in schema.go and their resolvers in resolvers.go.
using Npgsql;

public class Mutation
{
    [GraphQLDescription("Create a new product")]
    public async Task<Product> CreateProduct(
        [Service] NpgsqlDataSource db, string name, double price, string category)
    {
        await using var cmd = db.CreateCommand(
            "INSERT INTO products (name, price, category) VALUES ($1, $2, $3) RETURNING id, name, price, category");
        cmd.Parameters.AddWithValue(name);
        cmd.Parameters.AddWithValue(price);
        cmd.Parameters.AddWithValue(category);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new Product
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Price = reader.GetDouble(2),
            Category = reader.GetString(3)
        };
    }

    [GraphQLDescription("Update an existing product")]
    public async Task<Product> UpdateProduct(
        [Service] NpgsqlDataSource db, int id,
        string? name = null, double? price = null, string? category = null)
    {
        Product product;
        await using (var cmd = db.CreateCommand("SELECT id, name, price, category FROM products WHERE id = $1"))
        {
            cmd.Parameters.AddWithValue(id);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new GraphQLException("product not found");

            product = new Product
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Price = reader.GetDouble(2),
                Category = reader.GetString(3)
            };
        }

        product = product with
        {
            Name = name ?? product.Name,
            Price = price ?? product.Price,
            Category = category ?? product.Category
        };

        await using (var cmd = db.CreateCommand(
            "UPDATE products SET name=$1, price=$2, category=$3 WHERE id=$4 RETURNING id, name, price, category"))
        {
            cmd.Parameters.AddWithValue(product.Name);
            cmd.Parameters.AddWithValue(product.Price);
            cmd.Parameters.AddWithValue(product.Category);
            cmd.Parameters.AddWithValue(id);

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return new Product
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Price = reader.GetDouble(2),
                Category = reader.GetString(3)
            };
        }
    }

    [GraphQLDescription("Delete a product by ID")]
    public async Task<bool> DeleteProduct([Service] NpgsqlDataSource db, int id)
    {
        await using var cmd = db.CreateCommand("DELETE FROM products WHERE id = $1");
        cmd.Parameters.AddWithValue(id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }
}
