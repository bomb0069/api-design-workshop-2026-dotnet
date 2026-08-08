// Entities (what we STORE) and DTOs (what we RETURN, per role).
// The whole lesson of this lab lives in the gap between the two.
using System.Text.Json.Serialization;

// ---------------------------------------------------------------------------
// Entities — the raw records. Full of PII and internal-only fields.
// These must NEVER be serialized directly (see /leaky/users/{id} for what
// happens when you do).
// ---------------------------------------------------------------------------

public record UserEntity
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("full_name")] public string FullName { get; init; } = "";
    [JsonPropertyName("email")] public string Email { get; init; } = "";
    [JsonPropertyName("phone")] public string Phone { get; init; } = "";
    [JsonPropertyName("citizen_id")] public string CitizenId { get; init; } = "";
    [JsonPropertyName("password_hash")] public string PasswordHash { get; init; } = "";
    [JsonPropertyName("credit_score")] public int CreditScore { get; init; }
    [JsonPropertyName("internal_notes")] public string InternalNotes { get; init; } = "";
}

public record PaymentEntity
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("user_id")] public int UserId { get; init; }
    [JsonPropertyName("card_number")] public string CardNumber { get; init; } = "";
    [JsonPropertyName("card_holder")] public string CardHolder { get; init; } = "";
    [JsonPropertyName("amount")] public double Amount { get; init; }
    [JsonPropertyName("currency")] public string Currency { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("internal_notes")] public string InternalNotes { get; init; } = "";
}

// ---------------------------------------------------------------------------
// User DTOs — three shapes for the SAME resource, chosen by caller role.
// ---------------------------------------------------------------------------

public record UserPublicDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("email")] string Email,   // masked
    [property: JsonPropertyName("phone")] string Phone);  // masked

public record UserInternalDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("email")] string Email,   // full
    [property: JsonPropertyName("phone")] string Phone);  // full

// Everything EXCEPT password_hash — even admins never get password hashes.
public record UserAdminDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("citizen_id")] string CitizenId,
    [property: JsonPropertyName("credit_score")] int CreditScore,
    [property: JsonPropertyName("internal_notes")] string InternalNotes);

// ---------------------------------------------------------------------------
// Payment DTOs
// ---------------------------------------------------------------------------

public record PaymentPublicDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] string Status);

public record PaymentInternalDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("card_number")] string CardNumber,  // masked "****1234"
    [property: JsonPropertyName("card_holder")] string CardHolder,
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] string Status);

public record PaymentAdminDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("card_number")] string CardNumber,  // full (fake) PAN
    [property: JsonPropertyName("card_holder")] string CardHolder,
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("internal_notes")] string InternalNotes);

// ---------------------------------------------------------------------------
// Request/response shapes
// ---------------------------------------------------------------------------

public record ErrorResponse([property: JsonPropertyName("error")] string Error);

public record TokenRequest(string? Role, string? Username);

public record TokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("role")] string Role);

public record PaymentInput(
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("card_number")] string? CardNumber,
    [property: JsonPropertyName("card_holder")] string? CardHolder,
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("currency")] string? Currency);

// Never echo the full card number back — even to the client that just sent it.
public record PaymentCreatedDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("card_number")] string CardNumber,  // masked
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] string Status);

// ---------------------------------------------------------------------------
// In-memory demo data. All PII is realistic-shaped but fake.
// ---------------------------------------------------------------------------

public static class Store
{
    public static readonly List<UserEntity> Users =
    [
        new UserEntity
        {
            Id = 1,
            Username = "somchai",
            FullName = "Somchai Jaidee",
            Email = "somchai.jaidee@example.com",
            Phone = "081-234-5678",
            CitizenId = "1-1012-34567-89-0",
            PasswordHash = "$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy",
            CreditScore = 720,
            InternalNotes = "VIP customer; disputed a charge in 2025-06, resolved."
        },
        new UserEntity
        {
            Id = 2,
            Username = "jane",
            FullName = "Jane Doe",
            Email = "jane.doe@example.com",
            Phone = "089-876-5432",
            CitizenId = "3-4567-89012-34-5",
            PasswordHash = "$2a$10$hACwQ5/HQI6FhbIISOUVeusy3sKyUDhSq36fF5d/54aULe9imRQvW",
            CreditScore = 655,
            InternalNotes = "Two failed KYC attempts before approval."
        }
    ];

    public static readonly List<PaymentEntity> Payments =
    [
        new PaymentEntity
        {
            Id = 1,
            UserId = 1,
            CardNumber = "4111 1111 1111 1234",
            CardHolder = "SOMCHAI JAIDEE",
            Amount = 2490.00,
            Currency = "THB",
            Status = "completed",
            InternalNotes = "Manual fraud review passed."
        },
        new PaymentEntity
        {
            Id = 2,
            UserId = 2,
            CardNumber = "5555 4444 3333 9876",
            CardHolder = "JANE DOE",
            Amount = 149.50,
            Currency = "THB",
            Status = "pending",
            InternalNotes = "Waiting for 3-D Secure confirmation."
        }
    ];
}
