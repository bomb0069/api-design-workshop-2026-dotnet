using System.Diagnostics;

namespace StructuredLogging.Middleware;

// Writes exactly ONE structured log line per request, on the way OUT of the
// pipeline -- when the status code and the total latency are known.
//
// The named placeholders ({method}, {path}, ...) become individual JSON
// fields in the log output, so an aggregator can query them directly
// (e.g. "status >= 500 AND latency_ms > 1000") instead of regexing text.
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // --- way IN: start the clock ---
        var stopwatch = Stopwatch.StartNew();

        await _next(context); // run the rest of the pipeline (routing, handler...)

        // --- way OUT: the response is decided, log the summary ---
        stopwatch.Stop();

        _logger.LogInformation(
            "{method} {path} responded {status} in {latency_ms} ms ({user_agent})",
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Response.StatusCode,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            context.Request.Headers.UserAgent.ToString());
    }
}
