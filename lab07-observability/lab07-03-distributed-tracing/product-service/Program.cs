using System.Diagnostics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// OpenTelemetry setup.
//
// - Resource: identifies WHO emitted the spans. Jaeger shows this as the
//   service name in its dropdown. Read from OTEL_SERVICE_NAME env var.
// - AddAspNetCoreInstrumentation: one SERVER span per incoming HTTP request.
// - AddHttpClientInstrumentation: one CLIENT span per outgoing HttpClient call
//   (+ injects the W3C `traceparent` header automatically).
// - AddSource("product-service"): subscribes the SDK to our own ActivitySource,
//   so the custom spans below (db:query-products, check-stock) are exported.
// - AddOtlpExporter: ships spans over OTLP/gRPC. The endpoint is read
//   automatically from the OTEL_EXPORTER_OTLP_ENDPOINT env var — no code needed.
// ---------------------------------------------------------------------------
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "product-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("product-service")
        .AddOtlpExporter());

var app = builder.Build();

// In .NET, ActivitySource IS the OpenTelemetry tracer API.
// The name here must match the AddSource(...) registration above.
var activitySource = new ActivitySource("product-service");

var products = new List<Product>
{
    new(1, "Mechanical Keyboard", 89.90m, 24),
    new(2, "Wireless Mouse", 25.50m, 130),
    new(3, "27-inch Monitor", 219.00m, 8),
};

// Simulates a database round-trip and wraps it in a CUSTOM span.
// Auto-instrumentation cannot see inside your process — without this span,
// the 20-80ms spent "in the database" would just be unexplained dead time
// inside the server span. StartActivity automatically parents the new span
// under Activity.Current (the ASP.NET Core server span).
async Task SimulateDbQueryAsync(string operation, int? productId = null)
{
    using var span = activitySource.StartActivity("db:query-products");
    span?.SetTag("db.system", "memory");
    span?.SetTag("db.operation", operation);
    if (productId is int id)
    {
        span?.SetTag("product.id", id);
    }
    await Task.Delay(Random.Shared.Next(20, 81)); // pretend the DB is working
}

app.MapGet("/api/products", async () =>
{
    await SimulateDbQueryAsync("select-all");
    return Results.Ok(products);
});

app.MapGet("/api/products/{id:int}", async (int id) =>
{
    await SimulateDbQueryAsync("select-by-id", id);

    var product = products.FirstOrDefault(p => p.Id == id);
    if (product is null)
    {
        return Results.NotFound(new { error = "product not found" });
    }

    // A second custom span, sibling of db:query-products under the same
    // server span. Attributes turn a bare timeline into searchable data:
    // in Jaeger you can filter traces by `stock.remaining`.
    using (var stockSpan = activitySource.StartActivity("check-stock"))
    {
        stockSpan?.SetTag("product.id", id);
        stockSpan?.SetTag("stock.remaining", product.Stock);
        await Task.Delay(Random.Shared.Next(5, 15));
    }

    return Results.Ok(product);
});

// Debug endpoint: makes W3C trace context propagation VISIBLE.
// Returns the raw `traceparent` header this service received, plus how
// ASP.NET Core parsed it into the current Activity. Called directly it shows
// no traceparent; called through order-service (/api/debug/chain) it shows
// the same TraceId as the caller — proof that context crossed the network.
app.MapGet("/api/debug/traceparent", (HttpRequest request) =>
{
    var activity = Activity.Current;
    return Results.Ok(new
    {
        receivedTraceparent = request.Headers["traceparent"].FirstOrDefault(),
        traceId = activity?.TraceId.ToString(),
        spanId = activity?.SpanId.ToString(),
        parentSpanId = activity?.ParentSpanId.ToString(),
    });
});

app.Run();

record Product(int Id, string Name, decimal Price, int Stock);
