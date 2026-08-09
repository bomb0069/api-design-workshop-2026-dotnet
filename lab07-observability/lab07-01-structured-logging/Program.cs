using System.Text.Json;
using StructuredLogging.Middleware;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// 1. JSON console logging: every log line is ONE JSON object.
//
// ClearProviders() removes the default human-oriented console logger;
// AddJsonConsole() replaces it with a machine-first one.
//
// IncludeScopes = true is what makes the correlation-ID scope (opened in
// CorrelationIdMiddleware) appear on every log line of a request.
// -----------------------------------------------------------------------------
builder.Logging.ClearProviders().AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.JsonWriterOptions = new JsonWriterOptions
    {
        Indented = false // one line per log entry -- friendly to grep/jq/aggregators
    };
});

var app = builder.Build();

// Order matters: correlation ID first (so the scope covers everything),
// then request logging (so its line carries the correlation_id too).
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

var store = new ProductStore();

// Application logs use their own category names, so you can filter per
// component (e.g. "Logging__LogLevel__ProductsApi=Debug").
var productsLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ProductsApi");
var levelsLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LevelsDemo");

// GET /api/products - list all products
app.MapGet("/api/products", () =>
{
    var products = store.List();
    productsLogger.LogInformation("Listing products, {count} in store", products.Count);
    return Results.Ok(products);
});

// GET /api/products/{id} - get one product, 404 if missing
app.MapGet("/api/products/{id:int}", (int id) =>
{
    var product = store.Get(id);
    if (product is null)
    {
        // Warning, not Error: a client asking for a missing ID is unusual
        // but the API handled it correctly.
        productsLogger.LogWarning("Product {product_id} not found", id);
        return Results.NotFound(new { error = "Product not found" });
    }

    productsLogger.LogInformation("Fetched product {product_id} ({product_name})", product.Id, product.Name);
    return Results.Ok(product);
});

// POST /api/products - create a product
app.MapPost("/api/products", (CreateProductInput input) =>
{
    if (string.IsNullOrWhiteSpace(input.Name))
    {
        productsLogger.LogWarning("Rejected product creation: name is required");
        return Results.BadRequest(new { error = "Name is required" });
    }
    if (input.Price is null or <= 0)
    {
        productsLogger.LogWarning("Rejected product creation: invalid price {price}", input.Price);
        return Results.BadRequest(new { error = "Price must be greater than 0" });
    }

    var product = store.Create(input.Name, input.Price.Value);
    productsLogger.LogInformation("Created product {product_id} ({product_name})", product.Id, product.Name);
    return Results.Created($"/api/products/{product.Id}", product);
});

// GET /api/demo/levels - write one message at every log level.
// Which of them reach the console depends on the configured minimum level.
app.MapGet("/api/demo/levels", () =>
{
    levelsLogger.LogTrace("Trace: step-by-step flow, e.g. 'entering ProductStore.List'");
    levelsLogger.LogDebug("Debug: diagnostic detail, e.g. 'cache miss for product 42'");
    levelsLogger.LogInformation("Information: normal business event, e.g. 'order placed'");
    levelsLogger.LogWarning("Warning: odd but handled, e.g. 'retry 2/3 for payment provider'");
    levelsLogger.LogError("Error: this operation failed, e.g. 'payment provider unreachable'");
    levelsLogger.LogCritical("Critical: the service itself is in danger, e.g. 'out of disk space'");

    return Results.Ok(new
    {
        message = "Wrote one log line per level (Trace..Critical). "
                + "Check the console: lines below the configured minimum level were dropped."
    });
});

app.Run("http://0.0.0.0:8080");

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

// Nullable Price distinguishes "field not provided" from "price: 0".
public class CreateProductInput
{
    public string? Name { get; set; }
    public decimal? Price { get; set; }
}

// Thread-safe in-memory store, seeded with a few products.
public class ProductStore
{
    private readonly object _lock = new();
    private readonly Dictionary<int, Product> _products;
    private int _nextId;

    public ProductStore()
    {
        _products = new Dictionary<int, Product>
        {
            [1] = new Product { Id = 1, Name = "Laptop", Price = 999.99m },
            [2] = new Product { Id = 2, Name = "Mouse", Price = 19.99m },
            [3] = new Product { Id = 3, Name = "Monitor", Price = 249.50m },
        };
        _nextId = 4;
    }

    public List<Product> List()
    {
        lock (_lock)
        {
            return _products.Values.OrderBy(p => p.Id).ToList();
        }
    }

    public Product? Get(int id)
    {
        lock (_lock)
        {
            return _products.TryGetValue(id, out var product) ? product : null;
        }
    }

    public Product Create(string name, decimal price)
    {
        lock (_lock)
        {
            var product = new Product { Id = _nextId, Name = name, Price = price };
            _products[product.Id] = product;
            _nextId++;
            return product;
        }
    }
}
