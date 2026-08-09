# Lab 07-02: API Metrics with Prometheus + Grafana — the RED Method

## Overview

This lab instruments a small Products API with the three metrics that matter most for any request-driven service — **R**ate, **E**rrors, and **D**uration — and visualizes them on an auto-provisioned Grafana dashboard fed by Prometheus.

The API is deliberately imperfect so the dashboard has something to show:

| Endpoint | Behavior |
|----------|----------|
| `GET /api/products` | Fast, always 200 — the baseline traffic |
| `GET /api/products/{id}` | Adds 10–300ms of random latency; unknown ids return 404 |
| `POST /api/orders` | ~10% of valid orders fail with 500 (simulated flaky payment service); invalid bodies return 400 |
| `GET /health` | Health probe — **excluded** from metrics |
| `GET /metrics` | Prometheus scrape endpoint — also excluded from its own metrics |

## The RED Method

RED, coined by Tom Wilkie, defines the three signals to measure for **every service that handles requests**:

- **Rate** — how many requests per second is the service handling?
- **Errors** — how many of those requests are failing?
- **Duration** — how long do requests take (as a distribution, not an average)?

Why these three? Because together they answer the first question of every incident: *"is the service healthy from the caller's point of view?"* A service can have perfect CPU and memory graphs while returning 500s to every user — RED measures what users experience.

RED is not the only framework, and knowing when it applies is part of the lesson:

- **RED** fits *request-driven* services (APIs, RPC services, message consumers): measure the work being requested.
- **USE** (Utilization, Saturation, Errors) fits *resources* (CPUs, disks, connection pools, queues): measure the thing doing the work. You'd use USE for the database under this API, RED for the API itself.
- **The Four Golden Signals** (Google SRE book) = RED + **saturation**: latency, traffic, errors, and how "full" the service is. Saturation is the leading indicator that Rate/Duration are about to get worse.

## Metric Types: Counter vs Histogram

The whole lab is built on exactly two metrics, one of each fundamental type.

### Counter: `http_requests_total`

```
http_requests_total{method="GET", endpoint="/api/products/{id}", status="200"} 1547
```

A counter **only ever goes up** (it resets to zero only when the process restarts). The raw value — "1547 requests since startup" — is almost never what you want. What you want is the *rate of change*:

```promql
rate(http_requests_total[1m])   # requests per second, averaged over the last minute
```

This is why you always wrap counters in `rate()`: it converts an ever-growing total into a per-second rate, and it is robust against process restarts (Prometheus detects counter resets and compensates). Graphing a raw counter just gives you an ever-climbing line that tells you nothing.

Both the **R** and the **E** of RED come from this one counter — errors are simply the subset where the `status` label matches `5..`.

### Histogram: `http_request_duration_seconds`

A histogram records each observation into a set of **cumulative buckets**. One histogram becomes several series on `/metrics`:

```
http_request_duration_seconds_bucket{le="0.04", ...}  912   # requests that took <= 40ms
http_request_duration_seconds_bucket{le="0.16", ...} 1403   # requests that took <= 160ms
http_request_duration_seconds_bucket{le="+Inf", ...} 1547   # all requests
http_request_duration_seconds_sum{...}                 98.2  # total seconds spent
http_request_duration_seconds_count{...}             1547   # total observations
```

Why not just record the average (`_sum / _count`)? Because averages hide pain: if 99 requests take 10ms and 1 takes 5 seconds, the average is ~60ms and looks fine, while 1% of your users waited 5 seconds. Percentiles from a histogram surface that tail.

### How `histogram_quantile` works

```promql
histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))
```

Reading it inside-out:

1. `rate(..._bucket[5m])` — per-second rate of observations landing in each bucket, over the last 5 minutes.
2. `sum(...) by (le)` — aggregate across all endpoints/methods, but **keep the `le` (less-than-or-equal) label** — that label *is* the bucket boundary, and `histogram_quantile` cannot work without it.
3. `histogram_quantile(0.95, ...)` — find the bucket where the 95th percentile falls, then **linearly interpolate** within that bucket to estimate the value.

The key consequence: percentiles are **estimates whose accuracy depends on bucket layout**. If p95 lands in the bucket spanning 160–320ms, Grafana interpolates between those bounds — a p95 of "230ms" might really be anywhere in that range. This lab configures buckets to bracket the API's real latency:

```csharp
Buckets = Histogram.ExponentialBuckets(0.005, 2, 10)
// 5ms, 10ms, 20ms, 40ms, 80ms, 160ms, 320ms, 640ms, 1.28s, 2.56s
```

