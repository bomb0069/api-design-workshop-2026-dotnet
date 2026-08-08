using System.Security.Cryptography;
using System.Text;

// Signing client: builds the string-to-sign, computes the HMAC, prints every
// step, then sends. Same registry as the api's:
const string ClientId = "mobile-app";
const string Secret = "demo-signing-secret-1";
const string WrongSecret = "not-the-real-secret";

var apiBase = Environment.GetEnvironmentVariable("API_BASE") ?? "http://api:8080";
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

Console.WriteLine($"Signing client starting, API base: {apiBase}");
await WaitForApiAsync();

// --- (a) valid GET -------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== (a) Valid GET /orders — expect 200 ===");
var requestA = BuildSignedRequest("GET", "/orders", body: "", ClientId, Secret);
await SendAsync(requestA);

// --- (b) valid POST ------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== (b) Valid POST /orders — expect 201 ===");
var requestB = BuildSignedRequest("POST", "/orders",
    body: """{"item":"Mechanical Keyboard","amount":2590}""", ClientId, Secret);
await SendAsync(requestB);

// --- (c) body tampered AFTER signing -------------------------------------
Console.WriteLine();
Console.WriteLine("=== (c) POST with body tampered AFTER signing — expect 401 invalid signature ===");
var requestC = BuildSignedRequest("POST", "/orders",
    body: """{"item":"Mechanical Keyboard","amount":2590}""", ClientId, Secret);
var tamperedBody = """{"item":"Mechanical Keyboard","amount":1}""";
Console.WriteLine($"  body sent on the wire (tampered): {tamperedBody}");
Console.WriteLine("  the signature still covers the ORIGINAL body — the server will notice");
await SendAsync(requestC with { Body = tamperedBody });

// --- (d) stale timestamp -------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== (d) X-Timestamp 10 minutes old — expect 401 timestamp outside allowed window ===");
var staleTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600;
var requestD = BuildSignedRequest("GET", "/orders", body: "", ClientId, Secret, staleTimestamp);
Console.WriteLine("  signature itself is VALID for that timestamp — only the age is wrong");
await SendAsync(requestD);

// --- (e) wrong secret ----------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== (e) Signed with the WRONG secret — expect 401 invalid signature ===");
Console.WriteLine($"  signing with \"{WrongSecret}\" but claiming to be {ClientId}");
var requestE = BuildSignedRequest("GET", "/orders", body: "", ClientId, WrongSecret);
await SendAsync(requestE);

// --- (f) replay of (a) ---------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== (f) Replay of request (a) VERBATIM — expect 200 ===");
Console.WriteLine("  re-sending the exact same headers and signature captured in (a)");
await SendAsync(requestA);
Console.WriteLine("  replay within the window still succeeds — see README exercise: add a nonce cache");

Console.WriteLine();
Console.WriteLine("Demo sequence complete.");
return;

// -------------------------------------------------------------------------

SignedRequest BuildSignedRequest(string method, string path, string body,
    string clientId, string secret, long? timestampOverride = null)
{
    var timestamp = timestampOverride ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // The exact recipe the server reconstructs:
    //   {METHOD}\n{PATH}\n{X-Timestamp}\n{raw body}
    var stringToSign = $"{method}\n{path}\n{timestamp}\n{body}";

    var signature = Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(stringToSign)))
        .ToLowerInvariant();

    Console.WriteLine($"  method:         {method}");
    Console.WriteLine($"  path:           {path}");
    Console.WriteLine($"  timestamp:      {timestamp}");
    Console.WriteLine($"  body:           {(body == "" ? "(empty)" : body)}");
    Console.WriteLine($"  string-to-sign: \"{stringToSign.Replace("\n", "\\n")}\"");
    Console.WriteLine($"  signature:      HMAC-SHA256(string-to-sign, secret) = {signature}");

    return new SignedRequest(method, path, timestamp, body, clientId, signature);
}

async Task SendAsync(SignedRequest signed)
{
    using var request = new HttpRequestMessage(new HttpMethod(signed.Method), apiBase + signed.Path);
    if (signed.Body != "")
    {
        request.Content = new StringContent(signed.Body, Encoding.UTF8, "application/json");
    }
    request.Headers.Add("X-Client-Id", signed.ClientId);
    request.Headers.Add("X-Timestamp", signed.Timestamp.ToString());
    request.Headers.Add("X-Signature", signed.Signature);

    using var response = await http.SendAsync(request);
    var responseBody = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"  -> {signed.Method} {signed.Path}");
    Console.WriteLine($"  <- {(int)response.StatusCode} {responseBody}");
    if (response.Headers.TryGetValues("X-Debug-String-To-Sign", out var debug))
    {
        Console.WriteLine($"  server reconstructed: \"{string.Join("", debug)}\"");
    }
}

async Task WaitForApiAsync()
{
    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try
        {
            using var response = await http.GetAsync(apiBase + "/health");
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("API is up.");
                return;
            }
        }
        catch (HttpRequestException)
        {
            // api not up yet
        }
        Console.WriteLine($"Waiting for API ({attempt})...");
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
    throw new InvalidOperationException($"API at {apiBase} did not become healthy");
}

record SignedRequest(string Method, string Path, long Timestamp, string Body, string ClientId, string Signature);
