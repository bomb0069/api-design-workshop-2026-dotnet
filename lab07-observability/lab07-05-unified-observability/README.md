# Lab 07-05: Unified Observability

## Overview

This is the capstone of the observability group. The earlier sub-labs each covered **one** signal in isolation — structured logs (07-01), metrics (07-02), distributed traces (07-03), and eBPF-based zero-instrumentation telemetry (07-04). This lab wires **all three signals together**:

- Both services emit **metrics, logs, and traces** through a single SDK (OpenTelemetry) over a single protocol (OTLP) to a single agent (the **OpenTelemetry Collector**).
- The collector fans the signals out to the backend that is best at each one: **Prometheus** (metrics), **Loki** (logs), **Tempo** (traces).
- **Grafana** queries all three — and, because every log line carries the `trace_id` of the request that produced it, you can **click from a trace to its logs and from a log line back to its full trace**.

That last part is the whole point. By the end of this lab you will debug a failing request by hopping between signals in a few clicks, instead of grepping log files with a timestamp in one hand and a trace UI in the other.

## The Three Pillars — and Why Silos Hurt

| Signal | Answers | Strength | Weakness |
|--------|---------|----------|----------|
| **Metrics** | "How many? How fast? What percentage failing?" | Cheap, aggregatable, alertable | No context about *which* request |
| **Logs** | "What exactly happened at this point in the code?" | Arbitrary detail, human-readable | Millions of lines, no request grouping by default |
| **Traces** | "Where did this one request spend its time, across services?" | Causality across service boundaries | Not built for aggregate detail or free-form messages |

Traditionally each pillar came from a different tool with a different agent, a different query language, and — crucially — **no shared key**. The 3 a.m. debugging session then looks like this:

1. An alert fires: error rate on `POST /api/orders` is 12% (metrics, Prometheus).
2. You want to know *why*, so you open the log tool and search for errors "around 03:04, give or take" (logs).
3. You find fifteen error lines from three services. Which ones belong to the *same* request? You guess, based on timestamps and gut feeling.
4. You open the tracing UI and try to find a trace that *might* match the log lines you *think* are related.

That step 3 — **correlation by timestamp** — is manual, slow, and wrong just often enough to be dangerous. Two requests failing in the same 200 ms window look identical.

**The fix is a join key.** Every request already has a globally unique ID: the **trace id**, propagated across services in the W3C `traceparent` header. If every log line records the trace id of the request that produced it, and every metric spike can be sampled down to example traces, then the three pillars stop being three databases and become **three indexes over the same events**:

```
metric spike ──▶ example trace ──▶ exact log lines ──▶ root cause
                    trace_id           trace_id
```

The best part, as you'll see below: with OpenTelemetry in .NET, putting the trace id on every log line requires **zero code**.

## Architecture

```
 ┌───────────────┐  POST /api/orders                ┌─────────────────┐
 │ order-service │ ───────────────────────────────▶ │ product-service │
 │    (:8080)    │   GET /api/products/{id}         │     (:8081)     │
 └───────┬───────┘   (+ traceparent header)         └────────┬────────┘
         │                                                   │
         │        OTLP gRPC (traces + metrics + logs)        │
         └───────────────────────┬───────────────────────────┘
                                 ▼
                       ┌──────────────────┐
                       │  otel-collector  │   one agent, three pipelines
                       └────────┬─────────┘
                 ┌──────────────┼───────────────┐
          traces │       logs   │        metrics│ (scraped from :8889)
                 ▼              ▼               ▼
           ┌───────────┐ ┌───────────┐  ┌──────────────┐
           │   Tempo   │ │   Loki    │  │  Prometheus  │
           │  (:3200)  │ │  (:3100)  │  │   (:9090)    │
           └─────┬─────┘ └─────┬─────┘  └──────┬───────┘
                 │             │               │
                 └─────────────┼───────────────┘
                               ▼
                       ┌──────────────┐
                       │   Grafana    │  one UI, correlated by trace_id
                       │   (:3000)    │
                       └──────────────┘
```

Two things to notice:

