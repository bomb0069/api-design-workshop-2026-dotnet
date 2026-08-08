using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");

var dataSource = NpgsqlDataSource.Create(Db.BuildConnectionString());
builder.Services.AddSingleton(dataSource);

var app = builder.Build();

// Ping the database, create the table, and seed sample data on startup.
await Db.CreateTableAndSeed(dataSource);

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

app.MapGet("/products", async (HttpRequest request, NpgsqlDataSource db) =>
{
    var query = request.Query;

    // Parse pagination params (invalid values fall back to defaults)
    _ = int.TryParse(query["page"], out var page);
    if (page < 1)
    {
        page = 1;
    }
    _ = int.TryParse(query["limit"], out var limit);
    if (limit < 1 || limit > 100)
    {
        limit = 10;
    }

    // Parse filter params
    var category = query["category"].ToString();
    var inStockStr = query["in_stock"].ToString();
    var minPriceStr = query["min_price"].ToString();
    var maxPriceStr = query["max_price"].ToString();

    // Parse sort params
    var sortField = query["sort"].ToString();
    if (sortField == "")
    {
        sortField = "id";
    }
    var sortOrder = query["order"].ToString();
    if (sortOrder != "desc")
    {
        sortOrder = "asc";
    }

    // Validate sort field (whitelist to prevent SQL injection via ORDER BY)
    var validSortFields = new HashSet<string> { "id", "name", "price", "category" };
    if (!validSortFields.Contains(sortField))
    {
        return Results.Json(new { error = "Invalid sort field. Use: id, name, price, category" },
            statusCode: StatusCodes.Status400BadRequest);
    }

    // Build WHERE clause with parameterized filters
    var where = "WHERE 1=1";
    var parameters = new List<NpgsqlParameter>();
    var argIdx = 1;

    if (category != "")
    {
        where += $" AND category = @p{argIdx}";
        parameters.Add(new NpgsqlParameter($"p{argIdx}", category));
        argIdx++;
    }
    if (inStockStr != "" && GoStrconv.TryParseBool(inStockStr, out var inStock))
    {
        where += $" AND in_stock = @p{argIdx}";
        parameters.Add(new NpgsqlParameter($"p{argIdx}", inStock));
        argIdx++;
    }
    if (minPriceStr != "" && double.TryParse(minPriceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var minPrice))
    {
        where += $" AND price >= @p{argIdx}";
        parameters.Add(new NpgsqlParameter($"p{argIdx}", minPrice));
        argIdx++;
    }
    if (maxPriceStr != "" && double.TryParse(maxPriceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxPrice))
    {
        where += $" AND price <= @p{argIdx}";
        parameters.Add(new NpgsqlParameter($"p{argIdx}", maxPrice));
        argIdx++;
    }

    try
    {
        await using var conn = await db.OpenConnectionAsync();

        // Count total
        var totalItems = 0;
        await using (var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM products " + where, conn))
        {
            foreach (var p in parameters)
            {
                countCmd.Parameters.Add(p.Clone());
            }
            totalItems = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        // Fetch page
        var offset = (page - 1) * limit;
        var sql = $"SELECT id, name, price, category, in_stock FROM products {where} ORDER BY {sortField} {sortOrder} LIMIT @p{argIdx} OFFSET @p{argIdx + 1}";

        var products = new List<Product>();
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            foreach (var p in parameters)
            {
                cmd.Parameters.Add(p.Clone());
            }
            cmd.Parameters.Add(new NpgsqlParameter($"p{argIdx}", limit));
            cmd.Parameters.Add(new NpgsqlParameter($"p{argIdx + 1}", offset));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                products.Add(Product.From(reader));
            }
        }

        var totalPages = (int)Math.Ceiling(totalItems / (double)limit);

        return Results.Json(new PaginatedResponse
        {
            Data = products,
            Metadata = new PageMetadata
            {
                CurrentPage = page,
                PageSize = limit,
                TotalItems = totalItems,
                TotalPages = totalPages,
            },
        });
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

    await using var conn = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("SELECT id, name, price, category, in_stock FROM products WHERE id = @id", conn);
    cmd.Parameters.AddWithValue("id", productId);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.Json(new { error = "Product not found" }, statusCode: StatusCodes.Status404NotFound);
    }
    return Results.Json(Product.From(reader));
});

app.Logger.LogInformation("Server starting on :8080");
app.Run();

class Product
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("price")] public double Price { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("in_stock")] public bool InStock { get; set; }

    public static Product From(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        Price = reader.GetDouble(2),
        Category = reader.GetString(3),
        InStock = reader.GetBoolean(4),
    };
}

class PaginatedResponse
{
    [JsonPropertyName("data")] public List<Product> Data { get; set; } = new();
    [JsonPropertyName("metadata")] public PageMetadata Metadata { get; set; } = new();
}

class PageMetadata
{
    [JsonPropertyName("current_page")] public int CurrentPage { get; set; }
    [JsonPropertyName("page_size")] public int PageSize { get; set; }
    [JsonPropertyName("total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
}

static class GoStrconv
{
    // Mirrors Go's strconv.ParseBool: accepts 1, t, T, TRUE, true, True, 0, f, F, FALSE, false, False.
    public static bool TryParseBool(string value, out bool result)
    {
        switch (value)
        {
            case "1" or "t" or "T" or "true" or "TRUE" or "True":
                result = true;
                return true;
            case "0" or "f" or "F" or "false" or "FALSE" or "False":
                result = false;
                return true;
            default:
                result = false;
                return false;
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

    public static async Task CreateTableAndSeed(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();

        await using (var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS products (
                id SERIAL PRIMARY KEY,
                name TEXT NOT NULL,
                price DECIMAL(10,2) NOT NULL,
                category TEXT NOT NULL,
                in_stock BOOLEAN DEFAULT TRUE
            )
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        long count;
        await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM products", conn))
        {
            count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        if (count > 0)
        {
            return;
        }

        var products = new (string Name, double Price, string Category, bool InStock)[]
        {
            ("Laptop Pro 15", 1299.99, "electronics", true),
            ("Wireless Mouse", 29.99, "electronics", true),
            ("Mechanical Keyboard", 89.99, "electronics", true),
            ("USB-C Hub", 49.99, "electronics", false),
            ("Monitor 27 inch", 449.99, "electronics", true),
            ("Go Programming Language", 39.99, "books", true),
            ("Clean Code", 34.99, "books", true),
            ("Design Patterns", 44.99, "books", false),
            ("API Design Patterns", 49.99, "books", true),
            ("The Pragmatic Programmer", 42.99, "books", true),
            ("Cotton T-Shirt", 19.99, "clothing", true),
            ("Denim Jeans", 59.99, "clothing", true),
            ("Winter Jacket", 129.99, "clothing", false),
            ("Running Shoes", 89.99, "clothing", true),
            ("Baseball Cap", 14.99, "clothing", true),
            ("Organic Coffee", 12.99, "food", true),
            ("Green Tea Pack", 8.99, "food", true),
            ("Dark Chocolate", 5.99, "food", true),
            ("Protein Bars", 24.99, "food", false),
            ("Olive Oil", 15.99, "food", true),
        };

        foreach (var p in products)
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO products (name, price, category, in_stock) VALUES (@name, @price, @category, @inStock)", conn);
            cmd.Parameters.AddWithValue("name", p.Name);
            cmd.Parameters.AddWithValue("price", p.Price);
            cmd.Parameters.AddWithValue("category", p.Category);
            cmd.Parameters.AddWithValue("inStock", p.InStock);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
