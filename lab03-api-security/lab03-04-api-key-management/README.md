# Lab 03-04: API Key Management

## Learning Objectives

- Manage the full lifecycle of an API key: **create → use → rotate → revoke**
- Store keys safely: the database holds only a SHA-256 hash, never the raw key
- Return the raw key exactly **once** at creation, with a display prefix for later identification
- Attach **scopes** to keys and enforce them per endpoint (401 vs 403)
- Rotate keys with a **grace period** so clients migrate with zero downtime
- Rate limit **per key** (not per IP) with a token bucket
- Keep an **audit trail** of every authenticated request

## Why hash the keys?

If keys were stored raw, a database dump would be a credential dump: every row is a working key. Storing `SHA-256(raw_key)` instead means:

- **Auth still works** — hash the incoming key and look the hash up.
- **A leaked DB is useless** — hashes cannot be reversed into working keys.
- **Not even the operator can recover a key** — that is why create/rotate return the raw key exactly once with a "store this now" warning, and why "I lost my key" is answered with *rotation*, not *retrieval*.

A fast hash (SHA-256) is fine here — unlike passwords, these keys are 128-bit random values, so brute-forcing a preimage is hopeless and there is nothing for bcrypt's slowness to protect. The `key_prefix` column (first characters of the raw key, e.g. `ak_live_4f9a`) is stored alongside the hash so admins can tell keys apart without ever seeing them again.

## Key Lifecycle

```
POST /admin/keys
      |
      v
+-----------+   Authorization: ApiKey ak_live_...   +--------------------+
|  ACTIVE   |-------------------------------------->|  200 / 403 / 429   |
+-----------+     (every request audited)           +--------------------+
   |      |
   |      |  POST /admin/keys/{id}/rotate
   |      |  new key issued; old key gets expires_at = now + grace (24h)
   |      v
   |  +-----------------+  grace period elapses   +-----------+
   |  |  GRACE PERIOD   |------------------------>|  EXPIRED  |--> 401 "API key expired"
   |  | (BOTH keys work)|                         +-----------+
   |  +-----------------+
   |
   |  DELETE /admin/keys/{id}   (immediate, no grace)
   v
+-----------+
|  REVOKED  |--> 401 "API key revoked"
+-----------+
```

Rotation and revocation answer different questions:

- **Rotate** = "this key is *old*" (routine hygiene, suspected but unconfirmed exposure). The old key keeps working through a grace window so nothing breaks mid-migration.
- **Revoke** = "this key is *burned*" (confirmed leak, offboarded client). It dies immediately.

## Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/admin/keys` | `X-Admin-Token` | Create key → **201**, raw key returned once |
| GET | `/admin/keys` | `X-Admin-Token` | List keys (prefixes and metadata, never hashes) |
| POST | `/admin/keys/{id}/rotate` | `X-Admin-Token` | New key for same client/scopes; old key expires after grace |
| DELETE | `/admin/keys/{id}` | `X-Admin-Token` | Revoke immediately |
| GET | `/admin/keys/{id}/usage` | `X-Admin-Token` | Last 50 audit rows for the key |
| GET | `/products` | `Authorization: ApiKey <key>` | Requires scope `read:products` |
| POST | `/products` | `Authorization: ApiKey <key>` | Requires scope `write:products` |
| GET | `/health` | none | Liveness check |

> The static admin token (`X-Admin-Token: admin-secret`, env `ADMIN_TOKEN`) **stands in for real operator auth** — in production the control plane sits behind SSO, mTLS, or cloud IAM. The lesson here is the lifecycle of the keys being managed, not operator login.

### Configuration

| Env var | Default | Purpose |
|---------|---------|---------|
| `DATABASE_URL` | `Host=localhost;Database=workshop;Username=postgres;Password=postgres` | Postgres connection |
| `ADMIN_TOKEN` | `admin-secret` | Control-plane token |
| `KEY_ROTATION_GRACE_HOURS` | `24` | How long a rotated-out key keeps working |
| `APP_URL` | `http://0.0.0.0:8080` | Listen address override |

## Getting Started

```bash
docker compose up --build
```

The API will be available at `http://localhost:8080`.

To run locally without Docker (requires a PostgreSQL instance on `localhost:5432`):

```bash
dotnet run
```

## Walkthrough: a key's whole life

### 1. Create a key

