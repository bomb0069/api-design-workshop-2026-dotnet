using System.Text.Json;
using System.Text.Json.Serialization;

// Second backend behind the gateway — same story as users-service: business
// logic only, no auth/rate-limit/CORS code anywhere.

var orders = new List<Order>
{
    new(1, "john", "Laptop", 34900.00m, "shipped"),
    new(2, "jane", "Keyboard", 2590.00m, "pending"),
};
var nextId = 3;
var locker = new object();

var app = WebApplication.CreateBuilder(args).Build();

app.Use(async (context, next) =>
{
    await next();
    app.Logger.LogInformation("[orders-service] {Method} {Path} -> {Status} client={Client} rid={RequestId}",
        context.Request.Method, context.Request.Path, context.Response.StatusCode,
        context.Request.Headers["X-Client-Name"].FirstOrDefault() ?? "-",
        context.Request.Headers["X-Request-Id"].FirstOrDefault() ?? "-");
});

app.MapGet("/orders", () =>
{
    lock (locker) return Results.Json(orders.ToList());
});

app.MapGet("/orders/{id}", (string id) =>
{
    if (!int.TryParse(id, out var orderId))
        return Results.Json(new { error = "Invalid ID" }, statusCode: 400);
    lock (locker)
    {
        var order = orders.FirstOrDefault(o => o.Id == orderId);
        return order is null
            ? Results.Json(new { error = "Order not found" }, statusCode: 404)
            : Results.Json(order);
    }
});

app.MapPost("/orders", async (HttpRequest request) =>
{
    CreateOrderRequest? body;
    try
    {
        body = await JsonSerializer.DeserializeAsync<CreateOrderRequest>(request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (JsonException)
    {
        return Results.Json(new { error = "Invalid request body" }, statusCode: 400);
    }
    if (body is null || string.IsNullOrWhiteSpace(body.Item))
        return Results.Json(new { error = "Item is required" }, statusCode: 400);

    // The creating user comes from the gateway-authenticated client, not
    // from anything the caller could forge in the body.
    var client = request.Headers["X-Client-Name"].FirstOrDefault() ?? "unknown";
    lock (locker)
    {
        var order = new Order(nextId++, client, body.Item, body.Amount, "pending");
        orders.Add(order);
        return Results.Json(order, statusCode: 201);
    }
});

app.MapGet("/health", () => Results.Json(new { status = "ok", service = "orders-service" }));

app.Run("http://0.0.0.0:8082");

internal record Order(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("customer")] string Customer,
    [property: JsonPropertyName("item")] string Item,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("status")] string Status);

internal record CreateOrderRequest(
    [property: JsonPropertyName("item")] string? Item,
    [property: JsonPropertyName("amount")] decimal Amount);
