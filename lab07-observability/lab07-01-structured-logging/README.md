# Lab 07-01: Structured Logging

## Overview

This lab is the first stop in the observability group. Before dashboards, metrics, or traces, there is the humble log line — and the single biggest upgrade you can make to it is switching from free-form text to **structured JSON**, stamping every line with a **correlation ID**, and using **log levels** deliberately.

The lab builds a small in-memory Products API and adds three observability pieces around it:

1. **JSON console logging** — every log line is one JSON object (built-in `AddJsonConsole`, no packages).
2. **Correlation ID middleware** — `X-Correlation-ID` in, echoed out, attached to every log line of the request.
3. **Request logging middleware** — one summary line per request: method, path, status, latency, user agent.

Plus a demo endpoint that writes one message at every log level so you can watch the minimum-level filter in action.

## Why Machines-First Logs Beat printf Logs

A classic printf-style log line:

```
Product 42 not found for user bob (took 3ms)
```

Readable — until you have a million of them and need to answer "how many 404s did we serve to bob last hour?" Now you are writing fragile regexes against prose that changes every time someone rewords a message.

The same event as a structured log line:

```json
{"LogLevel":"Warning","Category":"ProductsApi","Message":"Product 42 not found","State":{"product_id":42}}
```

| | printf logs | Structured (JSON) logs |
|---|---|---|
| Primary audience | Humans tailing a terminal | Machines (then humans, via queries) |
| Query "all 404s for product 42" | Regex over prose, breaks on rewording | `jq 'select(.State.product_id == 42)'` or an aggregator filter |
| Fields | Baked into the sentence | First-class keys: `status`, `latency_ms`, `correlation_id` |
| Aggregator support (ELK, Loki, CloudWatch...) | Needs custom parsing rules | Ingested as-is, every field indexed |
| Adding a field later | Edit the sentence, break the parsers | Add a key, old queries keep working |

The rule of thumb: **log events, not sentences**. The `Message` is still there for humans, but every interesting value is also a separate field.

## Correlation IDs: Stitching a Story Across Log Lines (and Services)

One user request produces many log lines — the request summary, business logs, maybe an error. Under load, lines from different requests interleave in the console. A **correlation ID** is the thread that ties one request's lines together:

- If the client sends `X-Correlation-ID`, we **reuse** it.
- If not, we **generate** a GUID.
- Either way we **echo it back** in the response header and stamp it on **every log line** of that request.

The payoff multiplies in a microservice world: when service A calls service B and forwards the same header, one grep across both services' logs reconstructs the entire distributed journey of a single request. That is the manual ancestor of distributed tracing (a later lab).

## Log Levels

| Level | Use for | Example | Production default? |
|-------|---------|---------|---------------------|
| `Trace` | Step-by-step flow, potentially sensitive detail | "entering ProductStore.List" | Off |
| `Debug` | Diagnostic detail useful while developing | "cache miss for product 42" | Off |
| `Information` | Normal business events worth keeping | "order placed", request summaries | **On (typical minimum)** |
| `Warning` | Odd but handled; nobody failed yet | 404 for a missing ID, retry 2/3 | On |
| `Error` | This operation failed; someone should look | payment provider unreachable | On |
| `Critical` | The service itself is in danger | out of disk space, can't reach DB at startup | On |

Two habits worth stealing:

- **A 404 is a `Warning`, not an `Error`** — the API did its job correctly; the client asked for something missing.
- **Levels are a runtime dial, not a rebuild.** You ship with `Information` and turn on `Debug` for one component while diagnosing an incident — see "Changing the Log Level" below.

## Code Walkthrough

### 1. JSON console logging (`Program.cs`)

```csharp
builder.Logging.ClearProviders().AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.JsonWriterOptions = new JsonWriterOptions
    {
        Indented = false // one line per log entry -- friendly to grep/jq/aggregators
    };
});
```

- `ClearProviders()` removes the default human-oriented console logger; without it you would get **two** copies of every line, one plain and one JSON.
- `IncludeScopes = true` is the linchpin of this whole lab: it is what makes the correlation-ID scope (opened in the middleware below) appear on every log line. Forget it and the middleware still runs — but the ID silently vanishes from the output.
- `Indented = false` keeps each entry on a single line. Log shippers and `grep` both treat a line as a record; pretty-printed multi-line JSON breaks that contract.
- UTC timestamps in ISO-8601 (`Z` suffix) sort correctly and never lie about time zones when logs from different hosts meet in one aggregator.

.NET's logging already *is* structured — `LogInformation("Product {product_id} not found", id)` captures `product_id` as a key-value pair, not just interpolated text. The default console formatter throws that structure away and prints prose; `AddJsonConsole` is what finally lets you see it.

### 2. Correlation ID middleware (`Middleware/CorrelationIdMiddleware.cs`)

