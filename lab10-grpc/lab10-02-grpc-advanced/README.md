# Lab 10-02 - gRPC Advanced: Streaming and REST Gateway (.NET)

## Learning Objectives

- Implement all four gRPC communication patterns (unary, server streaming, client streaming, bidirectional streaming) in .NET 8
- Build a REST-to-gRPC gateway that exposes gRPC services as REST endpoints
- Understand streaming use cases and lifecycle management

## Architecture

```
                    +-----------+
                    |  gRPCUI   |
                    |  :8081    |
                    +-----+-----+
                          |
+--------+          +-----v-----+          +---------+
| Client  +--------->  gRPC     <----------+ Gateway |
| (CLI)   |  gRPC   |  Server   |   gRPC   | :8080   |
+--------+          |  :50051   |          +----^----+
                    +-----------+               |
                                           REST | HTTP
                                           curl/browser
```

| Service   | Port  | Protocol | Description                          |
|-----------|-------|----------|--------------------------------------|
| server    | 50051 | gRPC     | ASP.NET Core product service with streaming |
| client    | -     | gRPC     | .NET console client demonstrating streams   |
| gateway   | 8080  | HTTP     | ASP.NET Core REST-to-gRPC proxy      |
| grpcui    | 8081  | HTTP     | Web UI for gRPC testing              |

## Getting Started

```bash
docker-compose up --build
```

### Running locally without Docker

```bash
# Terminal 1
dotnet run --project server

# Terminal 2
GRPC_SERVER=http://localhost:50051 dotnet run --project gateway

# Terminal 3
GRPC_SERVER=http://localhost:50051 dotnet run --project client
```

## Streaming Patterns Explained

### 1. Unary RPC (single request, single response)

Standard request-response, like a regular function call.

```
Client --[Request]--> Server
Client <--[Response]-- Server
```

**RPCs:** `GetProduct`, `CreateProduct`

### 2. Server-Side Streaming (single request, multiple responses)

Client sends one request, server streams back multiple responses. Useful for listing data, real-time feeds, or large result sets.

```
Client --[Request]-----> Server
Client <--[Response 1]-- Server
Client <--[Response 2]-- Server
Client <--[Response N]-- Server
Client <--[EOF]--------- Server
```

**RPC:** `ListProducts` - client requests products (optionally filtered by category), server streams them one by one.

### 3. Client-Side Streaming (multiple requests, single response)

Client sends multiple messages, server processes them and returns a single response. Useful for batch uploads, aggregation, or file uploads.

```
Client --[Request 1]--> Server
Client --[Request 2]--> Server
Client --[Request N]--> Server
Client --[EOF]--------> Server
Client <--[Response]--- Server
```

**RPC:** `BatchCreateProducts` - client streams multiple product creation requests, server responds with the total count and created products.

### 4. Bidirectional Streaming (multiple requests AND responses)

Both client and server send streams of messages independently. Useful for chat, real-time collaboration, or interactive queries.

```
Client --[Request 1]---> Server
Client <--[Response 1]-- Server
Client --[Request 2]---> Server
Client <--[Response 2]-- Server
Client <--[Response 3]-- Server
Client --[EOF]---------> Server
Client <--[EOF]--------- Server
```

**RPC:** `ProductChat` - client sends search queries, server responds with matching products for each query.

## Test the Gateway

The REST gateway translates HTTP requests into gRPC calls.

### List all products

```bash
curl http://localhost:8080/api/products
```

### Filter by category

```bash
curl http://localhost:8080/api/products?category=electronics
```

### Get a single product

```bash
curl http://localhost:8080/api/products/1
```

### Create a product

```bash
curl -X POST http://localhost:8080/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Headphones", "price": 149.99, "category": "electronics"}'
```

## View Client Output

The client container runs all streaming demos automatically:

```bash
docker-compose logs client
```

Expected output:
```
=== Server-Side Streaming: ListProducts ===
  Received: [1] Laptop - $999.99
  Received: [2] Go Book - $39.99
  Received: [3] T-Shirt - $19.99

=== Client-Side Streaming: BatchCreateProducts ===
  Sending: Mouse
  Sending: Keyboard
  Sending: Monitor
  Batch created 3 products

=== Bidirectional Streaming: ProductChat ===
  Searching: electronics
  Searching: book
  Searching: shirt
  Found: [1] Laptop - $999.99 (electronics)
  Found: [2] Go Book - $39.99 (books)
  Found: [3] T-Shirt - $19.99 (clothing)

Done!
```

## Test with gRPCUI

