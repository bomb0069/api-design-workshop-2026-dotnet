# Lab 03-01: Authentication

## Learning Objectives

- Implement JWT (JSON Web Token) authentication in ASP.NET Core
- Password hashing with bcrypt
- JWT bearer middleware for protecting routes
- Protected vs public routes
- Token-based API security

## Getting Started

```bash
docker compose up --build
```

The API will be available at `http://localhost:8080`.

To run locally without Docker (requires a PostgreSQL instance on `localhost:5432`):

```bash
dotnet run
```

## Test Workflow

### 1. Health Check (Public)

```bash
curl http://localhost:8080/health
```

### 2. Register a New User

```bash
curl -s -X POST http://localhost:8080/register \
  -H "Content-Type: application/json" \
  -d '{"username":"john","email":"john@example.com","password":"secret123"}'
```

Response:
```json
{"id":1,"username":"john","email":"john@example.com"}
```

### 3. Login to Get a Token

```bash
curl -s -X POST http://localhost:8080/login \
  -H "Content-Type: application/json" \
  -d '{"username":"john","password":"secret123"}'
```

Response:
```json
{"token":"eyJhbGciOiJIUzI1NiIs...","expires_in":86400}
```

Save the token value for subsequent requests.

### 4. Access Protected Route with Token

```bash
curl -s http://localhost:8080/products \
  -H "Authorization: Bearer <token>"
```

Replace `<token>` with the token from the login response.

### 5. Access Protected Route Without Token (401)

```bash
curl -s http://localhost:8080/products
```

Response:
```json
{"error":"Authorization header required"}
```

### 6. Get User Profile

```bash
curl -s http://localhost:8080/me \
  -H "Authorization: Bearer <token>"
```

Response:
```json
{"id":1,"username":"john","email":"john@example.com"}
```

### 7. Create a Product (Authenticated)

```bash
curl -s -X POST http://localhost:8080/products \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"name":"Keyboard","price":79.99,"category":"electronics"}'
```

## Code Walkthrough

### JWT Claims

When a user logs in, the server creates a JWT containing claims:

```csharp
var payload = new JwtPayload
{
    { "user_id", user.Id },
    { "username", user.Username },
    { "exp", DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds() }
};
var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
```

The token is signed with a secret key (`JWT_SECRET`) using HMAC-SHA256 and returned to the client. The token has three parts separated by dots: `header.payload.signature`.

### Bcrypt Password Hashing

Passwords are never stored in plain text. We use bcrypt (the `BCrypt.Net-Next` package) to hash them:

```csharp
// Hashing during registration
var hashedPassword = BCrypt.Net.BCrypt.HashPassword(input.Password, workFactor: 10);

// Comparing during login
var ok = BCrypt.Net.BCrypt.Verify(input.Password, passwordHash);
```

### JWT Bearer Middleware

Instead of a hand-rolled middleware, ASP.NET Core ships with `Microsoft.AspNetCore.Authentication.JwtBearer`. It intercepts requests, extracts the token from the `Authorization: Bearer <token>` header, validates the signature and expiry, and populates `HttpContext.User` with the token claims:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep raw claim names like "user_id"
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ClockSkew = TimeSpan.Zero
        };
    });
```

Routes opt in to protection with `RequireAuthorization()`, so the middleware only rejects requests to protected endpoints:

```csharp
app.MapGet("/products", ProductHandlers.List).RequireAuthorization();
app.MapGet("/me", AuthHandlers.Me).RequireAuthorization();
```

### Claims Principal

The authenticated user is available to handlers through the `ClaimsPrincipal`:

```csharp
public static async Task<IResult> Me(ClaimsPrincipal principal, NpgsqlDataSource db)
{
    var userId = int.Parse(principal.FindFirstValue("user_id") ?? "0");
    // load the full user from the database
}
```

## Exercises

1. **Role-Based Access Control** - Add a `role` field to the users table (e.g., `admin`, `user`). Include the role in the JWT claims. Use an authorization policy (`RequireAuthorization(policy => policy.RequireClaim("role", "admin"))`) so that only admins can create products.

2. **Refresh Token Endpoint** - Implement a `POST /refresh` endpoint that accepts a valid (non-expired) token and returns a new token with a refreshed expiration time. This allows clients to stay logged in without re-entering credentials.

3. **API Key Authentication** - Add an alternative authentication method using API keys. Create an `api_keys` table and a `POST /api-keys` endpoint (authenticated) to generate keys. Add a custom authentication handler that accepts either a Bearer token or an `X-API-Key` header.

4. **Token Revocation (Logout)** - Implement a `POST /logout` endpoint that adds the current token to a blacklist (in-memory dictionary or database table). Use the `OnTokenValidated` event of the JWT bearer middleware to check the blacklist before allowing access.

## Key Concepts

| Concept | Description |
|---------|-------------|
| **JWT** | JSON Web Token with three parts: header (algorithm), payload (claims), and signature. Stateless authentication -- the server does not need to store session data. |
| **Password Hashing** | Bcrypt is a one-way hashing algorithm designed for passwords. It includes a salt and a cost factor to resist brute-force attacks. |
| **Authentication Middleware** | `UseAuthentication()` runs the JWT bearer handler on every request; `RequireAuthorization()` marks which endpoints reject unauthenticated callers. |
| **Bearer Token** | An authentication scheme where the client sends the token in the `Authorization: Bearer <token>` header with each request. |

## Cleanup

```bash
docker compose down -v
```
