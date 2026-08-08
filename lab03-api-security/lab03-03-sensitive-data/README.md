# Lab 03-03: Sensitive Data Handling

Labs 03-01 and 03-02 controlled **who can call** the API. This lab is about the other half of API security: **what the API may expose**. The same `GET /users/1` can be perfectly authenticated and still be a data breach if it returns a citizen ID to a mobile app that only needed a display name.

Three techniques, all in one small service:

1. **Masking** — show enough to be useful, never the whole value (`****1234`, `s***@example.com`)
2. **Field-level security by role** — the *same* endpoint returns a different DTO shape per caller role
3. **Log scrubbing** — secrets in request bodies are `[REDACTED]` before they can reach a log line

```
                      GET /users/1
        ┌───────────────────┼─────────────────────┐
   no token            internal token         admin token
        │                   │                     │
  s***@example.com    somchai.jaidee@…      + citizen_id
  08x-xxx-5678        081-234-5678          + credit_score
                                            + internal_notes
                                            (password_hash: NEVER)
```

## Learning Objectives

- Classify fields as public / internal / restricted before designing responses
- Separate entities (what you store) from DTOs (what you return)
- Serve different response shapes from one endpoint based on a JWT role claim
- Mask PII with small, pure, testable functions
- Scrub sensitive fields from request logs with middleware
- Spot a "leaky" endpoint that serializes a raw entity

## PII Classification

Decide the classification **first**; the DTOs fall out of the table:

| Class | Fields in this lab | Who may see it |
|-------|--------------------|----------------|
| Public | `id`, `username`, `full_name`, masked email/phone, payment `amount`/`currency`/`status` | anyone |
| Internal | full `email`, full `phone`, masked `card_number` (`****1234`), `card_holder` | internal staff |
| Restricted | `citizen_id`, full `card_number`, `credit_score`, `internal_notes` | admin only |
| Never returned | `password_hash` | **no one — not even admin** |

The last row is the point of the lab: some fields have no legitimate read use-case. Password hashes are compared, never displayed; a response shape that *can* carry them is already a bug.

## Project Layout

```
lab03-03-sensitive-data/
  Program.cs        # routes + middleware wiring
  Models.cs         # entities (what we store) vs DTOs (what we return) + seed data
  Masking.cs        # pure masking functions: card, email, phone
  Auth.cs           # JWT issue/validate, role resolution (same format as lab03-01)
  Handlers.cs       # role-shaped reads, POST /payments, the leaky endpoint
  LogScrubbing.cs   # middleware: [REDACTED] before anything hits the log
```

## Run It

```bash
docker compose up --build
```

The API listens on `http://localhost:8080`. No database — demo data is in-memory and all PII is fake.

## Try It

**1. Get a token for each role.** `POST /auth/token` is a *teaching shortcut*: it issues a token for whatever role you ask for. Real systems derive the role from the authenticated identity (a user record, a group directory) — they never hand out roles on request.

```bash
INTERNAL=$(curl -s -X POST http://localhost:8080/auth/token \
  -H "Content-Type: application/json" -d '{"role": "internal"}' | jq -r .token)
ADMIN=$(curl -s -X POST http://localhost:8080/auth/token \
  -H "Content-Type: application/json" -d '{"role": "admin"}' | jq -r .token)
```

**2. One endpoint, three shapes.** Same URL, three different responses:

```bash
# public (no token): masked email and phone, nothing else
curl -s http://localhost:8080/users/1 | jq
```

```json
{ "id": 1, "username": "somchai", "full_name": "Somchai Jaidee",
  "email": "s***@example.com", "phone": "08x-xxx-5678" }
```

```bash
# internal: full contact details, still no citizen_id / credit_score
curl -s -H "Authorization: Bearer $INTERNAL" http://localhost:8080/users/1 | jq

# admin: everything EXCEPT password_hash
curl -s -H "Authorization: Bearer $ADMIN" http://localhost:8080/users/1 | jq
```

Note what the admin response still does **not** contain: `password_hash`. There is no role for which returning it is correct.

**3. Payments follow the same ladder** — public gets no card at all, internal gets `****1234`, admin gets the full (fake) number:

```bash
curl -s http://localhost:8080/payments/1 | jq
curl -s -H "Authorization: Bearer $INTERNAL" http://localhost:8080/payments/1 | jq
curl -s -H "Authorization: Bearer $ADMIN" http://localhost:8080/payments/1 | jq
```

