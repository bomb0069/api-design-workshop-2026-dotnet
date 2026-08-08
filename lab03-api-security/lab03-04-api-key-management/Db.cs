using Npgsql;

// All persistence for the lab: the key store and the usage audit trail.
// Same raw-Npgsql style as lab03-02 — no ORM, so every query is visible.
public static class Db
{
    public static void CreateTables(NpgsqlDataSource db)
    {
        using (var cmd = db.CreateCommand(@"CREATE TABLE IF NOT EXISTS api_keys (
            id SERIAL PRIMARY KEY,
            key_hash TEXT NOT NULL UNIQUE,      -- SHA-256 hex of the raw key; the raw key is never stored
            key_prefix TEXT NOT NULL,           -- first characters of the raw key, for display only
            client_name TEXT NOT NULL,
            scopes TEXT[] NOT NULL DEFAULT '{}',
            created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            expires_at TIMESTAMPTZ,             -- NULL = never expires
            revoked_at TIMESTAMPTZ,             -- NULL = not revoked
            rotated_from INT                    -- id of the key this one replaced
        )"))
        {
            cmd.ExecuteNonQuery();
        }

        using (var cmd = db.CreateCommand(@"CREATE TABLE IF NOT EXISTS api_key_usage (
            id SERIAL PRIMARY KEY,
            api_key_id INT NOT NULL REFERENCES api_keys(id),
            method TEXT NOT NULL,
            path TEXT NOT NULL,
            status_code INT NOT NULL,
            occurred_at TIMESTAMPTZ NOT NULL DEFAULT now()
        )"))
        {
            cmd.ExecuteNonQuery();
        }
    }

    private const string KeyColumns =
        "id, key_hash, key_prefix, client_name, scopes, created_at, expires_at, revoked_at, rotated_from";

    private static ApiKeyRecord ReadKey(NpgsqlDataReader reader) => new(
        Id: reader.GetInt32(0),
        KeyHash: reader.GetString(1),
        KeyPrefix: reader.GetString(2),
        ClientName: reader.GetString(3),
        Scopes: reader.GetFieldValue<string[]>(4),
        CreatedAt: reader.GetDateTime(5),
        ExpiresAt: reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        RevokedAt: reader.IsDBNull(7) ? null : reader.GetDateTime(7),
        RotatedFrom: reader.IsDBNull(8) ? null : reader.GetInt32(8));

    public static async Task<ApiKeyRecord> InsertKey(
        NpgsqlDataSource db, string keyHash, string keyPrefix, string clientName,
        string[] scopes, DateTime? expiresAt, int? rotatedFrom)
    {
        await using var cmd = db.CreateCommand(
            $"INSERT INTO api_keys (key_hash, key_prefix, client_name, scopes, expires_at, rotated_from) " +
            $"VALUES ($1, $2, $3, $4, $5, $6) RETURNING {KeyColumns}");
        cmd.Parameters.AddWithValue(keyHash);
        cmd.Parameters.AddWithValue(keyPrefix);
        cmd.Parameters.AddWithValue(clientName);
        cmd.Parameters.AddWithValue(scopes);
        cmd.Parameters.AddWithValue((object?)expiresAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)rotatedFrom ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return ReadKey(reader);
    }

    // The auth lookup: the incoming raw key is hashed and matched against
    // key_hash. Equal-length hex strings make this an index-friendly lookup.
    public static async Task<ApiKeyRecord?> FindKeyByHash(NpgsqlDataSource db, string keyHash)
    {
        await using var cmd = db.CreateCommand($"SELECT {KeyColumns} FROM api_keys WHERE key_hash = $1");
        cmd.Parameters.AddWithValue(keyHash);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadKey(reader) : null;
    }

    public static async Task<ApiKeyRecord?> FindKeyById(NpgsqlDataSource db, int id)
    {
        await using var cmd = db.CreateCommand($"SELECT {KeyColumns} FROM api_keys WHERE id = $1");
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadKey(reader) : null;
    }

    public static async Task<List<ApiKeyRecord>> ListKeys(NpgsqlDataSource db)
    {
        var keys = new List<ApiKeyRecord>();
        await using var cmd = db.CreateCommand($"SELECT {KeyColumns} FROM api_keys ORDER BY id");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            keys.Add(ReadKey(reader));
        return keys;
    }

    public static async Task SetExpiry(NpgsqlDataSource db, int id, DateTime expiresAt)
    {
        await using var cmd = db.CreateCommand("UPDATE api_keys SET expires_at = $1 WHERE id = $2");
        cmd.Parameters.AddWithValue(expiresAt);
        cmd.Parameters.AddWithValue(id);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<DateTime?> RevokeKey(NpgsqlDataSource db, int id)
    {
        await using var cmd = db.CreateCommand(
            "UPDATE api_keys SET revoked_at = now() WHERE id = $1 AND revoked_at IS NULL RETURNING revoked_at");
        cmd.Parameters.AddWithValue(id);
        var result = await cmd.ExecuteScalarAsync();
        return result is DateTime revokedAt ? revokedAt : null;
    }

    // Audit trail: one row per authenticated request, whatever the outcome
    // (200, 403 missing scope, 429 rate limited — all of it).
    public static async Task RecordUsage(NpgsqlDataSource db, int apiKeyId, string method, string path, int statusCode)
    {
        await using var cmd = db.CreateCommand(
            "INSERT INTO api_key_usage (api_key_id, method, path, status_code) VALUES ($1, $2, $3, $4)");
        cmd.Parameters.AddWithValue(apiKeyId);
        cmd.Parameters.AddWithValue(method);
        cmd.Parameters.AddWithValue(path);
        cmd.Parameters.AddWithValue(statusCode);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<List<UsageEntry>> ListUsage(NpgsqlDataSource db, int apiKeyId, int limit = 50)
    {
        var entries = new List<UsageEntry>();
        await using var cmd = db.CreateCommand(
            "SELECT id, api_key_id, method, path, status_code, occurred_at FROM api_key_usage " +
            "WHERE api_key_id = $1 ORDER BY occurred_at DESC, id DESC LIMIT $2");
        cmd.Parameters.AddWithValue(apiKeyId);
        cmd.Parameters.AddWithValue(limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new UsageEntry
            {
                Id = reader.GetInt32(0),
                ApiKeyId = reader.GetInt32(1),
                Method = reader.GetString(2),
                Path = reader.GetString(3),
                StatusCode = reader.GetInt32(4),
                OccurredAt = reader.GetDateTime(5)
            });
        }
        return entries;
    }
}
