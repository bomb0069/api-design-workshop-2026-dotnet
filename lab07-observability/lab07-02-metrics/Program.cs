using Prometheus;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// In-memory catalog — ids 1..5 exist, anything else is a 404.
var products = new List<Product>
{
    new(1, "Laptop", 999.99m),
    new(2, "Keyboard", 79.99m),
    new(3, "Mouse", 29.99m),
    new(4, "Monitor", 249.99m),
    new(5, "Headset", 59.99m),
};
var orders = new List<Order>();

// RED metrics middleware — must be registered BEFORE the endpoints so it
// wraps every request. It records Rate, Errors, and Duration for each route.
app.UseMiddleware<RedMetricsMiddleware>();
app.MapMetrics(); // exposes /metrics in Prometheus text format

// Health endpoint — polled by the loadtest container and by orchestrators.
// Deliberately EXCLUDED from metrics (see RedMetricsMiddleware) so probe
// traffic doesn't drown out real request signals.
app.MapGet("/health", () => Results.Json(new { status = "ok" }));

// GET /api/products — fast, always 200. Baseline for the Rate panel.
app.MapGet("/api/products", () => Results.Json(products));

// GET /api/products/{id} — adds 10–300ms of simulated work (cache miss,
// slow query…) so the Duration histogram has an interesting spread.
// Unknown ids return 404 — a CLIENT error, not counted as a 5xx error.
app.MapGet("/api/products/{id}", async (int id) =>
{
    await Task.Delay(Random.Shared.Next(10, 301)); // simulated latency
    var product = products.FirstOrDefault(p => p.Id == id);
    return product is null
        ? Results.Json(new { error = "product not found" }, statusCode: 404)
        : Results.Json(product);
});

// POST /api/orders — the Errors signal:
//   * 400 when the body is invalid (client error)
//   * 500 for ~10% of valid requests (simulated flaky downstream dependency)
app.MapPost("/api/orders", (OrderRequest? request) =>
{
    if (request is null || request.ProductId <= 0 || request.Quantity <= 0)
        return Results.Json(
            new { error = "productId and quantity are required and must be positive" },
            statusCode: 400);

    var product = products.FirstOrDefault(p => p.Id == request.ProductId);
    if (product is null)
        return Results.Json(new { error = "unknown productId" }, statusCode: 400);

    // Simulated downstream failure — ~10% of orders fail at the "payment service".
    if (Random.Shared.Next(100) < 10)
        return Results.Json(new { error = "payment service unavailable" }, statusCode: 500);

    var order = new Order(orders.Count + 1, product.Id, request.Quantity,
        product.Price * request.Quantity, "confirmed");
    orders.Add(order);
    return Results.Json(order, statusCode: 201);
});

app.Run("http://0.0.0.0:8080");

record Product(int Id, string Name, decimal Price);
record OrderRequest(int ProductId, int Quantity);
record Order(int Id, int ProductId, int Quantity, decimal Total, string Status);
