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

        var version = context.GetRequestedApiVersion()?.ToString() ?? "unknown";
        var endpoint = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;
        var status = context.Response.StatusCode.ToString();

        RequestCounter.WithLabels(version, endpoint, method, status).Inc();
        RequestDuration.WithLabels(version, endpoint, method).Observe(sw.Elapsed.TotalSeconds);
    }
}
