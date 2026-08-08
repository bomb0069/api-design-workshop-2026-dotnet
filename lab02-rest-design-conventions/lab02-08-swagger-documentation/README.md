# Lab 02-08: Swagger Documentation

## Learning Objectives

- Understand the OpenAPI/Swagger specification and its role in API development
- Document APIs using the OpenAPI 3.0 specification format
- Use Swagger UI to explore and test APIs interactively
- Learn about API documentation best practices

## Prerequisites

- Docker and Docker Compose installed
- Basic understanding of REST APIs and JSON

## Getting Started

Start all services with Docker Compose:

```bash
docker compose up --build
```

This starts three services:

| Service    | URL                              | Description                    |
|------------|----------------------------------|--------------------------------|
| API        | http://localhost:8080             | The Products REST API          |
| Swagger UI | http://localhost:8081             | Interactive API documentation  |
| PostgreSQL | localhost:5432                    | Database (internal)            |

Open http://localhost:8081 in your browser to see the Swagger UI with the full API documentation.

The API also embeds its own Swagger UI (via **Swashbuckle.AspNetCore**) at http://localhost:8080/swagger, rendering the same specification — no separate container required. The raw spec is served at http://localhost:8080/swagger.json.

## Project Structure

```
lab02-08-swagger-documentation/
├── Program.cs                  # ASP.NET Core API server + Swashbuckle Swagger UI
├── swagger.json                # Hand-crafted OpenAPI 3.0 specification
├── SwaggerDocumentation.csproj # Project file with package references
├── Dockerfile                  # Multi-stage Docker build
├── docker-compose.yml          # Docker Compose with api, db, and swagger-ui
└── README.md                   # This file
```

## Understanding the OpenAPI Specification

The `swagger.json` file is an OpenAPI 3.0 specification that describes the entire API. Here is a walkthrough of its structure:

### Info Section

The `info` object provides metadata about the API:

```json
{
  "info": {
    "title": "Products API",
    "description": "A sample Products API for the API Design Workshop",
    "version": "1.0.0"
  }
}
```

This appears at the top of the Swagger UI and tells consumers what the API does and which version they are using.

### Servers

The `servers` array defines the base URLs where the API is available:

```json
{
  "servers": [
    {
      "url": "http://localhost:8080"
    }
  ]
}
```

In production, you would list your staging and production URLs here.

### Paths

The `paths` object is the core of the specification. Each key is a URL path, and under it you define the HTTP methods available:

```json
{
  "paths": {
    "/products": {
      "get": { "summary": "List all products", ... },
      "post": { "summary": "Create a product", ... }
    },
    "/products/{id}": {
      "get": { "summary": "Get a product", ... },
      "put": { "summary": "Update a product", ... },
      "delete": { "summary": "Delete a product", ... }
    }
  }
}
```

Each operation can define:

- **summary** and **description** -- what the endpoint does
- **tags** -- grouping for the UI
- **parameters** -- path, query, or header parameters
- **requestBody** -- the expected request payload with a JSON schema reference
- **responses** -- possible response codes with descriptions and schemas

### Parameters

Path parameters are defined with `in: "path"`:

```json
{
  "name": "id",
  "in": "path",
  "required": true,
  "schema": { "type": "integer" }
}
```

### Request Bodies

Request bodies reference shared schemas:

```json
{
  "requestBody": {
    "required": true,
    "content": {
      "application/json": {
        "schema": { "$ref": "#/components/schemas/CreateProductInput" }
      }
    }
  }
}
```

### Responses

Responses are keyed by HTTP status code and can include a schema for the response body:

```json
{
  "responses": {
    "200": {
      "description": "Product found",
      "content": {
        "application/json": {
          "schema": { "$ref": "#/components/schemas/Product" }
        }
      }
    },
    "404": {
      "description": "Product not found",
      "content": {
        "application/json": {
          "schema": { "$ref": "#/components/schemas/ErrorResponse" }
        }
      }
    }
  }
}
```

### Components / Schemas

The `components.schemas` section defines reusable data models:

```json
{
  "components": {
    "schemas": {
      "Product": {
        "type": "object",
        "properties": {
          "id": { "type": "integer", "example": 1 },
          "name": { "type": "string", "example": "Laptop" },
          "price": { "type": "number", "example": 999.99 },
          "category": { "type": "string", "example": "electronics" }
        }
      }
    }
  }
}
```

