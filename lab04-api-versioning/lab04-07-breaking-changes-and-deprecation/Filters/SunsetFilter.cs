using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Asp.Versioning;

public class SunsetFilter : IActionFilter
{
    private static readonly Dictionary<string, DateTime> Sunsets = new()
    {
        { "1.0", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) }
    };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var version = context.HttpContext.GetRequestedApiVersion()?.ToString();
        if (version != null && Sunsets.TryGetValue(version, out var sunset) && DateTime.UtcNow > sunset)
        {
            context.Result = new ObjectResult(new
            {
                error = "VERSION_SUNSET",
                message = $"API v{version} was sunset on {sunset:yyyy-MM-dd}",
                migrateUrl = "/docs/migrate-v1-v2"
            })
            { StatusCode = 410 };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
