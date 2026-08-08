using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var store = new TodoStore();
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// GET /todos - list all todos
app.MapGet("/todos", () => Results.Json(store.List()));

// POST /todos - create a todo
app.MapPost("/todos", async (HttpRequest request) =>
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

    var todo = store.Create(input.Title);
    return Results.Json(todo, statusCode: 201);
});

// GET /todos/{id} - get a single todo
app.MapGet("/todos/{id}", (string id) =>
{
    if (!int.TryParse(id, out var todoId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: 400);
    }

    var todo = store.Get(todoId);
    if (todo is null)
    {
        return Results.Json(new { error = "Todo not found" }, statusCode: 404);
    }

    return Results.Json(todo);
});

// PUT /todos/{id} - update a todo (partial update: only provided fields change)
app.MapPut("/todos/{id}", async (string id, HttpRequest request) =>
{
    if (!int.TryParse(id, out var todoId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: 400);
    }

    if (!store.Exists(todoId))
    {
        return Results.Json(new { error = "Todo not found" }, statusCode: 404);
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

    var todo = store.Update(todoId, input?.Title, input?.Completed);
    if (todo is null)
    {
        return Results.Json(new { error = "Todo not found" }, statusCode: 404);
    }

    return Results.Json(todo);
});

// DELETE /todos/{id} - delete a todo
app.MapDelete("/todos/{id}", (string id) =>
{
    if (!int.TryParse(id, out var todoId))
    {
        return Results.Json(new { error = "Invalid ID" }, statusCode: 400);
    }

    if (!store.Delete(todoId))
    {
        return Results.Json(new { error = "Todo not found" }, statusCode: 404);
    }

    return Results.NoContent();
});

Console.WriteLine("Server starting on :8080");
app.Run("http://0.0.0.0:8080");

public class Todo
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}

// Nullable properties let us distinguish "field not provided" (null)
// from "field set to a zero value" (empty string or false),
// just like pointer fields (*string, *bool) in the Go version.
public class CreateTodoInput
{
    public string? Title { get; set; }
}

public class UpdateTodoInput
{
    public string? Title { get; set; }
    public bool? Completed { get; set; }
}

// Thread-safe in-memory store. A lock protects the dictionary against
// concurrent access, playing the role of sync.RWMutex in the Go version.
public class TodoStore
{
    private readonly object _lock = new();
    private readonly Dictionary<int, Todo> _todos = new();
    private int _nextId = 1;

    public List<Todo> List()
    {
        lock (_lock)
        {
            return _todos.Values.ToList();
        }
    }

    public Todo Create(string title)
    {
        lock (_lock)
        {
            var todo = new Todo { Id = _nextId, Title = title, Completed = false };
            _todos[todo.Id] = todo;
            _nextId++;
            return todo;
        }
    }

    public Todo? Get(int id)
    {
        lock (_lock)
        {
            return _todos.TryGetValue(id, out var todo) ? todo : null;
        }
    }

    public bool Exists(int id)
    {
        lock (_lock)
        {
            return _todos.ContainsKey(id);
        }
    }

    public Todo? Update(int id, string? title, bool? completed)
    {
        lock (_lock)
        {
            if (!_todos.TryGetValue(id, out var todo))
            {
                return null;
            }

            if (title is not null)
            {
                todo.Title = title;
            }
            if (completed is not null)
            {
                todo.Completed = completed.Value;
            }
            return todo;
        }
    }

    public bool Delete(int id)
    {
        lock (_lock)
        {
            return _todos.Remove(id);
        }
    }
}
