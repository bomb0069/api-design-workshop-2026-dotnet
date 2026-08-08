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

External keys work on `/api/*` routes and share a rate-limit budget of **10 requests/minute** (token bucket: 10 burst, 1 token per 6 s). The internal key also unlocks `/internal/*` routes and is **not** rate limited — a common pattern: strict edge controls for public consumers, lighter controls for trusted service-to-service traffic.

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

**6. Burn through the rate limit (11th request within a minute → 429):**

```bash
for i in $(seq 1 11); do
  curl -s -o /dev/null -w "%{http_code} " -H "X-Api-Key: demo-key-mobile" http://localhost:8080/api/users
done; echo
```

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
| Centralized rate limiting | One token bucket per client across *all* backends |
| Internal vs external | Route metadata `AuthClass` in `appsettings.json` |
| Path rewriting | `PathSet` / `PathPattern` transforms in the route table |
| Correlation ID | `X-Request-Id` created at the edge, logged by every service |

## Trade-offs

A gateway is not free: it adds a network hop, is a single point of failure (run replicas in production), and can become a dumping ground for business logic — keep it to *cross-cutting* concerns only. Managed equivalents of what you built here: Kong, AWS API Gateway, Azure API Management, nginx/Envoy.

## Exercises

1. **Add a third backend** — a `products-service` with `GET /products`. Add its cluster and routes to `appsettings.json` (external only). No gateway code changes should be needed — that's the point of a config-driven route table.
2. **Per-route rate limits** — give `/api/orders` a stricter budget than `/api/users` (hint: partition the rate limiter by `clientName + routeId` and read the route's `RateLimit` metadata, like `AuthClass`).
3. **Key rotation** — add a second key for `mobile-app` that maps to the same client name. Confirm both keys share one rate-limit bucket (partitioning is by client, not by key).
4. **JWT at the edge** — replace API keys on the external routes with the JWT validation from lab03-01: validate the token at the gateway and forward the username as `X-User-Name`.
