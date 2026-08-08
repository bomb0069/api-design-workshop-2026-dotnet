using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProductsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var products = await _db.Products.OrderBy(p => p.Id).ToListAsync();
        return Ok(products.Select(p => new
        {
            p.Id, p.Name, p.Price,
            category = p.Category,        // deprecated but still returned
            p.Description,
            tags = p.Tags ?? Array.Empty<string>(),
            p.Sku,
            categories = new[] { p.Category }  // new field
        }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound(new { error = "Product not found" });
        return Ok(new
        {
            product.Id, product.Name, product.Price,
            category = product.Category,
            product.Description,
            tags = product.Tags ?? Array.Empty<string>(),
            product.Sku,
            categories = new[] { product.Category }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        if (string.IsNullOrEmpty(request.Name))
            return BadRequest(new { error = "Name is required" });

        var category = request.Category ?? "";
        if (request.Categories != null && request.Categories.Length > 0)
            category = request.Categories[0];

        var product = new Product
        {
            Name = request.Name,
            Price = request.Price,
            Category = category,
            Description = request.Description ?? "",
            Tags = request.Tags ?? Array.Empty<string>(),
            Sku = request.Sku ?? "N/A"
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return StatusCode(201, new
        {
            product.Id, product.Name, product.Price,
            category = product.Category,
            product.Description,
            tags = product.Tags,
            product.Sku,
            categories = new[] { product.Category }
        });
    }
}

public class CreateProductRequest
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string[]? Tags { get; set; }
    public string? Sku { get; set; }
    public string[]? Categories { get; set; }
}
