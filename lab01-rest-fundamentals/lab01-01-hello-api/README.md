# Lab 01 - Hello API

## Learning Objectives

- Understand how to create a basic HTTP server in ASP.NET Core
- Return JSON responses
- Use Docker to containerize a .NET application

## Prerequisites

- .NET 8 SDK
- Docker and Docker Compose

## Getting Started

1. Build and run the application using Docker Compose:

```bash
docker compose up --build
```

Or run it directly with the .NET SDK:

```bash
dotnet run
```

2. Test the API by sending a request:

```bash
curl http://localhost:8080/
```

You should see the following response:

```json
{"message":"Hello, World!"}
```

## Explain the Code

Let's walk through `Program.cs` step by step.

### Creating the Application

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
```

`WebApplication.CreateBuilder` sets up an ASP.NET Core application with sensible defaults (Kestrel web server, logging, configuration). Calling `Build()` produces the `app` object we register routes on. This replaces the boilerplate you would otherwise write to configure an HTTP server by hand.

### Registering a Route

```csharp
app.MapFallback(() => Results.Json(new { message = "Hello, World!" }));
```

`MapFallback` registers a handler that matches any request that no other route handled — every path and every HTTP method. This mirrors the Go version, which registered a handler on `/` with the default mux (a pattern that matches everything). In later labs we will use `MapGet`, `MapPost`, etc. to handle specific paths and methods.

`Results.Json(...)` serializes the anonymous object to JSON and sets the `Content-Type` header to `application/json` for us.

### Starting the Server

```csharp
Console.WriteLine("Server starting on :8080");
app.Run("http://0.0.0.0:8080");
```

`app.Run` starts the Kestrel HTTP server and blocks until the application shuts down. Passing `http://0.0.0.0:8080` makes it listen on port 8080 on all network interfaces — important inside a Docker container so the port mapping works.

### The Handler Function

The lambda `() => Results.Json(new { message = "Hello, World!" })` is a **minimal API** handler. ASP.NET Core inspects its parameters and return value:

1. It takes no parameters, so nothing is bound from the request.
2. It returns an `IResult` (`Results.Json`), which writes the status code, headers, and JSON body to the response.

The anonymous object `new { message = "Hello, World!" }` is serialized with `System.Text.Json` using camelCase property names, producing `{"message":"Hello, World!"}`.

## Exercises

1. **Add a `/health` endpoint** - Use `app.MapGet("/health", ...)` to respond with `{"status": "ok"}`. This is a common pattern used by load balancers and orchestrators to check if a service is running.

2. **Add a `/time` endpoint** - Create a handler that returns the current server time. Use `DateTime.UtcNow.ToString("o")` (ISO 8601 / RFC 3339 format) and return it as `{"current_time": "2026-03-09T10:30:00Z"}`.

3. **Add your name to the response** - Modify the handler to include an `author` field in the JSON response: `{"message": "Hello, World!", "author": "Your Name"}`.

## Key Concepts

### HTTP Methods

HTTP defines several request methods (also called verbs). The most common ones are:

- **GET** - Retrieve data from the server
- **POST** - Send data to the server to create a resource
- **PUT** - Update an existing resource
- **DELETE** - Remove a resource

In this lab, our handler responds to all HTTP methods. In later labs, you will learn how to handle specific methods with `MapGet`, `MapPost`, `MapPut`, and `MapDelete`.

### JSON Response

JSON (JavaScript Object Notation) is the most common format for API responses. In ASP.NET Core, `System.Text.Json` converts .NET objects (anonymous objects, records, classes) into JSON strings automatically.

### Content-Type Header

The `Content-Type` header tells the client what format the response body is in. For JSON APIs, this should be `application/json`. `Results.Json` (and minimal APIs returning objects) set this header for you.

## Cleanup

When you are done, stop and remove the containers:

```bash
docker compose down
```
