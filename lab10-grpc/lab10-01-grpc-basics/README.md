# Lab 10-01 - gRPC Basics (.NET)

## Learning Objectives

- Define services and messages with Protocol Buffers
- Implement a gRPC server and client in .NET 8
- Understand protobuf serialization and code generation
- Use gRPCUI for interactive testing
- Compare gRPC with REST

## Architecture

```
┌──────────────────┐     gRPC (HTTP/2)     ┌──────────────────┐
│   gRPC Client    │ ───────────────────>   │   gRPC Server    │
│   (.NET console) │     protobuf binary    │   :50051         │
└──────────────────┘                        └──────────────────┘
                                                    ^
┌──────────────────┐     gRPC reflection            │
│   gRPCUI         │ ──────────────────────────────-┘
│   :8080          │
└──────────────────┘
```

- **Server** (:50051) - ASP.NET Core gRPC server implementing the ProductService
- **Client** (console) - .NET client that calls all five RPCs and prints results
- **gRPCUI** (:8080) - Web UI for interactively calling gRPC methods

## Prerequisites

- Docker and Docker Compose (or the .NET 8 SDK to run locally)

## Getting Started

Start all services:

```bash
docker-compose up --build
```

The client runs once, calls all RPCs, and exits. View its output:

```bash
docker-compose logs client
```

Open gRPCUI at [http://localhost:8080](http://localhost:8080) to interactively call methods.

### Running locally without Docker

```bash
# Terminal 1
dotnet run --project server

# Terminal 2 (point the client at localhost)
GRPC_SERVER=http://localhost:50051 dotnet run --project client
```

## Understanding the Proto File

The file `proto/product.proto` defines the entire API contract:

```protobuf
syntax = "proto3";                       // Use proto3 syntax
package product;                         // Protobuf package namespace
option csharp_namespace = "ProductGrpc"; // C# namespace for generated code
```

### Service Definition

The `service` block defines the RPC methods, similar to an interface:

```protobuf
service ProductService {
  rpc ListProducts(ListProductsRequest) returns (ListProductsResponse);
  rpc GetProduct(GetProductRequest) returns (Product);
  rpc CreateProduct(CreateProductRequest) returns (Product);
  rpc UpdateProduct(UpdateProductRequest) returns (Product);
  rpc DeleteProduct(DeleteProductRequest) returns (DeleteProductResponse);
}
```

Each RPC takes exactly one request message and returns exactly one response message (unary RPCs).

### Message Types

Messages define the data structures. Each field has a type, name, and unique field number:

```protobuf
message Product {
  int32 id = 1;        // Field number 1
  string name = 2;     // Field number 2
  double price = 3;    // Field number 3
  string category = 4; // Field number 4
}
```

Field numbers are used in the binary encoding -- they must never be changed once the schema is in use.

### Common Proto Types

| Proto Type | C# Type              | Description           |
|------------|----------------------|-----------------------|
| `int32`    | `int`                | Variable-length int   |
| `int64`    | `long`               | Variable-length int   |
| `double`   | `double`             | 64-bit floating point |
| `string`   | `string`             | UTF-8 string          |
| `bool`     | `bool`               | Boolean               |
| `repeated` | `RepeatedField<T>`   | List of values        |

## Code Walkthrough

### Proto Code Generation

Unlike Go (which runs `protoc` manually), .NET generates the C# code automatically at build time via the **Grpc.Tools** package. The `.csproj` references the proto file:

```xml
<ItemGroup>
  <Protobuf Include="../proto/product.proto" GrpcServices="Server" ProtoRoot="../proto" />
</ItemGroup>
```

- `GrpcServices="Server"` generates message types plus the `ProductServiceBase` server base class
- `GrpcServices="Client"` (in the client project) generates message types plus the `ProductServiceClient` stub

The generated code lands in `obj/` — you never edit or commit it.

### Server Implementation

The server subclasses the generated `ProductService.ProductServiceBase`. Unimplemented RPCs return `Unimplemented` by default (same forward-compatibility idea as Go's `UnimplementedProductServiceServer`):

```csharp
public class ProductServiceImpl : ProductService.ProductServiceBase
{
    private static readonly object Lock = new();
    private static readonly Dictionary<int, Product> Products = new() { ... };
    private static int _nextId = 4;
}
```

Each method overrides the corresponding RPC:

```csharp
public override Task<Product> GetProduct(GetProductRequest request, ServerCallContext context)
{
    lock (Lock)
    {
        if (!Products.TryGetValue(request.Id, out var product))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"product {request.Id} not found"));
        }
        return Task.FromResult(product);
    }
}
```

Register the service and enable reflection (required for gRPCUI). Kestrel must listen with **plain-text HTTP/2** to match the Go server:

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(50051, lo => lo.Protocols = HttpProtocols.Http2);
});
builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();

app.MapGrpcService<ProductServiceImpl>();
app.MapGrpcReflectionService();
```

### Client Implementation

The client creates a channel and a typed client stub. An `http://` address gives plain-text HTTP/2 — the equivalent of Go's `insecure.NewCredentials()`:

