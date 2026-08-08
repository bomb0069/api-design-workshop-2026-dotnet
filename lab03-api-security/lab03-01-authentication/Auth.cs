// Mirrors auth.go: register/login handlers, JWT creation, and the /me handler.
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

public record JwtSettings(string Secret);

public record User
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("email")] public string Email { get; init; } = "";
}

public record ErrorResponse([property: JsonPropertyName("error")] string Error);

public record RegisterInput(string? Username, string? Email, string? Password);

public record LoginInput(string? Username, string? Password);

public record TokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public static class AuthHandlers
{
    public static async Task<IResult> Register(HttpRequest request, NpgsqlDataSource db)
    {
        RegisterInput? input;
        try
        {
            input = await request.ReadFromJsonAsync<RegisterInput>();
        }
        catch (JsonException)
        {
            input = null;
        }
        if (input is null)
            return Results.Json(new ErrorResponse("Invalid request body"), statusCode: 400);

        if (string.IsNullOrEmpty(input.Username) || string.IsNullOrEmpty(input.Email) || string.IsNullOrEmpty(input.Password))
            return Results.Json(new ErrorResponse("Username, email, and password are required"), statusCode: 400);

        if (input.Password.Length < 6)
            return Results.Json(new ErrorResponse("Password must be at least 6 characters"), statusCode: 400);

        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(input.Password, workFactor: 10);

        try
        {
            await using var cmd = db.CreateCommand(
                "INSERT INTO users (username, email, password_hash) VALUES ($1, $2, $3) RETURNING id, username, email");
            cmd.Parameters.AddWithValue(input.Username);
            cmd.Parameters.AddWithValue(input.Email);
            cmd.Parameters.AddWithValue(hashedPassword);

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var user = new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                Email = reader.GetString(2)
            };
            return Results.Json(user, statusCode: 201);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Json(new ErrorResponse("Username or email already exists"), statusCode: 409);
        }
        catch (Exception)
        {
            return Results.Json(new ErrorResponse("Internal server error"), statusCode: 500);
        }
    }

    public static async Task<IResult> Login(HttpRequest request, NpgsqlDataSource db, JwtSettings jwt)
    {
        LoginInput? input;
        try
        {
            input = await request.ReadFromJsonAsync<LoginInput>();
        }
        catch (JsonException)
        {
            input = null;
        }
        if (input is null)
            return Results.Json(new ErrorResponse("Invalid request body"), statusCode: 400);

        User user;
        string passwordHash;
        await using (var cmd = db.CreateCommand(
            "SELECT id, username, email, password_hash FROM users WHERE username = $1"))
        {
            cmd.Parameters.AddWithValue(input.Username ?? "");
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Results.Json(new ErrorResponse("Invalid credentials"), statusCode: 401);

            user = new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                Email = reader.GetString(2)
            };
            passwordHash = reader.GetString(3);
        }

        if (!BCrypt.Net.BCrypt.Verify(input.Password ?? "", passwordHash))
            return Results.Json(new ErrorResponse("Invalid credentials"), statusCode: 401);

        // Same claims and expiry as the Go lab: user_id, username, exp = now + 24h.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));
        var header = new JwtHeader(new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var payload = new JwtPayload
        {
            { "user_id", user.Id },
            { "username", user.Username },
            { "exp", DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds() }
        };
        var tokenString = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));

        return Results.Json(new TokenResponse(tokenString, 86400));
    }

    public static async Task<IResult> Me(ClaimsPrincipal principal, NpgsqlDataSource db)
    {
        var userId = int.Parse(principal.FindFirstValue("user_id") ?? "0");

        var user = new User();
        await using var cmd = db.CreateCommand("SELECT id, username, email FROM users WHERE id = $1");
        cmd.Parameters.AddWithValue(userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            user = new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                Email = reader.GetString(2)
            };
        }
        return Results.Json(user);
    }
}
