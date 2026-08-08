# Lab 01-03: CRUD In-Memory

Build a complete CRUD (Create, Read, Update, Delete) REST API backed by a thread-safe in-memory data store. This lab covers every fundamental operation you need when working with resources in a REST API.

## Learning Objectives

- Implement full CRUD operations for a resource
- Use proper HTTP methods (GET, POST, PUT, DELETE)
- Return correct HTTP status codes (200, 201, 204, 400, 404)
- Build a thread-safe in-memory data store using a `lock`

## Getting Started

### Run Locally

```bash
dotnet run
```

### Run with Docker Compose

```bash
docker compose up --build
```

The server starts on http://localhost:8080.

## Test with curl

### Create a Todo (POST)

```bash
curl -s -X POST http://localhost:8080/todos \
  -H "Content-Type: application/json" \
  -d '{"title":"Learn Go"}' | jq
```

Expected response (`201 Created`):

```json
{
  "id": 1,
  "title": "Learn Go",
  "completed": false
}
```

### List All Todos (GET)

```bash
curl -s http://localhost:8080/todos | jq
```

Expected response (`200 OK`):

```json
[
  {
    "id": 1,
    "title": "Learn Go",
    "completed": false
  }
]
```

### Get a Single Todo (GET by ID)

```bash
curl -s http://localhost:8080/todos/1 | jq
```

Expected response (`200 OK`):

```json
{
  "id": 1,
  "title": "Learn Go",
  "completed": false
}
```

### Update a Todo (PUT)

```bash
curl -s -X PUT http://localhost:8080/todos/1 \
  -H "Content-Type: application/json" \
  -d '{"completed":true}' | jq
```

Expected response (`200 OK`):

```json
{
  "id": 1,
  "title": "Learn Go",
  "completed": true
}
```

### Delete a Todo (DELETE)

```bash
curl -s -X DELETE http://localhost:8080/todos/1 -w "\nHTTP Status: %{http_code}\n"
```

Expected response (`204 No Content`):

```
HTTP Status: 204
```

### Verify Deletion

```bash
curl -s http://localhost:8080/todos/1 | jq
```

Expected response (`404 Not Found`):

```json
{
  "error": "Todo not found"
}
```

## Code Walkthrough

### The TodoStore Class

```csharp
public class TodoStore
{
    private readonly object _lock = new();
    private readonly Dictionary<int, Todo> _todos = new();
    private int _nextId = 1;
}
```

The `TodoStore` holds all todos in a dictionary keyed by ID. The `_nextId` field acts as an auto-incrementing primary key. The `_lock` object protects concurrent access to the dictionary.

### Thread Safety with lock

Every store method wraps its work in a `lock (_lock)` block:

```csharp
public Todo? Get(int id)
{
    lock (_lock)
    {
        return _todos.TryGetValue(id, out var todo) ? todo : null;
    }
}
```

ASP.NET Core handles each request on a thread-pool thread, so multiple requests can touch the store at the same time. The `lock` statement guarantees that only one thread mutates or reads the dictionary at a time, preventing data corruption. (Go's version uses `sync.RWMutex`, which additionally allows concurrent readers; the .NET equivalent would be `ReaderWriterLockSlim`, but a simple `lock` keeps the code clearer for this lab.)

### Handler Breakdown

| Handler     | Method   | Path           | Description                        |
|-------------|----------|----------------|------------------------------------|
| `MapGet`    | `GET`    | `/todos`       | Return all todos as a JSON array   |
| `MapPost`   | `POST`   | `/todos`       | Create a new todo from JSON body   |
| `MapGet`    | `GET`    | `/todos/{id}`  | Return a single todo by ID         |
| `MapPut`    | `PUT`    | `/todos/{id}`  | Update fields on an existing todo  |
| `MapDelete` | `DELETE` | `/todos/{id}`  | Remove a todo by ID                |

### Status Codes Used

| Code  | Meaning        | When Used                                      |
|-------|----------------|-------------------------------------------------|
| `200` | OK             | Successful GET or PUT                           |
| `201` | Created        | Successful POST that creates a new resource     |
| `204` | No Content     | Successful DELETE with no response body         |
| `400` | Bad Request    | Invalid JSON body or missing required fields    |
| `404` | Not Found      | Requested todo ID does not exist                |

### Nullable Fields in Update Input

```csharp
public class UpdateTodoInput
{
    public string? Title { get; set; }
    public bool? Completed { get; set; }
}
```

Using nullable types (`string?`, `bool?`) lets us distinguish between "field not provided" (`null`) and "field set to a zero value" (empty string or `false`). This allows partial updates -- only the fields included in the request body are changed. This mirrors the pointer fields (`*string`, `*bool`) used in the Go version.

### Manual JSON Deserialization

```csharp
input = await JsonSerializer.DeserializeAsync<CreateTodoInput>(request.Body, jsonOptions);
```

Instead of letting ASP.NET Core bind the body automatically (which returns its own error format on failure), the handlers deserialize the body manually. This gives us full control over the error response, so an invalid body returns `{"error": "Invalid request body"}` with status `400` — the same contract as the Go version.

## Exercises

1. **Add a "priority" field** -- Extend the `Todo` class with a `Priority` property that accepts `"low"`, `"medium"`, or `"high"`. Validate the value on create and update.

2. **Add PATCH support** -- Implement `PATCH /todos/{id}` with `MapPatch` for partial updates. Compare it with PUT: PUT traditionally replaces the entire resource, while PATCH applies only the provided changes.

3. **List completed todos** -- Add a `GET /todos/completed` endpoint that returns only todos where `completed` is `true`.

4. **Search by title** -- Add a `GET /todos/search?q=keyword` endpoint that returns todos whose title contains the given keyword (case-insensitive).

## HTTP Methods Reference

| Method   | Description                         | Typical Status Codes |
|----------|-------------------------------------|----------------------|
| `GET`    | Retrieve a resource or collection   | 200, 404             |
| `POST`   | Create a new resource               | 201, 400             |
| `PUT`    | Replace / update an existing resource | 200, 400, 404      |
| `PATCH`  | Partially update an existing resource | 200, 400, 404      |
| `DELETE` | Remove a resource                   | 204, 404             |

## Cleanup

```bash
docker compose down
```