```bash
curl -s -X POST http://localhost:8080/admin/keys \
  -H "X-Admin-Token: admin-secret" \
  -H "Content-Type: application/json" \
  -d '{"client_name":"mobile-app","scopes":["read:products","write:products"]}'
```

```json
{
  "id": 1,
  "api_key": "ak_live_113b9b02370cc97c79bb6a13a638052b",
  "key_prefix": "ak_live_113b",
  "client_name": "mobile-app",
  "scopes": ["read:products", "write:products"],
  "created_at": "2026-08-08T23:13:29Z",
  "expires_at": null,
  "warning": "Store this key now. It is shown only once and cannot be recovered — only its hash is stored."
}
```

Save the key — the walkthrough uses `$KEY` from here on:

```bash
KEY=ak_live_113b9b02370cc97c79bb6a13a638052b
```

Optional: pass `"expires_in_days": 30` to create a key that expires on its own.

### 2. Use it

```bash
curl -i http://localhost:8080/products -H "Authorization: ApiKey $KEY"
```

`200`, plus per-key rate-limit headers:

```
X-RateLimit-Limit: 10
X-RateLimit-Remaining: 9
X-RateLimit-Reset: 1786230881
```

The failure modes are all distinct on purpose:

```bash
# no key at all
curl http://localhost:8080/products
# → 401 {"error":"missing API key. Send 'Authorization: ApiKey <key>'"}

# a key that was never issued
curl http://localhost:8080/products -H "Authorization: ApiKey ak_live_0000...."
# → 401 {"error":"invalid API key"}

# key in the query string — rejected, with the reason
curl "http://localhost:8080/products?api_key=$KEY"
# → 400 {"error":"API keys must not be sent in the query string: query strings are
#         written to server logs, proxies, and browser history. Send the key in a
#         header instead: 'Authorization: ApiKey <key>'"}
```

### 3. Scopes: 401 vs 403

Create a **read-only** key and try to write with it:

```bash
RO_KEY=$(curl -s -X POST http://localhost:8080/admin/keys \
  -H "X-Admin-Token: admin-secret" -H "Content-Type: application/json" \
  -d '{"client_name":"reporting-batch","scopes":["read:products"]}' \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['api_key'])")

curl -X POST http://localhost:8080/products \
  -H "Authorization: ApiKey $RO_KEY" -H "Content-Type: application/json" \
  -d '{"name":"Mug","price":9.5}'
# → 403 {"error":"missing scope: write:products"}
```

`401` means "I don't know who you are"; `403` means "I know exactly who you are — and you may not do this." The 403 still lands in the audit trail, attributed to the key.

### 4. Rotate — the zero-downtime story

```bash
curl -s -X POST http://localhost:8080/admin/keys/1/rotate -H "X-Admin-Token: admin-secret"
```

```json
{
  "new_key": { "id": 3, "api_key": "ak_live_98e277b818c8...", "key_prefix": "ak_live_98e2", "...": "..." },
  "old_key": {
    "id": 1,
    "key_prefix": "ak_live_113b",
    "expires_at": "2026-08-09T23:13:51Z",
    "note": "The old key keeps working until expires_at so clients can migrate without downtime."
  },
  "grace_period_hours": 24
}
```

**Both keys now authenticate.** The old key was not killed — it was given a deadline (`now + 24h`, or its original expiry if that was sooner). Deploy the new key to your clients at your own pace; when the grace period ends the old key returns:

```bash
curl http://localhost:8080/products -H "Authorization: ApiKey $KEY"      # old key, during grace → 200
curl http://localhost:8080/products -H "Authorization: ApiKey $NEW_KEY"  # new key → 200
# ... 24 hours later, old key:
# → 401 {"error":"API key expired"}
```

To watch the expiry happen without waiting a day, restart with `KEY_ROTATION_GRACE_HOURS=0` and rotate — the old key dies on the next request. The new key row records `rotated_from: 1`, so `GET /admin/keys` shows the whole ancestry chain.

### 5. Revoke — the kill switch

```bash
curl -X DELETE http://localhost:8080/admin/keys/2 -H "X-Admin-Token: admin-secret"
# → 200 {"id":2, "key_prefix":"ak_live_3a38", "revoked_at":"...", "note":"Revocation is immediate: ..."}

curl http://localhost:8080/products -H "Authorization: ApiKey $RO_KEY"
# → 401 {"error":"API key revoked"}
```

