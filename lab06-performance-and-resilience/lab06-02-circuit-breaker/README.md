# Lab 06-02: Circuit Breaker & Health Checks

Your API is only as reliable as the services it calls. When a downstream dependency starts failing or slowing down, the naive approach — keep calling it — makes everything worse: every incoming request waits out a full timeout, your thread pool and connection pool fill up with doomed calls, and the struggling dependency gets hammered by exactly the traffic it cannot handle. One slow service takes down the whole chain. That is a **cascade failure**.

A **circuit breaker** is the classic defense, named after the electrical device. It sits in front of the outbound call and counts failures:

- While things work, it stays **closed** and traffic flows through.
- After too many consecutive failures it **trips open**: calls fail *instantly* (no network attempt, no timeout wait) and the API serves a **fallback** instead. The dependency gets breathing room to recover.
- After a cool-down it goes **half-open**: exactly one probe request is let through. Success closes the circuit; failure opens it again.

```
                        3 consecutive failures
        ┌──────────┐ ─────────────────────────────▶ ┌──────────┐
        │  CLOSED  │                                │   OPEN   │
        │ (normal) │ ◀──┐                           │(fallback)│
        └──────────┘    │                           └──────────┘
                        │                    10 s cool-down │ ▲
          probe succeeds│                        elapsed    │ │ probe fails
                        │       ┌───────────┐               │ │
                        └────── │ HALF-OPEN │ ◀─────────────┘ │
                                │ (1 probe) │ ────────────────┘
                                └───────────┘
```

This lab runs two services: a public `api` and a deliberately breakable `downstream` product catalog. You will break the downstream on purpose, watch the circuit trip open, see fallback responses take over, then let it recover and watch the circuit close again.

## Learning Objectives

- Understand why calling a failing dependency harder causes cascade failure
- Implement the three circuit breaker states: Closed → Open → Half-Open
- Serve fallback responses (stale cache, then static default) while the circuit is open
- See why a timeout must accompany the breaker — slowness is a failure mode too
- Design health checks: liveness (`/health/live`) vs readiness (`/health/ready`) vs simple (`/health`)
- Know what a health endpoint should expose — and what it must hide

## Project Layout

```
lab06-02-circuit-breaker/
  docker-compose.yml     # api on :8080, downstream on :8081
  api/                   # public API; all downstream calls go through the breaker
    Program.cs           # GET /products, GET /circuit, health endpoints, CircuitBreaker class
  downstream/            # flaky product catalog you can break at runtime
    Program.cs           # POST /admin/mode/{ok|fail|slow} flips its behavior
```

