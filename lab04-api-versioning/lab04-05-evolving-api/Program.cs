using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=workshop;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    if (!db.Products.Any())
    {
        db.Products.AddRange(
            new Product { Name = "Laptop", Price = 999.99m, Category = "electronics", Description = "A powerful laptop for developers", Tags = new[] { "portable", "computing" }, Sku = "ELEC-001" },
            new Product { Name = "Go Book", Price = 39.99m, Category = "books", Description = "Learn Go programming", Tags = new[] { "programming", "education" }, Sku = "BOOK-001" },
            new Product { Name = "T-Shirt", Price = 19.99m, Category = "clothing", Description = "Comfortable cotton t-shirt", Tags = new[] { "casual", "cotton" }, Sku = "CLTH-001" }
        );
        db.SaveChanges();
    }
}

// Deprecation headers for deprecated fields
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Deprecated-Fields", "category");
    context.Response.Headers.Append("X-Deprecated-Message", "The 'category' field is deprecated. Use 'categories' array instead.");
    context.Response.Headers.Append("X-API-Sunset", "2026-12-31");
    await next();
});

app.MapControllers();
app.Run("http://0.0.0.0:8080");
