# Lab 07-03: Distributed Tracing with OpenTelemetry + Jaeger

## Overview

This lab traces a request across two services. An `order-service` receives `POST /api/orders`, calls a `product-service` to validate the product and fetch its price, and both services export their spans to Jaeger via OpenTelemetry. The result: one click in the Jaeger UI shows the complete journey of a single request — which service was involved, in what order, and where the time went.

```
client ──POST /api/orders──▶ order-service ──GET /api/products/{id}──▶ product-service
                                   │                                        │
                                   └──────────── spans (OTLP) ──────────────┘
                                                     │
                                                     ▼
                                                  Jaeger UI  http://localhost:16686
```

## Why Metrics and Logs Are Not Enough

Metrics and logs are per-service views. When an order takes 900ms, they can tell you:

- **Metrics**: "order-service p99 latency is up" — but not *why*, and not *whose fault* it is.
- **Logs**: "order-service called product-service at 10:00:01" and, in a *different* log stream, "product-service handled a request at 10:00:01" — but nothing connects those two lines. With 50 req/s you cannot tell which product-service log line belongs to which order.

The question neither can answer is: **"Where did *this specific request* spend its time, across all services it touched?"** That is exactly what a trace is: all the work done on behalf of one request, stitched together by a shared ID that travels *with* the request over the network.

## Vocabulary

| Term | Meaning |
|------|---------|
| **Trace** | The whole story of one request across all services. Identified by a `TraceId` (16 bytes, shown as 32 hex chars). |
| **Span** | One timed operation inside a trace: "handle POST /api/orders", "call product-service", "query DB". Has a name, start time, duration, attributes, and a status. |
| **Parent / child** | Spans form a tree. A span started while another is active becomes its child. The root span has no parent. |
| **Trace context** | The tiny piece of data (TraceId + parent SpanId + flags) that must cross process boundaries so the next service can attach its spans to the same trace. Carried in the W3C `traceparent` HTTP header. |
| **Baggage** | Optional key/value pairs that propagate *alongside* the trace context (`baggage` header), e.g. `customer.id=42`. Unlike attributes, baggage travels downstream. |
| **Sampling** | The decision whether a given trace is recorded and exported at all. At scale you keep a fraction (head sampling: decided at the root; tail sampling: decided after the trace completes). |
| **Exporter** | The component that ships finished spans out of the process — here, OTLP/gRPC to Jaeger. |

## The `traceparent` Header (W3C Trace Context)

Propagation is not magic — it is one HTTP header with four dash-separated fields:

```
traceparent: 00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01
             │  │                                │                │
             │  │                                │                └─ flags (01 = sampled)
             │  │                                └─ parent span id (16 hex chars)
             │  └─ trace id (32 hex chars)
             └─ version (00)
```

The sender writes its current TraceId and SpanId into the header; the receiver parses it and starts its server span as a **child** of that SpanId, with the **same** TraceId. That is the entire mechanism that makes "one trace across N services" work.

### See it with your own eyes

This lab ships a debug endpoint pair that makes the invisible visible. `order-service GET /api/debug/chain` calls `product-service GET /api/debug/traceparent` and returns both sides' view of the same trace:

```bash
curl -s http://localhost:8080/api/debug/chain | jq
```

```json
{
  "order": {
    "traceId": "0af7651916cd43dd8448eb211c80319c",
    "spanId": "00f067aa0ba902b7"
  },
  "product": {
    "receivedTraceparent": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
    "traceId": "0af7651916cd43dd8448eb211c80319c",
    "spanId": "53995c3f42cd8ad8",
    "parentSpanId": "b7ad6b7169203331"
  }
}
```

Read it like a detective:

- `order.traceId` == `product.traceId` — both services are inside the **same trace**.
- `product.receivedTraceparent` carries that TraceId across the network — nobody in this lab's code sets that header; HttpClient instrumentation injected it.
- `product.parentSpanId` is **not** `order.spanId`. It is the ID of the *HTTP client span* that order-service's HttpClient instrumentation created for the outgoing call — a child of the order server span. So the chain is: order server span → HTTP client span → product server span. That middle hop is exactly what you will see in Jaeger.

Now call product-service directly (`curl -s http://localhost:8081/api/debug/traceparent | jq`): `receivedTraceparent` is `null` and a brand-new TraceId appears — no caller, no propagation, new trace.

## OpenTelemetry in .NET: `Activity` IS the Tracing API

.NET did not bolt OpenTelemetry on top — the OTel tracing API for .NET *is* the built-in `System.Diagnostics` types. What other languages call a "tracer", .NET has had since long before OTel:

| OpenTelemetry term | .NET type / call |
|--------------------|------------------|
| Tracer | `ActivitySource` |
| Span | `Activity` |
| Start a span | `activitySource.StartActivity("name")` |
| Current span | `Activity.Current` |
| Span attribute | `activity.SetTag("key", value)` |
| Span status | `activity.SetStatus(ActivityStatusCode.Error, "...")` |
| Trace / span ID | `activity.TraceId`, `activity.SpanId` |
| Context propagation | W3C `traceparent` (the default `ActivityIdFormat.W3C`) |

