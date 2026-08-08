using Prometheus;
using Asp.Versioning;
using System.Diagnostics;

public class PrometheusVersionMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly Counter RequestCounter = Metrics.CreateCounter(
        "api_requests_total",
        "Total API requests",
        new CounterConfiguration { LabelNames = new[] { "version", "endpoint", "method", "status" } });

    private static readonly Histogram RequestDuration = Metrics.CreateHistogram(
        "api_request_duration_seconds",
        "Request duration in seconds",
        new HistogramConfiguration { LabelNames = new[] { "version", "endpoint", "method" } });

    public PrometheusVersionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        // Count ONLY versioned API requests, labeled "v1"/"v2" — matching the
        // Go edition (which attaches this middleware inside /api/v1 and
        // /api/v2 only) and the Grafana dashboard's version="v1|v2" queries.
        // Unversioned paths (/metrics, /health, /api/lifecycle) are skipped so
        // they don't pollute the traffic-share denominators.
        var requested = context.GetRequestedApiVersion();
        if (requested is null)
            return;

        var version = $"v{requested.MajorVersion}";
        var endpoint = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;
        var status = context.Response.StatusCode.ToString();

        RequestCounter.WithLabels(version, endpoint, method, status).Inc();
        RequestDuration.WithLabels(version, endpoint, method).Observe(sw.Elapsed.TotalSeconds);
    }
}
