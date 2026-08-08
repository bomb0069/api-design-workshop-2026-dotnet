using System.Text.Json;
using Npgsql;

// The key management (control) plane: create, list, rotate, revoke, audit.
// In production this sits behind real operator auth (SSO, mTLS, IAM); here a
// static admin token stands in so the lab can focus on the key lifecycle.
public static class AdminHandlers
{
    public static async Task<IResult> CreateKey(HttpRequest request, NpgsqlDataSource db)
    {
        CreateKeyRequest? input;
        try
        {
            input = await request.ReadFromJsonAsync<CreateKeyRequest>();
        }
        catch (JsonException)
        {
            input = null;
        }
        if (input is null || string.IsNullOrWhiteSpace(input.ClientName))
            return Results.Json(new ErrorResponse("client_name is required"), statusCode: 400);
        if (input.ExpiresInDays is < 1)
            return Results.Json(new ErrorResponse("expires_in_days must be at least 1"), statusCode: 400);

        // Generate the raw key, store ONLY its hash, and hand the raw key
        // back exactly once. After this response it cannot be recovered.
        var rawKey = Keys.NewRawKey();
        DateTime? expiresAt = input.ExpiresInDays is int days ? DateTime.UtcNow.AddDays(days) : null;
        var key = await Db.InsertKey(db, Keys.Sha256Hex(rawKey), Keys.Prefix(rawKey),
            input.ClientName.Trim(), input.Scopes ?? [], expiresAt, rotatedFrom: null);

        return Results.Json(ToIssued(key, rawKey), statusCode: 201);
    }

    public static async Task<IResult> ListKeys(NpgsqlDataSource db)
    {
        var keys = await Db.ListKeys(db);
        // Only prefixes and metadata leave the admin API — never key_hash.
        return Results.Json(keys.Select(k => new KeySummary
        {
            Id = k.Id,
            KeyPrefix = k.KeyPrefix,
            ClientName = k.ClientName,
            Scopes = k.Scopes,
            Status = StatusOf(k),
            CreatedAt = k.CreatedAt,
            ExpiresAt = k.ExpiresAt,
            RevokedAt = k.RevokedAt,
            RotatedFrom = k.RotatedFrom
        }));
    }

    // Zero-downtime rotation: issue a NEW key for the same client and scopes,
    // and give the OLD key a grace window instead of killing it instantly.
    // During the grace period BOTH keys authenticate, so clients can deploy
    // the new key at their own pace before the old one expires.
    public static async Task<IResult> RotateKey(int id, NpgsqlDataSource db, double graceHours)
    {
        var oldKey = await Db.FindKeyById(db, id);
        if (oldKey is null)
            return Results.Json(new ErrorResponse("API key not found"), statusCode: 404);
        if (oldKey.RevokedAt is not null)
            return Results.Json(new ErrorResponse("cannot rotate a revoked key"), statusCode: 409);

        var rawKey = Keys.NewRawKey();
        var newKey = await Db.InsertKey(db, Keys.Sha256Hex(rawKey), Keys.Prefix(rawKey),
            oldKey.ClientName, oldKey.Scopes, expiresAt: null, rotatedFrom: oldKey.Id);

        // Old key: expires after the grace window — unless it was already
        // going to expire sooner, in which case the earlier deadline stands.
        var graceExpiry = DateTime.UtcNow.AddHours(graceHours);
        var oldExpiry = oldKey.ExpiresAt is DateTime existing && existing < graceExpiry ? existing : graceExpiry;
        await Db.SetExpiry(db, oldKey.Id, oldExpiry);

        return Results.Json(new RotateResponse
        {
            NewKey = ToIssued(newKey, rawKey),
            OldKey = new OldKeyInfo { Id = oldKey.Id, KeyPrefix = oldKey.KeyPrefix, ExpiresAt = oldExpiry },
            GracePeriodHours = graceHours
        });
    }

    public static async Task<IResult> RevokeKey(int id, NpgsqlDataSource db)
    {
        var key = await Db.FindKeyById(db, id);
        if (key is null)
            return Results.Json(new ErrorResponse("API key not found"), statusCode: 404);
        if (key.RevokedAt is not null)
            return Results.Json(new ErrorResponse("API key already revoked"), statusCode: 409);

        var revokedAt = await Db.RevokeKey(db, id);
        return Results.Json(new
        {
            id = key.Id,
            key_prefix = key.KeyPrefix,
            revoked_at = revokedAt,
            note = "Revocation is immediate: the next request with this key gets 401."
        });
    }

    public static async Task<IResult> Usage(int id, NpgsqlDataSource db)
    {
        var key = await Db.FindKeyById(db, id);
        if (key is null)
            return Results.Json(new ErrorResponse("API key not found"), statusCode: 404);
        return Results.Json(await Db.ListUsage(db, id));
    }

    private static IssuedKeyResponse ToIssued(ApiKeyRecord key, string rawKey) => new()
    {
        Id = key.Id,
        ApiKey = rawKey,
        KeyPrefix = key.KeyPrefix,
        ClientName = key.ClientName,
        Scopes = key.Scopes,
        CreatedAt = key.CreatedAt,
        ExpiresAt = key.ExpiresAt
    };

    private static string StatusOf(ApiKeyRecord key)
    {
        if (key.RevokedAt is not null) return "revoked";
        if (key.ExpiresAt is DateTime expires && expires <= DateTime.UtcNow) return "expired";
        return "active";
    }
}
