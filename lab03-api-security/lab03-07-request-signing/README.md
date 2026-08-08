# Lab 03-07: Request Signing (HMAC-SHA256)

An API key (lab03-04) or a JWT (lab03-01) proves *who is calling* — but the credential itself travels with every request, and it says nothing about the request *content*. If a proxy, a log file, or a man-in-the-middle alters the body in transit, the server can't tell.

**Request signing** fixes both at once. Client and server share a secret; the client computes an HMAC-SHA256 over the request itself (method, path, timestamp, body) and sends only the *signature*. The server recomputes the same HMAC from what it received and compares:

- **The secret never travels** — only a hash derived from it. Nothing to steal off the wire.
- **Tampering is detectable** — change one byte of the body and the signature no longer matches.
- **Old requests expire** — the timestamp is inside the signed string, so a captured request is only useful within the allowed window.

This is exactly how AWS signs API calls (SigV4), how Binance authenticates trading requests, and — in the reverse direction, server signing for a client — how Stripe and GitHub sign webhooks (you verified those in lab09-01; this lab is the client-to-API counterpart).

```
  client                                          api
  ──────                                          ───
  string-to-sign =                                middleware:
    GET\n/orders\n1770000000\n                      read raw body (EnableBuffering)
  sig = HMAC-SHA256(string-to-sign, secret)         rebuild the SAME string-to-sign
                                                    recompute HMAC with the client's secret
  ── X-Client-Id, X-Timestamp, X-Signature ──▶      FixedTimeEquals(received, computed)?
                                                    ├── match → attach client id, continue
                                                    └── else  → 401
```

## Learning Objectives

- Sign a request with HMAC-SHA256 and verify it server-side from the raw bytes
- Understand why the secret never travels — and why that beats sending an API key
- See tampering, stale timestamps, and wrong secrets each get rejected
- Compare signatures in fixed time (`CryptographicOperations.FixedTimeEquals`)
- Understand what a timestamp window does — and does *not* — protect against (replay)

## Project Layout

```
lab03-07-request-signing/
  docker-compose.yml     # api on :8080; client runs the demo sequence and exits
  api/                   # minimal API: GET/POST /orders behind a verification middleware
    Program.cs           # middleware order: headers -> client -> timestamp -> signature
  client/                # console app: prints string-to-sign + signature, then sends
    Program.cs           # demo sequence (a)–(f): happy paths and every failure mode
```

## The Signing Recipe

**String to sign** (byte-exact — this is a contract, not a suggestion):

```
{METHOD}\n{PATH}\n{X-Timestamp}\n{raw body}
```

The body is the *raw bytes on the wire*, not re-serialized JSON. For a GET, the body is the empty string, so the string-to-sign ends with the trailing `\n`.

**Headers:**

| Header | Meaning |
|--------|---------|
| `X-Client-Id` | Which client is signing — tells the server which secret to verify with |
| `X-Timestamp` | Unix seconds when the request was signed; rejected when more than 300 s from server time (either direction) |
| `X-Signature` | Lowercase hex HMAC-SHA256 of the string-to-sign, keyed with the client's secret |

**Client registry** (in `api/Program.cs`):

| Client id | Secret |
|-----------|--------|
| `mobile-app` | `demo-signing-secret-1` |
| `partner-web` | `demo-signing-secret-2` |

A dictionary keeps the lab focused; real systems store signing secrets the way lab03-04 stores API key hashes — in a database or secrets manager, per client, with rotation.

### Worked example

Method `GET`, path `/orders`, timestamp `1770000000`, empty body. The string to sign is `GET\n/orders\n1770000000\n`, and you can reproduce the exact signature with one openssl line:

```bash
$ printf 'GET\n/orders\n1770000000\n' | openssl dgst -sha256 -hmac "demo-signing-secret-1"
SHA2-256(stdin)= 1b2eb5edeb1f3c3d94a55982d801ca375d3d71be86e2626ea3b6b4cba389af81
```

`1b2eb5…af81` is precisely what the client computes and what the api recomputes. Any tool that produces a different hex for this input has a recipe bug — usually a missing trailing `\n` or a re-serialized body.

## Run It

```bash
docker compose up --build
```

The `client` container waits for the api, runs the demo sequence (a)–(f) printing every string-to-sign and signature, then exits. Watch its output next to the api's log lines.

Compose sets `SIGNING_DEBUG=true`, which makes the api return an `X-Debug-String-To-Sign` response header showing exactly what *it* reconstructed — invaluable when your signature "should" match but doesn't. **You would never ship this**: it hands an attacker the exact preimage to brute-force the secret offline, and debug headers have a way of surviving into production.

## Try It: sign a request by hand

The whole recipe fits in three shell lines (`$NF` takes the last field because openssl prefixes its output, e.g. `SHA2-256(stdin)= …`):

**GET:**

```bash
timestamp=$(date +%s)
sig=$(printf 'GET\n/orders\n%s\n' "$timestamp" | openssl dgst -sha256 -hmac "demo-signing-secret-1" | awk '{print $NF}')
curl -s http://localhost:8080/orders \
  -H "X-Client-Id: mobile-app" \
  -H "X-Timestamp: $timestamp" \
  -H "X-Signature: $sig"
```

