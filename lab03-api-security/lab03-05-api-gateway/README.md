# Lab 03-05: API Gateway

In labs 03-01 and 03-02 you added authentication, rate limiting, and CORS *inside* one API. That works for one service — but with five services you would copy the same middleware five times, and rotating an API key would mean redeploying everything.

An **API gateway** moves those cross-cutting concerns to a single edge service. Backends stay small and focused on business logic; the gateway owns the route table, authenticates every caller, enforces one rate-limit budget, stamps a correlation ID on each request, and is the only container exposed to the outside world.

```
                        ┌─────────────────────────────┐
                        │           gateway            │
  client ── :8080 ────▶ │  auth ▸ rate limit ▸ logging │
                        │  CORS ▸ routing ▸ rewriting  │
                        └──────┬───────────────┬──────┘
                     internal  │               │  network only
                        ┌──────▼─────┐   ┌─────▼──────┐
                        │users-service│   │orders-service│
                        │   :8081     │   │   :8082      │
                        └────────────┘   └─────────────┘
```

## Learning Objectives

- Understand why cross-cutting concerns belong at the gateway, not in every service
- Route and rewrite paths with a config-driven route table (YARP)
- Authenticate at the edge and pass identity to backends via injected headers
- Distinguish **external** routes (public clients) from **internal** routes (service-to-service)
- Trace one request across services with a correlation ID

## Project Layout

```
lab03-05-api-gateway/
  docker-compose.yml       # gateway exposed on :8080; backends internal-only
  gateway/                 # YARP reverse proxy + centralized middleware
    appsettings.json       # the route table: match paths, clusters, rewrites
    Program.cs             # auth, rate limiting, correlation ID, logging, CORS
  users-service/           # plain backend — no auth code at all
  orders-service/          # plain backend — no auth code at all
```

