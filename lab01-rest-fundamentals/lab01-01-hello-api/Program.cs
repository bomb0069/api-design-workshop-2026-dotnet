var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Like the Go version's http.HandleFunc("/", ...) on the default mux,
// MapFallback matches every path and every HTTP method.
app.MapFallback(() => Results.Json(new { message = "Hello, World!" }));

Console.WriteLine("Server starting on :8080");
app.Run("http://0.0.0.0:8080");
