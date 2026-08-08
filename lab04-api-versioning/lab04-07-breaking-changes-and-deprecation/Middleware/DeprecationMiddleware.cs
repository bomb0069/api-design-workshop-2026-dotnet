using Asp.Versioning;

public class DeprecationMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly Dictionary<string, (string Deprecation, string Sunset)> Schedule = new()
    {
        ["1.0"] = ("Sat, 01 Mar 2026 00:00:00 GMT", "Tue, 01 Sep 2026 00:00:00 GMT")
    };

    public DeprecationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        var version = context.GetRequestedApiVersion()?.ToString();
        if (version != null && Schedule.TryGetValue(version, out var schedule))
        {
            context.Response.Headers.Append("Deprecation", schedule.Deprecation);
            context.Response.Headers.Append("Sunset", schedule.Sunset);
            context.Response.Headers.Append("Link", "</docs/migrate-v1-v2>; rel=\"successor-version\"");
        }
    }
}
