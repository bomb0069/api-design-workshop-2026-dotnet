# Lab 04-02: Query Parameter Versioning

## Overview

Version is specified as a query parameter: `/api/products?api-version=1`. The URL path stays stable while the version travels as a parameter.

## How It Works

- Single endpoint `/api/products` serves all versions
- Version resolved from `?api-version=N` query parameter
- Defaults to v1 when parameter is omitted

## Benefits and Drawbacks

| Aspect | Details |
|--------|---------|
| Benefits | Resource URL stays stable; easy to test in browser; natively supported by frameworks |
| Drawbacks | Version leaks into logs/bookmarks; can break HTTP caching; version can be accidentally omitted |
| Cache Impact | CDN/proxy caches store separate responses per `?api-version=` value — cache miss rate inflation |
| Verdict | Excellent as secondary reader alongside URL versioning. Avoid as sole strategy for CDN-cached public APIs |

## Getting Started

```bash
docker compose up --build
```

## Try It Out

### Default (V1 — no parameter)

```bash
curl http://localhost:8080/api/products
```

### Explicit V1

```bash
curl "http://localhost:8080/api/products?api-version=1"
curl "http://localhost:8080/api/products/1?api-version=1"
```

### V2

```bash
curl "http://localhost:8080/api/products?api-version=2"
curl "http://localhost:8080/api/products/1?api-version=2"
```

### Create product (V2)

```bash
curl -X POST "http://localhost:8080/api/products?api-version=2" \
  -H "Content-Type: application/json" \
  -d '{"name":"Keyboard","price":79.99,"category":"electronics","description":"Mechanical keyboard","tags":["input","mechanical"]}'
```

### Expected V1 Response

```json
[
  {"id":1,"name":"Laptop","price":999.99,"category":"electronics"},
  {"id":2,"name":"Go Book","price":39.99,"category":"books"},
  {"id":3,"name":"T-Shirt","price":19.99,"category":"clothing"}
]
```

### Expected V2 Response

```json
{
  "data": [
    {"id":1,"name":"Laptop","price":999.99,"category":"electronics","description":"A powerful laptop for developers","tags":["portable","computing"]},
    ...
  ],
  "version": "2.0"
}
```

## When to Use Query Parameter Versioning

- As a secondary versioning mechanism alongside URL path
- For internal tools that need browser-testable versioning
- When URL stability is important but discoverability is still needed

## When NOT to Use

- As the sole strategy for CDN-heavy public APIs (cache fragmentation)
- When version omission could cause silent v1 lock-in

## Key Concepts

- Same URL, different responses based on query parameter
- Default version prevents breaking existing clients
- Cache-unfriendly compared to URL path versioning
- Middleware extracts version before handlers execute

## Cleanup

```bash
docker compose down -v
```
