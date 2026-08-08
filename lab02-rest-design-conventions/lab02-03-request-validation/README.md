# Lab 02-03: Request Validation

Learn how to validate incoming request bodies in an ASP.NET Core REST API using explicit, rule-based validation that returns structured, field-level error messages.

## Learning Objectives

- Validate request bodies before processing them
- Return structured validation errors with field-level messages
- Implement declarative-style validation rules in a dedicated validator
- Understand common validation rules and how to combine them

## Prerequisites

- .NET SDK 8.0 or later
- Docker and Docker Compose
- curl or a similar HTTP client

## Getting Started

Start the application with Docker Compose:

```bash
docker compose up --build
```

The API will be available at `http://localhost:8080`.

## API Endpoints

| Method | Path             | Description          |
|--------|------------------|----------------------|
| GET    | /products        | List all products    |
| POST   | /products        | Create a product     |
| GET    | /products/{id}   | Get a product by ID  |
| PUT    | /products/{id}   | Update a product     |
| DELETE | /products/{id}   | Delete a product     |

## Test Examples

### Create a valid product

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Wireless Mouse",
    "price": 29.99,
    "category": "electronics",
    "sku": "WMSE1234"
  }' | jq
```

Expected response (201 Created):

```json
{
  "id": 1,
  "name": "Wireless Mouse",
  "price": 29.99,
  "category": "electronics",
  "sku": "WMSE1234"
}
```

### Missing required fields

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{}' | jq
```

Expected response (400 Bad Request):

```json
{
  "error": "Validation failed",
  "details": [
    { "field": "name", "message": "name is required" },
    { "field": "price", "message": "price is required" },
    { "field": "category", "message": "category is required" },
    { "field": "sku", "message": "sku is required" }
  ]
}
```

Note that a missing (or `0`) price fails the `required` rule, not the `gt=0` rule — `0` is the "zero value" for a number, so the required check trips first, exactly as in the Go version of this lab.

### Price must be greater than zero

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Cheap Item",
    "price": -1,
    "category": "books",
    "sku": "CHEA0001"
  }' | jq
```

Expected response (400 Bad Request):

```json
{
  "error": "Validation failed",
  "details": [
    { "field": "price", "message": "price must be greater than 0" }
  ]
}
```

### Invalid category

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Mystery Item",
    "price": 9.99,
    "category": "toys",
    "sku": "TOYS0001"
  }' | jq
```

Expected response (400 Bad Request):

```json
{
  "error": "Validation failed",
  "details": [
    { "field": "category", "message": "category must be one of: electronics books clothing food" }
  ]
}
```

### Wrong SKU length

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Short SKU Item",
    "price": 5.00,
    "category": "food",
    "sku": "AB12"
  }' | jq
```

Expected response (400 Bad Request):

```json
{
  "error": "Validation failed",
  "details": [
    { "field": "sku", "message": "sku must be exactly 8 characters" }
  ]
}
```

### Multiple validation errors at once

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -d '{
    "name": "X",
    "price": -5,
    "category": "toys",
    "sku": "AB!@"
  }' | jq
```

Expected response (400 Bad Request):

```json
{
  "error": "Validation failed",
  "details": [
    { "field": "name", "message": "name must be at least 2 characters" },
    { "field": "price", "message": "price must be greater than 0" },
    { "field": "category", "message": "category must be one of: electronics books clothing food" },
    { "field": "sku", "message": "sku must be exactly 8 characters" }
  ]
}
```

## Code Walkthrough

### Validation Rules

The `Validation.Validate` method declares the same rules as the Go lab's `validate` struct tags, evaluated in order per field so the first failing rule wins:

| Field      | Rules                                        |
|------------|----------------------------------------------|
| `name`     | required, min length 2, max length 100       |
| `price`    | required (non-zero), greater than 0          |
| `category` | required, one of: electronics books clothing food |
| `sku`      | required, exactly 8 characters, alphanumeric |

