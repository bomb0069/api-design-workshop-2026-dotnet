# Lab 04-03: Header-Based Versioning

## Overview

Version is specified via the `X-Api-Version` request header. The URL stays completely clean -- no version information in the path or query string.

## How It Works

- Endpoint: `/api/products` (no version in URL)
- Client sends `X-Api-Version: 1` or `X-Api-Version: 2` header
- Response includes `X-Api-Version` confirmation header
- `Vary: X-Api-Version` header ensures proper cache behavior
- Defaults to v1 when header is missing

## Benefits and Drawbacks

| Aspect | Details |
|--------|---------|
| Benefits | URL stays completely clean; no caching pollution; ideal for internal service-to-service APIs |
| Drawbacks | Not testable in plain browser; easy to forget; not visible in logs unless explicitly captured |
| Pitfall | Silent API drift -- if `AssumeDefaultVersionWhenUnspecified=true` and client forgets header, they get v1 forever |
| Verdict | Best for internal microservice APIs where all consumers are controlled services |

## Getting Started

```bash
docker compose up --build
```

## Try It Out

### Default (V1 -- no header)

```bash
curl http://localhost:8080/api/products
```

### V1 (explicit)

```bash
curl -H "X-Api-Version: 1" http://localhost:8080/api/products
```

### V2

```bash
curl -H "X-Api-Version: 2" http://localhost:8080/api/products
curl -H "X-Api-Version: 2" http://localhost:8080/api/products/1
```

### Check response headers

```bash
curl -v -H "X-Api-Version: 2" http://localhost:8080/api/products 2>&1 | grep -i "x-api-version\|vary"
```

Expected:

```
< X-Api-Version: 2
< Vary: X-Api-Version
```

### Create product (V2)

```bash
curl -X POST http://localhost:8080/api/products \
  -H "X-Api-Version: 2" \
  -H "Content-Type: application/json" \
  -d '{"name":"Keyboard","price":79.99,"category":"electronics","description":"Mechanical keyboard","tags":["input","mechanical"]}'
```

## Expected Responses

### V1 -- Flat Array

```json
[
  {"id":1,"name":"Laptop","price":999.99,"category":"electronics"},
  {"id":2,"name":"Go Book","price":39.99,"category":"books"},
  {"id":3,"name":"T-Shirt","price":19.99,"category":"clothing"}
]
```

### V2 -- Wrapped Envelope

```json
{
  "data": [
    {"id":1,"name":"Laptop","price":999.99,"category":"electronics","description":"A powerful laptop for developers","tags":["portable","computing"]},
    ...
  ],
  "version": "2.0"
}
```

## When to Use Header Versioning

- Internal microservices where all consumers are controlled
- When clean URLs are a strict requirement
- As part of a combined strategy (URL + query + header)

## When NOT to Use

- Public APIs where developers test in browsers
- When discoverability is important
- When you cannot guarantee all clients will send the header

## Key Concepts

- Clean URLs at the cost of discoverability
- `Vary` header is critical for correct HTTP caching
- Silent drift is the biggest risk -- monitor which clients send the header
- Middleware pattern: extract from header -> set in context -> handlers read from context

## Cleanup

```bash
docker compose down -v
```