These schemas are referenced throughout the spec using `$ref` to avoid duplication.

## Swashbuckle (Reference)

The project references [Swashbuckle.AspNetCore](https://github.com/domaindrivendev/Swashbuckle.AspNetCore), the standard Swagger toolchain for ASP.NET Core. In this lab we only use its **Swagger UI middleware**, pointed at the hand-crafted spec:

```csharp
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger.json", "Products API");
    options.RoutePrefix = "swagger";
});
```

Swashbuckle can also **generate** an OpenAPI spec directly from your code — the .NET equivalent of Go's swaggo annotations:

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// ...
app.UseSwagger();   // serves the generated spec at /swagger/v1/swagger.json
```

With generation enabled, you enrich the documentation via metadata on each endpoint:

| Swashbuckle / minimal API metadata     | Purpose                                      |
|----------------------------------------|----------------------------------------------|
| `.WithSummary("...")`                  | Short description of the endpoint            |
| `.WithDescription("...")`              | Longer description                           |
| `.WithTags("products")`                | Group endpoints in the UI                    |
| `.Accepts<CreateProductInput>("application/json")` | Accepted request body type       |
| `.Produces<Product>(200)`              | Success response code and schema             |
| `.Produces<ErrorResponse>(404)`        | Error response code and schema               |

In this lab we use a hand-crafted `swagger.json` so you can understand the full specification. In production projects, generating the spec from code keeps documentation and implementation in sync automatically.

## Exercises

### Exercise 1: Add Descriptions to Parameters and Schemas

Open `swagger.json` and add `description` fields to all parameters and schema properties. For example:

```json
{
  "name": "id",
  "in": "path",
  "required": true,
  "description": "The unique identifier of the product",
  "schema": { "type": "integer" }
}
```

Add descriptions to each property in the `Product`, `CreateProductInput`, and `ErrorResponse` schemas as well. Reload Swagger UI to see the improved documentation.

### Exercise 2: Add Example Values for Request and Response Bodies

Add `example` objects at the schema level to show complete request/response examples in Swagger UI:

```json
{
  "schema": {
    "$ref": "#/components/schemas/CreateProductInput"
  },
  "example": {
    "name": "Wireless Mouse",
    "price": 29.99,
    "category": "accessories"
  }
}
```

Try adding examples for both request bodies and response bodies across all endpoints.

### Exercise 3: Add a Health Endpoint

Add a `/health` endpoint to both the server and the OpenAPI specification:

1. In `Program.cs`, add `app.MapGet("/health", () => Results.Json(new { status = "ok" }));`.
2. In `swagger.json`, add the `/health` path with a GET operation, including a response schema.

### Exercise 4: Add Authentication Documentation

Add API key authentication to the OpenAPI spec. In the `components` section, add a `securitySchemes` definition:

```json
{
  "components": {
    "securitySchemes": {
      "ApiKeyAuth": {
        "type": "apiKey",
        "in": "header",
        "name": "X-API-Key"
      }
    }
  }
}
```

Then apply it globally or to individual operations using the `security` field:

```json
{
  "security": [
    { "ApiKeyAuth": [] }
  ]
}
```

Reload Swagger UI and observe the "Authorize" button that appears.

## Key Concepts

- **OpenAPI Specification (OAS)**: A standard, language-agnostic format for describing REST APIs. The current version is 3.0 (also known as 3.1 in its latest iteration). It defines endpoints, parameters, request/response schemas, authentication, and more in a machine-readable JSON or YAML file.

- **Swagger UI**: An open-source tool that renders an OpenAPI specification as an interactive web page. It allows developers to read documentation, see example payloads, and execute API requests directly from the browser.

- **API Documentation**: Good API documentation describes not just what endpoints exist, but how to use them effectively. It includes descriptions, examples, error codes, and authentication details. The OpenAPI spec serves as both human-readable documentation (via Swagger UI) and machine-readable contract (for code generation and testing).

- **Schema Definitions**: Reusable data models defined in `components/schemas`. Using `$ref` references keeps the specification DRY (Don't Repeat Yourself) and ensures consistency across endpoints that share the same data structures.

## Cleanup

Stop and remove all containers and volumes:

```bash
docker compose down -v
```
