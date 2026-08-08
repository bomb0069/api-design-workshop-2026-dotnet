# Lab 04-09: Version Observability at the API Gateway (Kong)

Lab 04-08 answered "which version are clients calling?" by instrumenting the application — a Prometheus middleware inside the API. This lab answers the **same question with zero instrumentation code**: the versioned API sits behind **Kong**, and the gateway itself exports per-version traffic metrics.

```
                       ┌───────────────────────────────┐
 client ── :8080 ────▶ │             Kong               │        ┌────────────┐
                       │  route products-v1  (/api/v1)  │ ─────▶ │    api     │──▶ Postgres
                       │  route products-v2  (/api/v2)  │        │ (v1 + v2,  │
                       │  prometheus plugin             │        │ NO metrics │
                       └───────────────┬───────────────┘        │   code!)   │
                              :8100/metrics                      └────────────┘
                                       │
                                  Prometheus ──▶ Grafana
```

## Learning Objectives

- Observe the v1/v2 traffic split **at the edge** instead of inside the app
- Use Kong's **route names as the version dimension** in metrics
- Enable and read Kong's bundled `prometheus` plugin
- Decide when gateway-level observability is enough — and when it isn't

## The Core Idea: Route = Version

One backend service, but each API version gets its own Kong route:

```yaml
services:
  - name: products-api
    url: http://api:8080
    routes:
      - name: products-v1          # <-- this name becomes the metric label
        paths: ["/api/v1"]
        strip_path: false          # pass the path through unchanged
      - name: products-v2
        paths: ["/api/v2"]
        strip_path: false
```

Kong's prometheus plugin labels every metric with the route that served the request:

```
kong_http_requests_total{service="products-api", route="products-v1", code="200", ...} 4521
kong_http_requests_total{service="products-api", route="products-v2", code="200", ...} 9820
```

That `route` label plays exactly the role the `version` label played in lab 04-08 — but it was produced by **configuration**, not code. The backend in this lab is the same versioned API as 04-08 with the Prometheus middleware **deleted** (`grep -r prometheus api/` finds nothing).

The plugin is enabled globally in `kong/kong.yml`:

```yaml
plugins:
  - name: prometheus
    config:
      status_code_metrics: true    # kong_http_requests_total{route,code,...}
      latency_metrics: true        # kong_{request,upstream,kong}_latency_ms
      bandwidth_metrics: true
      upstream_health_metrics: true
```

and served from Kong's **status listener** (`KONG_STATUS_LISTEN: 0.0.0.0:8100`), which Prometheus scrapes instead of the API:

```yaml
scrape_configs:
  - job_name: 'kong'
    static_configs:
      - targets: ['kong:8100']
```

## Run It

```bash
docker compose up --build
```

| Service | URL | Purpose |
|---------|-----|---------|
| Kong proxy | http://localhost:8080 | The only way to reach the API |
| Kong admin | http://localhost:8001 | Inspect routes/services (read-only) |
| Prometheus | http://localhost:9090 | Query the gateway metrics |
| Grafana | http://localhost:3000 | Dashboard "API Version Traffic (via Kong)" (admin/admin) |

Note the backend has no published port — all traffic (including the load test) flows through Kong, which is what makes the gateway's numbers trustworthy.

## Try It Out

### Generate traffic manually

```bash
for i in $(seq 1 10); do curl -s -o /dev/null -w "%{http_code} " http://localhost:8080/api/v1/products; done; echo
for i in $(seq 1 20); do curl -s -o /dev/null -w "%{http_code} " http://localhost:8080/api/v2/products; done; echo
```

### Run the load test

Same load test as lab 04-08, but pointed at the gateway:

```bash
docker compose --profile loadtest up loadtest

# Custom split / duration
V1_WEIGHT=50 V2_WEIGHT=50 docker compose --profile loadtest up loadtest
DURATION_SECONDS=300 REQUESTS_PER_SEC=10 docker compose --profile loadtest up loadtest
```

### Useful Prometheus queries

```promql
# Request rate per version (the route label IS the version)
sum by (route) (rate(kong_http_requests_total{route=~"products-v.*"}[1m]))

# V1 traffic share (%)
sum(rate(kong_http_requests_total{route="products-v1"}[1m]))
  / sum(rate(kong_http_requests_total{route=~"products-v.*"}[1m])) * 100

# Upstream latency per version (ms) — time the BACKEND took, as seen by Kong
sum by (route) (rate(kong_upstream_latency_ms_sum{route=~"products-v.*"}[1m]))
  / sum by (route) (rate(kong_upstream_latency_ms_count{route=~"products-v.*"}[1m]))

# Errors per version
sum by (route, code) (rate(kong_http_requests_total{route=~"products-v.*", code=~"5.."}[1m]))
```

The regex `route=~"products-v.*"` keeps the `health` and `lifecycle` routes out of the traffic-share denominator — the same "don't count unversioned requests" rule lab 04-08 implemented in code.

### Latency: three flavors

Kong splits latency into three histograms — a distinction an in-app middleware cannot make:

| Metric | What it measures |
|--------|------------------|
| `kong_request_latency_ms` | Total, as the client experienced it |
| `kong_upstream_latency_ms` | Time waiting for the backend — use this to compare v1 vs v2 performance |
| `kong_kong_latency_ms` | Overhead Kong itself added |

## Gateway vs In-App Observability

| | In-app middleware (lab 04-08) | Gateway (this lab) |
|---|------------------------------|--------------------|
| Code changes required | every service, every language | none |
| Covers | only instrumented apps | everything routed through the gateway |
| Version detection | framework knows the real resolved version | route/path mapping must mirror your versioning scheme |
| Latency breakdown | app-internal only | client vs upstream vs gateway overhead |
| Blind spots | traffic that bypasses the app? none | traffic that bypasses the gateway; per-handler detail inside the app |
| Works with header/query versioning | yes, automatically | needs header-based routing config, not just paths |

In practice mature platforms use **both**: gateway metrics for fleet-wide version traffic and SLAs, in-app metrics for handler-level detail. Note this lab's path-based routes only observe *URL* versioning — that's a real limitation, not a simplification (see exercise 3).

## Exercises

1. **Deprecation watch** — add a Grafana alert (or just a query) that fires when `products-v1` still receives traffic within 30 days of its sunset date, mirroring lab 04-08's deprecation workflow but with gateway data.
2. **Consumer dimension** — add Kong `key-auth` + consumers (like lab 03-06) and set the prometheus plugin's `per_consumer: true`; now answer "*which client* is still calling v1?" from metrics alone. Contrast with the cardinality warning in lab 04-08 — why is per-consumer safer at the gateway than per-user in the app?
3. **Header versioning** — the backend also accepts `X-Api-Version` (combined reader). Add a Kong route that matches on that header (`routes[].headers`) so header-versioned traffic is labeled correctly too.
4. **Kill switch rehearsal** — remove the `products-v1` route from `kong.yml` and restart Kong: v1 now dies at the edge (404) without touching the backend. What status code and body *should* a sunset version return instead? (See lab 04-07.)
