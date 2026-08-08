# Group 03: API Security

Protect your APIs — authentication, rate limiting, sensitive data handling, API keys, and gateway patterns.

## Labs

| # | Lab | Description | Status |
|---|-----|-------------|--------|
| 03-01 | [Authentication](lab03-01-authentication/) | JWT tokens, bcrypt password hashing, auth middleware | ✅ Implemented |
| 03-02 | [Rate Limiting & CORS](lab03-02-rate-limiting-and-cors/) | Token bucket rate limiting, CORS configuration | ✅ Implemented |
| 03-03 | [Sensitive Data Handling](lab03-03-sensitive-data/) | Data masking, field-level security per role, log scrubbing | ✅ Implemented |
| 03-04 | API Key Management | Key lifecycle (create, rotate, revoke), header-based auth | ❌ Not yet implemented |
| 03-05 | [API Gateway](lab03-05-api-gateway/) | YARP reverse proxy, centralized auth/rate limiting/logging, path rewriting | ✅ Implemented |
| 03-06 | [API Gateway with Kong](lab03-06-api-gateway-kong/) | Same gateway, zero code — Kong DB-less with key-auth/ACL/rate-limiting plugins | ✅ Implemented |

## How to Run

```bash
cd lab03-01-authentication
docker compose up --build
```
