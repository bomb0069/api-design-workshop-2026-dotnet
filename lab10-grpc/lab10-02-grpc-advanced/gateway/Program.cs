using Grpc.Core;
using Grpc.Net.Client;
using ProductGrpc;

var builder = WebApplication.CreateBuilder(args);

// REST gateway listens on :8080 (plain HTTP/1.1 for curl/browser)
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(8080));

var serverAddress = Environment.GetEnvironmentVariable("GRPC_SERVER") ?? "http://server:50051";

// Single shared channel to the gRPC server (plain-text HTTP/2)
var channel = GrpcChannel.ForAddress(serverAddress);
var client = new ProductService.ProductServiceClient(channel);

var app = builder.Build();

// GET /api/products[?category=...] — consumes the server-side stream into a JSON array
app.MapGet("/api/products", async (string? category) =>
{
    using var call = client.ListProducts(new ListProductsRequest { Category = category ?? "" });
    var products = new List<object>();
    try
    {
        await foreach (var p in call.ResponseStream.ReadAllAsync())
        {
            products.Add(ToJson(p));
        }
    }
    catch (RpcException ex)
    {
        return Results.Json(new { error = ex.Status.Detail }, statusCode: 500);
    }
    return Results.Json(products);
});

// POST /api/products — unary CreateProduct
app.MapPost("/api/products", async (CreateProductInput input) =>
{
    try
    {
        var product = await client.CreateProductAsync(new CreateProductRequest
        {
            Name = input.Name ?? "",
            Price = input.Price,
            Category = input.Category ?? "",
        });
        return Results.Json(ToJson(product), statusCode: 201);
    }
    catch (RpcException ex)
    {
        return Results.Json(new { error = ex.Status.Detail }, statusCode: 400);
    }
});

// GET /api/products/{id} — unary GetProduct
app.MapGet("/api/products/{id}", async (string id) =>
{
    if (!int.TryParse(id, out var productId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: 400);
    }
    try
    {
        var product = await client.GetProductAsync(new GetProductRequest { Id = productId });
        return Results.Json(ToJson(product));
    }
    catch (RpcException)
    {
        return Results.Json(new { error = "Product not found" }, statusCode: 404);
    }
});

Console.WriteLine("REST-to-gRPC Gateway on :8080");
app.Run();

// Shape the JSON like the Go gateway (lowercase field names)
static object ToJson(Product p) => new { id = p.Id, name = p.Name, price = p.Price, category = p.Category };

record CreateProductInput(string? Name, double Price, string? Category);
