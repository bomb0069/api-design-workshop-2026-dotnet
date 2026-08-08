# Group 04: API Versioning

API versioning is how you evolve your API without breaking existing clients. This lab covers the full spectrum: from choosing where to place the version identifier (URL, query string, header, media type) through deprecation workflows and production observability. Each sub-lab is a self-contained, runnable project that demonstrates one versioning strategy or operational concern.

## Learning Path

Work through the sub-labs in order. Each one builds on concepts introduced in the previous labs.

| #  | Sub-Lab | Topic | Description |
|----|---------|-------|-------------|
| 01 | [lab04-01-url-path-versioning](lab04-01-url-path-versioning/) | URL Path Versioning | Four URL placement variations: version as root prefix (`/v1/api/resource`), after the API prefix (`/api/v1/resource`), as resource suffix (`/api/resource/v1`), and baked into the resource name (`/api/resource-v1`). Covers trade-offs, routing pitfalls, and the recommended default. |
| 02 | [lab04-02-query-parameter-versioning](lab04-02-query-parameter-versioning/) | Query Parameter Versioning | Version selected via `?api-version=1`. Keeps URLs stable, easy to test in a browser. Explores caching implications and default-version behavior when the parameter is omitted. |
| 03 | [lab04-03-header-versioning](lab04-03-header-versioning/) | Header-Based Versioning | Version selected via the `X-Api-Version` request header. Ideal for internal service-to-service APIs where URL cleanliness matters and all consumers are controlled. |
| 04 | [lab04-04-content-negotiation](lab04-04-content-negotiation/) | Content Negotiation / Media Type Versioning | Version embedded in the `Accept` header (`application/vnd.myapi.v1+json`). The most REST-pure approach; examines why it is architecturally correct but operationally painful. |
| 05 | [lab04-05-evolving-api](lab04-05-evolving-api/) | Evolving API Without Versioning | Additive-only changes: add fields, never remove them. Demonstrates how far you can go without introducing a version at all, using tolerant readers and extensibility contracts. |
| 06 | [lab04-06-combining-strategies](lab04-06-combining-strategies/) | Combining Multiple Versioning Strategies | Run URL path, query parameter, and header versioning simultaneously. A single request can carry the version in any of the three locations, with a defined precedence order. |
| 07 | [lab04-07-breaking-changes-and-deprecation](lab04-07-breaking-changes-and-deprecation/) | Breaking Changes and Deprecation | Classify changes as safe, breaking, or context-dependent. Implement `Deprecation` and `Sunset` response headers (RFC 8594), `Link` headers (RFC 8288), and HTTP 410 Gone tombstone responses. |
| 08 | [lab04-08-version-lifecycle-and-observability](lab04-08-version-lifecycle-and-observability/) | Version Lifecycle and Observability | Full lifecycle management (current, maintained, deprecated, end-of-life) backed by Prometheus metrics and structured logs. Build dashboards showing per-version traffic splits to decide when it is safe to sunset a version. |

## Which Labs Should I Do?

Not everyone has time for all 8 labs. Here is a guide based on how much time you have.

### Must-Do (core knowledge)

These two labs cover what 90% of real-world APIs need. Do these first.

| Lab | Why it matters | Time |
|-----|---------------|------|
| **01 -- URL Path Versioning** | The industry default. You will use this pattern in almost every API you build. | ~20 min |
| **07 -- Breaking Changes & Deprecation** | Knowing what counts as a breaking change and how to sunset a version is more important than any specific versioning mechanism. | ~20 min |

### Recommended (if you have a few more hours)

These deepen your understanding and cover common production needs.

| Lab | Why it matters | Time |
|-----|---------------|------|
| **05 -- Evolving API** | Many teams version too early. This lab teaches you how far you can go without versioning at all -- a skill that saves real engineering effort. | ~15 min |
| **06 -- Combining Strategies** | The realistic production pattern. Most public APIs combine URL + query + header. Worth seeing how the pieces fit together. | ~15 min |
| **08 -- Lifecycle & Observability** | Without metrics you are guessing when to sunset. This lab connects versioning to operational reality. | ~20 min |

### Optional (nice to know)

These cover alternative strategies. Useful for broadening your perspective, but you can skip them if time is tight.

| Lab | When it is useful |
|-----|------------------|
| **02 -- Query Parameter** | If your API needs a secondary version reader for browser/tool testing. The concept is simple -- skim the README, run one curl. |
| **03 -- Header Versioning** | If you build internal microservice APIs. Same middleware pattern as lab 04-02, just a different extraction point. |

### Skip Unless You Need It

| Lab | Why you can skip it |
|-----|-------------------|
| **04 -- Content Negotiation** | The most REST-pure approach, but almost nobody uses it in practice. High friction, poor tooling support, CORS issues. Read the README for awareness, but don't spend time coding it unless your team specifically requires media-type versioning. |

### Quick Reference

```
Have 1 hour?     -> Lab 04-01, 04-07
Have 2-3 hours?  -> Lab 04-01, 04-05, 04-06, 04-07
Have half a day? -> Lab 04-01, 04-05, 04-06, 04-07, 04-08
Have a full day? -> All labs in order (01-08)
```

## How to Run a Sub-Lab

Each sub-lab is a self-contained ASP.NET Core project. Start the containers:

```bash
# Example: run the URL path versioning lab
cd lab04-01-url-path-versioning
docker compose up --build
```

To stop and clean up:

```bash
docker compose down -v
```

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/)
- [Docker Compose](https://docs.docker.com/compose/install/) (v2+)

No local .NET SDK is required -- everything runs inside containers.

## Project Structure

```
lab04-api-versioning/
  README.md                              # This file
  lab04-01-url-path-versioning/
    ApiVersioning.csproj
    Program.cs
    Dockerfile
    docker-compose.yml
    ...
  lab04-02-query-parameter-versioning/
  ...
  lab04-08-version-lifecycle-and-observability/
```

Each sub-lab is fully self-contained with its own `Dockerfile`, `docker-compose.yml`, source code, and configuration files.

## Key Concepts

- **URL versioning strategies** -- where to place the version in the URL path and why `/api/v1/resource` is the industry default
- **Query parameter and header versioning** -- keeping URLs stable by moving the version to `?api-version=` or `X-Api-Version`
- **Media type versioning** -- content negotiation via the `Accept` header for strict REST compliance
- **Breaking vs non-breaking changes** -- classification rules for deciding whether a change requires a new version
- **Deprecation and Sunset headers** -- RFC 8594 (`Sunset`), RFC 8288 (`Link`), and the `Deprecation` header for communicating end-of-life to clients
- **Version lifecycle management** -- the four stages (current, maintained, deprecated, end-of-life) and policies for transitioning between them
- **Observability** -- Prometheus metrics labeled by `api_version`, structured log fields, and distributed trace attributes for tracking per-version traffic and deciding when to sunset

## Reference

The [CLAUDE.md](CLAUDE.md) file in this directory contains the full knowledge base for this lab series, including detailed implementation patterns, code samples in Go and .NET, decision guides, and workshop exercise ideas.
