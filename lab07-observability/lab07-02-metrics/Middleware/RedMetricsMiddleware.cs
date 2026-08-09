using System.Diagnostics;
using Prometheus;

/// <summary>
/// Records the three RED signals for every API request:
///   Rate     — http_requests_total (counter)
///   Errors   — http_requests_total filtered by status=~"5.." in PromQL
///   Duration — http_request_duration_seconds (histogram)
/// </summary>
public class RedMetricsMiddleware
{
    private readonly RequestDelegate _next;

    // Metrics are static readonly: a metric must be created ONCE per process.
    // Every request increments the same underlying series.
    private static readonly Counter RequestsTotal = Metrics.CreateCounter(
        "http_requests_total",
        "Total HTTP requests, labeled by method, route template, and status code.",
        new CounterConfiguration { LabelNames = new[] { "method", "endpoint", "status" } });

    private static readonly Histogram RequestDuration = Metrics.CreateHistogram(
        "http_request_duration_seconds",
        "HTTP request duration in seconds, labeled by method and route template.",
        new HistogramConfiguration
        {
            LabelNames = new[] { "method", "endpoint" },
            // 10 exponential buckets: 5ms, 10ms, 20ms, 40ms, ... ~2.56s.
            // Chosen to bracket this API's 10–300ms latency range — percentile
            // accuracy depends entirely on bucket boundaries (see README).
            Buckets = Histogram.ExponentialBuckets(0.005, 2, 10),
        });

    public RedMetricsMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        await _next(context); // run the rest of the pipeline (routing + handler)
        sw.Stop();

        var path = context.Request.Path.Value ?? "/";

        // Skip observability plumbing: scraping /metrics every 5s and health
        // probes would otherwise dominate the request rate panels.
        if (path == "/metrics" || path == "/health")
            return;

        // CARDINALITY LESSON — label by the ROUTE TEMPLATE, never the raw path.
        // Routing has already run by the time _next returns, so the matched
        // endpoint is available on the HttpContext. For /api/products/42 this
        // yields "/api/products/{id}" — ONE time series for the whole id space.
        // Labeling with the raw path instead would mint a brand-new series per
        // distinct id (/api/products/1, /api/products/2, ...) and blow up
        // Prometheus memory. Fall back to the raw path only when no route
        // matched (typically 404s on unknown paths).
        var endpoint = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? path;
        if (!endpoint.StartsWith('/'))
            endpoint = "/" + endpoint;

        var method = context.Request.Method;
        var status = context.Response.StatusCode.ToString();

        RequestsTotal.WithLabels(method, endpoint, status).Inc();
        RequestDuration.WithLabels(method, endpoint).Observe(sw.Elapsed.TotalSeconds);
    }
}
