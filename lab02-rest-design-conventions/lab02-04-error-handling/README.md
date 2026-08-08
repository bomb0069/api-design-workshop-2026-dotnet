# Lab 07 - Error Handling

## Learning Objectives

- Define a **consistent error response format** across all API endpoints
- Create **centralized error types** with factory functions
- Use **middleware** for logging, exception recovery, and default headers
- **Separate error concerns from business logic** for cleaner handlers

## Error Response Format

All errors from this API follow a standard JSON structure:

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Product not found"
  }
}
```

| Field     | Description                                         |
|-----------|-----------------------------------------------------|
| `code`    | Machine-readable error code (e.g., `BAD_REQUEST`)   |
| `message` | Human-readable description of what went wrong       |

The HTTP status code is set on the response header but not repeated in the body.

## Getting Started

```bash
docker compose up --build
```

The API will be available at `http://localhost:8080`.

To stop the services:

```bash
docker compose down
```

To stop and remove the database volume:

```bash
docker compose down -v
```

## Test Examples

### 400 Bad Request - Missing required field

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{"price": 9.99, "category": "books"}' | jq
```

Response:

```json
{
  "error": {
    "code": "BAD_REQUEST",
    "message": "Name is required"
  }
}
```

### 400 Bad Request - Invalid ID format

```bash
curl -s http://localhost:8080/products/abc | jq
```

Response:

```json
{
  "error": {
    "code": "BAD_REQUEST",
    "message": "Invalid ID format"
  }
}
```

### 400 Bad Request - Invalid price

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Widget", "price": -5, "category": "tools"}' | jq
```

Response:

```json
{
  "error": {
    "code": "BAD_REQUEST",
    "message": "Price must be greater than 0"
  }
}
```

### 404 Not Found - Product does not exist

```bash
curl -s http://localhost:8080/products/9999 | jq
```

Response:

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Product not found"
  }
}
```

### 409 Conflict - Duplicate product name

First, create a product:

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Gadget", "price": 29.99, "category": "electronics"}' | jq
```

Then try to create another with the same name:

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Gadget", "price": 19.99, "category": "electronics"}' | jq
```

Response:

```json
{
  "error": {
    "code": "CONFLICT",
    "message": "A product with this name already exists"
  }
}
```

### 201 Created - Successful creation

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Book", "price": 12.99, "category": "books"}' | jq
```

Response:

```json
{
  "id": 1,
  "name": "Book",
  "price": 12.99,
  "category": "books"
}
```

## Code Walkthrough

### Errors.cs - Centralized Error Types

The `ApiError` class provides a consistent shape for all error responses:

```csharp
public class ApiError
{
    [JsonIgnore]                       // HTTP status code (not included in JSON body)
    public int StatusCode { get; init; }

    [JsonPropertyName("code")]         // Machine-readable error code
    public string Code { get; init; } = "";

    [JsonPropertyName("message")]      // Human-readable message
    public string Message { get; init; } = "";
}
```

Factory functions create specific error types, keeping the details in one place:

- `ApiError.NewBadRequestError(message)` -- 400 for validation failures and malformed input
- `ApiError.NewNotFoundError(resource)` -- 404 when a resource does not exist
- `ApiError.NewConflictError(message)` -- 409 for uniqueness constraint violations
- `ApiError.NewInternalError()` -- 500 for unexpected server errors (message is generic on purpose)

The `Send()` method produces an `IResult` that writes the status code and JSON body to the response:

```csharp
public IResult Send() =>
    Results.Json(new ErrorResponse { Error = this }, statusCode: StatusCode);
```

### Handlers Using Centralized Errors

Handlers call error factory functions and return `Send()` instead of manually building responses. This pattern keeps handlers focused on business logic:

```csharp
public static async Task<IResult> GetProduct(string id, NpgsqlDataSource db)
{
    if (!int.TryParse(id, out var productId))
    {
        return ApiError.NewBadRequestError("Invalid ID format").Send();
    }

    // ... query database ...

    if (!await reader.ReadAsync())
    {
        return ApiError.NewNotFoundError("Product").Send();
    }
}
```

### Middleware

Three middleware components are applied to every request, in the same order as the Go version:

1. **Logger** -- Logs every request with method, path, status code, and duration.
2. **Recoverer** -- Catches unhandled exceptions in handlers and returns a 500 instead of crashing the server.
3. **JsonContentType** (custom) -- Defaults `Content-Type: application/json` on responses so individual handlers do not need to set it.

```csharp
app.Use(async (context, next) => { /* Logger */ });
app.Use(async (context, next) => { /* Recoverer */ });
app.Use(async (context, next) => { /* JsonContentType */ });
```

## Exercises

### 1. Add a NewValidationError with Field Errors

Create a `NewValidationError` factory that accepts a list of field-level errors and returns them in a structured format:

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Validation failed",
    "fields": [
      {"field": "name", "message": "Name is required"},
      {"field": "price", "message": "Price must be greater than 0"}
    ]
  }
}
```

Hints:
- Add a `Fields` list to `ApiError` (or create a new `ValidationError` class) with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` so it only appears when present
- Validate all fields at once instead of returning on the first error
- Return 422 Unprocessable Entity

### 2. Add Request ID to Error Responses

Attach a unique request ID to every response and include it in error responses:

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Product not found",
    "request_id": "abc-123-def"
  }
}
```

Hints:
- ASP.NET Core already assigns `HttpContext.TraceIdentifier` to every request (or generate a `Guid` in a middleware)
- Pass the `HttpContext` into `Send` so the error body can include the ID
- Set the `X-Request-Id` response header as well

### 3. Custom JSON Recovery Middleware

The recoverer in this lab already returns JSON. Extend it to include exception details in development only:

```csharp
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception ex)
    {
        // Log the exception, return JSON 500 error
        // In Development, include ex.Message in the body; in Production, keep it generic
    }
});
```

Test it by adding a temporary endpoint that throws:

```csharp
app.MapGet("/panic", () => { throw new Exception("something went wrong"); });
```

### 4. Error Logging Middleware

Create middleware that captures the response status code, then logs details for any response with status >= 400:

```
ERROR [POST /products] 409 - 2.3ms - Body: {"name": "Gadget", ...}
```

Hints:
- The status code is available on `context.Response.StatusCode` after `await next(context)`
- Call `context.Request.EnableBuffering()` before reading the body so it can be re-read by the handler
- Log method, path, status, duration, and request body for error responses

## Key Concepts

### Consistent Error Format

Every error response uses the same JSON structure. Clients can rely on parsing `error.code` for programmatic handling and `error.message` for display. This eliminates guesswork about what shape an error response will take.

### Error Factory Functions

Factory functions like `ApiError.NewNotFoundError("Product")` centralize error creation. If you need to change the format, status code, or add fields, you change it in one place. They also make handlers more readable -- the intent is immediately clear.

### Middleware Pipeline

Middleware runs in order for every request. The pipeline in this lab:

```
Request -> Logger -> Recoverer -> JsonContentType -> Handler -> Response
```

- **Logger** wraps the request to log timing and status after the handler runs
- **Recoverer** catches any exceptions so the server stays up
- **JsonContentType** registers an `OnStarting` callback that sets the content type header before the response starts

### Separation of Concerns

Error formatting is in `Errors.cs`. Business logic is in `Handlers.cs`, and routing plus middleware are in `Program.cs`. Handlers do not need to know how errors are serialized -- they just call `Send()`. This makes it straightforward to change error formatting, add fields, or switch serialization formats without touching handler code.

## Cleanup

```bash
docker compose down -v
```
