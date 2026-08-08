# Lab 04-05: Evolving API Without Versioning

## Overview

Instead of creating new versions, evolve the API by only making additive, non-breaking changes. No version number appears anywhere — the API grows over time while maintaining backward compatibility.

This approach works best for internal APIs with controlled clients.

## Evolution History

This API started with basic fields and evolved through additive changes:

| Evolution | Fields Added | Breaking? |
|-----------|-------------|-----------|
| Initial | `id`, `name`, `price`, `category` | - |
| Evolution 1 | `description` | No (additive) |
| Evolution 2 | `tags` | No (additive) |
| Evolution 3 | `sku` | No (additive) |
| Evolution 4 | `categories` (replaces `category`) | No (old field kept) |

## Backward Compatibility Rules

1. **Add fields, never remove** — new fields are simply ignored by old clients
2. **Deprecate, don't delete** — mark old fields as deprecated but keep returning them
3. **Accept both old and new** — the `POST` endpoint accepts both `category` (old) and `categories` (new)

## Deprecation Headers

Every response includes headers signaling the deprecated `category` field:

```
X-Deprecated-Fields: category
X-Deprecated-Message: The 'category' field is deprecated. Use 'categories' array instead.
X-API-Sunset: 2026-12-31
```

## Tolerant Reader Pattern

Clients should be designed to ignore unknown fields. The .NET implementation includes a `TolerantProductDto` that uses `JsonExtensionData` to absorb unknown fields. In Go, the default JSON unmarshaling already ignores unknown fields.

## Getting Started

```bash
docker compose up --build
```

## Try It Out

### List products

```bash
curl http://localhost:8080/api/products
```

### Get single product

```bash
curl http://localhost:8080/api/products/1
```

### Create product (using old `category` field)

```bash
curl -X POST http://localhost:8080/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Keyboard","price":79.99,"category":"electronics"}'
```

### Create product (using new `categories` field)

```bash
curl -X POST http://localhost:8080/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Mouse","price":29.99,"categories":["electronics"],"description":"Wireless mouse","tags":["wireless"],"sku":"ELEC-002"}'
```

### Check deprecation headers

```bash
curl -v http://localhost:8080/api/products 2>&1 | grep -i "x-deprecated\|x-api-sunset"
```

### Expected Response

```json
{
  "id": 1,
  "name": "Laptop",
  "price": 999.99,
  "category": "electronics",
  "description": "A powerful laptop for developers",
  "tags": ["portable", "computing"],
  "sku": "ELEC-001",
  "categories": ["electronics"]
}
```

Note: Both `category` (deprecated) and `categories` (new) are present in every response.

## When This Works

- Internal APIs with controlled clients
- APIs where you can enforce the tolerant reader pattern
- Small teams where you can coordinate client updates
- APIs with simple, additive evolution needs

## When This Fails

- Public APIs with many third-party clients
- When you need fundamentally different response shapes
- When breaking changes are unavoidable (type changes, field renames)
- Large organizations where client coordination is impractical

## Key Concepts

- Additive-only changes avoid the need for version numbers
- Deprecated fields are kept for backward compatibility, not removed
- Deprecation headers inform clients programmatically
- The tolerant reader pattern makes clients resilient to API evolution
- This approach has limits — sometimes versioning IS the right answer

## Cleanup

```bash
docker compose down -v
```
