using System.Diagnostics;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy.Model;

// API keys the gateway accepts. In production these live in a database or
// secret store (see lab03-04 API Key Management); a static table keeps this
// lab focused on the gateway itself.
var apiKeys = new Dictionary<string, ApiClient>
{
    ["demo-key-mobile"] = new("mobile-app", IsInternal: false),
    ["demo-key-partner"] = new("partner-web", IsInternal: false),
    ["internal-secret-key"] = new("billing-service", IsInternal: true),
};

// One token bucket per client name: 10 requests burst, refills 1 token every
// 6 seconds (10 requests/minute) — same budget as lab03-02, but enforced
// here once for every backend service.
var rateLimiter = PartitionedRateLimiter.Create<string, string>(clientName =>
    RateLimitPartition.GetTokenBucketLimiter(clientName, _ => new TokenBucketRateLimiterOptions
    {
        TokenLimit = 10,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(6),
        AutoReplenishment = true,
        QueueLimit = 0,
    }));

// GLOBAL limiter: ONE bucket shared by every caller — internal services
// included. This is capacity protection, not fairness: "orders-service can
// handle ~600 req/min", written per second (10 tokens refilled every 1 s).
// Routes opt into it with "RateLimitPolicy": "global" in their metadata.
var globalCapacityLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
{
    TokenLimit = 10,                                  // burst: 10 at once
    TokensPerPeriod = 10,                             // refill 10 tokens...
    ReplenishmentPeriod = TimeSpan.FromSeconds(1),    // ...every second = 600/min
    AutoReplenishment = true,
    QueueLimit = 0,
});

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:3000")
    .AllowAnyMethod()
    .AllowAnyHeader()
    .WithExposedHeaders("X-Request-Id", "X-RateLimit-Limit", "X-RateLimit-Remaining")));

var app = builder.Build();

// --- Correlation ID -------------------------------------------------------
// Every request gets an X-Request-Id at the edge. Backends receive it and
// echo it in their own logs, so one ID traces a request across services.
app.Use(async (context, next) =>
{
    var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault()
                    ?? Guid.NewGuid().ToString("N")[..12];
    context.Items["RequestId"] = requestId;
    context.Request.Headers["X-Request-Id"] = requestId;
    context.Response.Headers["X-Request-Id"] = requestId;
    await next();
});

// --- Centralized request logging ------------------------------------------
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();
    var client = context.Items.TryGetValue("ClientName", out var c) ? c : "anonymous";
    app.Logger.LogInformation("[gateway] {Method} {Path} -> {Status} ({Elapsed} ms) client={Client} rid={RequestId}",
        context.Request.Method, context.Request.Path, context.Response.StatusCode,
        sw.ElapsedMilliseconds, client, context.Items["RequestId"]);
});

app.UseCors();

// The gateway's own health endpoint — not proxied.
app.MapGet("/health", () => Results.Json(new { status = "ok", service = "gateway" }));

app.MapReverseProxy(proxyPipeline =>
{
    // --- Centralized authentication ---------------------------------------
    // Routes carry an AuthClass in their metadata: "external" routes accept
    // any valid API key; "internal" routes only accept internal service keys.
    proxyPipeline.Use(async (context, next) =>
    {
        var route = context.GetReverseProxyFeature().Route.Config;
        var authClass = route.Metadata?.GetValueOrDefault("AuthClass") ?? "external";

        var key = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (key is null || !apiKeys.TryGetValue(key, out var client))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "missing or invalid API key" });
            return;
        }

        if (authClass == "internal" && !client.IsInternal)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "internal route: external clients not allowed" });
            return;
        }

        context.Items["ClientName"] = client.Name;
        context.Items["IsInternal"] = client.IsInternal;

        // The backend never sees the API key — it trusts headers the
        // gateway injects after authenticating the caller.
        context.Request.Headers.Remove("X-Api-Key");
        context.Request.Headers["X-Client-Name"] = client.Name;
        context.Request.Headers["X-Forwarded-By"] = "api-gateway";
        await next();
    });

    // --- Centralized rate limiting ----------------------------------------
    // Each route picks its policy via "RateLimitPolicy" metadata:
    //   "per-client" (default) — every authenticated client has its own
    //       bucket (10/min), shared across all per-client routes. Internal
    //       traffic is exempt: this policy is about fairness between clients.
    //   "global" — ONE bucket for everyone, internal included (10 req/s).
    //       This policy is about protecting backend capacity.
    proxyPipeline.Use(async (context, next) =>
    {
        var route = context.GetReverseProxyFeature().Route.Config;
        var policy = route.Metadata?.GetValueOrDefault("RateLimitPolicy") ?? "per-client";

        if (policy == "global")
        {
            using var lease = globalCapacityLimiter.AttemptAcquire(1);
            context.Response.Headers["X-RateLimit-Policy"] = "global";
            context.Response.Headers["X-RateLimit-Limit"] = "10";
            if (!lease.IsAcquired)
            {
                context.Response.Headers["X-RateLimit-Remaining"] = "0";
                context.Response.Headers["Retry-After"] = "1";
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new { error = "server at capacity, retry later" });
                return;
            }
            await next();
            return;
        }

        // per-client policy — internal service-to-service traffic is exempt
        if (context.Items["IsInternal"] is true)
        {
            await next();
            return;
        }

        var clientName = (string)context.Items["ClientName"]!;
        using var clientLease = await rateLimiter.AcquireAsync(clientName, 1, context.RequestAborted);

        context.Response.Headers["X-RateLimit-Policy"] = "per-client";
        context.Response.Headers["X-RateLimit-Limit"] = "10";
        if (!clientLease.IsAcquired)
        {
            context.Response.Headers["X-RateLimit-Remaining"] = "0";
            context.Response.Headers["Retry-After"] = "6";
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "rate limit exceeded, retry later" });
            return;
        }
        await next();
    });
});

app.Run("http://0.0.0.0:8080");

internal record ApiClient(string Name, bool IsInternal);
