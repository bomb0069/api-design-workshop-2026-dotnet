# Lab 04-06: Combining Multiple Versioning Strategies

## Overview

In production, APIs often accept version information from multiple sources simultaneously. This lab demonstrates combining URL path, query parameter, and header versioning with a clear priority resolution order.

## Resolution Priority

| Priority | Source | Example | Use Case |
|----------|--------|---------|----------|
| 1 (highest) | URL path | `/api/v2/products` | Public API consumers, documentation |
| 2 | Query parameter | `/api/products?api-version=2` | Browser testing, debugging tools |
| 3 | Header | `X-Api-Version: 2` | Internal microservices |
| 4 (fallback) | Default | v1 | Backward compatibility |

## How It Works

- URL-versioned routes: `/api/v1/products` and `/api/v2/products` (highest priority)
- Non-versioned route: `/api/products` -- resolves from query param, then header, then default
- Response includes `X-Version-Source` header showing which mechanism was used
- Response includes `X-Api-Version` header confirming the resolved version

## Getting Started

```bash
docker compose up --build
```

## Try It Out

### URL path versioning (highest priority)

```bash
curl http://localhost:8080/api/v1/products
curl http://localhost:8080/api/v2/products
```

### Query parameter (second priority)

```bash
curl "http://localhost:8080/api/products?api-version=2"
```

### Header (third priority)

```bash
curl -H "X-Api-Version: 2" http://localhost:8080/api/products
```

### Default (fallback to v1)

```bash
curl http://localhost:8080/api/products
```

### Check version source (Go implementation)

```bash
curl -v http://localhost:8080/api/v2/products 2>&1 | grep "X-Version-Source"
# X-Version-Source: url-path

curl -v "http://localhost:8080/api/products?api-version=2" 2>&1 | grep "X-Version-Source"
# X-Version-Source: query-parameter

curl -v -H "X-Api-Version: 2" http://localhost:8080/api/products 2>&1 | grep "X-Version-Source"
# X-Version-Source: header

curl -v http://localhost:8080/api/products 2>&1 | grep "X-Version-Source"
# X-Version-Source: default
```

### Discovery endpoint (Go)

```bash
curl http://localhost:8080/
```

## When to Combine Strategies

- Public APIs that also serve internal microservices
- APIs that need browser testability AND clean URLs for services
- Migration scenarios: start with header, add URL path later
- Teams that want maximum flexibility

## Implementation Notes

### Go

Uses chained middleware with explicit priority. URL path routes set version directly. Non-URL routes use `combinedVersionMiddleware` that checks query param first, then header, then defaults.

### .NET

Uses `ApiVersionReader.Combine(...)` from `Asp.Versioning`:

```csharp
opt.ApiVersionReader = ApiVersionReader.Combine(
    new UrlSegmentApiVersionReader(),
    new QueryStringApiVersionReader("api-version"),
    new HeaderApiVersionReader("X-Api-Version")
);
```

## Key Concepts

- Multiple version readers can coexist with defined priority
- `X-Version-Source` response header aids debugging
- URL path should always be highest priority (most explicit)
- Combining strategies provides flexibility without sacrificing clarity
- This is the recommended production pattern for large APIs

## Cleanup

```bash
docker compose down -v
```
