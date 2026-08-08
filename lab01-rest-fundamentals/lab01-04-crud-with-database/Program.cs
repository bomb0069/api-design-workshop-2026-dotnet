using System.Text.Json;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=workshop;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

// Ensure the todos table exists on startup (like createTable in the Go version).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS todos (
        id SERIAL PRIMARY KEY,
        title TEXT NOT NULL,
        completed BOOLEAN DEFAULT FALSE
    )");
}

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// GET /todos - list all todos
app.MapGet("/todos", async (AppDbContext db) =>
    Results.Json(await db.Todos.OrderBy(t => t.Id).ToListAsync()));

// POST /todos - create a todo
app.MapPost("/todos", async (HttpRequest request, AppDbContext db) =>
{
    CreateTodoInput? input;
    try
    {
        input = await JsonSerializer.DeserializeAsync<CreateTodoInput>(request.Body, jsonOptions);
    }
    catch (JsonException)
    {
        return Results.Json(new { error = "Invalid request body" }, statusCode: 400);
    }

    if (string.IsNullOrEmpty(input?.Title))
    {
        return Results.Json(new { error = "Title is required" }, statusCode: 400);
    }

    var todo = new Todo { Title = input.Title, Completed = false };
    db.Todos.Add(todo);
    await db.SaveChangesAsync();

    return Results.Json(todo, statusCode: 201);
});

// GET /todos/{id} - get a single todo
app.MapGet("/todos/{id}", async (string id, AppDbContext db) =>
{
    if (!int.TryParse(id, out var todoId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: 400);
    }

    var todo = await db.Todos.FindAsync(todoId);
    if (todo is null)
    {
        return Results.Json(new { error = "Todo not found" }, statusCode: 404);
    }

    return Results.Json(todo);
});

// PUT /todos/{id} - update a todo (partial update: only provided fields change)
app.MapPut("/todos/{id}", async (string id, HttpRequest request, AppDbContext db) =>
{
    if (!int.TryParse(id, out var todoId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: 400);
    }

    UpdateTodoInput? input;
    try
    {
        input = await JsonSerializer.DeserializeAsync<UpdateTodoInput>(request.Body, jsonOptions);
    }
    catch (JsonException)
    {
        return Results.Json(new { error = "Invalid request body" }, statusCode: 400);
    }

    var todo = await db.Todos.FindAsync(todoId);
    if (todo is null)
    {
        return Results.Json(new { error = "Todo not found" }, statusCode: 404);
    }

    if (input?.Title is not null)
    {
        todo.Title = input.Title;
    }
    if (input?.Completed is not null)
    {
        todo.Completed = input.Completed.Value;
    }
    await db.SaveChangesAsync();

    return Results.Json(todo);
});

// DELETE /todos/{id} - delete a todo
app.MapDelete("/todos/{id}", async (string id, AppDbContext db) =>
{
    if (!int.TryParse(id, out var todoId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: 400);
    }

    var rowsAffected = await db.Todos.Where(t => t.Id == todoId).ExecuteDeleteAsync();
    if (rowsAffected == 0)
    {
        return Results.Json(new { error = "Todo not found" }, statusCode: 404);
    }

    return Results.NoContent();
});

Console.WriteLine("Server starting on :8080");
app.Run("http://0.0.0.0:8080");

// Nullable properties distinguish "field not provided" from "field set to
// a zero value", like the pointer fields in the Go version.
public class CreateTodoInput
{
    public string? Title { get; set; }
}

public class UpdateTodoInput
{
    public string? Title { get; set; }
    public bool? Completed { get; set; }
}
