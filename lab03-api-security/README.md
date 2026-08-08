# Group 03: API Security

Protect your APIs — authentication, rate limiting, sensitive data handling, API keys, and gateway patterns.

## Labs

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 03-01 | [Authentication](lab03-01-authentication/) | JWT tokens, bcrypt password hashing, auth middleware | ✅ Implemented |
| 03-02 | [Rate Limiting & CORS](lab03-02-rate-limiting-and-cors/) | Token bucket rate limiting, CORS configuration | ✅ Implemented |
| 03-03 | Sensitive Data Handling | Data masking, field-level security per role, PII rules | ❌ Not yet implemented |
| 03-04 | API Key Management | Key lifecycle (create, rotate, revoke), header-based auth | ❌ Not yet implemented |
| 03-05 | API Gateway | Reverse proxy, centralized auth/rate limiting/logging | ❌ Not yet implemented |

## How to Run

```bash
cd lab03-01-authentication
docker compose up --build
```
