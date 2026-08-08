using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;

namespace ApiVersioning.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProductsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var products = await _db.Products.OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.Price, p.Category })
            .ToListAsync();
        return Ok(products);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound(new { error = "Product not found" });
        return Ok(new { product.Id, product.Name, product.Price, product.Category });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductV1Request request)
    {
        if (string.IsNullOrEmpty(request.Name))
            return BadRequest(new { error = "Name is required" });

        var product = new Product
        {
            Name = request.Name,
            Price = request.Price,
            Category = request.Category ?? ""
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return StatusCode(201, new { product.Id, product.Name, product.Price, product.Category });
    }
}

public class CreateProductV1Request
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string? Category { get; set; }
}
