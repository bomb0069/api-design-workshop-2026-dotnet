using System.Text.Json.Serialization;

// The "unreliable dependency" in this lab. A product catalog that you can
// break on purpose: POST /admin/mode/{ok|fail|slow} flips its behavior at
// runtime so you can watch the api's circuit breaker react.

var products = new List<Product>
{
    new(1, "Laptop", 35000m),
    new(2, "Mouse", 590m),
    new(3, "Keyboard", 1290m),
    new(4, "Monitor", 7900m),
};

// ok   -> respond normally
// fail -> every request returns 500 (a crashed/buggy dependency)
// slow -> every request takes 5 s (a saturated dependency; the api's 2 s
//         HttpClient timeout turns this into a failure on the caller's side)
var mode = "ok";

var app = WebApplication.CreateBuilder(args).Build();

app.Use(async (context, next) =>
{
    await next();
    app.Logger.LogInformation("[downstream] {Method} {Path} -> {Status} (mode={Mode})",
        context.Request.Method, context.Request.Path, context.Response.StatusCode, mode);
});

app.MapGet("/products", async () =>
{
    switch (mode)
    {
        case "fail":
            return Results.Json(new { error = "internal error (simulated)" }, statusCode: 500);
        case "slow":
            await Task.Delay(TimeSpan.FromSeconds(5));
            break;
    }
    return Results.Json(products);
});

// Health degrades together with /products so the api's deep readiness
// check (/health/ready) reflects reality instead of always saying "ok".
app.MapGet("/health", async () =>
{
    switch (mode)
    {
        case "fail":
            return Results.Json(new { status = "failing", service = "downstream" }, statusCode: 503);
        case "slow":
            await Task.Delay(TimeSpan.FromSeconds(5));
            break;
    }
    return Results.Json(new { status = "ok", service = "downstream" });
});

app.MapPost("/admin/mode/{newMode}", (string newMode) =>
{
    if (newMode is not ("ok" or "fail" or "slow"))
        return Results.Json(new { error = "mode must be one of: ok, fail, slow" }, statusCode: 400);
    mode = newMode;
    app.Logger.LogInformation("[downstream] mode changed to '{Mode}'", newMode);
    return Results.Json(new { mode });
});

app.MapGet("/admin/mode", () => Results.Json(new { mode }));

app.Run("http://0.0.0.0:8081");

internal record Product(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("price")] decimal Price);
