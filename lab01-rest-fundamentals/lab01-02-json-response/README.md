# Lab 01-02: JSON Response

## Learning Objectives

By the end of this lab, you will be able to:

- Define C# records that serialize to JSON
- Serialize .NET data structures to JSON
- Handle multiple endpoints that return different JSON shapes
- Understand how property naming policies control serialization

## Prerequisites

- .NET 8 SDK installed
- Docker and Docker Compose installed
- Basic understanding of HTTP (covered in Lab 01-01)
- A terminal and a text editor

## Getting Started

1. Navigate to the lab directory:

```bash
cd lab01-02-json-response
```

2. Start the server using Docker Compose:

```bash
docker compose up --build
```

Or run it directly:

```bash
dotnet run
```

The server will start on port **8080**.

## Test the Endpoints

Open a new terminal and run the following commands:

### Get all books

```bash
curl http://localhost:8080/books
```

Expected response:

```json
[
  {"id":1,"title":"The Go Programming Language","author":"Alan Donovan","year":2015},
  {"id":2,"title":"Go in Action","author":"William Kennedy","year":2015},
  {"id":3,"title":"Learning Go","author":"Jon Bodner","year":2021}
]
```

### Get the book count

```bash
curl http://localhost:8080/books/count
```

Expected response:

```json
{"count":3}
```

### Health check

```bash
curl http://localhost:8080/health
```

Expected response:

```json
{"status":"ok"}
```

> **Tip:** Pipe the output through `jq` for pretty-printed JSON:
> ```bash
> curl -s http://localhost:8080/books | jq .
> ```

## Code Walkthrough

Open `Program.cs` and follow along with the explanation below.

### 1. Record Definition

```csharp
public record Book(int Id, string Title, string Author, int Year);
```

A **record** in C# is a concise way to declare an immutable data type with a set of properties. When serialized with `System.Text.Json` (the default in ASP.NET Core), each property becomes a JSON key.

### 2. The Books List

```csharp
var books = new List<Book>
{
    new(Id: 1, Title: "The Go Programming Language", Author: "Alan Donovan", Year: 2015),
    new(Id: 2, Title: "Go in Action", Author: "William Kennedy", Year: 2015),
    new(Id: 3, Title: "Learning Go", Author: "Jon Bodner", Year: 2021),
};
```

This is an in-memory list of `Book` records. In a real application, this data would come from a database. For this lab, we use an in-memory list to keep things simple and focus on JSON serialization.

### 3. Returning JSON with `Results.Json`

```csharp
app.Map("/books", () => Results.Json(books));
```

`Results.Json(...)` serializes the list into JSON and writes it to the response body with the `Content-Type: application/json` header — all in a single step. Minimal API handlers can also return the object directly (`() => books`) and ASP.NET Core will serialize it the same way.

### 4. How the Naming Policy Controls JSON Field Names

| C# Property | Naming Policy | JSON Key   |
|-------------|---------------|------------|
| `Id`        | camelCase     | `"id"`     |
| `Title`     | camelCase     | `"title"`  |
| `Author`    | camelCase     | `"author"` |
| `Year`      | camelCase     | `"year"`   |

ASP.NET Core's default JSON options use the **camelCase naming policy**, so PascalCase C# properties become lowercase/camelCase JSON keys automatically. When you need a specific key name (e.g., `snake_case`), annotate the property with `[JsonPropertyName("key_name")]` — the equivalent of Go's struct tags.

## Exercises

Try these exercises to deepen your understanding. Each one builds on the starter code.

### Exercise 1: Add a Genre Field

Add a new property `Genre` to the `Book` record. Then update the existing book data to include genres.

**Steps:**

1. Add `string Genre` to the `Book` record's parameter list.
2. Add a genre to each book in the `books` list (e.g., `"Programming"`).
3. Rebuild and test:

```bash
docker compose up --build
curl -s http://localhost:8080/books | jq .
```

4. Verify that each book now includes a `"genre"` field in the JSON output.

### Exercise 2: Create a `/books/summary` Endpoint

Create a new endpoint at `/books/summary` that returns an array of strings. Each string should describe a book in the format:

```
"{Title} by {Author} ({Year})"
```

**Expected response:**

```json
[
  "The Go Programming Language by Alan Donovan (2015)",
  "Go in Action by William Kennedy (2015)",
  "Learning Go by Jon Bodner (2021)"
]
```

**Hints:**

- Register a new route with `app.Map("/books/summary", ...)`.
- Use string interpolation: `$"{b.Title} by {b.Author} ({b.Year})"`.
- Use LINQ: `books.Select(b => ...).ToList()` and return the list as JSON.

### Exercise 3: Create a Nested Response Structure

Instead of returning a plain array from `/books`, create a wrapper response that includes both the data and metadata.

**Target response structure:**

```json
{
  "data": [
    {"id":1,"title":"The Go Programming Language","author":"Alan Donovan","year":2015},
    {"id":2,"title":"Go in Action","author":"William Kennedy","year":2015},
    {"id":3,"title":"Learning Go","author":"Jon Bodner","year":2021}
  ],
  "count": 3
}
```

**Hints:**

- Define a new record (e.g., `BooksResponse`) with `Data` and `Count` properties.
- The camelCase naming policy will produce `"data"` and `"count"` automatically.
- Populate the record and return it from the handler.

### Exercise 4: Experiment with Serialization Attributes

Try modifying serialization attributes to observe different behaviors:

1. **Hide a property with `[JsonIgnore]`**: Add `[property: JsonIgnore]` to the `Year` parameter and observe that it no longer appears in the JSON output (requires `using System.Text.Json.Serialization;`).

2. **Omit default values**: Add `[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]` to `Title`. Then add a book with a `null` title to the list and see that the `"title"` key is omitted for that entry.

3. **Rename a key**: Add `[property: JsonPropertyName("book_author")]` to `Author` to see the key change.

After experimenting, remember to restore the original code before moving on.

## Key Concepts

### JSON Serialization

**Serialization** is the process of converting a .NET data structure (record, class, list, dictionary, etc.) into JSON format. ASP.NET Core provides several ways to do this:

- `JsonSerializer.Serialize(v)` -- returns a `string`
- `Results.Json(v)` -- writes JSON directly to the HTTP response
- Returning an object from a minimal API handler -- serialized automatically

For HTTP handlers, `Results.Json` (or returning the object directly) is preferred because ASP.NET Core streams the output to the response efficiently.

### Serialization Attributes

Attributes on properties provide metadata for `System.Text.Json` — the counterpart of Go's struct tags:

| Attribute                                                       | Effect                                        |
|-----------------------------------------------------------------|-----------------------------------------------|
| `[JsonPropertyName("name")]`                                    | Sets the JSON key to `"name"`                 |
| `[JsonIgnore]`                                                  | Excludes the property from JSON output        |
| `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` | Omits the property when it is `null`          |
| (default camelCase policy)                                      | `PropertyName` becomes `propertyName`         |

### Content-Type Header

Setting `Content-Type: application/json` in the response header tells the client that the response body is JSON. This is important because:

- Browsers and HTTP clients use it to parse the response correctly.
- API tools like Postman and curl use it for formatting and syntax highlighting.
- It is part of the HTTP specification for proper content negotiation.

`Results.Json` sets this header for you, before the body is written.

## Cleanup

When you are finished with the lab, stop the running containers:

```bash
docker compose down
```
