using System.Text.Json.Serialization;

// The public-facing API. Every call to the downstream product catalog goes
// through one CircuitBreaker instance (bottom of this file). When the
// downstream is healthy the breaker is invisible; when it starts failing,
// the breaker trips OPEN and this API answers from a fallback instead of
// queueing up doomed 2-second timeouts.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var downstreamUrl = Environment.GetEnvironmentVariable("DOWNSTREAM_URL") ?? "http://localhost:8081";

// The timeout is part of the resilience design: without it, a "slow"
// dependency (5 s per request) would tie up this API's capacity long before
// the breaker ever saw a failure. Timeout turns slowness into fast failure.
var http = new HttpClient { BaseAddress = new Uri(downstreamUrl), Timeout = TimeSpan.FromSeconds(2) };

var breaker = new CircuitBreaker(
    failureThreshold: 3,
    openDuration: TimeSpan.FromSeconds(10),
    log: message => app.Logger.LogWarning("{Message}", message));

// Fallbacks, best first: the last response that succeeded (stale but real),
// then a hardcoded minimal catalog if we have never seen a good response.
List<Product>? lastGoodProducts = null;
var staticFallback = new List<Product>
{
    new(1, "Laptop", 35000m),
    new(2, "Mouse", 590m),
};

app.MapGet("/products", async () =>
{
    if (!breaker.AllowRequest())
    {
        // Circuit is OPEN: fail fast. No network call, no 2 s wait — the
        // downstream gets breathing room and the client gets an instant answer.
        var (products, source) = lastGoodProducts is not null
            ? (lastGoodProducts, "fallback-cache")
            : (staticFallback, "fallback-static");
        return Results.Json(new ProductsResponse(source, breaker.StateName, products));
    }

    try
    {
        var products = await http.GetFromJsonAsync<List<Product>>("/products")
            ?? throw new HttpRequestException("empty response body");
        lastGoodProducts = products;
        breaker.RecordSuccess();
        return Results.Json(new ProductsResponse("live", breaker.StateName, products));
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        var reason = ex is TaskCanceledException ? "timeout after 2s" : ex.Message;
        breaker.RecordFailure(reason);
        return Results.Json(
            new { error = "downstream unavailable", detail = reason, circuit = breaker.StateName },
            statusCode: 502);
    }
});

// Observability into the breaker itself — in the demo you will watch this
// endpoint while flipping the downstream between ok/fail.
app.MapGet("/circuit", () => Results.Json(breaker.Snapshot()));

app.MapPost("/circuit/reset", () =>
{
    breaker.Reset();
    return Results.Json(breaker.Snapshot());
});

// ---- Health check design -------------------------------------------------
// /health/live  : "is the process running?" — never checks dependencies.
//                 An orchestrator restarts the container when this fails,
//                 and restarting *us* does not fix a broken downstream.
// /health/ready : "can we serve real traffic?" — deep check that pings the
//                 dependency. A load balancer stops routing to us while 503.
// /health       : cheap human-friendly summary (and backwards compatible
//                 with every other lab in this workshop).
// Note what the ready response exposes: dependency *names and statuses*
// only. No connection strings, no internal URLs, no credentials.

app.MapGet("/health/live", () => Results.Json(new { status = "alive" }));

app.MapGet("/health/ready", async () =>
{
    string downstreamStatus;
    var ready = false;
    try
    {
        var response = await http.GetAsync("/health");
        ready = response.IsSuccessStatusCode;
        downstreamStatus = ready ? "ok" : $"failing (status {(int)response.StatusCode})";
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        downstreamStatus = ex is TaskCanceledException ? "timeout" : "unreachable";
    }

    var body = new
    {
        status = ready ? "ready" : "not-ready",
        checks = new { downstream = downstreamStatus, circuit = breaker.StateName },
    };
    return Results.Json(body, statusCode: ready ? 200 : 503);
});

app.MapGet("/health", () => Results.Json(new { status = "ok", service = "circuit-breaker-api" }));

app.Run("http://0.0.0.0:8080");

internal record Product(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("price")] decimal Price);

internal record ProductsResponse(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("circuit")] string Circuit,
    [property: JsonPropertyName("products")] List<Product> Products);

internal enum CircuitState { Closed, Open, HalfOpen }

// A deliberately small, explicit circuit breaker so every state transition
// is visible. Production code would reach for Polly
// (Microsoft.Extensions.Http.Resilience) — same state machine, more knobs.
internal sealed class CircuitBreaker(int failureThreshold, TimeSpan openDuration, Action<string> log)
{
    private readonly object _lock = new();
    private CircuitState _state = CircuitState.Closed;
    private int _consecutiveFailures;
    private DateTimeOffset? _openedAt;
    private bool _probeInFlight;
    private string? _lastError;

    public string StateName
    {
        get
        {
            lock (_lock)
            {
                return Name(_state);
            }
        }
    }

    // Called before every downstream request. false means "do not even try —
    // serve the fallback". Also where OPEN -> HALF-OPEN happens: the first
    // caller after the cool-down becomes the probe.
    public bool AllowRequest()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitState.Closed:
                    return true;
                case CircuitState.Open:
                    if (DateTimeOffset.UtcNow - _openedAt >= openDuration)
                    {
                        _state = CircuitState.HalfOpen;
                        _probeInFlight = true;
                        log("[circuit] OPEN -> HALF-OPEN: cool-down elapsed, allowing one probe request");
                        return true;
                    }
                    return false;
                default: // HalfOpen: only one probe at a time
                    if (_probeInFlight)
                        return false;
                    _probeInFlight = true;
                    return true;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == CircuitState.HalfOpen)
                log("[circuit] HALF-OPEN -> CLOSED: probe succeeded, downstream is back");
            _state = CircuitState.Closed;
            _consecutiveFailures = 0;
            _openedAt = null;
            _probeInFlight = false;
            _lastError = null;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_lock)
        {
            _lastError = error;
            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Open;
                _openedAt = DateTimeOffset.UtcNow;
                _probeInFlight = false;
                log("[circuit] HALF-OPEN -> OPEN: probe failed, cooling down again");
                return;
            }

            _consecutiveFailures++;
            if (_state == CircuitState.Closed && _consecutiveFailures >= failureThreshold)
            {
                _state = CircuitState.Open;
                _openedAt = DateTimeOffset.UtcNow;
                log($"[circuit] CLOSED -> OPEN: {_consecutiveFailures} consecutive failures");
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitState.Closed;
            _consecutiveFailures = 0;
            _openedAt = null;
            _probeInFlight = false;
            _lastError = null;
            log("[circuit] reset to CLOSED");
        }
    }

    public object Snapshot()
    {
        lock (_lock)
        {
            return new
            {
                state = Name(_state),
                consecutiveFailures = _consecutiveFailures,
                failureThreshold,
                openDurationSeconds = (int)openDuration.TotalSeconds,
                openedAt = _openedAt,
                retryAt = _openedAt?.Add(openDuration),
                lastError = _lastError,
            };
        }
    }

    private static string Name(CircuitState state) => state switch
    {
        CircuitState.Closed => "closed",
        CircuitState.Open => "open",
        _ => "half-open",
    };
}
