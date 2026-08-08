# Lab 04-04: Content Negotiation (Media Type Versioning)

## Overview

Version is embedded in the `Accept` header using vendor media types. This is the most REST-pure approach, as the resource URL never changes -- only the representation does.

Example: `Accept: application/vnd.workshop.v1+json`

## How It Works

- Endpoint: `/api/products` (no version anywhere in URL)
- Client sends `Accept: application/vnd.workshop.v1+json` for V1
- Client sends `Accept: application/vnd.workshop.v2+json` for V2
- Response `Content-Type` matches the vendor media type
- `Vary: Accept` header for proper caching
- Returns 406 Not Acceptable for unsupported media types
- Defaults to v1 when standard `application/json` or no Accept header

## Benefits and Drawbacks

| Aspect | Details |
|--------|---------|
| Benefits | True REST compliance; URL perfectly stable forever; GitHub API v3 uses this |
| Drawbacks | High developer friction; minimal tooling/Swagger support; middleware often strips custom media types |
| Pitfall | CORS, caching proxies, and API gateways often strip Accept header variants |
| Verdict | Architecturally correct per REST purists. Practically painful for most teams |

## Real-World Examples

- GitHub API v3: `Accept: application/vnd.github.v3+json`

## Getting Started

```bash
docker compose up --build
```

## Try It Out

### Default (V1 -- standard Accept or none)

```bash
curl http://localhost:8080/api/products
```

### V1 (explicit vendor media type)

```bash
curl -H "Accept: application/vnd.workshop.v1+json" http://localhost:8080/api/products
```

### V2

```bash
curl -H "Accept: application/vnd.workshop.v2+json" http://localhost:8080/api/products
curl -H "Accept: application/vnd.workshop.v2+json" http://localhost:8080/api/products/1
```

### 406 Not Acceptable

```bash
curl -H "Accept: application/xml" http://localhost:8080/api/products
```

### Check response headers

```bash
curl -v -H "Accept: application/vnd.workshop.v2+json" http://localhost:8080/api/products 2>&1 | grep -i "content-type\|vary"
```

Expected:

```
< Content-Type: application/vnd.workshop.v2+json
< Vary: Accept
```

## When to Use Content Negotiation

- When URL stability is an absolute requirement
- When REST compliance matters (academic, standardization-heavy environments)
- When you can control all consumers (SDKs, internal services)

## When NOT to Use

- Public APIs with many third-party developers
- When browser testability matters
- Behind API gateways that strip custom Accept headers
- When CORS is a concern (preflight may strip custom media types)

## Key Concepts

- The most REST-pure versioning approach
- URL never changes -- only the representation
- Requires custom Accept header parsing (not built into most frameworks)
- CORS and API gateways can silently break this approach
- `Vary: Accept` is essential for proper HTTP caching

## Cleanup

```bash
docker compose down -v
```
