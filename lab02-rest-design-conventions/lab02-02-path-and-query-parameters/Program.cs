using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");

var app = builder.Build();

var items = new List<Item>
{
    new(1, "Laptop", 999.99),
    new(2, "Mouse", 29.99),
    new(3, "Keyboard", 79.99),
    new(4, "Monitor", 549.99),
};

app.MapGet("/items", () => Results.Json(items));

app.MapGet("/items/{id}", (string id) =>
{
    if (!int.TryParse(id, out var itemId))
    {
        return Results.Json(new { error = "Invalid ID format" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var item = items.FirstOrDefault(i => i.Id == itemId);
    if (item is null)
    {
        return Results.Json(new { error = "Item not found" }, statusCode: StatusCodes.Status404NotFound);
    }

    return Results.Json(item);
});

app.Logger.LogInformation("Server starting on :8080");
app.Run();

record Item(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("price")] double Price);