No grace period: revocation is for keys that must stop working *now*. A revoked key can be neither revoked again nor rotated (both `409`).

### 6. Per-key rate limiting

Each key has its own token bucket: 10 requests burst, refilled at 10/minute.

```bash
for i in $(seq 1 12); do
  curl -s -o /dev/null -w "%{http_code} " http://localhost:8080/products \
    -H "Authorization: ApiKey $NEW_KEY"
done; echo
# 200 200 200 200 200 200 200 200 200 200 429 429
```

Because the bucket is keyed by **key id**, quotas follow the credential, not the caller's IP — two clients sharing an office NAT don't share a budget, and one client with two keys has two budgets.

### 7. Audit trail

Every authenticated request — including the 403s and 429s above — was written to `api_key_usage`:

```bash
curl -s http://localhost:8080/admin/keys/3/usage -H "X-Admin-Token: admin-secret"
```

```json
[
  {"id":18,"api_key_id":3,"method":"GET","path":"/products","status_code":429,"occurred_at":"..."},
  {"id":17,"api_key_id":3,"method":"GET","path":"/products","status_code":200,"occurred_at":"..."},
  {"id":16,"api_key_id":3,"method":"POST","path":"/products","status_code":403,"occurred_at":"..."}
]
```

This answers the operational questions keys exist for: *Is anyone still using the old key I rotated last week? What did the leaked key touch before I revoked it?* Failed auth (401) is **not** recorded here — an unknown or expired key cannot be attributed to a row in `api_keys`.

The final `GET /admin/keys` after the walkthrough tells the whole story at a glance:

```json
[
  {"id":1, "key_prefix":"ak_live_113b", "status":"active",  "rotated_from":null, "expires_at":"<grace deadline>"},
  {"id":2, "key_prefix":"ak_live_3a38", "status":"revoked", "rotated_from":null},
  {"id":3, "key_prefix":"ak_live_98e2", "status":"expired", "rotated_from":1},
  {"id":4, "key_prefix":"ak_live_5113", "status":"active",  "rotated_from":3}
]
```

## API Key vs JWT vs HMAC Signature

| | API Key (this lab) | JWT (lab 03-01) | HMAC request signing (lab 03-07) |
|---|---|---|---|
| What the client sends | Opaque random string | Signed, self-describing token | Per-request signature over the payload |
| Server-side state | Lookup on every request | None to validate (just the signing key) | Shared secret per client |
| Instant revocation | **Yes** — flip `revoked_at` | Hard — token is valid until `exp` unless you keep a denylist | Yes — remove the secret |
| Carries claims/scopes | In the database row | In the token itself | No — identifies and integrity-protects only |
| Protects request body | No — key just identifies the caller | No | **Yes** — tampered payload fails verification |
| Expiry | Optional, set server-side, changeable later | Baked into the token at issue time | Per-request timestamp window |
| Best for | Server-to-server, partner APIs, long-lived credentials with an operator kill switch | User sessions, short-lived delegated access | Webhooks, payment APIs — when the *content* must be provably untouched |

The per-request DB lookup is the price an API key pays for its superpower: the server can change its mind (revoke, expire, re-scope) at any moment, which stateless JWTs cannot do without reintroducing a lookup anyway.

## Exercises

1. **Shorten the grace period via config.** The default 24h grace comes from `KEY_ROTATION_GRACE_HOURS`. Run the stack with `KEY_ROTATION_GRACE_HOURS=0.01` (36 seconds), rotate a key, and watch the old key flip from `200` to `401 "API key expired"` in real time. What would a sensible grace period be for a mobile app whose users update over weeks?
2. **Add `last_used_at`.** Add a `last_used_at TIMESTAMPTZ` column to `api_keys` and update it in the auth middleware. Then extend `GET /admin/keys` to show it — now an operator can spot abandoned keys ("created 2 years ago, last used 8 months ago") and rotate the stale ones. Consider: should the update be fire-and-forget so it never slows down a request?
3. **IP allowlist per key.** Add an `allowed_ips TEXT[]` column (empty = allow all). In the middleware, after the hash lookup succeeds, reject requests from other addresses with `403 {"error":"IP not allowed for this key"}`. A stolen key that only works from the partner's datacenter egress IPs is a much smaller problem — and the rejected attempts land in the audit trail, telling you the key has leaked.
