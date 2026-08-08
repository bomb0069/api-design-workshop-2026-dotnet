using Grpc.Core;
using ProductGrpc;

namespace Server.Services;

public class ProductServiceImpl : ProductService.ProductServiceBase
{
    // In-memory store shared across all requests — the .NET equivalent of the
    // Go server struct with a sync.RWMutex-protected map.
    private static readonly object Lock = new();
    private static readonly Dictionary<int, Product> Products = new()
    {
        [1] = new Product { Id = 1, Name = "Laptop", Price = 999.99, Category = "electronics" },
        [2] = new Product { Id = 2, Name = "Go Book", Price = 39.99, Category = "books" },
        [3] = new Product { Id = 3, Name = "T-Shirt", Price = 19.99, Category = "clothing" },
    };
    private static int _nextId = 4;

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

    public override Task<Product> CreateProduct(CreateProductRequest request, ServerCallContext context)
    {
        lock (Lock)
        {
            var product = new Product
            {
                Id = _nextId,
                Name = request.Name,
                Price = request.Price,
                Category = request.Category,
            };
            Products[product.Id] = product;
            _nextId++;
            return Task.FromResult(product);
        }
    }

    // Server-side streaming: sends products one by one
    public override async Task ListProducts(
        ListProductsRequest request,
        IServerStreamWriter<Product> responseStream,
        ServerCallContext context)
    {
        List<Product> snapshot;
        lock (Lock)
        {
            snapshot = Products.Values.ToList();
        }

        foreach (var p in snapshot)
        {
            if (!string.IsNullOrEmpty(request.Category) && p.Category != request.Category)
            {
                continue;
            }
            await Task.Delay(500, context.CancellationToken); // Simulate delay
            await responseStream.WriteAsync(p);
            Console.WriteLine($"Streamed product: {p.Name}");
        }
    }

    // Client-side streaming: receives multiple products from client
    public override async Task<BatchCreateResponse> BatchCreateProducts(
        IAsyncStreamReader<CreateProductRequest> requestStream,
        ServerCallContext context)
    {
        var created = new List<Product>();

        // ReadAllAsync ends when the client completes its stream (io.EOF in Go)
        await foreach (var req in requestStream.ReadAllAsync(context.CancellationToken))
        {
            Product product;
            lock (Lock)
            {
                product = new Product
                {
                    Id = _nextId,
                    Name = req.Name,
                    Price = req.Price,
                    Category = req.Category,
                };
                Products[product.Id] = product;
                _nextId++;
            }

            created.Add(product);
            Console.WriteLine($"Batch created: {product.Name}");
        }

        // The single response sent after the client closes (SendAndClose in Go)
        var response = new BatchCreateResponse { Count = created.Count };
        response.Products.AddRange(created);
        return response;
    }

    // Bidirectional streaming: receive search queries, send matching products
    public override async Task ProductChat(
        IAsyncStreamReader<ProductQuery> requestStream,
        IServerStreamWriter<Product> responseStream,
        ServerCallContext context)
    {
        await foreach (var query in requestStream.ReadAllAsync(context.CancellationToken))
        {
            List<Product> matches;
            lock (Lock)
            {
                matches = Products.Values
                    .Where(p => p.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                             || p.Category.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            foreach (var p in matches)
            {
                await responseStream.WriteAsync(p);
            }
        }
    }
}
