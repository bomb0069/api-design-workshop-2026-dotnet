using Grpc.Core;
using ProductGrpc;

namespace Server.Services;

public class ProductServiceImpl : ProductService.ProductServiceBase
{
    // In-memory store shared across all requests (the service is registered
    // per-request by default, so state lives in static fields — the .NET
    // equivalent of the Go server struct with a sync.RWMutex).
    private static readonly object Lock = new();
    private static readonly Dictionary<int, Product> Products = new()
    {
        [1] = new Product { Id = 1, Name = "Laptop", Price = 999.99, Category = "electronics" },
        [2] = new Product { Id = 2, Name = "Go Book", Price = 39.99, Category = "books" },
        [3] = new Product { Id = 3, Name = "T-Shirt", Price = 19.99, Category = "clothing" },
    };
    private static int _nextId = 4;

    public override Task<ListProductsResponse> ListProducts(ListProductsRequest request, ServerCallContext context)
    {
        var response = new ListProductsResponse();
        lock (Lock)
        {
            response.Products.AddRange(Products.Values);
        }
        return Task.FromResult(response);
    }

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
        if (string.IsNullOrEmpty(request.Name))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "name is required"));
        }

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

    public override Task<Product> UpdateProduct(UpdateProductRequest request, ServerCallContext context)
    {
        lock (Lock)
        {
            if (!Products.TryGetValue(request.Id, out var product))
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"product {request.Id} not found"));
            }
            if (!string.IsNullOrEmpty(request.Name))
            {
                product.Name = request.Name;
            }
            if (request.Price > 0)
            {
                product.Price = request.Price;
            }
            if (!string.IsNullOrEmpty(request.Category))
            {
                product.Category = request.Category;
            }
            return Task.FromResult(product);
        }
    }

    public override Task<DeleteProductResponse> DeleteProduct(DeleteProductRequest request, ServerCallContext context)
    {
        lock (Lock)
        {
            if (!Products.Remove(request.Id))
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"product {request.Id} not found"));
            }
            return Task.FromResult(new DeleteProductResponse { Success = true });
        }
    }
}