**POST** (note: no trailing `\n` after a non-empty body, and `$body` must be byte-identical to what curl sends):

```bash
timestamp=$(date +%s)
body='{"item":"Webcam","amount":990}'
sig=$(printf 'POST\n/orders\n%s\n%s' "$timestamp" "$body" | openssl dgst -sha256 -hmac "demo-signing-secret-1" | awk '{print $NF}')
curl -s -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -H "X-Client-Id: mobile-app" \
  -H "X-Timestamp: $timestamp" \
  -H "X-Signature: $sig" \
  -d "$body"
```

Now break it on purpose: re-run the curl with `-d '{"item":"Webcam","amount":1}'` but the *old* `$sig` — the api answers `401 {"error":"invalid signature"}` and the `X-Debug-String-To-Sign` header shows you the string it reconstructed from the tampered body.

`GET /health` needs no signature — health probes shouldn't hold secrets.

## Failure Modes

The middleware checks in a fixed order; the first failure wins:

| # | Check | Response | Demo step |
|---|-------|----------|-----------|
| 1 | Any of the three headers missing | `401 {"error":"missing signature headers"}` | — |
| 2 | `X-Client-Id` not in the registry | `401 {"error":"unknown client"}` | — |
| 3 | Timestamp skew > 300 s (either direction) | `401 {"error":"timestamp outside allowed window"}` | (d) |
| 4 | Recomputed HMAC ≠ `X-Signature` | `401 {"error":"invalid signature"}` | (c) tampered body, (e) wrong secret |

Note that a tampered body and a wrong secret produce the *same* error — the server can't distinguish them, and shouldn't try: any mismatch means the request cannot be trusted.

**Fixed-time comparison.** The api compares the raw signature bytes with `CryptographicOperations.FixedTimeEquals`, never `==`. A naive comparison returns as soon as one byte differs, so response timing leaks how many leading bytes matched — enough, statistically, to forge a signature byte by byte.

## API Key vs JWT vs Request Signing

| | API key (lab03-04) | JWT (lab03-01) | HMAC signing (this lab) |
|---|---|---|---|
| What travels | The secret itself | A signed, expiring token | Only a hash — never the secret |
| Proves | Possession of the key | Claims signed by the issuer | Possession of the secret **and** request integrity |
| Body tampering detected | No | No | **Yes** |
| Replay protection | None | Until token expiry | Timestamp window (+ nonce, see below) |
| Cost per request | String lookup | Signature + claims validation | HMAC over the full body, both sides |
| Client complexity | Trivial | Trivial (attach header) | Must implement the recipe byte-exactly |
| Typical use | Server-to-server, low stakes | User sessions, delegated identity | Payments, trading, cloud APIs (AWS SigV4, Binance) |

These compose: AWS credentials are essentially an API key *pair* where the secret part is only ever used to sign.

## Replay: what the window does and doesn't do

Step (f) of the demo replays request (a) **verbatim** — same timestamp, same signature — and it succeeds. That's not a bug in the lab; it's the honest limit of the scheme so far:

- The **timestamp window** bounds the damage: a captured request is useful for at most 300 s, not forever. It also prevents an attacker from stockpiling old requests.
- Within the window, the server has no way to tell a legitimate request from its byte-perfect copy — the signature is, by design, reproducible.

The standard fix is a **nonce**: the client adds a random `X-Nonce` to the signed string; the server remembers nonces seen inside the window and rejects duplicates. The window is what makes this practical — the nonce cache only needs to hold 300 seconds of traffic, not eternity. That's exercise 1.

## Key Concepts

| Concept | Where in this lab |
|---------|-------------------|
| String-to-sign contract | `BuildSignedRequest` in `client/Program.cs`, mirrored in the api middleware |
| Raw-body verification | `EnableBuffering()` + reading bytes before model binding |
| Secret selection | `X-Client-Id` → dictionary lookup (never trust the client to name the secret's value) |
| Freshness | `X-Timestamp` inside the signed string, 300 s skew check |
| Fixed-time compare | `CryptographicOperations.FixedTimeEquals` on raw bytes |
| Debug transparency | `X-Debug-String-To-Sign` behind `SIGNING_DEBUG` (lab only) |

## Exercises

1. **Nonce cache** — add `X-Nonce` to the string-to-sign and the headers. In the api, keep a set of `(clientId, nonce)` seen in the last 300 s and reject duplicates with `401 {"error":"nonce already used"}`. Confirm demo step (f) now fails. Bonus: evict expired entries so the cache doesn't grow forever.
2. **Verify at the gateway** — move signature verification into the lab03-05 YARP gateway: the gateway verifies, strips the signature headers, and injects `X-Client-Name`, so backends never see (or need) signing code. What new problem does the gateway's *path rewriting* create for the signed path?
3. **Sign responses too** — have the api sign its response bodies with the same recipe (a status line instead of method/path) and have the client verify. Now the client can also detect a tampered *response* — which is exactly what webhook signing in lab09-01 does.
4. **Canonical querystrings** — the current recipe signs only the path. Add `GET /orders?item=Webcam` support: sort the query parameters, include them in the string-to-sign, and think about why *sorting* is required (hint: proxies and clients don't agree on parameter order — this is most of the complexity of AWS SigV4).
