# Signed updater distribution

HealthAutoArrange 1.1.6+ never trusts a mirror by hostname alone.

Tagged releases generate a small `latest.txt` manifest whose canonical fields are signed with RSA-SHA256 (PKCS#1 v1.5). The client pins the matching public key and rejects unsigned/modified manifests. The downloaded ZIP must also match the signed SHA-256 and byte size before it is offered to the user.

Sources checked by the client:

- Official: `raw.githubusercontent.com/.../update-dist/latest.txt`
- CDN fallback: `cdn.jsdelivr.net/gh/...@update-dist/latest.txt`

The release workflow copies the same release ZIP to the `update-dist` branch for CDN fallback. The mirror is only a transport path; authenticity comes from the pinned signature and package hash. If `MirrorManifestUrl` points to another signed mirror (for example a GitCode/Gitee/object-storage mirror), keep the same `latest.txt` + `packages/<version>/<asset>` layout. When that manifest verifies, the client derives the package URL from the same mirror first, then still verifies the signed byte size and SHA-256.

The updater intentionally does **not** replace loaded DLLs and never executes downloaded content. Verified packages are staged under the BepInEx config directory and the user must exit the game before manually replacing the plugin files.

## Maintainer secret

Tagged releases require the GitHub Actions secret `UPDATE_SIGNING_PRIVATE_KEY_PEM`. It must contain the private PEM corresponding to `TRUSTED_UPDATE_PUBLIC_KEY.pem`. Never commit that private key. If the key is lost or compromised, publish a client update with a new pinned public key before switching signing keys.

## Mainland-China mirror note

The default CDN fallback is jsDelivr, but no overseas public CDN can be promised to work on every mainland-China ISP/route. For reliable domestic distribution, mirror the `update-dist` branch to a service you control (GitCode/Gitee/object storage) and set `[Updates] MirrorManifestUrl` to that mirror's HTTPS `latest.txt`. The transport host is deliberately not a trust root: changing mirrors does not require changing the signing key.