If all your requests fell into a single bucket, every percentile would be a wild guess — that is Exercise 1.

## Code Walkthrough: `Middleware/RedMetricsMiddleware.cs`

### 1. Define the metrics (once, statically)

```csharp
private static readonly Counter RequestsTotal = Metrics.CreateCounter(
    "http_requests_total",
    "Total HTTP requests, labeled by method, route template, and status code.",
    new CounterConfiguration { LabelNames = new[] { "method", "endpoint", "status" } });

private static readonly Histogram RequestDuration = Metrics.CreateHistogram(
    "http_request_duration_seconds",
    "HTTP request duration in seconds, labeled by method and route template.",
    new HistogramConfiguration
    {
        LabelNames = new[] { "method", "endpoint" },
        Buckets = Histogram.ExponentialBuckets(0.005, 2, 10),
    });
```

They are `static readonly` because a metric must exist **once per process** — every request must increment the same underlying series, regardless of how middleware instances are constructed.

Note the histogram has **no `status` label**: you rarely need duration percentiles per status code, and every label you add multiplies the series count (each series = ~10 bucket series for a histogram).

### 2. Time the request with the middleware sandwich

```csharp
public async Task InvokeAsync(HttpContext context)
{
    var sw = Stopwatch.StartNew();
    await _next(context);   // run the rest of the pipeline (routing + handler)
    sw.Stop();
    ...
}
```

Everything before `await _next` happens on the way in; everything after happens on the way out — when the status code is final and the stopwatch holds the full request duration.

### 3. Label by ROUTE TEMPLATE — the cardinality lesson

```csharp
// Skip observability plumbing — scrapes and health probes aren't user traffic.
if (path == "/metrics" || path == "/health")
    return;

// Route template, NOT the raw path.
var endpoint = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? path;
```

This is the single most important line in the lab. Every distinct **label combination** becomes its own time series in Prometheus, stored and indexed separately, forever (until it ages out).

- Labeling with the **route template**: `/api/products/1`, `/api/products/2`, … `/api/products/999999` all collapse into **one** series — `endpoint="/api/products/{id}"`. Series count = (routes × methods × statuses) — a few dozen, stable no matter how much traffic arrives.
- Labeling with the **raw path**: every distinct id mints a brand-new series. A crawler walking a million product ids creates a million series. Prometheus memory usage grows with series count, so this is a well-known production failure mode: the **cardinality explosion**. Prometheus slows down, eats RAM, and eventually gets OOM-killed — taken down not by traffic volume but by *label diversity*. The same trap hides in labels like `user_id`, `session_id`, `request_id`, or raw query strings: never put unbounded values in a metric label. High-cardinality questions ("which user saw the error?") belong in logs and traces, not metrics.

Two mechanical details make the route template available here:

- We read the endpoint **after** `await _next`, because routing (which decides the matched endpoint) runs inside the pipeline — before `_next` returns, `context.GetEndpoint()` would be `null`.
- `context.GetEndpoint()` returns the matched endpoint; casting to `RouteEndpoint` exposes `RoutePattern.RawText` — the literal template string (`/api/products/{id}`). If no route matched (a 404 on an unknown path), we fall back to the raw path.

### 4. Record

```csharp
RequestsTotal.WithLabels(method, endpoint, status).Inc();
RequestDuration.WithLabels(method, endpoint).Observe(sw.Elapsed.TotalSeconds);
```

`Program.cs` wires it all up — the middleware registered before the endpoints, and `app.MapMetrics()` exposing `/metrics` for Prometheus to scrape:

```csharp
app.UseMiddleware<RedMetricsMiddleware>();
app.MapMetrics();
```

## The Dashboard, Panel by Panel

The Grafana dashboard (`grafana/dashboards/red-method.json`, auto-provisioned, refreshes every 5s) has four panels:

### Panel 1 — Request Rate by Endpoint (R)

```promql
sum(rate(http_requests_total[1m])) by (endpoint)
```

Per-second rate for each counter series, summed per route template. Three lines — one per route. Because the `endpoint` label is a template, the by-id line stays a single clean series no matter which ids the load test hits.

### Panel 2 — Error Rate % (E)

```promql
sum(rate(http_requests_total{status=~"5.."}[1m])) / sum(rate(http_requests_total[1m])) * 100
```