This is why ASP.NET Core, HttpClient, and many libraries are traceable with zero code changes: they already emit `Activity` objects; the OpenTelemetry SDK just listens, enriches, and exports them.

## The Setup (identical in both services)

From `order-service/Program.cs` and `product-service/Program.cs`:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "order-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // SERVER span per incoming request
        .AddHttpClientInstrumentation()   // CLIENT span per outgoing call + traceparent injection
        .AddSource("order-service")       // export spans from our own ActivitySource
        .AddOtlpExporter());              // ship via OTLP — endpoint from env, see below
```

Line by line, and **why**:

- **`ConfigureResource(...AddService(...))`** — the *resource* answers "who emitted this span?". The service name set here is what Jaeger's service dropdown shows. We read it from the `OTEL_SERVICE_NAME` environment variable so the same image can run under any identity; docker-compose sets it per container.
- **`AddAspNetCoreInstrumentation()`** — auto-instrumentation for the *inbound* side: one server span per HTTP request, pre-populated with `http.request.method`, `http.route`, `http.response.status_code`, and automatically parented to the incoming `traceparent` header if there is one.
- **`AddHttpClientInstrumentation()`** — auto-instrumentation for the *outbound* side: one client span per HttpClient request, and it **injects `traceparent`** into the outgoing request. This is the line that makes propagation happen; delete it and each service becomes an island.
- **`AddSource("...")`** — the SDK only exports spans from `ActivitySource`s it has been explicitly subscribed to. Our hand-made spans (`db:query-products`, `check-stock`) come from `new ActivitySource("product-service")`; without a matching `AddSource("product-service")` they would be silently dropped (`StartActivity` even returns `null` when nobody listens — which is why every custom-span line uses `?.`).
- **`AddOtlpExporter()`** — no URL in code. The OTLP exporter reads the standard **`OTEL_EXPORTER_OTLP_ENDPOINT`** environment variable automatically; docker-compose points it at `http://jaeger:4317` (Jaeger's OTLP/gRPC port). Retargeting the whole lab to a different backend (Tempo, an OTel Collector, a SaaS vendor) is a compose-file change, not a code change.

## Auto vs Manual Instrumentation

| You get for free (auto) | You add by hand (manual) |
|-------------------------|--------------------------|
| Server span for every incoming request, with HTTP attributes | Custom spans for interesting *internal* work (`db:query-products`, `check-stock`) |
| Client span for every outgoing HttpClient call | Domain attributes (`order.id`, `order.total`, `stock.remaining`) |
| `traceparent` injection + extraction (propagation) | Span status for *business* errors (`SetStatus(Error, "unknown product")`) |
| Trace/span IDs, timing, parenting | Baggage, events, links (exercises) |

Auto-instrumentation sees the *edges* of your process (HTTP in, HTTP out). Everything between the edges is a black box unless you open spans yourself. In product-service:

```csharp
async Task SimulateDbQueryAsync(string operation, int? productId = null)
{
    using var span = activitySource.StartActivity("db:query-products");
    span?.SetTag("db.system", "memory");
    span?.SetTag("db.operation", operation);
    if (productId is int id) span?.SetTag("product.id", id);
    await Task.Delay(Random.Shared.Next(20, 81)); // pretend the DB is working
}
```

Three details worth noticing:

- **`using`** ends the span when the scope exits — the span's duration is exactly the block's duration. Forget it and the span never closes.
- **Parenting is automatic.** `StartActivity` looks at `Activity.Current` (the ambient server span, flowing through `async/await` via `AsyncLocal`) and becomes its child. No parent parameter anywhere.
- **`span?.`** — if no listener is subscribed (e.g. you removed `AddSource`), `StartActivity` returns `null` and the whole thing costs nearly nothing. Instrumentation you can leave in production code.

Enriching a span you did *not* create works the same way — grab the ambient one. From order-service:

```csharp
Activity.Current?.SetTag("order.id", order.Id);
Activity.Current?.SetTag("order.total", order.Total);
```

And for business errors, the status is an explicit judgement call — a 400 response does **not** automatically mark the span as failed:

```csharp
if (response.StatusCode == HttpStatusCode.NotFound)
{
    Activity.Current?.SetStatus(ActivityStatusCode.Error, $"unknown product {request.ProductId}");
    return Results.BadRequest(new { error = "unknown product" });
}
```

## Anatomy of One Trace

After `POST /api/orders` with a valid product, find the trace in Jaeger. The span tree you should see:

```
order-service: POST /api/orders                        ~120ms   ← server span (auto)
│    order.id=1  order.total=449.50
└── order-service: GET  http://product-service:8080/…  ~110ms   ← client span (auto)
    └── product-service: GET /api/products/{id}        ~100ms   ← server span (auto, other process!)
        ├── db:query-products                          20–80ms  ← custom span
        │      db.system=memory  db.operation=select-by-id  product.id=2
        └── check-stock                                 5–15ms  ← custom span
               stock.remaining=130
```

