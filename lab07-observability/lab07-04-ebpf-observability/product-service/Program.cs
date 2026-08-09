// Product Service — a PLAIN .NET 8 minimal API.
//
// Note what is NOT here: no OpenTelemetry packages, no tracing middleware,
// no metrics code. Every trace and metric you will see in Jaeger and
// Prometheus for this service is produced by Grafana Beyla watching this
// process from the kernel via eBPF.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var products = new List<Product>
{
    new(Id: 1, Name: "Laptop", Price: 45000, Stock: 10),
    new(Id: 2, Name: "Mouse", Price: 590, Stock: 100),
    new(Id: 3, Name: "Keyboard", Price: 1290, Stock: 50),
};

app.MapGet("/api/products", async () =>
{
    // Simulate a database lookup with a small random delay.
    await Task.Delay(Random.Shared.Next(10, 50));
    return Results.Json(products);
});

app.MapGet("/api/products/{id:int}", async (int id) =>
{
    // Simulate a database lookup with a small random delay.
    await Task.Delay(Random.Shared.Next(10, 50));

    var product = products.FirstOrDefault(p => p.Id == id);
    return product is null
        ? Results.NotFound(new { error = "product not found" })
        : Results.Json(product);
});

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

Console.WriteLine("Product service starting on :8080");
app.Run("http://0.0.0.0:8080");

public record Product(int Id, string Name, decimal Price, int Stock);