```csharp
using var channel = GrpcChannel.ForAddress("http://server:50051");
var client = new ProductService.ProductServiceClient(channel);
```

Then calls methods with full type safety:

```csharp
var product = await client.GetProductAsync(new GetProductRequest { Id = 1 });
```

### gRPC Status Codes

gRPC uses its own status codes instead of HTTP status codes:

| gRPC Code          | HTTP Equivalent | Meaning                    |
|--------------------|-----------------|----------------------------|
| `OK`               | 200             | Success                    |
| `NotFound`         | 404             | Resource not found         |
| `InvalidArgument`  | 400             | Bad request                |
| `AlreadyExists`    | 409             | Resource already exists    |
| `Unauthenticated`  | 401             | Missing/invalid auth       |
| `PermissionDenied` | 403             | Insufficient permissions   |
| `Internal`         | 500             | Server error               |
| `Unimplemented`    | 501             | RPC not implemented        |
| `Unavailable`      | 503             | Service unavailable        |

Return errors with:

```csharp
throw new RpcException(new Status(StatusCode.NotFound, $"product {request.Id} not found"));
```

## gRPC vs REST Comparison

| Aspect            | gRPC                          | REST                         |
|-------------------|-------------------------------|------------------------------|
| Protocol          | HTTP/2                        | HTTP/1.1 or HTTP/2           |
| Data Format       | Protocol Buffers (binary)     | JSON (text)                  |
| API Contract      | `.proto` file (strict)        | OpenAPI/informal (flexible)  |
| Code Generation   | Built-in (protoc/Grpc.Tools)  | Optional (openapi-generator) |
| Streaming         | Native (4 patterns)           | SSE, WebSocket (separate)    |
| Browser Support   | Requires gRPC-Web proxy       | Native                       |
| Type Safety       | Strong (generated types)      | Weak (JSON parsing)          |
| Performance       | Faster (binary, HTTP/2)       | Slower (text, HTTP/1.1)      |
| Tooling           | grpcurl, gRPCUI, Buf          | curl, Postman, Swagger       |
| Human Readable    | No (binary wire format)       | Yes (JSON)                   |
| Load Balancing    | Requires L7 / client-side     | Standard L4/L7               |

**When to use gRPC:** microservice-to-microservice communication, high-performance requirements, streaming, polyglot environments.

**When to use REST:** public APIs, browser clients, simple CRUD, broad ecosystem compatibility.

## Exercises

### Exercise 1: Add SearchProducts RPC

Add a new RPC that searches products by a query string:

```protobuf
message SearchProductsRequest {
  string query = 1;
}

rpc SearchProducts(SearchProductsRequest) returns (ListProductsResponse);
```

Implement it in the server to search by name or category (case-insensitive substring match). Update the client to test it.

### Exercise 2: Add Field Validation

Improve input validation in `CreateProduct`:

- Name must be non-empty and under 100 characters
- Price must be positive
- Category must be one of: "electronics", "books", "clothing", "food"

Return `StatusCode.InvalidArgument` with descriptive error messages for each violation.

### Exercise 3: Add Metadata (Headers)

gRPC supports metadata, similar to HTTP headers. Add a request ID:

**Server side** - read metadata:
```csharp
var requestId = context.RequestHeaders.GetValue("x-request-id");
if (requestId is not null)
{
    Console.WriteLine($"Request ID: {requestId}");
}
```

**Client side** - send metadata:
```csharp
var headers = new Metadata { { "x-request-id", "req-123" } };
var product = await client.GetProductAsync(new GetProductRequest { Id = 1 }, headers);
```

### Exercise 4: Add Interceptors

Interceptors are gRPC's middleware. Add a logging interceptor:

```csharp
public class LoggingInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        var response = await continuation(request, context);
        Console.WriteLine($"Method: {context.Method} | Duration: {sw.Elapsed}");
        return response;
    }
}

// Register it:
builder.Services.AddGrpc(options => options.Interceptors.Add<LoggingInterceptor>());
```

Add this to the server and observe the logs when the client makes calls.

## Key Concepts

| Concept                  | Description                                                       |
|--------------------------|-------------------------------------------------------------------|
| Protocol Buffers         | Language-neutral serialization format; defines messages and types  |
| gRPC Service Definition  | `.proto` service block defining RPCs with request/response types   |
| Unary RPC                | Single request, single response (like a function call)            |
| Code Generation          | Grpc.Tools generates typed client stubs and server base classes at build time |
| gRPC Status Codes        | Structured error codes (NotFound, InvalidArgument, etc.)          |
| Reflection               | Server exposes its schema at runtime for tools like gRPCUI        |
| Interceptors             | Middleware pattern for cross-cutting concerns (logging, auth)     |
| Metadata                 | Key-value pairs sent alongside RPCs (like HTTP headers)           |

## Cleanup

```bash
docker-compose down
```