```csharp
public async Task InvokeAsync(HttpContext context)
{
    // 1. Reuse the caller's ID if they sent one; otherwise mint a new one.
    var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString();
    }

    // 2. Make it available to anything downstream that wants it.
    context.Items[ItemKey] = correlationId;

    // 3. Always echo it back, so the client can quote the ID
    //    when reporting a problem -- even if we generated it ourselves.
    context.Response.Headers[HeaderName] = correlationId;

    // 4. Wrap the REST of the pipeline in a logging scope.
    using (_logger.BeginScope(new Dictionary<string, object>
    {
        ["correlation_id"] = correlationId
    }))
    {
        await _next(context);
    }
}
```

The key move is step 4: `BeginScope` opens an ambient context, and because `await _next(context)` — the *entire rest of the pipeline* — runs **inside** the `using` block, every log line written by any logger during this request (the request summary, the 404 warning, everything) automatically carries `correlation_id`. Nobody downstream has to remember to pass it; that is exactly what makes it reliable.

This is also why registration order in `Program.cs` matters:

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();   // opens the scope first...
app.UseMiddleware<RequestLoggingMiddleware>();  // ...so this one's log line is covered too
```

### 3. Request logging middleware (`Middleware/RequestLoggingMiddleware.cs`)

```csharp
public async Task InvokeAsync(HttpContext context)
{
    // --- way IN: start the clock ---
    var stopwatch = Stopwatch.StartNew();

    await _next(context); // run the rest of the pipeline (routing, handler...)

    // --- way OUT: the response is decided, log the summary ---
    stopwatch.Stop();

    _logger.LogInformation(
        "{method} {path} responded {status} in {latency_ms} ms ({user_agent})",
        context.Request.Method,
        context.Request.Path.Value ?? "/",
        context.Response.StatusCode,
        Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
        context.Request.Headers.UserAgent.ToString());
}
```

This is the standard **middleware sandwich**: everything before `await _next` happens on the way in, everything after happens on the way out. Only on the way out do we know the two most interesting facts about the request — the **status code** the handler chose and the **total latency** including everything downstream. That is why the request log is written by middleware at the edge, not by each handler.

The placeholders are deliberately named `{method}`, `{path}`, `{status}`, `{latency_ms}`, `{user_agent}` — those names become the JSON field names, i.e. the query surface of your logs.

### 4. Log levels demo (`Program.cs`)

```csharp
app.MapGet("/api/demo/levels", () =>
{
    levelsLogger.LogTrace("Trace: step-by-step flow, e.g. 'entering ProductStore.List'");
    levelsLogger.LogDebug("Debug: diagnostic detail, e.g. 'cache miss for product 42'");
    levelsLogger.LogInformation("Information: normal business event, e.g. 'order placed'");
    levelsLogger.LogWarning("Warning: odd but handled, e.g. 'retry 2/3 for payment provider'");
    levelsLogger.LogError("Error: this operation failed, e.g. 'payment provider unreachable'");
    levelsLogger.LogCritical("Critical: the service itself is in danger, e.g. 'out of disk space'");
    ...
});
```

The endpoint always *writes* six messages; the configured minimum level decides how many *survive* to the console. With the default `Information`, you see four (Trace and Debug are dropped before any formatting happens — filtered-out logs are nearly free).

## Anatomy of a Log Line

One request to `GET /api/products/999` with header `X-Correlation-ID: demo-123` produces (trimmed for readability — the real output is one line each):

```json
{
  "Timestamp": "2026-08-09T10:15:03.412Z",
  "LogLevel": "Warning",
  "Category": "ProductsApi",
  "Message": "Product 999 not found",
  "State": { "product_id": 999 },
  "Scopes": [ { "correlation_id": "demo-123" } ]
}
{
  "Timestamp": "2026-08-09T10:15:03.418Z",
  "LogLevel": "Information",
  "Category": "StructuredLogging.Middleware.RequestLoggingMiddleware",
  "Message": "GET /api/products/999 responded 404 in 4.31 ms (curl/8.4.0)",
  "State": {
    "method": "GET",
    "path": "/api/products/999",
    "status": 404,
    "latency_ms": 4.31,
    "user_agent": "curl/8.4.0"
  },
  "Scopes": [ { "correlation_id": "demo-123" } ]
}
```

Note how both lines — written by completely different components — share the same `correlation_id`, courtesy of the scope. (ASP.NET Core adds its own scope with `RequestId`/`RequestPath` as well; you will see it in the real output.)

## Changing the Log Level Without Rebuilding

The minimum level lives in configuration, and configuration has layers. Baseline in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

(`Microsoft.AspNetCore` is capped at `Warning` so framework chatter doesn't drown your application logs — per-category levels are set exactly like this.)

Override it per environment with an env var — `__` (double underscore) stands in for the `:` separator, so `Logging__LogLevel__Default` overrides `Logging:LogLevel:Default`. In `docker-compose.yml`:

```yaml
    environment:
      Logging__LogLevel__Default: Information   # change to Debug or Warning