Open [http://localhost:8081](http://localhost:8081) in your browser. gRPCUI provides a web interface to call any gRPC method, including streaming RPCs.

## Code Walkthrough

### Server-Side Streaming (server)

```csharp
public override async Task ListProducts(
    ListProductsRequest request,
    IServerStreamWriter<Product> responseStream,
    ServerCallContext context)
{
    foreach (var p in products)
    {
        await responseStream.WriteAsync(p);  // Send each product individually
    }
    // Returning from the method closes the stream
}
```

### Server-Side Streaming (client)

```csharp
using var call = client.ListProducts(new ListProductsRequest());
await foreach (var product in call.ResponseStream.ReadAllAsync())
{
    // Process product... loop ends when the server closes the stream
}
```

### Client-Side Streaming (server)

```csharp
public override async Task<BatchCreateResponse> BatchCreateProducts(
    IAsyncStreamReader<CreateProductRequest> requestStream,
    ServerCallContext context)
{
    await foreach (var req in requestStream.ReadAllAsync())
    {
        // Process each request... loop ends when the client completes
    }
    return response;  // The single final response (SendAndClose in Go)
}
```

### Client-Side Streaming (client)

```csharp
using var call = client.BatchCreateProducts();
foreach (var p in products)
{
    await call.RequestStream.WriteAsync(p);   // Send each product
}
await call.RequestStream.CompleteAsync();     // Close the send side
var resp = await call;                        // Await the final response
```

### Bidirectional Streaming (server)

```csharp
public override async Task ProductChat(
    IAsyncStreamReader<ProductQuery> requestStream,
    IServerStreamWriter<Product> responseStream,
    ServerCallContext context)
{
    await foreach (var query in requestStream.ReadAllAsync())  // Receive query
    {
        await responseStream.WriteAsync(matchingProduct);      // Send matches back
    }
}
```

### Bidirectional Streaming (client)

```csharp
using var call = client.ProductChat();
// Send queries
await call.RequestStream.WriteAsync(new ProductQuery { Search = "electronics" });
await call.RequestStream.CompleteAsync();  // Done sending
// Receive results
await foreach (var product in call.ResponseStream.ReadAllAsync())
{
    // ...
}
```

### REST-to-gRPC Gateway

The gateway is a minimal API app that holds a gRPC client and translates each HTTP route into an RPC:

```csharp
var channel = GrpcChannel.ForAddress("http://server:50051");
var client = new ProductService.ProductServiceClient(channel);

app.MapGet("/api/products", async (string? category) =>
{
    using var call = client.ListProducts(new ListProductsRequest { Category = category ?? "" });
    var products = new List<object>();
    await foreach (var p in call.ResponseStream.ReadAllAsync())
    {
        products.Add(...);
    }
    return Results.Json(products);  // Stream collected into a JSON array
});
```

## Exercises

1. **Real-Time Price Updates** - Add a server-side streaming RPC `WatchPriceUpdates` that streams price changes in real-time. Simulate random price fluctuations every second.

2. **gRPC-to-HTTP Status Mapping** - Improve the gateway error handling to map gRPC status codes to appropriate HTTP status codes (e.g., `StatusCode.NotFound` to `404`, `StatusCode.InvalidArgument` to `400`) by inspecting `RpcException.StatusCode`.

3. **Logging Interceptor** - Add a gRPC server `Interceptor` that overrides both `UnaryServerHandler` and the streaming handlers to log the method name, duration, and status code for every RPC call.

4. **Deadline Propagation** - Add timeout/deadline support in the gateway so that REST requests with a `?timeout=5` query parameter propagate deadlines to the gRPC server (pass `deadline: DateTime.UtcNow.AddSeconds(n)` to the call).

## Key Concepts

| Concept                        | Description                                                    |
|--------------------------------|----------------------------------------------------------------|
| **Server Streaming**           | Server sends multiple messages; client reads until the stream ends |
| **Client Streaming**           | Client sends multiple messages; server reads until the stream ends |
| **Bidi Streaming**             | Both sides send/receive independently                          |
| **WriteAsync(msg)**            | Send a message on the stream                                   |
| **ReadAllAsync()**             | Async-enumerate incoming messages until the stream ends (Go's `Recv` + `io.EOF` loop) |
| **RequestStream.CompleteAsync()** | Client signals it is done sending (Go's `CloseSend`)        |
| **Returning the response**     | Server's single response after a client stream (Go's `SendAndClose`) |
| **await call**                 | Client awaits the final response (Go's `CloseAndRecv`)         |
| **REST-to-gRPC Gateway**       | HTTP server that translates REST calls into gRPC calls         |
| **gRPC Reflection**            | Allows tools like gRPCUI to discover services at runtime       |

## Cleanup

```bash
docker-compose down
```
