using Microsoft.EntityFrameworkCore;
using Asp.Versioning;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddApiVersioning(opt =>
{
    opt.DefaultApiVersion = new ApiVersion(1, 0);
    opt.AssumeDefaultVersionWhenUnspecified = true;
    opt.ReportApiVersions = true;
    opt.ApiVersionReader = new HeaderApiVersionReader("X-Api-Version");
})
.AddApiExplorer(opt =>
{
    opt.GroupNameFormat = "'v'VVV";
});

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=workshop;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

// Seed database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    if (!db.Products.Any())
    {
        db.Products.AddRange(
            new Product { Name = "Laptop", Price = 999.99m, Category = "electronics", Description = "A powerful laptop for developers", Tags = new[] { "portable", "computing" } },
            new Product { Name = "Go Book", Price = 39.99m, Category = "books", Description = "Learn Go programming", Tags = new[] { "programming", "education" } },
            new Product { Name = "T-Shirt", Price = 19.99m, Category = "clothing", Description = "Comfortable cotton t-shirt", Tags = new[] { "casual", "cotton" } }
        );
        db.SaveChanges();
    }
}

// Add Vary header for caching
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Vary", "X-Api-Version");
    await next();
});

app.MapControllers();
app.Run("http://0.0.0.0:8080");