Five spans, two services, one TraceId. Things this picture tells you that nothing else can:

- The gap between the client span and the product server span is **network + serialization** overhead.
- Most of product-service's time is the `db:query-products` span — with a real database, this is how you catch the slow query without reading a single log line.
- The two custom spans are sequential (the code awaits one, then starts the other). Parallel work would show as overlapping bars — see Exercise 2.

### Error traces

```bash
curl -i -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": 99, "quantity": 1}'
# HTTP/1.1 400 Bad Request — {"error":"unknown product"}
```

In Jaeger, filter with **Tags**: `error=true` (or just look for the red icon). Open the trace: the order-service server span is marked with an error status carrying the message `unknown product 99`, and its child spans show product-service answering 404 — the whole failure story in one view. This is the payoff of `Activity.Current?.SetStatus(ActivityStatusCode.Error, ...)`.

## Running the Lab

```bash
cd lab07-observability/lab07-03-distributed-tracing
docker compose up --build
```

| Service | URL | Purpose |
|---------|-----|---------|
| order-service | http://localhost:8080 | Creates orders, calls product-service |
| product-service | http://localhost:8081 | Product catalog (in-memory) |
| Jaeger UI | http://localhost:16686 | Trace visualization |

### Try it out

```bash
# 1. A successful order (201) — one trace across both services
curl -s -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": 2, "quantity": 3}' | jq

# 2. Read it back
curl -s http://localhost:8080/api/orders/1 | jq

# 3. A failing order (400) — produces a red trace
curl -s -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": 99, "quantity": 1}' | jq

# 4. Browse products directly
curl -s http://localhost:8081/api/products | jq
curl -s http://localhost:8081/api/products/1 | jq

# 5. Watch propagation happen (see "See it with your own eyes" above)
curl -s http://localhost:8080/api/debug/chain | jq
```

Then open **http://localhost:16686**, pick **order-service** in the Service dropdown, and click **Find Traces**. Click a trace to see the span tree; click a span to see its attributes (`order.total`, `db.operation`, `stock.remaining`, ...). The **System Architecture** tab (after some traffic) draws the service dependency graph derived purely from traces.

For a steady stream of traces (including ~20% error traces):

```bash
./loadtest.sh            # 50 rounds; REQUESTS=200 ./loadtest.sh for more
```

## Exercises

1. **Baggage — propagate `customer.id` downstream.** Attributes stay on their span; baggage travels with the request. In order-service's POST handler, call `Baggage.SetBaggage("customer.id", "42")` (namespace `OpenTelemetry`); in product-service, read `Baggage.GetBaggage("customer.id")` and add it as a tag on the `db:query-products` span. Verify in Jaeger that a value set in order-service appears on a product-service span, and find the `baggage` header with the `/api/debug/chain` technique (return `Request.Headers["baggage"]` too).

2. **Parallel spans.** Make order-service call product-service twice concurrently (e.g. fetch the product *and* the product list with `Task.WhenAll`). In Jaeger the two client spans now *overlap* in time instead of stacking sequentially — the waterfall shape instantly distinguishes serial from parallel fan-out.

3. **Sampling.** Add `.SetSampler(new AlwaysOffSampler())` to `WithTracing(...)` in order-service and restart. No traces arrive — and note that product-service's spans vanish too: the sampling decision is encoded in the `traceparent` flags (`...-00` instead of `...-01`) and honored downstream. Then try `new TraceIdRatioBasedSampler(0.25)` and confirm roughly 1 in 4 requests produces a trace. Check the flags with `/api/debug/chain`.

4. **Head vs tail sampling (discussion).** The samplers above decide at the *start* of a trace (head sampling) — cheap, but they can't know yet whether the trace will turn out slow or failed, so they drop interesting traces at the same rate as boring ones. Tail sampling buffers complete traces (typically in an OpenTelemetry Collector) and keeps "all errors + all traces > 500ms + 1% of the rest". What does that cost in memory and infrastructure? Where would a Collector sit in this lab's docker-compose?

## Key Concepts

- A trace stitches all work for one request across services via a shared TraceId; spans form a parent/child tree.
- Propagation is just the W3C `traceparent` header — injected and extracted automatically by HttpClient/ASP.NET Core instrumentation.
- In .NET, `ActivitySource`/`Activity` *are* the OpenTelemetry tracing API; `AddSource` is required or custom spans are dropped.
- Auto-instrumentation covers the edges (HTTP in/out); custom spans and attributes open up the black box in between.
- Span status is set explicitly for business errors — that is what makes traces searchable by `error=true`.
- The OTLP exporter is configured by environment (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME`), keeping the backend swappable without code changes.

## Cleanup

```bash
docker compose down
```
