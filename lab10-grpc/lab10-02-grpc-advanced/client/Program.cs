using System.Globalization;
using Grpc.Core;
using Grpc.Net.Client;
using ProductGrpc;

// Match Go's fmt output for prices ($999.99, not $999,99)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

var serverAddress = Environment.GetEnvironmentVariable("GRPC_SERVER") ?? "http://server:50051";

// GrpcChannel with an http:// address uses plain-text HTTP/2 (h2c),
// the equivalent of Go's insecure.NewCredentials().
using var channel = GrpcChannel.ForAddress(serverAddress);
var client = new ProductService.ProductServiceClient(channel);

// 1. Server-side streaming
Console.WriteLine("=== Server-Side Streaming: ListProducts ===");
using (var call = client.ListProducts(new ListProductsRequest()))
{
    await foreach (var product in call.ResponseStream.ReadAllAsync())
    {
        Console.WriteLine($"  Received: [{product.Id}] {product.Name} - ${product.Price:F2}");
    }
}

// 2. Client-side streaming
Console.WriteLine("\n=== Client-Side Streaming: BatchCreateProducts ===");
using (var batchCall = client.BatchCreateProducts())
{
    var newProducts = new[]
    {
        new CreateProductRequest { Name = "Mouse", Price = 29.99, Category = "electronics" },
        new CreateProductRequest { Name = "Keyboard", Price = 79.99, Category = "electronics" },
        new CreateProductRequest { Name = "Monitor", Price = 449.99, Category = "electronics" },
    };
    foreach (var p in newProducts)
    {
        Console.WriteLine($"  Sending: {p.Name}");
        await batchCall.RequestStream.WriteAsync(p);
        await Task.Delay(300);
    }
    await batchCall.RequestStream.CompleteAsync(); // CloseAndRecv in Go
    var batchResp = await batchCall;
    Console.WriteLine($"  Batch created {batchResp.Count} products");
}

// 3. Bidirectional streaming
Console.WriteLine("\n=== Bidirectional Streaming: ProductChat ===");
using (var chatCall = client.ProductChat())
{
    var queries = new[] { "electronics", "book", "shirt" };
    foreach (var q in queries)
    {
        Console.WriteLine($"  Searching: {q}");
        await chatCall.RequestStream.WriteAsync(new ProductQuery { Search = q });

        // Small delay to receive responses
        await Task.Delay(500);
    }
    await chatCall.RequestStream.CompleteAsync(); // CloseSend in Go

    await foreach (var product in chatCall.ResponseStream.ReadAllAsync())
    {
        Console.WriteLine($"  Found: [{product.Id}] {product.Name} - ${product.Price:F2} ({product.Category})");
    }
}

Console.WriteLine("\nDone!");
