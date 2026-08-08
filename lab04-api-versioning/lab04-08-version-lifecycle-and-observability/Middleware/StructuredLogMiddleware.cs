using Asp.Versioning;
using System.Diagnostics;
using System.Text.Json;

public class StructuredLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<StructuredLogMiddleware> _logger;

    public StructuredLogMiddleware(RequestDelegate next, ILogger<StructuredLogMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        var version = context.GetRequestedApiVersion()?.ToString() ?? "unknown";
        var logEntry = new
        {
            ts = DateTime.UtcNow.ToString("o"),
            api_version = version,
            method = context.Request.Method,
            endpoint = context.Request.Path.Value,
            status = context.Response.StatusCode,
            latency_ms = sw.ElapsedMilliseconds,
            user_agent = context.Request.Headers.UserAgent.ToString()
        };

        _logger.LogInformation("{LogEntry}", JsonSerializer.Serialize(logEntry));
    }
}
