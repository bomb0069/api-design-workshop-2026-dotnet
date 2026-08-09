// Order Service — a PLAIN .NET 8 minimal API.
//
// Note what is NOT here: no OpenTelemetry packages, no tracing middleware,
// no metrics code. Even the HttpClient call to product-service carries no
// instrumentation — Beyla injects the trace context at the kernel level so
// the two services still show up in one distributed trace.

var builder = WebApplication.CreateBuilder(args);

var productServiceUrl = builder.Configuration["PRODUCT_SERVICE_URL"]
    ?? "http://product-service:8080";

builder.Services.AddHttpClient("products", client =>
{
    client.BaseAddress = new Uri(productServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

var orders = new Dictionary<int, Order>();
var nextOrderId = 0;

app.MapPost("/api/orders", async (CreateOrderRequest request, IHttpClientFactory httpClientFactory) =>
{
    if (request.Quantity <= 0)
    {
        return Results.BadRequest(new { error = "quantity must be greater than zero" });
    }

    // Call product-service to look up the product. A plain HTTP call —
    // no propagation code, no instrumented handler.
    var client = httpClientFactory.CreateClient("products");
    var response = await client.GetAsync($"/api/products/{request.ProductId}");

    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = "product not found" });
    }
    response.EnsureSuccessStatusCode();

    var product = await response.Content.ReadFromJsonAsync<Product>();
    if (product is null)
    {
        return Results.Problem("invalid response from product service");
    }

    if (product.Stock < request.Quantity)
    {
        return Results.Conflict(new { error = "insufficient stock" });
    }

    var order = new Order(
        Id: ++nextOrderId,
        ProductId: product.Id,
        ProductName: product.Name,
        Quantity: request.Quantity,
        Total: product.Price * request.Quantity);
    orders[order.Id] = order;

    return Results.Created($"/api/orders/{order.Id}", order);
});

app.MapGet("/api/orders/{id:int}", (int id) =>
    orders.TryGetValue(id, out var order)
        ? Results.Json(order)
        : Results.NotFound(new { error = "order not found" }));

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

Console.WriteLine("Order service starting on :8080");
app.Run("http://0.0.0.0:8080");

public record CreateOrderRequest(int ProductId, int Quantity);
public record Product(int Id, string Name, decimal Price, int Stock);
public record Order(int Id, int ProductId, string ProductName, int Quantity, decimal Total);
