# Group 03 (API Security) Completion Plan

Goal: complete the API Security learning path so it covers identity, traffic control, data protection, credential lifecycle, edge enforcement, and message integrity.

## Current State

| # | Lab | Status | Covers |
|---|-----|--------|--------|
| 03-01 | Authentication | ✅ | JWT, password hashing, auth middleware |
| 03-02 | Rate Limiting & CORS | ✅ | per-IP + global token buckets, CORS |
| 03-03 | Sensitive Data Handling | ✅ | masking, field-level security, log scrubbing |
| 03-04 | API Key Management | ✅ | key lifecycle: create → rotate → revoke |
| 03-05 | API Gateway (YARP) | ✅ | centralized auth/limits, per-client vs global |
| 03-06 | API Gateway (Kong) | ✅ | same gateway as configuration |
| 03-07 | Request Signing (HMAC-SHA256) | ✅ | integrity + replay protection |
| 03-08 | OWASP API Top 10 Tour | ❌ optional | vulnerable-vs-fixed endpoint pairs |

Suggested learning order after completion: 01 (who are you) → 02 (how much may you call) → 03 (what may you see) → 04 (how credentials live and die) → 05/06 (enforce it all at the edge) → 07 (prove the message wasn't forged).

---

## lab03-03: Sensitive Data Handling

Scope (from the workshop gap-analysis plan):

- [x] Demo API: `users` + `payments` resources with realistic PII (email, phone, card number, national id)
- [x] Response masking helpers: `card: "****1234"`, `email: "j***@example.com"`, phone masking
- [x] Field-level security by role: the SAME endpoint returns public / internal / admin views (role from JWT claim, reusing the lab03-01 token format)
- [x] Log-scrubbing middleware: request/response logging that redacts configured fields (password, token, card) — prove it by grepping the logs
- [x] Rules section in README: secrets never in URLs, never in logs, don't over-expose ("return what the client needs, not the row")
- [x] Exercise: PII classification table + "find the leak" broken endpoint to fix
- [x] docker-compose (in-memory store is fine — the lesson is in the response shaping), build + runtime verify, README

## lab03-04: API Key Management

Scope (from the workshop gap-analysis plan):

- [x] Postgres `api_keys` table: `key_hash` (SHA-256 — never store raw keys), `client_name`, `scopes`, `created_at`, `expires_at`, `revoked_at`
- [x] Admin endpoints: `POST /admin/keys` (returns the raw key ONCE), `POST /admin/keys/{id}/rotate`, `DELETE /admin/keys/{id}` (revoke)
- [x] Key auth middleware: `Authorization: ApiKey <key>` header (never in URL), hash → lookup → expiry/revocation check
- [x] Dual-key rotation: rotating issues a new key while the old one keeps a grace period (e.g. 24 h) — zero-downtime rotation story
- [x] Scopes: e.g. `read:products` vs `write:products`, enforced per endpoint (403 on missing scope)
- [x] Per-key rate limiting (reuse the lab03-02 partitioned limiter, keyed by key id)
- [x] Audit trail: `api_key_usage` log table (key id, route, status, timestamp) + `GET /admin/keys/{id}/usage`
- [x] docker-compose with Postgres, build + runtime verify (full lifecycle in curl), README

## lab03-07: Request Signing with HMAC-SHA256

Scope (proposed — the client-to-API counterpart of lab09-01's webhook signing):

- [x] `api/` — validates `X-Signature` = HMAC-SHA256(secret, `METHOD\nPATH\nX-Timestamp\nBODY`), constant-time compare (`CryptographicOperations.FixedTimeEquals`)
- [x] Replay protection: reject `X-Timestamp` older/newer than 5 min; README exercise adds a nonce cache
- [x] `client/` — console app that signs correctly, printing the string-to-sign at each step
- [x] Failure-mode demos: missing signature (401), wrong secret (401), tampered body (401), stale timestamp (401), valid (200)
- [x] Hand-verifiable: README shows the same signature computed with `openssl dgst -sha256 -hmac`
- [x] README: API key vs HMAC comparison table (secret never travels, tamper-proof body, replay resistance); pointer to real-world versions (AWS SigV4, Stripe)
- [x] Exercise: move verification into the lab03-05 gateway
- [x] docker-compose, build + runtime verify, README

## lab03-08 (optional): OWASP API Security Top 10 Tour

- [ ] One API with paired endpoints: `/vulnerable/...` vs `/secure/...` for 4–5 of the OWASP API Top 10 (2023): BOLA/IDOR, broken authentication, excessive data exposure (links to 03-03), mass assignment, unrestricted resource consumption (links to 03-02)
- [ ] README maps each pair to its OWASP entry with an attack curl + the fix
- [ ] Decide scope before building — defensive teaching only, keep payloads illustrative

## Wrap-up (after labs land)

- [x] Update group README table + root README (mark ✅)
- [ ] Check off items in this file as they complete; delete or archive the file when the group is done
- [ ] Optional follow-up: port 03-03/03-04/03-07 to the Go repo for parity

## Build approach

One lab at a time (03-03 → 03-04 → 03-07), each: implement → `dotnet build` → runtime smoke test with curl against docker/local run → README → commit + push. Same verification bar as 03-05/03-06.
