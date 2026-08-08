using System.Globalization;
using Grpc.Net.Client;
using ProductGrpc;

// Match Go's fmt output for prices ($999.99, not $999,99)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

var serverAddress = Environment.GetEnvironmentVariable("GRPC_SERVER") ?? "http://server:50051";

// GrpcChannel with an http:// address uses plain-text HTTP/2 (h2c),
// the equivalent of Go's insecure.NewCredentials().
using var channel = GrpcChannel.ForAddress(serverAddress);
var client = new ProductService.ProductServiceClient(channel);

// One deadline for the whole run, like Go's 10s context
var deadline = DateTime.UtcNow.AddSeconds(10);

// List all products
Console.WriteLine("=== List Products ===");
var listResp = await client.ListProductsAsync(new ListProductsRequest(), deadline: deadline);
foreach (var p in listResp.Products)
{
    Console.WriteLine($"  [{p.Id}] {p.Name} - ${p.Price:F2} ({p.Category})");
}

// Create a product
Console.WriteLine("\n=== Create Product ===");
var created = await client.CreateProductAsync(new CreateProductRequest
{
    Name = "Headphones",
    Price = 79.99,
    Category = "electronics",
}, deadline: deadline);
Console.WriteLine($"  Created: [{created.Id}] {created.Name} - ${created.Price:F2}");

// Get single product
Console.WriteLine("\n=== Get Product ===");
var product = await client.GetProductAsync(new GetProductRequest { Id = 1 }, deadline: deadline);
Console.WriteLine($"  Got: [{product.Id}] {product.Name} - ${product.Price:F2} ({product.Category})");

// Update product
Console.WriteLine("\n=== Update Product ===");
var updated = await client.UpdateProductAsync(new UpdateProductRequest
{
    Id = 1,
    Price = 1099.99,
}, deadline: deadline);
Console.WriteLine($"  Updated: [{updated.Id}] {updated.Name} - ${updated.Price:F2}");

// Delete product
Console.WriteLine("\n=== Delete Product ===");
var delResp = await client.DeleteProductAsync(new DeleteProductRequest { Id = 3 }, deadline: deadline);
Console.WriteLine($"  Deleted: {delResp.Success.ToString().ToLowerInvariant()}"); // Go prints "true"

// List again
Console.WriteLine("\n=== List Products (after changes) ===");
listResp = await client.ListProductsAsync(new ListProductsRequest(), deadline: deadline);
foreach (var p in listResp.Products)
{
    Console.WriteLine($"  [{p.Id}] {p.Name} - ${p.Price:F2} ({p.Category})");
}
