# Lab 03-02: Rate Limiting and CORS

## Learning Objectives

- Implement token bucket rate limiting with .NET's built-in `System.Threading.RateLimiting`
- Configure CORS for cross-origin requests
- Set rate limit response headers
- Handle 429 Too Many Requests

## Getting Started

```bash
docker compose up --build
```

The API will be available at `http://localhost:8080`.

To run locally without Docker (requires a PostgreSQL instance on `localhost:5432`):

```bash
dotnet run
```

## Test Rate Limiting

Check the `X-RateLimit-*` headers in the response:

```bash
curl -v http://localhost:8080/products
```

Look for these headers in the response:

```
X-RateLimit-Limit: 10
X-RateLimit-Remaining: 9
X-RateLimit-Reset: 1709000000
```

Use a loop to hit the rate limit (the server allows 10 requests per minute):

```bash
for i in $(seq 1 15); do
  curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/products
  echo
done
```

You should see `200` for the first 10 requests and `429` for the remaining ones.

## Test CORS

Send a preflight OPTIONS request from an allowed origin:

```bash
curl -v -X OPTIONS http://localhost:8080/products \
  -H "Origin: http://localhost:3000" \
  -H "Access-Control-Request-Method: GET"
```

You should see CORS headers in the response:

```
Access-Control-Allow-Origin: http://localhost:3000
Access-Control-Allow-Methods: GET,POST,PUT,DELETE,OPTIONS
Access-Control-Allow-Headers: Accept,Authorization,Content-Type
Access-Control-Allow-Credentials: true
Access-Control-Max-Age: 300
```

Try from a disallowed origin:

```bash
curl -v -X OPTIONS http://localhost:8080/products \
  -H "Origin: http://evil.com" \
  -H "Access-Control-Request-Method: GET"
```

The CORS headers will not be present in the response.

## Code Walkthrough

### Token Bucket Rate Limiter

.NET 8 ships a token bucket implementation in `System.Threading.RateLimiting`, so we configure it instead of writing the algorithm by hand:

```csharp
var rateLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    RateLimitPartition.GetTokenBucketLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,                                // bucket capacity
            TokensPerPeriod = 1,                            // tokens added per period
            ReplenishmentPeriod = TimeSpan.FromSeconds(6),  // 1 token / 6s = 10 per minute
            QueueLimit = 0,
            AutoReplenishment = true
        }));
```

- `TokenLimit` -- the maximum capacity of the bucket
- `TokensPerPeriod` / `ReplenishmentPeriod` -- the refill rate (1 token every 6 seconds = 10 per minute)
- `QueueLimit = 0` -- reject immediately instead of queueing when the bucket is empty

### Per-IP Bucketing

`PartitionedRateLimiter` maintains a separate `TokenBucketRateLimiter` per partition key -- here, the client's IP address. Each IP gets its own independent bucket, so one client hitting the limit does not affect others.

### Rejection Handling

The rate limiter middleware invokes `OnRejected` when the bucket is empty:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = rateLimiter;
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "6";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ErrorResponse("Rate limit exceeded. Try again later."), cancellationToken);
    };
});
```

A small middleware registered before `UseRateLimiter()` reads the bucket statistics (`GetStatistics().CurrentAvailablePermits`) and stamps the `X-RateLimit-*` headers onto every response.

### CORS Configuration

The standard CORS middleware is configured with the same options as the Go lab:

```csharp
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:3000", "http://localhost:8081")
    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
    .WithHeaders("Accept", "Authorization", "Content-Type")
    .WithExposedHeaders("X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset")
    .AllowCredentials()
    .SetPreflightMaxAge(TimeSpan.FromSeconds(300))));
```

- **WithOrigins** -- only these origins can make cross-origin requests
- **WithMethods** -- HTTP methods permitted in cross-origin requests
- **WithHeaders** -- headers the client is allowed to send
- **WithExposedHeaders** -- headers the browser JavaScript can read from the response
- **AllowCredentials** -- allows cookies and auth headers in cross-origin requests
- **SetPreflightMaxAge** -- how long the browser caches preflight results

`app.UseCors()` runs before the rate limiter, so preflight requests are answered without consuming tokens.

## Rate Limit Headers

| Header | Description |
|---|---|
| `X-RateLimit-Limit` | Maximum number of requests allowed in the window |
| `X-RateLimit-Remaining` | Number of requests remaining in the current window |
| `X-RateLimit-Reset` | Unix timestamp when the rate limit window resets |
| `Retry-After` | Seconds to wait before retrying (only on 429 responses) |

## Exercises

1. **Per-endpoint rate limits** -- Add different rate limits for different endpoints using named policies (`AddPolicy` + `RequireRateLimiting("policyName")`). For example, allow 20 GET requests per minute but only 5 POST requests per minute.

2. **API key rate limiting** -- Instead of rate limiting by IP address, rate limit by an `X-API-Key` header. Partition buckets by the API key and fall back to IP-based limiting when no key is provided.

3. **Rate limit status endpoint** -- Add a `GET /rate-limit-status` endpoint that returns the current state of the caller's token bucket (remaining tokens, refill rate, reset time) using `GetStatistics()`.

4. **Environment-based CORS origins** -- Configure the allowed CORS origins from an environment variable (e.g., `CORS_ORIGINS=http://localhost:3000,https://myapp.com`) instead of hardcoding them.

## Key Concepts

### Token Bucket Algorithm

The token bucket algorithm controls the rate of requests by maintaining a "bucket" of tokens:

- The bucket starts full (e.g., 10 tokens)
- Each request consumes one token
- Tokens are replenished at a fixed rate (e.g., 10 per minute)
- When the bucket is empty, requests are rejected with a 429 status
- This allows short bursts of traffic while enforcing an average rate

### CORS (Cross-Origin Resource Sharing)

CORS is a browser security mechanism that restricts cross-origin HTTP requests:

- **Preflight requests** -- the browser sends an OPTIONS request before the actual request to check if the server allows the cross-origin call
- **Allowed origins** -- the server specifies which origins are permitted
- **Exposed headers** -- by default, browsers only expose a small set of response headers to JavaScript; `WithExposedHeaders` lets you make additional headers accessible (like the rate limit headers)

### Rate Limit Headers

Standard rate limit headers help clients understand and respect the limits:

- Clients can check `X-RateLimit-Remaining` to throttle their own requests
- The `Retry-After` header on 429 responses tells clients exactly when to retry
- Exposing these headers via CORS ensures browser-based clients can read them

## Cleanup

```bash
docker compose down -v
```