- The **services only know about the collector** (`OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`). They have no idea Prometheus, Loki, or Tempo exist. Swap Tempo for Jaeger, or the whole backend for a SaaS vendor, and the application code and config don't change — only the collector's exporters do.
- Prometheus is the one **pull-based** backend in the picture: the collector exposes the metrics on `:8889` and Prometheus scrapes them. Traces and logs are pushed.

## Why a Collector at All?

The services could export straight to Tempo/Loki/Prometheus. The collector earns its place because it gives you, in one central process:

- **Fan-out and routing** — one OTLP stream in, per-signal backends out. Adding a second traces backend is one line of YAML, not a redeploy of every service.
- **Buffering and retry** — if Tempo restarts, the collector queues and retries; the apps never block or drop on backend hiccups.
- **Central redaction and sampling** — strip PII attributes, drop noisy health-check spans, or tail-sample "only errors and slow traces" in one place, instead of in N codebases.
- **Backend neutrality** — the apps speak only OTLP, an open standard. Vendor decisions become collector config, not code changes.

## Collector Pipeline Walkthrough (`otel-collector-config.yaml`)

The collector's model is a small pipeline algebra: **receivers → processors → exporters**, wired per signal in the `service.pipelines` block.

### Receivers — how telemetry gets in

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318
```

One receiver handles all three signals — OTLP has message types for traces, metrics, and logs. Both .NET services push here over gRPC.

### Processors — what happens in the middle

```yaml
processors:
  batch:
```

`batch` groups telemetry into fewer, larger export requests — always the first processor you add. This is also where production configs put `memory_limiter`, attribute redaction, filtering, and tail sampling (see the exercises).

### Exporters — where telemetry goes out

```yaml
exporters:
  otlp/tempo:                       # traces -> Tempo, which speaks OTLP itself
    endpoint: tempo:4317
    tls:
      insecure: true
  prometheus:                       # metrics -> exposed for Prometheus to SCRAPE
    endpoint: 0.0.0.0:8889
  otlphttp/loki:                    # logs -> Loki 3.x native OTLP ingest
    endpoint: http://loki:3100/otlp
```

The `type/name` convention (`otlp/tempo`) lets you define several exporters of the same type. Note the direction change on metrics: the `prometheus` exporter doesn't push anywhere — it opens an endpoint and waits to be scraped (`prometheus.yml` scrapes it every 5s with `honor_labels: true`, so the `job` label keeps the *service name* the collector derived from each app's OTel resource).

### Pipelines — wiring it together

```yaml
service:
  pipelines:
    traces:   { receivers: [otlp], processors: [batch], exporters: [otlp/tempo] }
    metrics:  { receivers: [otlp], processors: [batch], exporters: [prometheus] }
    logs:     { receivers: [otlp], processors: [batch], exporters: [otlphttp/loki] }
