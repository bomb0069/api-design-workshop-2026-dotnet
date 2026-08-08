# Lab 02-02: Path and Query Parameters

## Learning Objectives

- Use URL path parameters to identify specific resources
- Use ASP.NET Core minimal API routing with route parameters
- Return appropriate HTTP status codes (200, 400, 404)
- Parse and validate path parameters

## Prerequisites

- .NET SDK 8.0 or later installed
- Docker and Docker Compose installed
- Completion of Lab 01-02 or equivalent understanding of basic HTTP handlers and JSON responses
- A terminal and a tool for making HTTP requests (curl, Postman, or a browser)

## Getting Started

### Option A: Run with .NET

```bash
dotnet run
```

### Option B: Run with Docker Compose

```bash
docker compose up --build
```

The server will start on http://localhost:8080.

## Test Commands

### List all items

```bash
curl http://localhost:8080/items
```

Expected response (HTTP 200):

```json
[
  {"id": 1, "name": "Laptop", "price": 999.99},
  {"id": 2, "name": "Mouse", "price": 29.99},
  {"id": 3, "name": "Keyboard", "price": 79.99},
  {"id": 4, "name": "Monitor", "price": 549.99}
]
```

### Get a specific item by ID

```bash
curl http://localhost:8080/items/1
```

Expected response (HTTP 200):

```json
{"id": 1, "name": "Laptop", "price": 999.99}
```

### Request an item that does not exist (404)

```bash
curl http://localhost:8080/items/999
```

Expected response (HTTP 404):

```json
{"error": "Item not found"}
```

### Request with an invalid ID (400)

```bash
curl http://localhost:8080/items/abc
```

Expected response (HTTP 400):

```json
{"error": "Invalid ID format"}
```

## Code Walkthrough

### Minimal API Routing

ASP.NET Core minimal APIs let you map HTTP methods and route templates directly to handler functions:

```csharp
app.MapGet("/items", () => Results.Json(items));
app.MapGet("/items/{id}", (string id) => { ... });
```

The `{id}` syntax in the route template defines a named path parameter. ASP.NET Core will match any value in that position and bind it to the handler parameter with the same name.

### Extracting and Parsing Path Parameters

The handler receives the raw path parameter as a `string`. Since path parameters are always strings, you parse them into the appropriate type. In this lab, we convert the ID to an integer using `int.TryParse`:

```csharp
if (!int.TryParse(id, out var itemId))
{
    // handle invalid input -> 400 Bad Request
}
```

Note: ASP.NET Core also supports route constraints like `{id:int}`, but a failed constraint produces a 404 (the route simply does not match). To return **400 Bad Request** for a malformed ID — the behavior this lab teaches — we bind the parameter as a string and parse it ourselves.

### HTTP Status Codes

This lab demonstrates three important status codes:

- **200 OK** - The request succeeded. This is the default status code for a successful response. Used when returning the list of items or a single item.
- **400 Bad Request** - The client sent a request that the server cannot process. Used when the path parameter is not a valid integer (e.g., `/items/abc`).
- **404 Not Found** - The requested resource does not exist. Used when the ID is valid but no item matches (e.g., `/items/999`).

Set the status code together with the JSON body using `Results.Json`:

```csharp
return Results.Json(new { error = "Invalid ID format" }, statusCode: StatusCodes.Status400BadRequest);
```

`Results.Json` writes the status code and the `Content-Type: application/json` header before serializing the body, so you never have to worry about ordering the two operations.

## Exercises

### Exercise 1: Add a Price Endpoint

Add a `GET /items/{id}/price` endpoint that returns only the price of an item.

Expected response for `curl http://localhost:8080/items/1/price`:

```json
{"price": 999.99}
```

Handle the same error cases (400 for invalid ID, 404 for missing item).

### Exercise 2: Filter by Category

Add a `Category` property to the `Item` record and assign categories to each item (e.g., "electronics", "peripherals"). Then create a `GET /categories/{category}/items` endpoint that returns all items matching the given category.

Expected response for `curl http://localhost:8080/categories/peripherals/items`:

```json
[
  {"id": 2, "name": "Mouse", "price": 29.99, "category": "peripherals"},
  {"id": 3, "name": "Keyboard", "price": 79.99, "category": "peripherals"}
]
```

Return an empty array `[]` if no items match the category.

### Exercise 3: Multiple Path Parameters

Add a `StoreId` property to the `Item` record and create a `GET /stores/{storeId}/items/{itemId}` endpoint that looks up an item by both store and item ID.

This exercise demonstrates how to bind and use multiple path parameters in a single handler:

```csharp
app.MapGet("/stores/{storeId}/items/{itemId}", (string storeId, string itemId) => { ... });
```

Return 404 if the store or item is not found, and 400 if either parameter is not a valid integer.

## Key Concepts

### Path Parameters

Path parameters are variable segments in a URL path, identified by a placeholder name inside curly braces (e.g., `{id}`). They allow a single route to handle requests for many different resources. Unlike query parameters (which appear after `?`), path parameters are part of the URL path itself and typically identify a specific resource.

| Feature         | Path Parameter          | Query Parameter            |
| --------------- | ----------------------- | -------------------------- |
| Syntax          | `/items/{id}`           | `/items?id=1`              |
| Purpose         | Identify a resource     | Filter or modify a request |
| Required?       | Yes (part of the route) | Usually optional           |
| Example         | `/items/1`              | `/items?sort=name`         |

### Minimal APIs

ASP.NET Core offers two main styles for building HTTP APIs: MVC controllers and **minimal APIs**. Minimal APIs map routes directly to functions, which keeps small services concise and easy to read — very similar in spirit to routing libraries in other ecosystems:

- Named path parameters (`{id}`, `{category}`)
- Route groups and sub-routes (`MapGroup`)
- Middleware support
- Method-based routing (`MapGet`, `MapPost`, `MapPut`, `MapDelete`)

### HTTP Status Codes

Status codes communicate the result of a request to the client:

| Code | Name        | When to Use                                     |
| ---- | ----------- | ----------------------------------------------- |
| 200  | OK          | The request succeeded and a response is returned |
| 400  | Bad Request | The client sent invalid data (e.g., non-numeric ID) |
| 404  | Not Found   | The requested resource does not exist            |

Always return meaningful status codes so that API clients can handle errors programmatically, rather than relying on parsing error messages.

## Cleanup

Stop the running server with `Ctrl+C`, or if using Docker Compose:

```bash
docker compose down
```
