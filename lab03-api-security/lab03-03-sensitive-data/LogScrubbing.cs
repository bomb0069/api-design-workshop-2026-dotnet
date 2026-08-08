// Log-scrubbing middleware: logs method/path/status plus a SANITIZED copy of
// the request body. Fields named password/token/card_number/citizen_id are
// replaced with "[REDACTED]" before anything reaches the log.
using System.Text.Json.Nodes;

public class LogScrubbingMiddleware
{
    private static readonly HashSet<string> SensitiveFields =
        new(StringComparer.OrdinalIgnoreCase) { "password", "token", "card_number", "citizen_id" };

    private readonly RequestDelegate _next;

    public LogScrubbingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var body = "";
        if (context.Request.ContentLength is > 0)
        {
            // Buffer the body so the handler can still read it after we do.
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        await _next(context);

        var line = $"[audit] {context.Request.Method} {context.Request.Path} -> {context.Response.StatusCode}";
        var sanitized = Sanitize(body);
        if (sanitized.Length > 0)
            line += $" body={sanitized}";
        Console.WriteLine(line);
    }

    private static string Sanitize(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";
        try
        {
            var node = JsonNode.Parse(body);
            Scrub(node);
            return node?.ToJsonString() ?? "";
        }
        catch (Exception)
        {
            // Never log a body we could not parse — we cannot prove it is safe.
            return "[unparseable body omitted]";
        }
    }

    private static void Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kvp => kvp.Key).ToList())
                {
                    if (SensitiveFields.Contains(key))
                        obj[key] = "[REDACTED]";
                    else
                        Scrub(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    Scrub(item);
                break;
        }
    }
}
