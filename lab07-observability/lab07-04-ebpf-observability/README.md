# Lab 07-04: Zero-Code Observability with eBPF (Grafana Beyla)

## Overview

In lab07-03 we made a two-service system observable the "classic" way: we added
OpenTelemetry SDK packages to each project, configured exporters, and wrote
custom spans. It worked beautifully — but every service needed code changes,
package updates, and a redeploy.

This lab produces distributed traces and RED metrics for the **same two
services** with a radically different approach: **zero instrumentation**.

Open `order-service/Program.cs` and `product-service/Program.cs` and look for
observability code. There is none. No OpenTelemetry packages, no middleware,
no `Activity`, no metrics — just plain minimal APIs. Yet when you run this lab
you will see both services in Jaeger, joined into a single distributed trace,
and RED metrics for both in Prometheus.

The trick is **[Grafana Beyla](https://grafana.com/oss/beyla-ebpf/)**, an
auto-instrumentation tool built on **eBPF**. It watches your services from the
Linux kernel — outside the process, below the runtime — and emits OpenTelemetry
data on their behalf.

```text
            ┌─────────────────────┐        ┌─────────────────────┐
            │   order-service     │  HTTP  │  product-service    │
            │  (plain .NET 8,     │───────▶│  (plain .NET 8,     │
            │   no OTel code)     │        │   no OTel code)     │
            └─────────────────────┘        └─────────────────────┘
                   ▲    kernel-level                ▲
                   ┆    socket/HTTP events (eBPF)   ┆
            ┌──────┴──────┐                  ┌──────┴──────┐
            │ beyla-order │                  │beyla-product│
            └──────┬──────┘                  └──────┬──────┘
                   │ OTLP traces                    │ OTLP traces
                   ▼                                ▼
              ┌─────────────────────────────────────────┐
              │                 Jaeger                  │
              └─────────────────────────────────────────┘
                   ▲ RED metrics (Prometheus scrape) ▲
              ┌─────────────────────────────────────────┐
              │               Prometheus                │
              └─────────────────────────────────────────┘
```

## What is eBPF?

You do not need any kernel background for this lab — here is the whole idea.

Normally, the only code that runs inside the Linux kernel is the kernel itself
(plus kernel modules, which are risky: a buggy module can crash the machine).
**eBPF** (extended Berkeley Packet Filter) changes that: it lets you load small,
**sandboxed programs into the running kernel** without modifying kernel source
or loading a module.

Three things make this safe and useful:

1. **The verifier.** Before an eBPF program is loaded, the kernel statically
   analyzes it and rejects anything that could crash or hang the kernel:
   unbounded loops, out-of-bounds memory access, and so on. If the verifier
   is not convinced the program is safe, it simply refuses to load it.

2. **Attach points.** An eBPF program is attached to an event source and runs
   whenever that event fires:
   - **kprobes** — hook (almost) any kernel function, e.g. "a process called
     `tcp_sendmsg`";
   - **uprobes** — hook a function in a *user-space* binary or library;
   - **tracepoints** — stable, curated hook points the kernel exposes for
     tracing (e.g. syscall entry/exit, scheduler events);
   - **socket filters / networking hooks** — inspect packets and socket
     operations as they flow through the network stack.

3. **Maps.** eBPF programs share data with user space through efficient
   kernel data structures, so a user-space agent can collect what the probes
   observe.

Because every network call your service makes ultimately goes through the
kernel (sockets, `read`/`write`, TCP), a well-placed set of eBPF probes can see
**all** of a process's HTTP traffic — regardless of what language the process
is written in, and without the process cooperating or even knowing.

eBPF powers a lot of modern infrastructure: Cilium (Kubernetes networking),
kernel-level profilers, security tools like Falco and Tetragon — and
auto-instrumentation tools like Beyla.

## How Beyla Uses eBPF

Beyla attaches eBPF probes to kernel socket and HTTP-related events for a
target process (here, selected by `BEYLA_OPEN_PORT=8080`: "instrument whatever
process is listening on port 8080 in my PID namespace"). From the stream of
low-level events it:

1. **Reconstructs HTTP requests and responses** — method, path, status code,
   timing — by correlating socket reads/writes;
2. **Emits OpenTelemetry traces** (OTLP) for each server request and outgoing
   client request;
3. **Emits RED metrics** (Rate, Errors, Duration) as Prometheus metrics.

Because all of this happens at the kernel boundary, Beyla is
**language-agnostic**: the same binary instruments .NET, Go, Java, Python,
Node.js, Rust — anything that speaks HTTP over sockets. It works on
**unmodified binaries**. That is precisely why the two .NET services in this
lab contain zero observability code: from their point of view, nothing is
watching them.

### Distributed trace context without code: `BEYLA_BPF_CONTEXT_PROPAGATION`

There is one subtle problem with instrumenting from outside the process. A
distributed trace requires the caller to pass a `traceparent` header to the
callee (W3C Trace Context — you saw this in lab07-03, where the OTel SDK's
instrumented `HttpClient` added it automatically). Our services have no SDK,
so nobody adds that header... and each service would produce its own
disconnected trace.

Beyla's answer is `BEYLA_BPF_CONTEXT_PROPAGATION=all`: eBPF programs
**inject the `traceparent` into the outgoing HTTP request at the TCP layer**,
as the bytes leave the kernel — and read it back on the receiving side. The
application never sees the header being added; the two Beyla instances agree
on trace/span IDs purely through the wire. That is how order-service's call
and product-service's handler end up in one trace with zero code.

This works well for plain HTTP (which is what our services speak inside the
compose network). It is much harder for TLS-encrypted traffic and HTTP/2 —
see the caveats below.

## Requirements and Caveats

| Requirement / caveat | Details |
| --- | --- |
| Linux kernel ≥ 5.8 with BTF | Beyla needs a recent kernel with BTF (BPF Type Format) enabled. **Docker Desktop on macOS runs containers inside a Linux VM with a modern kernel, so this lab works on your Mac** — the eBPF programs run in that VM's kernel, not in macOS. Same for Docker Desktop on Windows (WSL2). |
| Elevated privileges | Loading eBPF programs requires privileges. This lab uses `privileged: true` for simplicity. On newer kernels you can be finer-grained instead: `CAP_BPF`, `CAP_SYS_PTRACE`, `CAP_NET_RAW`, `CAP_PERFMON`, `CAP_DAC_READ_SEARCH`, `CAP_CHECKPOINT_RESTORE` (the exact set depends on kernel version and enabled features — check the Beyla docs for your version). Either way, this is more privilege than an app container should ever have, which is one reason eBPF agents are typically operated by a platform team, not by app developers. |
| PID namespace access | Beyla must see the target process, hence `pid: "service:order-service"` in the compose file — the Beyla container joins the app container's PID namespace. |
| HTTP-level spans only | Beyla sees sockets, not your code. You get server/client HTTP spans — but **no** custom spans like lab07-03's `db:query` or `check-stock`. The interior of your handler is invisible. |
| No business attributes | You cannot attach `order.id`, `product.id`, or any app-specific attribute to a span. Beyla only knows what is on the wire. |
| Context propagation limits | Kernel-level `traceparent` injection needs `BEYLA_BPF_CONTEXT_PROPAGATION` and has protocol limits: plain HTTP/1.x works (our case); TLS-encrypted traffic and HTTP/2/gRPC are harder to rewrite at the TCP layer and support is partial/evolving — check the Beyla docs for your version. |
| Header/body invisibility for TLS | With TLS, Beyla relies on uprobes on SSL libraries where supported; coverage varies by runtime. |

## The Comparison: OTel SDK (lab07-03) vs eBPF (this lab)

This is the heart of the lab. Same system, two observability strategies:

| Dimension | OTel SDK (lab07-03) | eBPF / Beyla (this lab) |
| --- | --- | --- |
| Code changes | Every service: add packages, configure tracing, redeploy | **None** — apps are unmodified |
| Language coverage | Per-language SDKs and instrumentation libraries | Language-agnostic — any process speaking HTTP |
| Custom spans | Yes — `db:query`, `check-stock`, anything you want | No — HTTP request boundaries only |
| Business attributes | Yes — `order.id`, `product.quantity`, ... | No — only what is on the wire (method, route, status) |
| Context propagation | In-process, robust — works across TLS, HTTP/2, gRPC, messaging | Kernel-level injection; solid for plain HTTP, limited for TLS/HTTP2 |
| Overhead | In-process SDK work per request (small but inside your latency path) | Very low in-process overhead; probes run in the kernel |
| Privileges | None beyond the app itself | Privileged container / CAP_BPF etc. |
| Who operates it | Each dev team, per service | Platform/infra team, once per host — covers every service automatically |
| Coverage of legacy / third-party services | Only what you can rebuild | Everything, including binaries you cannot modify |

### What the trace looks like here vs lab07-03

In **this lab**, `POST /api/orders` produces roughly **3 HTTP-level spans**:

```text
order-service   POST /api/orders            (server span)
└── order-service   GET /api/products/{id}  (client span)
    └── product-service GET /api/products/{id}  (server span)
```

In **lab07-03**, the same request produced a richer tree, because the SDK let
us add custom spans from inside the code — the simulated `db:query` in
product-service, the `check-stock` step in order-service, plus business
attributes on each span. eBPF can never see those: they exist inside the
process, below no syscall boundary.

### When to choose which

- **eBPF** when you need broad coverage fast: hundreds of services, mixed
  languages, legacy binaries, teams that will never prioritize
  instrumentation work. One platform-level rollout, everything gets baseline
  traces and RED metrics.
- **SDK** when you need depth: business attributes, custom spans around the
  steps that matter, reliable propagation across every protocol.
- **Real platforms often run BOTH**: eBPF for automatic baseline coverage of
  every service on the fleet, plus the OTel SDK for deep instrumentation of
  the critical services where the extra detail pays for itself. The two
  emit the same OpenTelemetry data model, so they share one backend.

### Beyla is becoming OpenTelemetry eBPF Instrumentation (OBI)

Grafana donated Beyla to the OpenTelemetry project, where it is evolving into
**OpenTelemetry eBPF Instrumentation (OBI)**. Names and images will shift, but
everything you learn here — eBPF probes, kernel-level HTTP reconstruction,
zero-code OTLP export — transfers directly.

## Walkthrough

- `order-service/` and `product-service/` — plain .NET 8 minimal APIs.
  Deliberately boring: read them and confirm there is nothing
  observability-related. The order service calls the product service with a
  vanilla named `HttpClient`.
- `docker-compose.yml` — the interesting file:
  - the two app services have **no observability configuration at all**;
  - `beyla-order` / `beyla-product` join each app's PID namespace
    (`pid: "service:..."`), run privileged, find the target via
    `BEYLA_OPEN_PORT=8080`, name it via `OTEL_SERVICE_NAME`, and export OTLP
    to Jaeger over `http/protobuf`;
  - `BEYLA_BPF_CONTEXT_PROPAGATION=all` turns on kernel-level `traceparent`
    injection so the two services join one trace;
  - `BEYLA_PROMETHEUS_PORT=8999` makes each Beyla expose RED metrics.
- `prometheus.yml` — scrapes the two Beyla containers (not the apps — the
  apps have no metrics endpoint).

## Running the Lab

```bash
cd lab07-observability/lab07-04-ebpf-observability
docker compose up --build
```

Give Beyla a few seconds to attach its probes, then generate traffic:

```bash
# A few successful orders
curl -s -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": 1, "quantity": 2}'

curl -s -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": 3, "quantity": 1}'

# A 404 from product-service (unknown product)
curl -s -X POST http://localhost:8080/api/orders \
  -H "Content-Type: application/json" \
  -d '{"productId": 999, "quantity": 1}'

# Read an order back
curl -s http://localhost:8080/api/orders/1

# Hit product-service directly too
curl -s http://localhost:8081/api/products
```

### See the traces in Jaeger

Open <http://localhost:16686>. In the Service dropdown you will find
**order-service** and **product-service** — even though neither application
has any idea it is being traced. Find a `POST /api/orders` trace and confirm
the ~3-span shape shown above, spanning both services in one trace.

### See the RED metrics in Prometheus

Open <http://localhost:9090> and try:

```promql
# Request rate per service and route
sum by (service_name, http_route) (
  rate(http_server_request_duration_seconds_count[1m])
)

# p95 server latency by service
histogram_quantile(0.95,
  sum by (service_name, le) (
    rate(http_server_request_duration_seconds_bucket[1m])
  )
)

# Error ratio (5xx share of all requests) by service
sum by (service_name) (
  rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[1m])
)
/
sum by (service_name) (
  rate(http_server_request_duration_seconds_count[1m])
)
```

(Metric and label names follow OTel HTTP semantic conventions as exported by
Beyla 1.9; if a query returns nothing, browse `http://localhost:9090/graph`
autocomplete for the exact names in your version.)

## Exercises

1. **Prove the zero coupling.** Kill one Beyla container while traffic is
   running:

   ```bash
   docker compose kill beyla-product
   curl -s -X POST http://localhost:8080/api/orders \
     -H "Content-Type: application/json" -d '{"productId": 1, "quantity": 1}'
   ```

   The order still succeeds — the app is completely untouched, because it
   never depended on Beyla in the first place. (Compare: crash-looping an
   in-process agent or a misconfigured SDK *can* take an app down.) New
   product-service spans stop appearing in Jaeger until you
   `docker compose up -d beyla-product` again.

2. **Break the distributed trace on purpose.** Remove
   `BEYLA_BPF_CONTEXT_PROPAGATION=all` from both Beyla containers and restart.
   Post an order and look at Jaeger: you now get **two disconnected traces**
   (one per service) for the same request. Put the flag back and watch them
   rejoin.

3. **Compare with lab07-03.** Run lab07-03, send the same
   `POST /api/orders` request, and put the two Jaeger traces side by side.
   Count the spans, look for `db:query` / `check-stock`, and inspect span
   attributes. Write down three things the SDK trace tells you that the eBPF
   trace cannot, and one thing the eBPF setup gave you that the SDK setup
   required code for.

4. **Watch Beyla think.** Uncomment `BEYLA_TRACE_PRINTER=text` on
   `beyla-order`, restart, and watch `docker compose logs -f beyla-order`
   while you send requests — every reconstructed HTTP transaction is printed
   as Beyla captures it from the kernel.
