# Lab 04-01: URL Path Versioning

## Overview

URL path versioning places the version number directly in the URL. This is the most common and visible versioning strategy, used by GitHub, Stripe, and Twilio.

## The Four URL Placement Variations

### Variation 1: Version as Root Prefix

Pattern: `/v{version}/api/products`
Example: `/v1/api/products`, `/v2/api/products`

| Aspect | Details |
|--------|---------|
| Benefits | Immediately obvious; easy to route at reverse proxy level |
| Drawbacks | Version prefix before `/api` breaks REST convention |
| Pitfall | Gateway routing rules written as `/api/*` will silently miss these |
| Verdict | Uncommon. Only use if you want the version to gate the entire API surface |

### Variation 2: Version After /api (Recommended)

Pattern: `/api/v{version}/products`
Example: `/api/v1/products`, `/api/v2/products`

| Aspect | Details |
|--------|---------|
| Benefits | Industry convention (GitHub, Stripe, Twilio); easy to bookmark and share |
| Drawbacks | URLs change when version changes; violates strict REST purism |
| Verdict | Default choice for most teams |

### Variation 3: Version as Resource Suffix

Pattern: `/api/products/v{version}`
Example: `/api/products/v1`, `/api/products/v2`

| Aspect | Details |
|--------|---------|
| Benefits | Resource name stays prominent |
| Drawbacks | Clashes with sub-resources; looks like a nested resource named "v1" |
| Pitfall | Route conflicts with `/api/products/{id}` |
| Verdict | Avoid |

### Variation 4: Version Baked into Name (Anti-pattern)

Pattern: `/api/products-v{version}`
Example: `/api/products-v1`, `/api/products-v2`

| Aspect | Details |
|--------|---------|
| Benefits | No routing conflicts; works without any versioning framework |
| Drawbacks | Not real versioning — just different resource names; no framework support |
| Verdict | Anti-pattern. Only as absolute last resort |

## Comparison Table

| Variation | Pattern | Recommended | Used By |
|-----------|---------|-------------|---------|
| Root Prefix | `/v1/api/products` | No | Rare |
| After /api | `/api/v1/products` | Yes | GitHub, Stripe, Twilio |
| Resource Suffix | `/api/products/v1` | No | None major |
| Baked into Name | `/api/products-v1` | No | None (anti-pattern) |

## Getting Started

```bash
docker compose up --build
```

## Try It Out

### Discovery endpoint

```bash
curl http://localhost:8080/
```

### V1 — Flat response (Variation 2, recommended)

```bash
# List all products
curl http://localhost:8080/api/v1/products

# Get single product
curl http://localhost:8080/api/v1/products/1

# Create product
curl -X POST http://localhost:8080/api/v1/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Keyboard","price":79.99,"category":"electronics"}'
```

### V2 — Wrapped response (Variation 2)

```bash
curl http://localhost:8080/api/v2/products
curl http://localhost:8080/api/v2/products/1
```

### Other URL variations

```bash
# Variation 1: Root prefix
curl http://localhost:8080/v1/api/products
curl http://localhost:8080/v2/api/products

# Variation 3: Resource suffix
curl http://localhost:8080/api/products/v1
curl http://localhost:8080/api/products/v2

# Variation 4: Baked into name (anti-pattern)
curl http://localhost:8080/api/products-v1
curl http://localhost:8080/api/products-v2
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

## Breaking Changes in V2

- Response wrapped in envelope (`{"data": ..., "version": "2.0"}`)
- Added `description` field
- Added `tags` array field

## Key Concepts

- URL path versioning is the most visible and cacheable strategy
- The `/api/v{N}/resource` pattern is the industry standard
- All four variations use the same handlers — only routing differs
- V1 and V2 share the same database, demonstrating version coexistence

## Cleanup

```bash
docker compose down -v
```

## Implementation Notes

The Go version demonstrates all 4 URL variations simultaneously. The .NET version uses `Asp.Versioning.Mvc` with `UrlSegmentApiVersionReader` for the recommended pattern.
