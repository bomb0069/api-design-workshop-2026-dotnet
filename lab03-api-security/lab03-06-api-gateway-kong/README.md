# Lab 03-06: API Gateway with Kong

In lab03-05 you **built** an API gateway in C# with YARP. This lab solves the exact same problem with **Kong**, an off-the-shelf open-source gateway — and you write **zero gateway code**. The entire gateway is one declarative YAML file.

Same architecture, same backends, same behavior:

```
                        ┌──────────────────────────────┐
  client ── :8080 ────▶ │            Kong               │
                        │  key-auth ▸ rate-limiting     │
  admin  ── :8001 ────▶ │  acl ▸ correlation-id ▸ cors  │──── kong.yml
                        └──────┬───────────────┬───────┘
                     internal  │               │  network only
                        ┌──────▼─────┐   ┌─────▼──────┐
                        │users-service│   │orders-service│
                        │   :8081     │   │   :8082      │
                        └────────────┘   └─────────────┘
```

## Learning Objectives

- Map gateway concepts to Kong's model: **service**, **route**, **plugin**, **consumer**
- Run Kong in **DB-less mode** — the whole gateway defined in `kong/kong.yml`
- Reproduce lab03-05's behavior with off-the-shelf plugins instead of middleware code
- Decide when to build a gateway (YARP) vs. adopt one (Kong)

## Kong's Model vs. lab03-05

| Concern | lab03-05 (YARP — you code it) | This lab (Kong — you configure it) |
|---------|-------------------------------|-------------------------------------|
| Routing & rewriting | route table + `PathPattern` transforms | `services` + `routes` with `strip_path` |
| Authentication | API-key middleware you wrote | `key-auth` plugin + `consumers` |
| Identity to backend | you inject `X-Client-Name` | Kong injects `X-Consumer-Username` |
| Credential stripping | you remove `X-Api-Key` | `hide_credentials: true` |
| Internal vs external | route metadata + middleware check | `acl` plugin with consumer groups |
| Rate limiting | `PartitionedRateLimiter` you wired | `rate-limiting` plugin (`limit_by` decides the scope) |
| Correlation ID | middleware generating `X-Request-Id` | `correlation-id` plugin |
| CORS | ASP.NET CORS middleware | `cors` plugin |

The backends are the same two services as lab03-05 with one change: they read Kong's `X-Consumer-Username` header instead of the hand-rolled `X-Client-Name`.

## API Keys

Same keys as lab03-05: `demo-key-mobile` (mobile-app), `demo-key-partner` (partner-web) — external; `internal-secret-key` (billing-service) — member of the `internal` ACL group, may call `/internal/*`.

## Rate Limit Scope in Kong: `limit_by` + plugin placement

Mirroring lab03-05, the two backends demonstrate the two scopes. In Kong the scope is a **configuration choice**, controlled by two things: `limit_by` (who shares a counter) and *where the plugin is attached* (which requests it covers).

**Config to config:**

```yaml
# PER-CONSUMER (fairness) — users-service          # GLOBAL (capacity) — orders-service
# plugin on the ROUTE, counted per consumer:       # plugin on the SERVICE, one counter for all:
routes:                                            services:
  - name: users-external                             - name: orders-service
    paths: ["/api/users"]                              url: http://orders-service:8082/orders
    plugins:                                           plugins:
      - name: rate-limiting                              - name: rate-limiting
        config:                                            config:
          minute: 10        # 10/min                         second: 10        # 10/s = 600/min
          policy: local                                      policy: local
          limit_by: consumer  # <-- WHO:                     limit_by: service   # <-- WHO:
                              #     each client                                  #     everyone
                              #     has its own                                  #     shares one
                              #     counter                                      #     counter
```

Two knobs, two different effects:

- **`limit_by`** — `consumer` gives every authenticated client its own counter; `service` counts all traffic to the service together (global); `ip` would give per-source-IP counters (the lab03-02 model), no auth needed.
- **Plugin placement** — a plugin on a *route* covers just that route (each instance keeps its own counter); on a *service* it covers **all** routes of that service with **one shared counter** (here: `/api/orders` *and* `/internal/orders` drain the same 10 req/s — internal traffic counts toward capacity, exactly like lab03-05); attached globally it would cover every route in the gateway.
- **Windows** — `second`, `minute`, `hour`, `day` are all first-class config keys; `second: 10` is the same average rate as `minute: 600` but never admits a 600-request burst.

