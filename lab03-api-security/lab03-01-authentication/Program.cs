using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

// Mirrors main.go: read config, connect to Postgres, create tables, wire routes.
var secret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? "super-secret-key-change-in-production";
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=workshop;Username=postgres;Password=postgres";

var builder = WebApplication.CreateBuilder(args);

var dataSource = NpgsqlDataSource.Create(connectionString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(new JwtSettings(secret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep raw claim names ("user_id", "username") instead of the default
        // Microsoft claim-type mapping.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            // Replace the default challenge response with the same JSON errors
            // the Go AuthMiddleware returns.
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var authHeader = context.Request.Headers.Authorization.ToString();
                string message;
                if (string.IsNullOrEmpty(authHeader))
                {
                    message = "Authorization header required";
                }
                else
                {
                    var parts = authHeader.Split(' ', 2);
                    message = parts.Length != 2 || parts[0] != "Bearer"
                        ? "Invalid authorization format. Use: Bearer <token>"
                        : "Invalid or expired token";
                }
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ErrorResponse(message));
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Fails fast (like log.Fatal) if the database is unreachable.
Db.CreateTables(dataSource);

app.UseAuthentication();
app.UseAuthorization();

// Public routes
app.MapPost("/register", AuthHandlers.Register);
app.MapPost("/login", AuthHandlers.Login);
app.MapGet("/health", () => Results.Json(new { status = "ok" }));

// Protected routes
app.MapGet("/products", ProductHandlers.List).RequireAuthorization();
app.MapPost("/products", ProductHandlers.Create).RequireAuthorization();
app.MapGet("/products/{id}", ProductHandlers.Get).RequireAuthorization();
app.MapGet("/me", AuthHandlers.Me).RequireAuthorization();

Console.WriteLine("Server starting on :8080");
app.Run("http://0.0.0.0:8080");
