using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;

namespace ApiVersioning.Controllers.V2;

[ApiController]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProductsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var products = await _db.Products.OrderBy(p => p.Id).ToListAsync();
        return Ok(new
        {
            data = products.Select(p => new
            {
                p.Id, p.Name, p.Price, p.Category, p.Description,
                tags = p.Tags ?? Array.Empty<string>()
            }),
            version = "2.0"
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound(new { error = "Product not found" });
        return Ok(new
        {
            data = new
            {
                product.Id, product.Name, product.Price, product.Category,
                product.Description, tags = product.Tags ?? Array.Empty<string>()
            },
            version = "2.0"
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductV2Request request)
    {
        if (string.IsNullOrEmpty(request.Name))
            return BadRequest(new { error = "Name is required" });

        var product = new Product
        {
            Name = request.Name,
            Price = request.Price,
            Category = request.Category ?? "",
            Description = request.Description ?? "",
            Tags = request.Tags ?? Array.Empty<string>()
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return StatusCode(201, new
        {
            data = new
            {
                product.Id, product.Name, product.Price, product.Category,
                product.Description, tags = product.Tags
            },
            version = "2.0"
        });
    }
}

public class CreateProductV2Request
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string[]? Tags { get; set; }
}
