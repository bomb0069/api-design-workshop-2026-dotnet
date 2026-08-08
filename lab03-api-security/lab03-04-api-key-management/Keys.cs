using System.Security.Cryptography;
using System.Text;

// Key material helpers. Two rules this whole lab hangs on:
//
//   1. Raw keys are generated from a CSPRNG and returned to the caller ONCE.
//   2. The database stores only SHA-256(raw key). If the DB leaks, the
//      attacker holds hashes they cannot reverse into working credentials.
//
// SHA-256 (not bcrypt/argon2) is fine here: API keys are 128-bit random
// values, not human passwords, so brute-forcing the preimage is hopeless
// and a fast hash keeps per-request lookups cheap.
public static class Keys
{
    // e.g. ak_live_4f9a0c2d5e6b71829384a5b6c7d8e9f0
    // "ak_live_" mimics real-world key formats (secret key, live environment):
    // the prefix makes leaked keys easy to recognize in code scans and logs.
    public static string NewRawKey() =>
        "ak_live_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    // Display prefix stored alongside the hash so admins can tell keys apart
    // ("ak_live_4f9a..." vs "ak_live_9c1b...") without ever storing the key.
    public static string Prefix(string rawKey) => rawKey[..Math.Min(12, rawKey.Length)];

    public static string Sha256Hex(string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
}
