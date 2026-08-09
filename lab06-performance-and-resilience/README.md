# Group 06: Performance & Resilience

The labs so far assume every dependency answers, and answers quickly. Production does not work that way: databases saturate, downstream services crash, networks stall. This group is about making an API **fast** (caching) and **reliable when its dependencies are not** (circuit breakers, timeouts, fallbacks, health checks).

## Learning Path

| #  | Sub-Lab | Topic | Description | Status |
|----|---------|-------|-------------|--------|
| 01 | lab06-01-caching-with-redis | Caching Strategies | Cache-aside and write-through with Redis, TTL expiry, invalidation on update/delete, HTTP cache headers (`Cache-Control`, `ETag`, `304 Not Modified`). | ⏳ Planned |
| 02 | [lab06-02-circuit-breaker](lab06-02-circuit-breaker/) | Circuit Breaker & Health Checks | Closed → Open → Half-Open state machine guarding a flaky downstream, fallback responses, timeout-as-failure, and liveness vs. readiness health check design. | ✅ |

## Why This Group Matters

A single slow dependency can take down an entire system through **cascade failure**: callers pile up waiting on timeouts, their callers pile up waiting on them, and the failure propagates upstream. The resilience patterns in this group — fail fast, degrade gracefully, recover automatically — are what separate an API that survives a bad day from one that amplifies it.

Prerequisites: comfortable with the REST fundamentals from Groups 01–02. Lab06-02 pairs naturally with the observability work in lab04-08 (you cannot tune a circuit breaker you cannot see).
