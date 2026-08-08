# Group 01: REST Fundamentals

Build a solid foundation — from your first HTTP endpoint to database-backed CRUD with file handling.

## Labs

| # | Lab | Description |
|---|-----|-------------|
| 01-01 | [Hello API](lab01-01-hello-api/) | Your first ASP.NET Core minimal API — return `{"message": "Hello, World!"}` |
| 01-02 | [JSON Response](lab01-02-json-response/) | Return structured JSON with C# records and serialization attributes |
| 01-03 | [CRUD In-Memory](lab01-03-crud-in-memory/) | Full Create/Read/Update/Delete API with an in-memory store |
| 01-04 | [CRUD with Database](lab01-04-crud-with-database/) | Replace in-memory store with PostgreSQL |
| 01-05 | [File Upload & Download](lab01-05-file-upload-download/) | Upload files via multipart form, store in MinIO (S3-compatible) |

## How to Run

```bash
cd lab01-01-hello-api
docker compose up --build
```