The fraction of all requests answered with a 5xx status. Thresholds: **green** below 1%, **yellow** 1–5%, **red** at 5% and above. Only ~25% of load-test traffic is POSTs and ~10% of those fail, so expect this stat to hover around 2–3% (yellow). Note that 404s and 400s do **not** count — a client asking for a product that doesn't exist is the client's error; RED's E measures *the service* failing.

### Panel 3 — Duration p50 / p95 / p99 (D)

```promql
histogram_quantile(0.50, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))
histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))
histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))
```

Latency percentiles across all endpoints. The `/api/products/{id}` handler sleeps 10–300ms uniformly, so p50 lands well below p95 — a visible gap between the median experience and the tail.

### Panel 4 — Requests by Status

```promql
sum(rate(http_requests_total[1m])) by (status)
```

The same counter sliced by the `status` label: 200/201 (success), 400/404 (client errors), 500 (server errors) move as independent lines. This is the drill-down view for Panel 2 — when the error stat goes red, this panel tells you *which* statuses spiked.

## Running the Lab

```bash
cd lab07-observability/lab07-02-metrics
docker compose up --build
```

This starts three services:

| Service | URL | Purpose |
|---------|-----|---------|
| API | http://localhost:8080 | The instrumented Products API |
| Prometheus | http://localhost:9090 | Scrapes `/metrics` every 5s |
| Grafana | http://localhost:3000 | Dashboards (login: admin / admin) |

### Try the API by hand

```bash
# Baseline traffic
curl http://localhost:8080/api/products

# Random 10–300ms latency; try a few
curl http://localhost:8080/api/products/1
curl http://localhost:8080/api/products/3

# 404 — a client error, not a 5xx
curl -i http://localhost:8080/api/products/99

# Place orders — repeat this and ~1 in 10 returns a 500
curl -i -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId":1,"quantity":2}'

# Invalid body -> 400
curl -i -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId":1,"quantity":0}'

# See the raw metrics Prometheus scrapes
curl -s http://localhost:8080/metrics | grep http_request
```

In the metrics output, notice `endpoint="/api/products/{id}"` — the route template, even though you requested `/api/products/1` and `/api/products/3`.

### Run the load test

For dashboards worth looking at, run sustained mixed traffic in a **second terminal**:

```bash
docker compose --profile loadtest up loadtest
```

The generator sends ~5 req/s for 3 minutes: 40% `GET /api/products`, 35% `GET /api/products/{id}` with ids 1–7 (6 and 7 produce 404s), 25% `POST /api/orders` (every 10th with an invalid body). All three RED signals move.

Customize with environment variables:

```bash
DURATION_SECONDS=300 REQUESTS_PER_SEC=10 docker compose --profile loadtest up loadtest
```

### Watch the results

1. Open Grafana at http://localhost:3000 (admin/admin) → Dashboards → **"RED Method"** (auto-provisioned, no setup needed).
2. Or query Prometheus directly at http://localhost:9090:

```promql
# Rate
sum(rate(http_requests_total[1m])) by (endpoint)

# Errors
sum(rate(http_requests_total{status=~"5.."}[1m])) / sum(rate(http_requests_total[1m])) * 100

# Duration
histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))
```

### Cleanup

```bash
docker compose down
```

## Exercises

1. **Bucket layout.** Replace the histogram's `ExponentialBuckets(0.005, 2, 10)` with a deliberately bad layout — e.g. `new double[] { 1, 2, 5 }` (all requests fall in the first bucket) — rebuild, rerun the load test, and watch the percentile panel: p50, p95, and p99 collapse toward the same interpolated guess. Then design a better layout: linear buckets concentrated in the 10–300ms range. What happens to the accuracy of p99?

2. **Alert rule.** Panels tell you when you look; alerts tell you when you don't. Write a Prometheus alerting rule that fires when the 5xx error rate exceeds 5% for 2 minutes:

   ```yaml
   - alert: HighErrorRate
     expr: sum(rate(http_requests_total{status=~"5.."}[1m])) / sum(rate(http_requests_total[1m])) > 0.05
     for: 2m
   ```

   Add it to a `rules.yml`, reference it from `prometheus.yml` (`rule_files:`), and verify it appears (and eventually fires — bump the 500 probability in `Program.cs`) under http://localhost:9090/alerts.

3. **Per-status duration.** Add a `status` label to the histogram, then chart p95 with `sum(...) by (le, status)` and `histogram_quantile(0.95, ...)`. Are failed requests faster or slower than successful ones? (Failures often return *fast* — which is exactly why average latency can improve during an outage.) Then count the new series on `/metrics`: how many extra series did one label create, given every histogram series is really ~10 bucket series?
