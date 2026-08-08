using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;

namespace ApiVersioning.Controllers;

[ApiController]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProductsController(AppDbContext db) => _db = db;

    [HttpGet, MapToApiVersion("1.0")]
    public async Task<IActionResult> ListV1()
    {
        var products = await _db.Products.OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.Price, p.Category }).ToListAsync();
        return Ok(products);
    }

    [HttpGet, MapToApiVersion("2.0")]
    public async Task<IActionResult> ListV2()
    {
        var products = await _db.Products.OrderBy(p => p.Id).ToListAsync();
        return Ok(new { data = products.Select(p => new { p.Id, p.Name, p.Price, p.Category, p.Description, tags = p.Tags ?? Array.Empty<string>() }), version = "2.0" });
    }

    [HttpGet("{id}"), MapToApiVersion("1.0")]
    public async Task<IActionResult> GetV1(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound(new { error = "Product not found" });
        return Ok(new { product.Id, product.Name, product.Price, product.Category });
    }

    [HttpGet("{id}"), MapToApiVersion("2.0")]
    public async Task<IActionResult> GetV2(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound(new { error = "Product not found" });
        return Ok(new { data = new { product.Id, product.Name, product.Price, product.Category, product.Description, tags = product.Tags ?? Array.Empty<string>() }, version = "2.0" });
    }

    [HttpPost, MapToApiVersion("1.0")]
    public async Task<IActionResult> CreateV1([FromBody] CreateProductRequest request)
    {
        if (string.IsNullOrEmpty(request.Name)) return BadRequest(new { error = "Name is required" });
        var product = new Product { Name = request.Name, Price = request.Price, Category = request.Category ?? "" };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return StatusCode(201, new { product.Id, product.Name, product.Price, product.Category });
    }

    [HttpPost, MapToApiVersion("2.0")]
    public async Task<IActionResult> CreateV2([FromBody] CreateProductRequest request)
    {
        if (string.IsNullOrEmpty(request.Name)) return BadRequest(new { error = "Name is required" });
        var product = new Product { Name = request.Name, Price = request.Price, Category = request.Category ?? "", Description = request.Description ?? "", Tags = request.Tags ?? Array.Empty<string>() };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return StatusCode(201, new { data = new { product.Id, product.Name, product.Price, product.Category, product.Description, tags = product.Tags }, version = "2.0" });
    }
}

public class CreateProductRequest
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string[]? Tags { get; set; }
}