```csharp
if (string.IsNullOrEmpty(input.Name))
    errors.Add(new("name", "name is required"));
else if (input.Name.Length < 2)
    errors.Add(new("name", "name must be at least 2 characters"));
else if (input.Name.Length > 100)
    errors.Add(new("name", "name must be at most 100 characters"));
```

Each error is a small record with the field name and a human-readable message:

```csharp
record ValidationError(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("message")] string Message);
```

This produces structured JSON errors that API consumers can programmatically handle.

### Using Validation in Handlers

Validation is applied in the handler after deserializing the JSON body:

```csharp
var input = await RequestBody.Read(request);
if (input is null)
{
    return Results.Json(new { error = "Invalid request body" }, statusCode: 400);
}

var errors = Validation.Validate(input);
if (errors.Count > 0)
{
    return Results.Json(new { error = "Validation failed", details = errors }, statusCode: 400);
}

// ... proceed with database insert
```

`RequestBody.Read` deserializes with `System.Text.Json` and returns `null` for malformed JSON, so the handler can return a `400` with a consistent error body instead of the framework's default response.

### Alternatives in the .NET Ecosystem

.NET has two popular declarative validation approaches:

- **DataAnnotations** — attributes like `[Required]`, `[StringLength]`, `[Range]` placed on model properties
- **FluentValidation** — a library where rules are declared in a validator class (`RuleFor(x => x.Name).NotEmpty().MinimumLength(2)`)

This lab uses explicit manual validation because it reproduces the Go lab's error response format (field names, messages, and rule ordering) exactly. In production code, FluentValidation gives you the same "rules in one place, structured errors out" pattern with less boilerplate.

## Validation Rules Reference

| Rule          | Description                              | Example (this lab)                 |
|---------------|------------------------------------------|------------------------------------|
| required      | Field must not be empty / zero           | all fields                         |
| min           | Minimum length (string) or value (number)| name: min 2                        |
| max           | Maximum length (string) or value (number)| name: max 100                      |
| len           | Exact length                             | sku: exactly 8                     |
| gt            | Greater than                             | price: > 0                         |
| oneof         | Must be one of the listed values         | category                           |
| alphanum      | Alphanumeric characters only             | sku                                |

## Exercises

### Exercise 1: Add Email Validation

Add a `contact_email` field to the product:

- Add a `ContactEmail` property to both `Product` and `CreateProductInput` (with `[JsonPropertyName("contact_email")]`)
- Validate it as required and a valid email (e.g., using `System.Net.Mail.MailAddress.TryCreate`)
- Update the database table and queries to include the new column
- Test with valid and invalid email addresses

### Exercise 2: Custom Price Precision Validation

Add a custom validation to ensure the price has no fractions of cents (must be a multiple of 0.01):

- Add a rule that checks that `price * 100` has no fractional remainder
- Test with values like `29.99` (valid) and `29.999` (invalid)

### Exercise 3: Optional Update Fields

Create an `UpdateProductInput` class where all fields are optional but validated when present:

- Use nullable types (`string?`, `double?`) for all fields
- Only run each field's rules when the value is non-null
- Modify the PUT handler to only update fields that are provided
- Build a dynamic SQL UPDATE statement based on which fields are non-null

### Exercise 4: Cross-Field Validation

Add a rule: if `category` is "electronics", then `price` must be greater than 10:

- Add a check in `Validate` that inspects multiple fields together
- Return a meaningful error message when the rule fails
- Test with `category: "electronics"` and `price: 5` to verify the error

## Key Concepts

### Input Validation

Never trust data coming from API clients. Always validate request bodies before processing them. Validation serves as the first line of defense against invalid or malicious data reaching your database.

### Rule Ordering

Rules for a field run in a fixed order and stop at the first failure. This gives the client exactly one actionable message per field instead of a pile of overlapping errors (e.g., an empty SKU reports "sku is required", not also "must be exactly 8 characters").

### Structured Error Responses

Rather than returning a single error string, return an array of field-level errors. This allows API consumers to map errors to specific form fields and display targeted feedback to users. Each error includes the field name and a human-readable message.

## Cleanup

Stop and remove the containers:

```bash
docker compose down
```

To also remove the database volume:

```bash
docker compose down -v
```
