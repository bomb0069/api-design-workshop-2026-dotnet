using System.Diagnostics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "product-service";

// Custom ActivitySource for spans we create by hand (the simulated DB query).
var activitySource = new ActivitySource("ProductService");

// --- OpenTelemetry: all three signals exported over OTLP to the collector. ---
// The OTLP exporters read OTEL_EXPORTER_OTLP_ENDPOINT from the environment
// (set to http://otel-collector:4317 in docker-compose.yml).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // server spans for every incoming request
        .AddHttpClientInstrumentation()   // client spans for outgoing HTTP calls
        .AddSource("ProductService")      // our custom db:query-products spans
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

var products = new List<Product>
{
    new(1, "Laptop", 1299.99m),
    new(2, "Mechanical Keyboard", 89.50m),
    new(3, "4K Monitor", 449.00m),
};

app.MapGet("/api/products", async (ILogger<Program> logger) =>
{
    // A child span representing the (simulated) database query.
    using var span = activitySource.StartActivity("db:query-products");
    span?.SetTag("db.operation", "SELECT");
    span?.SetTag("db.rows_returned", products.Count);

    await Task.Delay(Random.Shared.Next(10, 100)); // simulated query latency

    logger.LogInformation("listed {Count} products", products.Count);
    return Results.Ok(products);
});

app.MapGet("/api/products/{id:int}", async (int id, ILogger<Program> logger) =>
{
    using var span = activitySource.StartActivity("db:query-products");
    span?.SetTag("db.operation", "SELECT");
    span?.SetTag("product.id", id);

    await Task.Delay(Random.Shared.Next(10, 100)); // simulated query latency

    var product = products.FirstOrDefault(p => p.Id == id);
    if (product is null)
    {
        logger.LogWarning("product {ProductId} not found", id);
        return Results.NotFound(new { error = "product not found" });
    }

    logger.LogInformation("product {ProductId} found: {ProductName}", product.Id, product.Name);
    return Results.Ok(product);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

record Product(int Id, string Name, decimal Price);
