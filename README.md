# API Design Workshop 2026 — .NET Edition

Welcome to the **API Design Workshop** (.NET Core edition)! A hands-on, progressive workshop that takes you from building your first HTTP endpoint to mastering advanced API technologies — implemented with **ASP.NET Core on .NET 8**.

> This is the C#/.NET sibling of the Go-based [API Design Workshop](https://github.com/bomb0069). Lab numbering, endpoints, and behavior mirror the Go edition so the two can be used side by side.

Each lab includes:
- A **README** with learning objectives, step-by-step instructions, and exercises
- Working **C# / ASP.NET Core source code** you can run, read, and modify
- A **docker-compose.yml** that spins up everything you need with one command

No prior C# experience is required, but basic programming knowledge is helpful.

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and [Docker Compose](https://docs.docker.com/compose/install/)
- A terminal and a code editor
- `curl` (or an API client like Postman)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (optional — all labs run inside Docker)

## Getting Started

```bash
# Pick a lab group and sub-lab
cd lab01-rest-fundamentals/lab01-01-hello-api
docker compose up --build

# Or run locally with the .NET SDK
dotnet run

# Clean up when done
docker compose down -v
```

---

## Labs

### Part 1: REST API

#### Group 01: [REST Fundamentals](lab01-rest-fundamentals/)

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 01-01 | [Hello API](lab01-rest-fundamentals/lab01-01-hello-api/) | Your first ASP.NET Core minimal API | ✅ |
| 01-02 | [JSON Response](lab01-rest-fundamentals/lab01-02-json-response/) | Structured JSON with C# records and serialization attributes | ✅ |
| 01-03 | [CRUD In-Memory](lab01-rest-fundamentals/lab01-03-crud-in-memory/) | Full CRUD API with in-memory data store | ✅ |
| 01-04 | [CRUD with Database](lab01-rest-fundamentals/lab01-04-crud-with-database/) | PostgreSQL-backed CRUD with EF Core / Npgsql | ✅ |
| 01-05 | [File Upload & Download](lab01-rest-fundamentals/lab01-05-file-upload-download/) | Multipart upload, MinIO (S3-compatible) storage | ✅ |

#### Group 02: [REST Design Conventions](lab02-rest-design-conventions/)

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 02-02 | [Path & Query Parameters](lab02-rest-design-conventions/lab02-02-path-and-query-parameters/) | URL parameters, path vs query decision rules | ✅ |
| 02-03 | [Request Validation](lab02-rest-design-conventions/lab02-03-request-validation/) | Validation with structured error responses | ✅ |
| 02-04 | [Error Handling](lab02-rest-design-conventions/lab02-04-error-handling/) | Centralized error types and middleware | ✅ |
| 02-07 | [Pagination & Filtering](lab02-rest-design-conventions/lab02-07-pagination-and-filtering/) | `?page=`, `?sort=`, `?category=` with metadata | ✅ |
| 02-08 | [Swagger Documentation](lab02-rest-design-conventions/lab02-08-swagger-documentation/) | OpenAPI 3.0 with interactive Swagger UI (Swashbuckle) | ✅ |

#### Group 03: [API Security](lab03-api-security/)

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 03-01 | [Authentication](lab03-api-security/lab03-01-authentication/) | JWT tokens, password hashing, auth middleware | ✅ |
| 03-02 | [Rate Limiting & CORS](lab03-api-security/lab03-02-rate-limiting-and-cors/) | .NET rate limiting middleware, CORS headers | ✅ |

#### Group 04: [API Versioning](lab04-api-versioning/)

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 04-01 | [URL Path Versioning](lab04-api-versioning/lab04-01-url-path-versioning/) | `/api/v1/resource` — the industry default | ✅ |
| 04-02 | [Query Parameter](lab04-api-versioning/lab04-02-query-parameter-versioning/) | `?api-version=1` versioning | ✅ |
| 04-03 | [Header Versioning](lab04-api-versioning/lab04-03-header-versioning/) | `X-Api-Version` header | ✅ |
| 04-04 | [Content Negotiation](lab04-api-versioning/lab04-04-content-negotiation/) | Media type versioning via Accept header | ✅ |
| 04-05 | [Evolving API](lab04-api-versioning/lab04-05-evolving-api/) | Additive changes without versioning | ✅ |
| 04-06 | [Combining Strategies](lab04-api-versioning/lab04-06-combining-strategies/) | URL + query + header with priority | ✅ |
| 04-07 | [Breaking Changes](lab04-api-versioning/lab04-07-breaking-changes-and-deprecation/) | Deprecation/Sunset headers, 410 Gone | ✅ |
| 04-08 | [Lifecycle & Observability](lab04-api-versioning/lab04-08-version-lifecycle-and-observability/) | Prometheus metrics, Grafana dashboards | ✅ |

---

### Part 2: Beyond REST

#### Group 08: [GraphQL](lab08-graphql/)

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 08-01 | [GraphQL](lab08-graphql/lab08-01-graphql/) | Schemas, queries, mutations with HotChocolate | ✅ |

#### Group 09: [Real-Time APIs](lab09-real-time-apis/)

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 09-01 | [Webhook](lab09-real-time-apis/lab09-01-webhook/) | HMAC verification, retry logic | ✅ |
| 09-02 | [WebSocket](lab09-real-time-apis/lab09-02-websocket/) | Bidirectional real-time chat | ✅ |

#### Group 10: [gRPC](lab10-grpc/)

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 10-01 | [gRPC Basics](lab10-grpc/lab10-01-grpc-basics/) | Protocol Buffers, server and client | ✅ |
| 10-02 | [gRPC Advanced](lab10-grpc/lab10-02-grpc-advanced/) | Streaming, advanced gRPC features | ✅ |

#### Group 11: [Messaging](lab11-messaging/)

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 11-01 | [Message Queue](lab11-messaging/lab11-01-message-queue/) | RabbitMQ — publisher, consumer, exchanges | ✅ |
| 11-02 | [MQTT](lab11-messaging/lab11-02-mqtt/) | Mosquitto — topics, QoS, wildcards | ✅ |

---

## Learning Path

```
Part 1: REST API
  Group 01  →  Fundamentals (Hello API → CRUD → File handling)
  Group 02  →  Design Conventions (Params, Validation, Errors, Docs)
  Group 03  →  Security (Auth, Rate Limiting & CORS)
  Group 04  →  Versioning (URL, Header, Query, Deprecation, Observability)
        ↓
Part 2: Beyond REST
  Group 08  →  GraphQL
  Group 09  →  Real-Time (Webhook, WebSocket)
  Group 10  →  gRPC (Basics, Streaming)
  Group 11  →  Messaging (RabbitMQ, MQTT)
```
