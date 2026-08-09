# Group 07: Observability

You cannot operate an API you cannot see. Observability is how you answer, in production, the questions you asked with a debugger in development: *Is the API healthy? Which requests are failing? Where did this request spend its time?* This group builds up the three pillars — **structured logs**, **metrics**, and **distributed traces** — one at a time, then shows two very different ways to get them: instrumenting your code with the **OpenTelemetry SDK**, and instrumenting nothing at all with **eBPF**. The capstone wires every signal through one pipeline and correlates them by `trace_id`.

## Learning Path

Work through the sub-labs in order. Each one builds on concepts introduced in the previous labs.

| #  | Sub-Lab | Topic | Description |
|----|---------|-------|-------------|
| 01 | [lab07-01-structured-logging](lab07-01-structured-logging/) | Structured Logging | JSON logs with `AddJsonConsole`, a correlation-ID middleware that stamps every log line in a request, one request-summary log with latency, and runtime-tunable log levels. The foundation: logs a machine can query, stitched together per request. |
| 02 | [lab07-02-metrics](lab07-02-metrics/) | Metrics — the RED Method | Rate, Errors, Duration with Prometheus + Grafana. A single middleware emits a counter and a histogram labeled by *route template* (the cardinality lesson), and a provisioned dashboard computes request rate, error %, and p50/p95/p99 with PromQL. |
| 03 | [lab07-03-distributed-tracing](lab07-03-distributed-tracing/) | Distributed Tracing (OpenTelemetry + Jaeger) | Two services (`order-service` → `product-service`) auto-instrumented with the OTel SDK, exporting OTLP to Jaeger. See W3C `traceparent` propagation with your own eyes via a debug endpoint, add custom spans and attributes, and read a full cross-service span tree in the Jaeger UI. |
| 04 | [lab07-04-ebpf-observability](lab07-04-ebpf-observability/) | Zero-Code Observability (eBPF / Grafana Beyla) | The same two services with **zero** observability code or packages. Grafana Beyla watches them from the kernel with eBPF and emits traces to Jaeger and RED metrics to Prometheus. The heart of the lab is the honest comparison: what eBPF gives you for free, and what only the SDK can give you. |
| 05 | [lab07-05-unified-observability](lab07-05-unified-observability/) | Unified Observability (Capstone) | All three signals from both services flow over OTLP into one **OpenTelemetry Collector**, which fans out to Prometheus (metrics), Loki (logs), and Tempo (traces). Grafana correlates them by `trace_id`: click from a failing trace to its exact log lines, and from an error log back to the full trace. |

## The Two Instrumentation Strategies

This group deliberately shows both mainstream approaches so you can choose (or combine) them:

| | OpenTelemetry SDK (labs 03, 05) | eBPF / Beyla (lab 04) |
|---|---|---|
| Code changes | Yes — packages + setup in `Program.cs` | None — works on unmodified binaries |
| Custom spans & business attributes | Yes (`ActivitySource`, tags, baggage) | No — HTTP-level spans only |
| Who owns it | Application developers | Platform/infra team |
| Coverage | Only services you instrument | Every service on the node, any language |
| Privileges | None beyond the app's | Privileged container / kernel capabilities |

Real platforms often run **both**: eBPF for baseline coverage of everything, the SDK for deep instrumentation of critical services.

## Which Labs Should I Do?

### Must-Do (core knowledge)

| Lab | Why it matters | Time |
|-----|---------------|------|
| **02 -- Metrics (RED)** | RED is the industry-standard health language for request-driven APIs. Every on-call dashboard you will ever read is some variant of this lab. | ~25 min |
| **03 -- Distributed Tracing** | The one pillar you cannot retrofit with grep. Understanding spans, context propagation, and the Jaeger UI is the core skill of modern API debugging. | ~30 min |

### Recommended

| Lab | Why it matters | Time |
|-----|---------------|------|
| **01 -- Structured Logging** | Cheap to learn, huge payoff. Correlation IDs and JSON logs are prerequisites for everything the aggregators in lab 05 do. | ~15 min |
| **05 -- Unified Observability** | Where the industry is heading: one OTLP pipeline, one collector, signals joined by `trace_id`. Ties the whole group together. | ~30 min |

### Optional (nice to know)

| Lab | When it is useful |
|-----|------------------|
| **04 -- eBPF** | If you operate many services (or services you cannot modify) and want observability without touching code. Also simply one of the most interesting pieces of technology in this workshop — run it once to see traces appear from services that have no idea they are being watched. |

## Prerequisites

- Labs 01–02 need only Docker.
- Labs 03–05 run multi-container stacks (Jaeger, Prometheus, Grafana, Loki, Tempo, the OTel Collector) — still just `docker compose up --build`, but give them a minute to start.
- Lab 04 runs privileged containers (eBPF needs kernel access). On macOS/Windows this works because Docker Desktop runs a Linux VM.
