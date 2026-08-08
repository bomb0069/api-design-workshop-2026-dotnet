// JWT issuance and role resolution. Same token format as lab03-01
// (HS256, secret from JWT_SECRET, claims user_id/username) plus a "role" claim.
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

public static class Auth
{
    public const string RolePublic = "public";
    public const string RoleInternal = "internal";
    public const string RoleAdmin = "admin";

    public static readonly string Secret = Environment.GetEnvironmentVariable("JWT_SECRET")
        ?? "super-secret-key-change-in-production";

    public static string IssueToken(int userId, string username, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var header = new JwtHeader(new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var payload = new JwtPayload
        {
            { "user_id", userId },
            { "username", username },
            { "role", role },
            { "exp", DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds() }
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }

    /// <summary>
    /// Resolves the caller's role from the Authorization header.
    /// No header at all = anonymous caller = "public".
    /// A header that is present but malformed, invalid, or expired is an
    /// ERROR (401), never silently downgraded to public — a bad credential
    /// must not look like a working anonymous request.
    /// </summary>
    public static (string Role, IResult? Error) ResolveRole(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader))
            return (RolePublic, null);

        var parts = authHeader.Split(' ', 2);
        if (parts.Length != 2 || parts[0] != "Bearer")
            return ("", Results.Json(
                new ErrorResponse("Invalid authorization format. Use: Bearer <token>"), statusCode: 401));

        try
        {
            var handler = new JwtSecurityTokenHandler
            {
                // Keep raw claim names ("role") instead of Microsoft claim-type mapping.
                MapInboundClaims = false
            };
            var principal = handler.ValidateToken(parts[1], new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                ClockSkew = TimeSpan.Zero
            }, out _);

            var role = principal.FindFirstValue("role");
            // A valid token with an unknown role gets the least privilege.
            if (role != RoleInternal && role != RoleAdmin)
                role = RolePublic;
            return (role, null);
        }
        catch (Exception)
        {
            return ("", Results.Json(new ErrorResponse("Invalid or expired token"), statusCode: 401));
        }
    }
}
