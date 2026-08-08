using System.Text.Json.Serialization;

// A key row as loaded from the database. Note there is no RawKey field —
// after creation the server only ever sees the SHA-256 hash.
public record ApiKeyRecord(
    int Id,
    string KeyHash,
    string KeyPrefix,
    string ClientName,
    string[] Scopes,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    int? RotatedFrom);

public record ErrorResponse([property: JsonPropertyName("error")] string Error);

// ---- Admin request/response shapes ----------------------------------------

public record CreateKeyRequest(
    [property: JsonPropertyName("client_name")] string? ClientName,
    [property: JsonPropertyName("scopes")] string[]? Scopes,
    [property: JsonPropertyName("expires_in_days")] int? ExpiresInDays);

// Returned ONCE from create/rotate — the only time the raw key ever leaves
// the server. Everywhere else only the prefix is shown.
public record IssuedKeyResponse
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("api_key")] public string ApiKey { get; init; } = "";
    [JsonPropertyName("key_prefix")] public string KeyPrefix { get; init; } = "";
    [JsonPropertyName("client_name")] public string ClientName { get; init; } = "";
    [JsonPropertyName("scopes")] public string[] Scopes { get; init; } = [];
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("expires_at")] public DateTime? ExpiresAt { get; init; }
    [JsonPropertyName("warning")] public string Warning { get; init; } =
        "Store this key now. It is shown only once and cannot be recovered — only its hash is stored.";
}

// What GET /admin/keys shows: prefix for identification, never the hash.
public record KeySummary
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("key_prefix")] public string KeyPrefix { get; init; } = "";
    [JsonPropertyName("client_name")] public string ClientName { get; init; } = "";
    [JsonPropertyName("scopes")] public string[] Scopes { get; init; } = [];
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("expires_at")] public DateTime? ExpiresAt { get; init; }
    [JsonPropertyName("revoked_at")] public DateTime? RevokedAt { get; init; }
    [JsonPropertyName("rotated_from")] public int? RotatedFrom { get; init; }
}

public record RotateResponse
{
    [JsonPropertyName("new_key")] public IssuedKeyResponse NewKey { get; init; } = new();
    [JsonPropertyName("old_key")] public OldKeyInfo OldKey { get; init; } = new();
    [JsonPropertyName("grace_period_hours")] public double GracePeriodHours { get; init; }
}

public record OldKeyInfo
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("key_prefix")] public string KeyPrefix { get; init; } = "";
    [JsonPropertyName("expires_at")] public DateTime? ExpiresAt { get; init; }
    [JsonPropertyName("note")] public string Note { get; init; } =
        "The old key keeps working until expires_at so clients can migrate without downtime.";
}

public record UsageEntry
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("api_key_id")] public int ApiKeyId { get; init; }
    [JsonPropertyName("method")] public string Method { get; init; } = "";
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("status_code")] public int StatusCode { get; init; }
    [JsonPropertyName("occurred_at")] public DateTime OccurredAt { get; init; }
}

// ---- Business resource -----------------------------------------------------
// Deliberately tiny and in-memory: the lesson of this lab is the key
// lifecycle, not the products.

public record Product(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("price")] double Price);

public record ProductInput(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("price")] double Price);
