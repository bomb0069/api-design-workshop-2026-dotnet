# Group 02: REST Design Conventions

Learn to design APIs well — naming, parameters, validation, error handling, response formats, and documentation.

## Labs

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 02-01 | Resource Naming | Nouns, plural, kebab-case, nested resources | ❌ Not yet implemented |
| 02-02 | [Path & Query Parameters](lab02-02-path-and-query-parameters/) | Extract URL parameters, decision rules for path vs query | ✅ Implemented |
| 02-03 | [Request Validation](lab02-03-request-validation/) | Validate incoming data with go-playground/validator | ✅ Implemented |
| 02-04 | [Error Handling](lab02-04-error-handling/) | Consistent error responses with centralized error types | ✅ Implemented |
| 02-05 | Response Envelope & Status Codes | Unified envelope, status code decisions (200 vs 404 for empty search) | ❌ Not yet implemented |
| 02-06 | Data Formats | ISO 8601 dates, null handling, enums, money/decimal | ❌ Not yet implemented |
| 02-07 | [Pagination & Filtering](lab02-07-pagination-and-filtering/) | `?page=`, `?sort=`, `?category=` with response metadata | ✅ Implemented |
| 02-08 | [Swagger Documentation](lab02-08-swagger-documentation/) | OpenAPI 3.0 spec with interactive Swagger UI | ✅ Implemented |

## How to Run

```bash
cd lab02-02-path-and-query-parameters
docker compose up --build
```
