namespace StructuredLogging.Middleware;

// Reads X-Correlation-ID from the incoming request (or generates a new GUID),
// echoes it back on the response, and opens a logging scope so that EVERY log
// line written while this request is being handled carries "correlation_id".
//
// When service A calls service B and forwards the same header, the two
// services' logs can be stitched together by grepping for one ID.
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Reuse the caller's ID if they sent one; otherwise mint a new one.
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }

        // 2. Make it available to anything downstream that wants it
        //    (handlers, other middleware) without re-parsing headers.
        context.Items[ItemKey] = correlationId;

        // 3. Always echo it back, so the client can quote the ID when
        //    reporting a problem -- even if we generated it ourselves.
        context.Response.Headers[HeaderName] = correlationId;

        // 4. Wrap the REST of the pipeline in a logging scope. Combined with
        //    IncludeScopes = true on the JSON console, this stamps
        //    correlation_id onto every log line produced during this request.
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["correlation_id"] = correlationId
        }))
        {
            await _next(context);
        }
    }
}