```

Same receiver three times, different exporter each time. That's the fan-out.

## The Correlation Mechanism: Zero-Code trace_id on Every Log

The single most important code in this lab is the least spectacular:

```csharp
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeScopes = true;
    options.AddOtlpExporter();
});
```

Here is why this is all it takes. In .NET, the current trace context lives in `Activity.Current` (an ambient, async-local value). When ASP.NET Core starts handling a request, the OTel instrumentation creates an `Activity` for it — restoring the trace id from the incoming `traceparent` header if there is one. Any `ILogger` call made anywhere inside that request is therefore made *inside an active Activity*, and the OpenTelemetry logging provider **stamps the Activity's `TraceId` and `SpanId` onto the log record automatically**.

So when `order-service` writes:

```csharp
logger.LogError("product lookup failed for product {ProductId}: product-service returned {StatusCode}", ...);
```

…the exported OTLP log record already contains the trace id of the exact `POST /api/orders` request that failed — the same trace id Tempo stored for the trace, and the same one `product-service`'s logs carry, because `HttpClient` instrumentation propagated it in the `traceparent` header. Nobody ever wrote `logger.LogError($"[{traceId}] ...")`. **The join key is free.**

Loki 3.x's native OTLP ingest keeps `trace_id`/`span_id` as **structured metadata** fields on each log entry (and turns resource attributes like `service.name` into labels like `service_name`). Grafana then closes the loop from both ends — see `grafana/provisioning/datasources/datasources.yml`:

- **Tempo → Loki** (`tracesToLogsV2`): every span gets a "Logs for this span" button running `{service_name="..."} | trace_id=` + the span's trace id. We use a `customQuery` filtering on the structured-metadata field, because the default trace-id *line* filter (`filterByTraceID`) would search the log text — and with OTLP ingestion the trace id isn't in the text.
- **Loki → Tempo** (`derivedFields`): a derived field with `matcherType: label` picks up the `trace_id` structured-metadata field on every log entry and renders it as a "View trace" link into Tempo. (No regex over the message needed; if you ever ingest logs where the trace id *is* embedded in the text, that's what `matcherRegex` with a real regex is for.)

## The Correlation Workflow (Do This First)

This walkthrough is the heart of the lab. Start everything and generate traffic:

```bash
docker compose up --build -d
docker compose --profile loadtest up loadtest
```

The load test posts ~5 orders/second: most succeed (201), some hit an unknown product (404, logged as `product lookup failed`), and ~10% of otherwise-valid orders fail with a simulated `inventory reservation error` (500). Give it ~30 seconds, then open Grafana at http://localhost:3000 (admin / admin).

### Direction 1: from a failing trace to the exact log line

1. Open **Explore** (compass icon) and pick the **Tempo** datasource.
2. Query type **Search** → filter by Service Name `order-service` and Status `error` — or switch to **TraceQL** and query:
   ```
   { resource.service.name = "order-service" && status = error }
   ```
3. Click any result. The trace view opens: `POST /api/orders` (order-service) with the child spans `order:process` and the `GET` client/server span pair into product-service, including its `db:query-products` span. Red icons mark where it failed.
4. Expand the failed span and click **Logs for this span** (this is `tracesToLogsV2` at work). Grafana opens Loki, pre-filtered to this service *and this trace_id*.
5. There it is — the one log line that explains this particular failure, e.g. `product lookup failed for product 999: product-service returned 404` or `order processing failed for product 2: inventory reservation error`. Not "errors around that time" — **the** log lines of **this** request, on both services.

### Direction 2: from an error log to the whole story

1. In **Explore**, pick the **Loki** datasource and query error logs:
   ```logql
   {service_name="order-service"} | detected_level = `error`
   ```
   (If `detected_level` yields nothing on your Loki version, just use `{service_name="order-service"}` and eyeball the red lines.)
2. Expand any error line. Under its fields you'll see the structured metadata, including `trace_id` — with a **View trace** button next to it (the `derivedFields` link).
3. Click it. Grafana opens a split view with the full distributed trace in Tempo: now you can see *what else happened* in that request — how long the product lookup took, whether product-service also logged something (click *its* span's logs too), where the time went.
4. For the metrics leg of the triangle, open the provisioned **Unified Observability** dashboard (Dashboards → Unified Observability): request rate and p95 per service, the 5xx error-rate stat (hovering around 10–20% thanks to the simulated failures — the loadtest's 404s count as 4xx, not 5xx), and a live order-service log panel at the bottom. Spot a spike on the dashboard → jump to Explore/Tempo for that time range → find an example trace → jump to its logs. Full circle.

## Running the Lab

```bash
cd lab07-observability/lab07-05-unified-observability
docker compose up --build
```

| Service | URL | Purpose |
|---------|-----|---------|
| order-service | http://localhost:8080 | POST /api/orders |
| product-service | http://localhost:8081 | GET /api/products, /api/products/{id} |
| OTel Collector | (internal :4317, :8889) | Receives OTLP, fans out per signal |
| Tempo | http://localhost:3200 | Trace storage/query |
| Loki | http://localhost:3100 | Log storage/query (OTLP ingest at /otlp) |
| Prometheus | http://localhost:9090 | Metric storage/query |
| Grafana | http://localhost:3000 | One UI over all three (admin/admin) |

Manual requests, if you don't want the load test:

```bash
# A good order (201)
curl -s -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": 1, "quantity": 2}' | jq

# A failing lookup (404 + "product lookup failed" error log)
curl -s -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": 999, "quantity": 1}' | jq

# Repeat a valid order ~20 times and you'll hit the simulated 10% failure (500)
for i in $(seq 1 20); do
  curl -s -o /dev/null -w "%{http_code} " -X POST http://localhost:8080/api/orders \
    -H "Content-Type: application/json" -d '{"productId": 2, "quantity": 1}'
