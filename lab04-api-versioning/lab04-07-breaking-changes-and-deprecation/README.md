# Lab 04-07: Breaking Changes and Deprecation

## Overview

This lab demonstrates how to classify API changes, deprecate old versions using standard HTTP headers (RFC 8594), and handle the full sunset lifecycle including returning 410 Gone after the sunset date.

## Breaking Change Classification

### Safe Changes (No new version required)

- Add an optional request field
- Add a new field to the response body
- Add a new optional query parameter
- Add a new HTTP method to existing resource
- Add a new endpoint/resource entirely
- Widen an accepted value range (e.g. max length 50 -> 200)
- Fix a bug that makes behaviour match documented spec

### Breaking Changes (New version required)

- Remove or rename a request or response field
- Change a field type (string -> int, array -> object)
- Change an HTTP status code for the same outcome
- Make a previously optional request field required
- Narrow an accepted value range (max length 200 -> 50)
- Remove a supported HTTP method
- Change authentication mechanism
- Change pagination structure (cursor vs offset)
- Remove an endpoint entirely

### Context-Dependent

- **Add a new enum value** -- safe if clients handle unknown values; breaking if they use exhaustive switch
- **Change error response shape** -- safe if clients only check HTTP status; breaking if they parse error body
- **Add required header** -- safe for internal clients you control; breaking for external consumers
- **Change URL casing or trailing slash** -- technically safe by HTTP spec; breaks many hardcoded clients

> The new enum value rule of thumb: Document your extensibility contract explicitly. If your API docs say "clients MUST handle unknown enum values gracefully", adding a new value is always safe.

## Deprecation Headers (RFC 8594)

### Deprecation Header

Tells clients this version is deprecated:

```
Deprecation: Sat, 01 Mar 2026 00:00:00 GMT
```

### Sunset Header

Tells clients the exact date this version will stop responding:

```
Sunset: Tue, 01 Sep 2026 00:00:00 GMT
```

### Link Header (RFC 8288)

Points to the migration guide:

```
Link: </docs/migrate-v1-v2>; rel="successor-version"
```

## The Deprecation Lifecycle

1. **Deprecation announced** -- `Deprecation` and `Sunset` headers appear on V1 responses
2. **Before sunset** -- V1 continues to work normally, but every response warns the client
3. **After sunset** -- V1 returns `410 Gone` with a migration URL
4. **Cleanup** -- V1 code can be safely removed

## Getting Started

```bash
docker compose up --build
```

## Try It Out

### V1 (deprecated -- check headers)

```bash
curl -v http://localhost:8080/api/v1/products 2>&1 | grep -i "deprecation\|sunset\|link"
```

Expected:

```
< Deprecation: Sat, 01 Mar 2026 00:00:00 GMT
< Sunset: Tue, 01 Sep 2026 00:00:00 GMT
< Link: </docs/migrate-v1-v2>; rel="successor-version"
```

### V2 (current -- no deprecation headers)

```bash
curl -v http://localhost:8080/api/v2/products 2>&1 | grep -i "deprecation\|sunset"
# (no deprecation headers)
```

### Breaking changes classification endpoint

```bash
curl http://localhost:8080/api/changes | jq
```

### After sunset date -- V1 returns 410 Gone

After September 1, 2026, V1 endpoints will return:

```json
{
  "error": "VERSION_SUNSET",
  "message": "API v1 was sunset on 2026-09-01",
  "migrateUrl": "/docs/migrate-v1-v2"
}
```

## Exercise: Classify These Changes

Review the output of `GET /api/changes` and for each proposed change, decide:

1. Is it safe, breaking, or context-dependent?
2. If context-dependent, what context would make it breaking?
3. How would you implement each change without breaking clients?

## Key Concepts

- Use standard HTTP headers (RFC 8594) for deprecation communication
- Deprecation headers are more reliable than email campaigns
- 410 Gone is the correct HTTP status for sunset endpoints
- Always include a migration URL in both the Link header and 410 response body
- Classify every change before implementing -- prevention is cheaper than fixing

## Cleanup

```bash
docker compose down -v
```
