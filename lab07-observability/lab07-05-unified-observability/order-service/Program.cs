using System.Diagnostics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "order-service";

// Custom ActivitySource for the spans we create by hand.
var activitySource = new ActivitySource("OrderService");

// HttpClient for calling product-service. The OTel HttpClient instrumentation
// injects the W3C traceparent header on every outgoing request, so the
// product-service spans join the SAME trace automatically.
builder.Services.AddHttpClient("products", client =>
{
    var baseUrl = Environment.GetEnvironmentVariable("PRODUCT_SERVICE_URL")
                  ?? "http://product-service:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

// --- OpenTelemetry: all three signals exported over OTLP to the collector. ---
// The OTLP exporters read OTEL_EXPORTER_OTLP_ENDPOINT from the environment
// (set to http://otel-collector:4317 in docker-compose.yml).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // server spans for every incoming request
        .AddHttpClientInstrumentation()   // client spans for the product-service call
        .AddSource("OrderService")        // our custom order:process spans
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()   // http.server.request.duration histogram
        .AddRuntimeInstrumentation()      // GC, thread pool, exceptions...
        .AddOtlpExporter());

// Logs: every ILogger record emitted inside an active Activity automatically
// carries that Activity's TraceId/SpanId. This is the correlation mechanism —
// no manual plumbing of trace ids into log messages.
builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName));
    options.IncludeScopes = true;
    options.AddOtlpExporter();
});

var app = builder.Build();

app.MapPost("/api/orders", async (OrderRequest request, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    // Child span for the business logic of processing the order.
    using var span = activitySource.StartActivity("order:process");
    span?.SetTag("order.product_id", request.ProductId);
    span?.SetTag("order.quantity", request.Quantity);

    if (request.Quantity <= 0)
    {
        logger.LogWarning("rejected order with invalid quantity {Quantity}", request.Quantity);
        return Results.BadRequest(new { error = "quantity must be greater than zero" });
    }

    // 1. Look up the product in product-service (creates a client span,
    //    propagates the trace context via the traceparent header).
    var client = httpClientFactory.CreateClient("products");
    HttpResponseMessage response;
    try
    {
        response = await client.GetAsync($"/api/products/{request.ProductId}");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "product lookup failed for product {ProductId}: {Reason}", request.ProductId, ex.Message);
        span?.SetStatus(ActivityStatusCode.Error, "product lookup failed");
        return Results.Json(new { error = "product lookup failed" }, statusCode: 502);
    }

    if (!response.IsSuccessStatusCode)
    {
        logger.LogError("product lookup failed for product {ProductId}: product-service returned {StatusCode}",
            request.ProductId, (int)response.StatusCode);
        span?.SetStatus(ActivityStatusCode.Error, "product lookup failed");
        var status = response.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : 502;
        return Results.Json(new { error = "product lookup failed" }, statusCode: status);
    }

    var product = await response.Content.ReadFromJsonAsync<Product>();
    if (product is null)
    {
        logger.LogError("product lookup failed for product {ProductId}: empty response body", request.ProductId);
        span?.SetStatus(ActivityStatusCode.Error, "product lookup failed");
        return Results.Json(new { error = "product lookup failed" }, statusCode: 502);
    }

    // 2. ~10% simulated failure so there is always something interesting to
    //    correlate: a 500 response, an ERROR log line, and a trace marked as
    //    failed — all sharing one trace_id.
    if (Random.Shared.Next(100) < 10)
    {
        logger.LogError("order processing failed for product {ProductId}: inventory reservation error", request.ProductId);
        span?.SetStatus(ActivityStatusCode.Error, "inventory reservation error");
        return Results.Json(new { error = "inventory reservation error" }, statusCode: 500);
    }

    // 3. Create the order.
    var order = new Order(
        Id: Guid.NewGuid().ToString("N")[..8],
        ProductId: product.Id,
        ProductName: product.Name,
        Quantity: request.Quantity,
        Total: product.Price * request.Quantity);

    span?.SetTag("order.id", order.Id);

    logger.LogInformation("order created {OrderId}: {Quantity} x {ProductName} (total {Total})",
        order.Id, order.Quantity, order.ProductName, order.Total);

    return Results.Created($"/api/orders/{order.Id}", order);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

record OrderRequest(int ProductId, int Quantity);
record Product(int Id, string Name, decimal Price);
record Order(string Id, int ProductId, string ProductName, int Quantity, decimal Total);