```

Change the value, run `docker compose up` again (no `--build` needed — the image is untouched), and the same binary logs at the new level. That is the operational win: log verbosity is a deployment knob, not a code change.

## Running the Lab

```bash
cd lab07-observability/lab07-01-structured-logging
docker compose up --build
```

Or locally:

```bash
dotnet run
```

The API listens on http://localhost:8080.

### 1. List products — and meet your first correlation ID

```bash
curl -i http://localhost:8080/api/products
```

```
HTTP/1.1 200 OK
X-Correlation-ID: 0b8f7c2e-4a1d-4f3a-9c2d-8e5b6a7d9e10
Content-Type: application/json; charset=utf-8

[{"id":1,"name":"Laptop","price":999.99},{"id":2,"name":"Mouse","price":19.99},{"id":3,"name":"Monitor","price":249.50}]
```

You didn't send an ID, so the server generated one and echoed it back. In the compose console you'll see two JSON log lines (the `ProductsApi` listing log and the request summary), both carrying that same `correlation_id` in `Scopes`.

### 2. Send your own correlation ID

```bash
curl -i -H "X-Correlation-ID: demo-123" http://localhost:8080/api/products/1
```

```
HTTP/1.1 200 OK
X-Correlation-ID: demo-123
...
{"id":1,"name":"Laptop","price":999.99}
```

The server reused your ID — this is what a calling service would do when forwarding a request.

### 3. Trigger a 404 and watch the Warning

```bash
curl -i -H "X-Correlation-ID: demo-123" http://localhost:8080/api/products/999
```

```
HTTP/1.1 404 Not Found
X-Correlation-ID: demo-123
...
{"error":"Product not found"}
```

Console: a `Warning` line (`Product 999 not found`, with `product_id: 999` in `State`) followed by the request summary with `status: 404` — both tagged `demo-123`.

### 4. Create a product

```bash
curl -i -X POST http://localhost:8080/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Keyboard","price":49.90}'
```

```
HTTP/1.1 201 Created
Location: /api/products/4
...
{"id":4,"name":"Keyboard","price":49.90}
```

### 5. The log levels demo

```bash
curl http://localhost:8080/api/demo/levels
```

With the default `Information` minimum, the console shows **four** lines from `LevelsDemo` (Information, Warning, Error, Critical) plus the request summary — Trace and Debug were filtered out.

### 6. Query the logs like data

```bash
# Everything that happened under one correlation ID:
docker compose logs api | grep demo-123

# Or with jq -- all request summaries slower than 1 ms:
docker compose logs api --no-log-prefix | jq -c 'select(.State.latency_ms? > 1)'

# All warnings-and-worse:
docker compose logs api --no-log-prefix | jq -c 'select(.LogLevel == "Warning" or .LogLevel == "Error" or .LogLevel == "Critical")'
```

This is the point of the whole lab: the moment logs are JSON, the console becomes a queryable database.

## Exercises

1. **Follow one request across many lines.** Call two different endpoints with the same header (`-H "X-Correlation-ID: trace-me-42"`), then run `docker compose logs api | grep trace-me-42`. Every line from both requests appears — that grep is exactly how you would reconstruct a user's journey across *multiple services* if they all forwarded the header.

2. **Turn the verbosity dial.** In `docker-compose.yml`, set `Logging__LogLevel__Default: Warning`, run `docker compose up` (no rebuild), and call `/api/demo/levels` again. The six writes shrink to three surviving lines — and the request summary disappears too (it is logged at Information). Then try `Debug` and watch the demo grow to five lines. Which level would you run in production, and what would you lose?

3. **Add a field: `client_ip`.** Extend `RequestLoggingMiddleware` to log the caller's address as a `{client_ip}` placeholder (`context.Connection.RemoteIpAddress?.ToString()`). Rebuild, make a request, and confirm the new key appears in `State` — note that no existing query or parser broke, which is exercise 1 of "why structured logging".

4. **Per-category levels.** Without touching `Default`, silence only the request summaries by adding `Logging__LogLevel__StructuredLogging.Middleware.RequestLoggingMiddleware: Warning` — wait, env var names can't contain dots on all platforms. Solve it in `appsettings.json` instead (add the full category name under `LogLevel`). This is how you mute one noisy component in production while keeping everything else.

5. **Stretch: propagate downstream.** If you have lab04-08 (or any other lab) running on another port, write a tiny endpoint that calls it with `HttpClient`, forwarding the current request's correlation ID from `HttpContext.Items` as an outgoing `X-Correlation-ID` header. Grep both services' logs for one ID — congratulations, you have manually built the core idea of distributed tracing.
