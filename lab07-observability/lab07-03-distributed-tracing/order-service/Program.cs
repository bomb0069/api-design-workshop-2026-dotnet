using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// OpenTelemetry setup — identical shape to product-service (see README for
// the full explanation, told once):
// resource (service identity) + server spans + client spans + our own
// ActivitySource + OTLP exporter (endpoint read from
// OTEL_EXPORTER_OTLP_ENDPOINT automatically).
// ---------------------------------------------------------------------------
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "order-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("order-service") // registered for the baggage/custom-span exercises
        .AddOtlpExporter());

// Named HttpClient for calls to product-service. Because HttpClient
// instrumentation is enabled, every request through this client gets a CLIENT
// span and an injected `traceparent` header — we never touch headers ourselves.
var productServiceUrl = Environment.GetEnvironmentVariable("PRODUCT_SERVICE_URL")
    ?? "http://product-service:8080";
builder.Services.AddHttpClient("product-service",
    client => client.BaseAddress = new Uri(productServiceUrl));

var app = builder.Build();

var orders = new ConcurrentDictionary<int, Order>();
var nextOrderId = 0;

app.MapPost("/api/orders", async (NewOrder request, IHttpClientFactory httpClientFactory) =>
{
    if (request.Quantity < 1)
    {
        return Results.BadRequest(new { error = "quantity must be at least 1" });
    }

    // Cross-service call: validate the product and fetch its price.
    var client = httpClientFactory.CreateClient("product-service");
    var response = await client.GetAsync($"/api/products/{request.ProductId}");

    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        // Mark the SERVER span as failed so Jaeger paints it red. A 400 alone
        // would not do this — from the tracing point of view a 4xx response
        // is still a span that completed; the status is an explicit judgement.
        Activity.Current?.SetStatus(ActivityStatusCode.Error,
            $"unknown product {request.ProductId}");
        return Results.BadRequest(new { error = "unknown product" });
    }
    response.EnsureSuccessStatusCode();

    var product = await response.Content.ReadFromJsonAsync<Product>()
        ?? throw new InvalidOperationException("product-service returned an empty body");

    var id = Interlocked.Increment(ref nextOrderId);
    var order = new Order(
        Id: id,
        ProductId: product.Id,
        ProductName: product.Name,
        Quantity: request.Quantity,
        UnitPrice: product.Price,
        Total: product.Price * request.Quantity);
    orders[id] = order;

    // Enrich the auto-created server span. Activity.Current is the span that
    // AddAspNetCoreInstrumentation opened for this request — we do not need
    // to create anything to annotate it.
    Activity.Current?.SetTag("order.id", order.Id);
    Activity.Current?.SetTag("order.total", order.Total);

    return Results.Created($"/api/orders/{order.Id}", order);
});

app.MapGet("/api/orders/{id:int}", (int id) =>
    orders.TryGetValue(id, out var order)
        ? Results.Ok(order)
        : Results.NotFound(new { error = "order not found" }));

// Debug endpoint: shows BOTH sides of one propagated trace in a single
// response. The `order` half is this service's server span; the `product`
// half is what product-service received. Same traceId on both sides, and the
// product's parentSpanId is the HttpClient CLIENT span (a child of our server
// span) — which is exactly the chain Jaeger draws.
app.MapGet("/api/debug/chain", async (IHttpClientFactory httpClientFactory) =>
{
    var activity = Activity.Current;
    var orderSide = new
    {
        traceId = activity?.TraceId.ToString(),
        spanId = activity?.SpanId.ToString(),
    };

    var client = httpClientFactory.CreateClient("product-service");
    var productSide = await client.GetFromJsonAsync<JsonElement>("/api/debug/traceparent");

    return Results.Ok(new { order = orderSide, product = productSide });
});

app.Run();

record NewOrder(int ProductId, int Quantity);
record Product(int Id, string Name, decimal Price, int Stock);
record Order(int Id, int ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal Total);
