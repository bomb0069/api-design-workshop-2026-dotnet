using System.Text.Json.Serialization;

// A plain backend service. Notice what is NOT here: no API keys, no rate
// limiting, no CORS. Those concerns live in the gateway; this service only
// runs on the internal Docker network and trusts gateway-injected headers.

var users = new List<User>
{
    new(1, "john", "john@example.com"),
    new(2, "jane", "jane@example.com"),
    new(3, "somchai", "somchai@example.com"),
};

var app = WebApplication.CreateBuilder(args).Build();

app.Use(async (context, next) =>
{
    await next();
    app.Logger.LogInformation("[users-service] {Method} {Path} -> {Status} client={Client} rid={RequestId}",
        context.Request.Method, context.Request.Path, context.Response.StatusCode,
        context.Request.Headers["X-Consumer-Username"].FirstOrDefault() ?? "-",
        context.Request.Headers["X-Request-Id"].FirstOrDefault() ?? "-");
});

app.MapGet("/users", () => Results.Json(users));

app.MapGet("/users/{id}", (string id) =>
{
    if (!int.TryParse(id, out var userId))
        return Results.Json(new { error = "Invalid ID" }, statusCode: 400);
    var user = users.FirstOrDefault(u => u.Id == userId);
    return user is null
        ? Results.Json(new { error = "User not found" }, statusCode: 404)
        : Results.Json(user);
});

// Echoes the request headers this service received. Call it through the
// gateway (/api/users/headers) to see X-Consumer-Username / X-Request-Id injected
// — and X-Api-Key gone. The literal segment outranks the /users/{id} route.
app.MapGet("/users/headers", (HttpRequest request) =>
    Results.Json(request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())));

app.MapGet("/health", () => Results.Json(new { status = "ok", service = "users-service" }));

app.Run("http://0.0.0.0:8081");

internal record User(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email);