The gateway uses [YARP](https://microsoft.github.io/reverse-proxy/) (Yet Another Reverse Proxy), Microsoft's reverse-proxy library for ASP.NET Core. Routes live in `appsettings.json`; middleware in `Program.cs` runs before requests are proxied.

## Route Table

| Public path | Auth class | Rewritten to | Backend |
|-------------|-----------|--------------|---------|
| `/api/users`, `/api/users/{id}` | external | `/users…` | users-service |
| `/api/orders`, `/api/orders/{id}` | external | `/orders…` | orders-service |
| `/internal/users/…` | internal | `/users/…` | users-service |
| `/internal/orders/…` | internal | `/orders/…` | orders-service |

**Path rewriting**: clients call `/api/users`, but the backend serves `/users`. The public URL shape is a gateway decision — backends can be reorganized, split, or replaced without breaking clients.

## API Keys

| Key | Client | Class |
|-----|--------|-------|
| `demo-key-mobile` | mobile-app | external |
| `demo-key-partner` | partner-web | external |
| `internal-secret-key` | billing-service | internal |

External keys work on `/api/*` routes. The internal key also unlocks `/internal/*` routes — a common pattern: strict edge controls for public consumers, lighter controls for trusted service-to-service traffic.

## Rate Limit Policies: per-client vs global

Each route declares its policy in `appsettings.json` metadata (`"RateLimitPolicy"`), and the gateway middleware picks the matching limiter:

| Policy | Who shares the bucket | Question it answers | Used on |
|--------|----------------------|--------------------|---------|
| `per-client` | each authenticated client has its own (10 req/**min**; internal exempt) | **Fairness** — no client may hog the API | `/api/users` |
| `global` | *everyone together*, internal included (10 req/**s** = 600 req/min) | **Capacity** — orders-service can only take so much load | `/api/orders`, `/internal/orders` |

**Code to code** — both live in `gateway/Program.cs`. The difference is *partitioned by client* vs *one shared bucket*, and *per-minute* vs *per-second*:

```csharp
// PER-CLIENT (fairness): a bucket FACTORY —          // GLOBAL (capacity): ONE bucket object —
// each distinct clientName gets its own bucket.      // every request drains the same instance.
var rateLimiter =                                     var globalCapacityLimiter =
    PartitionedRateLimiter.Create<string, string>(        new TokenBucketRateLimiter(
        clientName =>                                         new TokenBucketRateLimiterOptions
        RateLimitPartition.GetTokenBucketLimiter(             {
            clientName,        // <-- WHO: per key            TokenLimit = 10,       // burst
            _ => new TokenBucketRateLimiterOptions            TokensPerPeriod = 10,  // 10 back...
            {                                                 ReplenishmentPeriod =
                TokenLimit = 10,                                  TimeSpan.FromSeconds(1), // ...per s
                TokensPerPeriod = 1,   // 1 back...           QueueLimit = 0,
                ReplenishmentPeriod =                         AutoReplenishment = true,
                    TimeSpan.FromSeconds(6), // ...per 6s // });
                QueueLimit = 0,                           // = 600 req/min TOTAL, all clients
                AutoReplenishment = true,                 //   + internal traffic combined
            }));
// = 10 req/min EACH client, internal exempt
```

And in the middleware, acquiring differs the same way:

```csharp
// per-client: which bucket depends on who calls     // global: no "who" — same bucket always
rateLimiter.AcquireAsync(clientName, 1);             globalCapacityLimiter.AttemptAcquire(1);
```

The rate is `TokensPerPeriod / ReplenishmentPeriod`, so per-second, per-minute, or per-hour windows are all expressible; a per-second window also smooths bursts (600/min arriving in one burst can hurt even if the average is fine). Note the deliberate difference in who is counted: fairness limits exempt trusted internal traffic, capacity limits count **everything** — the database doesn't care who the query came from. Responses carry `X-RateLimit-Policy` so you can see which policy handled the request.

## Run It

```bash
docker compose up --build
```

## Try It

**1. No API key → 401 (the gateway rejects it; backends never see the request):**

```bash
curl -i http://localhost:8080/api/users
```

**2. Valid key → proxied to users-service:**

```bash
curl -s -H "X-Api-Key: demo-key-mobile" http://localhost:8080/api/users
```

**3. See what the backend actually received:**

```bash
curl -s -H "X-Api-Key: demo-key-mobile" http://localhost:8080/api/users/headers
```

Note in the response: `X-Client-Name` and `X-Request-Id` were **injected by the gateway**, and `X-Api-Key` is **gone** — backends receive identity, never credentials.

**4. Internal route with an external key → 403:**

```bash
curl -i -H "X-Api-Key: demo-key-mobile" http://localhost:8080/internal/orders/
```

**5. Create an order — the customer comes from the authenticated client, not the body:**

```bash
curl -s -X POST http://localhost:8080/api/orders \
  -H "X-Api-Key: demo-key-partner" \
  -H "Content-Type: application/json" \
  -d '{"item": "Monitor", "amount": 7990}'
```

**6. Burn through the per-client rate limit (11th request within a minute → 429):**

```bash
for i in $(seq 1 11); do
  curl -s -o /dev/null -w "%{http_code} " -H "X-Api-Key: demo-key-mobile" http://localhost:8080/api/users
done; echo
```

`demo-key-partner` still gets 200s on `/api/users` — per-client buckets are independent.

**6b. The GLOBAL limit on `/api/orders` — different keys share ONE bucket:**

```bash
for i in $(seq 1 6); do curl -s -o /dev/null -w "%{http_code} " -H "X-Api-Key: demo-key-mobile"  http://localhost:8080/api/orders; done
for i in $(seq 1 6); do curl -s -o /dev/null -w "%{http_code} " -H "X-Api-Key: demo-key-partner" http://localhost:8080/api/orders; done; echo
```

Twelve rapid requests from **two different clients**: the last ones get `429 {"error":"server at capacity, retry later"}` because both clients drain the same 10 req/s bucket (even the internal key counts here — try it on `/internal/orders/`). Wait a second and it recovers.

**7. Backends are unreachable directly (the whole point):**

```bash
curl --max-time 3 http://localhost:8081/users   # connection refused/timeout — no published port
```

**8. Follow one request across services** — watch the compose logs and note the same `rid=` on the gateway line and the backend line:

```
gateway-1        | [gateway] GET /api/orders -> 200 (12 ms) client=partner-web rid=3f9c2a71b04e
orders-service-1 | [orders-service] GET /orders -> 200 client=partner-web rid=3f9c2a71b04e
```

## Key Concepts

| Concept | Where in this lab |
|---------|-------------------|
| Single entry point | Only `gateway` has a `ports:` mapping in docker-compose |
| Centralized auth | API key middleware in `gateway/Program.cs`; backends have zero auth code |
| Identity propagation | Gateway strips `X-Api-Key`, injects `X-Client-Name` |
| Centralized rate limiting | Per-client buckets (fairness) on `/api/users`; one global bucket (capacity, 10 req/s) on orders routes |
| Internal vs external | Route metadata `AuthClass` in `appsettings.json` |
| Policy per route | Route metadata `RateLimitPolicy`: `per-client` or `global` |
| Path rewriting | `PathSet` / `PathPattern` transforms in the route table |
| Correlation ID | `X-Request-Id` created at the edge, logged by every service |

## Trade-offs

A gateway is not free: it adds a network hop, is a single point of failure (run replicas in production), and can become a dumping ground for business logic — keep it to *cross-cutting* concerns only. Managed equivalents of what you built here: Kong, AWS API Gateway, Azure API Management, nginx/Envoy.

## Exercises

1. **Add a third backend** — a `products-service` with `GET /products`. Add its cluster and routes to `appsettings.json` (external only). No gateway code changes should be needed — that's the point of a config-driven route table.
2. **Layer the policies** — production gateways usually enforce *both*: make orders routes check the global capacity bucket **and** the per-client fairness bucket, so one client can't consume the whole 10 req/s capacity by itself.
3. **Key rotation** — add a second key for `mobile-app` that maps to the same client name. Confirm both keys share one rate-limit bucket (partitioning is by client, not by key).
4. **JWT at the edge** — replace API keys on the external routes with the JWT validation from lab03-01: validate the token at the gateway and forward the username as `X-User-Name`.