The breaker in `api/Program.cs` is hand-rolled (~100 lines) so every state transition is visible in one file. In production .NET you would use [Polly](https://www.pollydocs.org/) via `Microsoft.Extensions.Http.Resilience` — it implements the same state machine with more options (failure *rate* instead of a consecutive count, sampling windows, per-endpoint isolation).

## Configuration Used in This Lab

| Setting | Value | Meaning |
|---------|-------|---------|
| Failure threshold | 3 consecutive failures | Trip CLOSED → OPEN on the 3rd failure in a row |
| Open duration (cool-down) | 10 seconds | How long OPEN fails fast before trying a probe |
| Half-open probes | 1 | One trial request decides: back to CLOSED or OPEN |
| HTTP timeout | 2 seconds | A slow downstream counts as a failure |

## Endpoints

### `api` (port 8080)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/products` | Product list via the downstream, guarded by the circuit breaker |
| GET | `/circuit` | Breaker introspection: state, failure count, when the next probe is due |
| POST | `/circuit/reset` | Force the breaker back to closed (handy while experimenting) |
| GET | `/health/live` | Liveness — process is up, never checks dependencies |
| GET | `/health/ready` | Readiness — deep check, pings the downstream; 503 when not ready |
| GET | `/health` | Simple shallow status |

`GET /products` responses carry a `source` field so you can always tell where the data came from:

```json
{ "source": "live",            "circuit": "closed", "products": [ ... ] }   // downstream answered
{ "source": "fallback-cache",  "circuit": "open",   "products": [ ... ] }   // stale copy of the last good answer
{ "source": "fallback-static", "circuit": "open",   "products": [ ... ] }   // hardcoded default (never had a good answer)
```

While the circuit is **closed** and a call fails, the api returns `502 Bad Gateway` — that failure is what the breaker counts. Once **open**, requests return `200` with fallback data instead. That trade (an honest error vs. degraded data) is a design decision; the Exercises section asks you to argue the other side.

### `downstream` (port 8081)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/products` | The real catalog — behavior depends on the current mode |
| GET | `/health` | `200 ok` in `ok` mode, `503` in `fail` mode, 5 s delay in `slow` mode |
| POST | `/admin/mode/{mode}` | Set mode: `ok`, `fail` (500s), or `slow` (5 s responses) |
| GET | `/admin/mode` | Current mode |

## Running the Lab

```bash
cd lab06-performance-and-resilience/lab06-02-circuit-breaker
docker compose up --build
```

Keep the compose logs visible in one terminal — the breaker logs every state transition — and run the demo from a second terminal.

## Demo: Trip the Breaker, Watch It Recover

**1. Healthy baseline.** The circuit is closed, data is live:

```bash
$ curl -s localhost:8080/products
{"source":"live","circuit":"closed","products":[{"id":1,"name":"Laptop","price":35000},...]}

$ curl -s localhost:8080/circuit
{"state":"closed","consecutiveFailures":0,"failureThreshold":3,...}
```

**2. Break the downstream.**

```bash
$ curl -s -X POST localhost:8081/admin/mode/fail
{"mode":"fail"}
```

**3. Fail three times.** Each call reaches the downstream, gets a 500, and comes back as a 502. Watch the failure count climb:

```bash
$ curl -s localhost:8080/products    # failure 1
{"error":"downstream unavailable","detail":"...500...","circuit":"closed"}
$ curl -s localhost:8080/products    # failure 2
$ curl -s localhost:8080/products    # failure 3 — trips the breaker
```

The compose logs show the trip:

```
api-1  | warn: [circuit] CLOSED -> OPEN: 3 consecutive failures
```

**4. Circuit is open — fallback kicks in.** Requests no longer touch the downstream (no `[downstream]` log lines appear) and return instantly with the last good data:

```bash
$ curl -s localhost:8080/products
{"source":"fallback-cache","circuit":"open","products":[...]}

$ curl -s localhost:8080/circuit
{"state":"open","consecutiveFailures":3,...,"retryAt":"..."}
```

This is the whole point: clients get a fast, degraded answer instead of a slow error, and the downstream is no longer being hammered.

**5. Readiness reflects reality.** Liveness stays green (restarting this container would not fix the downstream), readiness goes red so a load balancer would stop routing here:

```bash
$ curl -s localhost:8080/health/live
{"status":"alive"}
$ curl -s -w "\n%{http_code}\n" localhost:8080/health/ready
{"status":"not-ready","checks":{"downstream":"failing (status 503)","circuit":"open"}}
503
```

**6. Half-open probe fails.** Wait ~10 s with the downstream still broken, then request again. The breaker lets exactly one probe through, it fails, and the circuit re-opens for another cool-down:

```
api-1  | warn: [circuit] OPEN -> HALF-OPEN: cool-down elapsed, allowing one probe request
api-1  | warn: [circuit] HALF-OPEN -> OPEN: probe failed, cooling down again
```

**7. Fix the downstream and recover.**

```bash
$ curl -s -X POST localhost:8081/admin/mode/ok
{"mode":"ok"}
# wait ~10 s, then:
$ curl -s localhost:8080/products
{"source":"live","circuit":"closed","products":[...]}
```

```
api-1  | warn: [circuit] OPEN -> HALF-OPEN: cool-down elapsed, allowing one probe request
api-1  | warn: [circuit] HALF-OPEN -> CLOSED: probe succeeded, downstream is back
```

**8. Slowness is failure too.** Try `slow` mode — the downstream now takes 5 s, the api's 2 s HttpClient timeout converts that into failures, and the breaker trips exactly as before:

```bash
$ curl -s -X POST localhost:8081/admin/mode/slow
$ time curl -s localhost:8080/products    # ~2 s, then: "detail":"timeout after 2s"
# two more calls -> breaker opens -> responses are instant again
```

Compare the response times before and after the trip: ~2 s of doomed waiting versus milliseconds of fallback. Without the timeout the picture is far worse — each request would hold a connection for the full 5 s.

## Health Check Design

Three endpoints, three different consumers:

| Endpoint | Question it answers | Consumer | Checks dependencies? |
|----------|--------------------|----------|---------------------|
| `/health/live` | Is the process running? | Orchestrator (restarts the container on failure) | **Never** — restarting you does not fix a broken dependency, and cascading restarts make outages worse |
| `/health/ready` | Can this instance serve real traffic *right now*? | Load balancer (pulls the instance out of rotation on 503) | Yes — deep check |
| `/health` | Quick manual status | Humans, simple monitors | No — cheap and shallow |

**What to expose vs. what to hide.** The ready response names each dependency and its status — enough to answer "what is broken?":

```json
{"status":"not-ready","checks":{"downstream":"failing (status 503)","circuit":"open"}}
```

It must **not** include connection strings, credentials, internal hostnames/IPs, or library versions. Health endpoints are frequently left unauthenticated (load balancers need them), which makes them a favorite reconnaissance target — an attacker who reads `{"db":"postgres://admin:secret@10.0.3.7"}` just got a map of your infrastructure. Names and statuses only.

## Exercises

1. **Tune the trade-offs.** Lower the threshold to 1 and the cool-down to 3 s. What goes wrong with an over-sensitive breaker when the downstream only *occasionally* hiccups? (Hint: a single blip now costs you 3 s of fallback for every client.)
2. **Failure rate instead of consecutive count.** Three consecutive failures at 1 request/min means something very different than at 1000 requests/s. Change the breaker to trip when >50% of the last 20 requests failed.
3. **Honest errors vs. degraded data.** Change the open-circuit behavior to return `503 Service Unavailable` with a `Retry-After` header instead of fallback data. Which behavior does an e-commerce product page want? A payment authorization endpoint?
4. **Per-dependency breakers.** Add a second downstream (copy the service) and give each its own breaker. Why must the breakers be separate? What happens with a shared one when only one dependency dies?
5. **Half-open capacity.** Allow 3 concurrent probes in half-open and require all 3 to succeed before closing. When is the extra caution worth it?

## Production Notes

- In real .NET services, use **Polly** through `Microsoft.Extensions.Http.Resilience` (`AddStandardResilienceHandler()` gives you retry + timeout + circuit breaker in the recommended order) rather than hand-rolling.
- A circuit breaker is one member of a family that works together: **timeout** (bound each attempt), **retry with backoff and jitter** (survive transient blips — but never retry through an open circuit), **bulkhead** (cap concurrent calls per dependency so one cannot exhaust the pool), and **fallback** (what to do when all else fails).
- The breaker state above lives in process memory: each instance of your API learns about the outage independently. That is usually fine — and far simpler than sharing breaker state — but know that 10 instances each need their own 3 failures to trip.
- Gateways from lab03 can host this pattern centrally: Kong, Envoy, and YARP (via Polly) all support circuit breaking at the gateway layer, protecting every route at once.