(In real card systems even admins never see a full PAN — PCI DSS forbids storing it displayable. The admin tier here exists to demonstrate the role mechanism.)

**4. A bad token is a 401, not a downgrade.** A missing token means "anonymous, serve the public shape" — but a *wrong* token is an error. Silently downgrading a broken credential to public access would hide bugs and probing:

```bash
curl -i -H "Authorization: Bearer not-a-real-token" http://localhost:8080/users/1
```

**5. Log scrubbing.** Send a payment with a card number, then look at what the server logged:

```bash
curl -s -X POST http://localhost:8080/payments \
  -H "Content-Type: application/json" \
  -d '{"user_id": 1, "card_number": "4111 1111 1111 1234", "card_holder": "SOMCHAI JAIDEE", "amount": 500, "currency": "THB"}' | jq

docker compose logs api | grep REDACTED
```

```
[audit] POST /payments -> 201 body={"user_id":1,"card_number":"[REDACTED]","card_holder":"SOMCHAI JAIDEE","amount":500,"currency":"THB"}
```

The middleware buffers the request body, replaces any field named `password`, `token`, `card_number`, or `citizen_id` with `[REDACTED]`, and only then writes the audit line. Also note the *response*: the card comes back as `****1234` — never echo a full card number, not even to the client that just sent it.

**6. Find the leak.** One endpoint in this lab was written the lazy way:

```bash
curl -s http://localhost:8080/leaky/users/1 | jq
```

It returns the raw `UserEntity` — `password_hash`, `citizen_id`, `credit_score`, `internal_notes` — to an *unauthenticated* caller. This is exercise 1 below.

## Rules for Sensitive Data

| Rule | Why |
|------|-----|
| **Never put sensitive data in URLs** | URLs land in access logs, browser history, proxies, and `Referer` headers. `GET /users?citizen_id=1101234567890` is logged everywhere; a body or header is not. |
| **Never log secrets** | Logs are copied to log aggregators, backups, and third-party dashboards with far weaker access control than the database. Scrub *before* the log call, not after. |
| **Return what the client needs, not what the table has** | Serialize DTOs, never entities. Every field in a response is a liability you chose; every field in an entity is one you forgot to choose. |
| **Some fields are write/compare-only** | `password_hash` exists to be compared. No response shape should be able to carry it — not even for admins. |
| **Mask by default, reveal by role** | Start every DTO at the public shape and let roles *add* fields. Never start from the entity and try to remove fields. |

## Key Concepts

| Concept | Where in this lab |
|---------|-------------------|
| Entity vs DTO | `Models.cs` — `UserEntity` vs `UserPublicDto`/`UserInternalDto`/`UserAdminDto` |
| Field-level security | `role switch` in `Handlers.GetUser` / `GetPayment` |
| Role claim in JWT | `Auth.IssueToken` adds `"role"`; `Auth.ResolveRole` reads it |
| Pure masking helpers | `Masking.MaskCard` / `MaskEmail` / `MaskPhone` |
| Log scrubbing | `LogScrubbingMiddleware` in `LogScrubbing.cs` |
| The anti-pattern | `Handlers.LeakyGetUser` returning the entity |

## Exercises

1. **Fix `/leaky/users/{id}`** — it must never return `password_hash` again. The fix is two lines: resolve the role like `GetUser` does, and return the matching DTO instead of the entity. (Or better: delete the route — `/users/{id}` already exists.) Confirm with `curl` that `password_hash` is gone for every role.
2. **Mask a new field** — add `Masking.MaskCitizenId` (e.g. `1-1012-34567-89-0` → `x-xxxx-xxxxx-89-0`) with the same keep-the-tail style as `MaskPhone`, and give the *internal* user shape a masked `citizen_id` while admin keeps the full value.
3. **Move the role check to a policy** — replace the manual `Auth.ResolveRole` call with ASP.NET Core authorization: `AddAuthentication().AddJwtBearer(...)` + `AddAuthorization(o => o.AddPolicy("internal", p => p.RequireClaim("role", "internal", "admin")))`, then split the handler by shape. Compare: what got simpler, and what did you lose (the per-request "no token is still OK" nuance)?
4. **Scrub nested fields** — send `{"card": {"card_number": "4111..."}}` to `POST /payments` and check the log. The scrubber already handles nesting — read `Scrub()` in `LogScrubbing.cs` and explain why.
