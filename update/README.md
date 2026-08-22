# Signed updater distribution

HealthAutoArrange 1.1.8+ uses an **unsigned** GitHub-hosted manifest for launch-time update notifications.

## Why no RSA signature (v1.1.8+)

The updater is notification-only since v1.1.7: it fetches a small `latest.txt` from GitHub, compares versions, and — if a newer version exists — shows one in-game notification and offers a button to open the GitHub Release page. It never downloads, stages, extracts, or replaces any DLL.

The previous RSA-2048 signing scheme (v1.1.6 / v1.1.7) was removed in v1.1.8 because:

1. **The signing private key was leaked** during initial key distribution (pasted into a chat session). Anyone with that key can sign manifests the v1.1.7 client would accept. The RSA layer was therefore providing zero actual security.
2. **The updater no longer handles binary content.** The security model for "notification only" is HTTPS transport + GitHub host whitelist + user-driven manual download. Adding RSA on top does not meaningfully reduce the attack surface for this mode.
3. **Simpler code, simpler ops.** No key rotation, no signing secret in CI, no public PEM to ship and validate, no `update_key_check.py` to keep in sync with hardcoded RSA parameters.

The remaining protections are:

- HTTPS-only fetch (UnityWebRequest enforces it; the URL is validated as `https://` before the request fires).
- Host whitelist: the manifest URL **and** any redirect target must be `github.com` or `raw.githubusercontent.com`.
- Notes URL in the manifest must also be a GitHub HTTPS URL.
- 32 KiB streaming cap on manifest size; 8-second timeout; one check per game launch.
- No custom User-Agent (Unity manages it per-version).
- The user manually downloads from the GitHub Release page after the game is closed.

If a future version re-introduces auto-download or staging, a **fresh** RSA key pair will be generated at that time and the public key pinned in the client. The old key (fingerprint below) is considered compromised and will never be re-used:

- Old KeyId: `haa-rsa-2026-01`
- Old public modulus base64 (revoked): `jvnv7JmJi5AB0wGk2FgfwfQ9tOIIafPJGRZvNOdA0kP7umh41EtGt7xhP7fhqToDatqlE/jtXCmVDGzXa/ZXjDIh2ceFK9jR1u3v7Y0oo8E+e21jnT5WVNb6cExcmb04v5P+X2/aTpiBPUrtl9uJAEM1f78jwLeWEk0Avp4grF/RPr/2l+VInJ3HXsEv2hy1oambtwuSnDGp/4DzQDFu2ItOkSUQZ7YsCQllgzagDij2zP8MVGaFVxMVrDPOsRQh+K3ZoQFwI6+UYFhuh485jldj7VlwDP8v7mOLp6AIlfbIGQUMWOe2fi8Kj3uHiMpergUfFPOQ6b6MvaRS3Kq4CQ==`
- Old public exponent base64 (revoked): `AQAB`

## Manifest format (schema=2, unsigned)

```
schema=2
version=<X.Y.Z>
notesUrl=https://github.com/<owner>/<repo>/releases/tag/v<X.Y.Z>
publishedUtc=<ISO 8601 UTC timestamp>
```

Lines are `key=value`, separated by `\n`. Lines starting with `#` are ignored (no comments shipped). Unknown fields are ignored (so v1.1.7 manifests with `assetName=`, `sha256=`, `signature=`, etc. can still be parsed for version comparison without verifying those fields).

## Release workflow

Tagged releases publish the unsigned `latest.txt` to the GitHub `update-dist` branch via the workflow in `.github/workflows/release.yml`. No GitHub Actions secrets are required for the manifest itself (`GAME_REFS_URL` is still required for the Plugin build).

## Network availability

GitHub is intentionally the only update source. If GitHub is unavailable on a route, the check fails quietly and the mod continues; users can check the repository manually later.