done; echo
```

Sustained traffic for meaningful dashboards:

```bash
docker compose --profile loadtest up loadtest
```

Useful PromQL against the collector-exported metrics (note the OTel metric `http.server.request.duration` arrives in Prometheus as `http_server_request_duration_seconds_*`, with `job` set to each service's name):

```promql
sum by (job) (rate(http_server_request_duration_seconds_count[1m]))
histogram_quantile(0.95, sum by (job, le) (rate(http_server_request_duration_seconds_bucket[1m])))
sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
```

### Version-sensitive configuration (read if something shows "No data")

- **Loki OTLP ingest**: native OTLP ingestion (`/otlp/v1/logs`) and structured metadata are supported out of the box in Loki **3.x** with the default `local-config.yaml` (it uses schema v13 + `allow_structured_metadata: true` by default). If logs don't appear on a different Loki version, check `limits_config.allow_structured_metadata` and that your schema config is v13 — see the Loki OTLP docs.
- **Grafana derived field `matcherType: label`** requires Grafana **≥ 10.3** (we pin 10.4.0). On older Grafana you'd have to fall back to a `matcherRegex` over the log line — which only works if the trace id is in the message text.
- **Prometheus `honor_labels: true`** in `prometheus.yml` is what keeps `job="order-service"` from being overwritten by `job="otel-collector"` at scrape time. Remove it and every dashboard query that groups by `job` collapses into one series.
- **Tempo storage paths** are under `/tmp/tempo` so the container works regardless of which user the image runs as (no volume/chown dance).

## Exercises

1. **Custom span attributes + TraceQL.** In `order-service/Program.cs`, add the order total to the processing span: `span?.SetTag("order.total", (double)order.Total);` (TraceQL compares numbers, so cast the `decimal`). Rebuild (`docker compose up --build -d order-service`), send some orders, then find big-ticket orders in Tempo's TraceQL editor:
   ```
   { span.order.total > 100 }
   ```
   Attributes turn traces from a latency tool into a queryable business dataset.

2. **Redact at the collector.** Add an `attributes` processor to `otel-collector-config.yaml` that drops a noisy attribute from every span, then add it to the traces pipeline:
   ```yaml
   processors:
     batch:
     attributes/scrub:
       actions:
         - key: url.query
           action: delete
   service:
     pipelines:
       traces:
         processors: [attributes/scrub, batch]
   ```
   Restart the collector and confirm in Tempo that new spans no longer carry the attribute. Note that *no service was redeployed* — that's the collector's central-control value proposition.

3. **Sampling (discussion + experiment).** This lab keeps 100% of traces — fine for a workshop, ruinous at production volume. Where would you sample? Head sampling in the SDK (cheap, but decides *before* knowing if the request fails) vs. tail sampling in the collector's `tail_sampling` processor (keep all errors + all slow traces + 5% of the rest — but the collector must buffer whole traces). Try configuring a `probabilistic_sampler` processor at 25% and watch the Tempo result count drop while metrics stay exact — a key point: **sampling traces never distorts your metrics**, because metrics are computed from every request in-process.

4. **Compare with one-stop vendors.** Datadog, New Relic, Grafana Cloud, Honeycomb, etc. sell exactly this correlation experience as a product. Discuss: what did we build by hand here that they bundle (correlation UI, retention, alerting, cross-signal navigation)? What do you keep by owning the pipeline (cost control, data locality, no per-host pricing, OTLP portability)? Note that because our services speak pure OTLP, moving to any of these vendors is a *collector exporter change* — try sketching the config for one.

## Key Concepts

- Metrics, logs, and traces are indexes over the same events; `trace_id` is the join key that unifies them.
- In .NET, log records emitted inside an active `Activity` carry `TraceId`/`SpanId` automatically — correlation costs zero code.
- The OTel Collector's receiver → processor → exporter pipelines decouple applications from backends and centralize batching, redaction, and sampling.
- One protocol out of the app (OTLP), best-of-breed storage per signal, one UI on top.
- Trace ↔ log navigation in Grafana is configuration (`tracesToLogsV2`, `derivedFields`), not application code.

## Cleanup

```bash
docker compose down -v
```