## Run It

```bash
docker compose up --build
```

## Try It

**1. No API key → 401 (Kong's own error shape):**

```bash
curl -i http://localhost:8080/api/users
# {"message":"No API key found in request", ...}
```

**2. Valid key → proxied and rewritten (`/api/users` → `/users`):**

```bash
curl -s -H "X-Api-Key: demo-key-mobile" http://localhost:8080/api/users
```

**3. What the backend received** — Kong injected `X-Consumer-Username` and `X-Request-Id`; `X-Api-Key` is gone (`hide_credentials`):

```bash
curl -s -H "X-Api-Key: demo-key-mobile" http://localhost:8080/api/users/headers
```

**4. Internal route with an external key → 403 (ACL):**

```bash
curl -i -H "X-Api-Key: demo-key-mobile" http://localhost:8080/internal/orders
# {"message":"You cannot consume this service", ...}
curl -s -H "X-Api-Key: internal-secret-key" http://localhost:8080/internal/orders   # 200
```

**5. Per-consumer rate limit on `/api/users` — Kong sends standard `RateLimit-*` headers, then 429:**

```bash
for i in $(seq 1 11); do
  curl -s -o /dev/null -w "%{http_code} " -H "X-Api-Key: demo-key-partner" http://localhost:8080/api/users
done; echo
# {"message":"API rate limit exceeded", ...} on the 11th — but demo-key-mobile still gets 200s
```

**5b. GLOBAL rate limit on orders (10 req/s shared by everyone):**

```bash
for i in $(seq 1 6); do curl -s -o /dev/null -w "%{http_code} " -H "X-Api-Key: demo-key-mobile"  http://localhost:8080/api/orders; done
for i in $(seq 1 6); do curl -s -o /dev/null -w "%{http_code} " -H "X-Api-Key: demo-key-partner" http://localhost:8080/api/orders; done; echo
```

Two different consumers, twelve rapid requests — once 10 land inside the same second, the rest get 429, because `limit_by: service` makes them drain **one shared counter**. The internal key on `/internal/orders` counts against the same counter. Wait a second and it recovers.

> **Bucket vs window:** Kong's `rate-limiting` plugin is a **fixed-window counter** (10 per wall-clock second), not a token bucket like the .NET limiter in lab03-05 — so if your loop happens to straddle a second boundary you may see all 200s; run it again or add a couple more requests. (Kong's `rate-limiting-advanced` in the enterprise tier does sliding windows.)

**6. Explore the gateway through the Admin API:**

```bash
curl -s http://localhost:8001/routes   | python3 -m json.tool | grep '"name"'
curl -s http://localhost:8001/plugins  | python3 -m json.tool | grep '"name"'
curl -s http://localhost:8001/consumers
```

In DB-less mode the Admin API is read-only — configuration changes go through `kong.yml` + restart (or `deck sync` in real deployments), which makes the gateway config reviewable in git, exactly like the rest of your infrastructure.

## Build vs. Buy

| | Build (lab03-05, YARP) | Adopt (this lab, Kong) |
|---|------------------------|------------------------|
| Custom logic | Anything you can code | Plugin ecosystem (or write Lua/Go plugins) |
| Effort | You own auth/limits/logging code | Configuration only |
| Battle-tested edge cases | On you | Included (retry, health checks, load balancing…) |
| Ops footprint | One small ASP.NET container | Bigger container, its own upgrade cycle |
| Typical fit | Few services, .NET shop, special requirements | Many services, standard cross-cutting needs |

Managed cousins of Kong: AWS API Gateway, Azure API Management, Apigee. Self-hosted alternatives: nginx, Envoy, Traefik, KrakenD.

## Exercises

1. **Add a `products-service`** route + service in `kong.yml` — confirm no code changes are needed anywhere.
2. **Different limits per client** — Kong applies rate limits per plugin instance. Move the `rate-limiting` plugin from the route level to a consumer: give `partner-web` 100/min while `mobile-app` keeps 10/min.
3. **Response caching** — enable Kong's `proxy-cache` plugin on `/api/users` and prove cache hits with the `X-Cache-Status` response header.
4. **Key rotation** — add a second `keyauth_credentials` entry to `mobile-app`, verify both keys work, then remove the old one — the lifecycle pattern from lab03-04.
