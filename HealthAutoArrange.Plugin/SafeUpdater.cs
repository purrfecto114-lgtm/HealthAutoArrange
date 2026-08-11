using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using HealthAutoArrange.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace HealthAutoArrange.Plugin
{
    internal sealed class SafeUpdater
    {
        private const int ManifestSchema = 1;
        private const long MaxPackageBytes = 16L * 1024L * 1024L;
        private const ulong MaxManifestBytes = 32UL * 1024UL;
        private const int ManifestTimeoutSeconds = 8;
        private const int DownloadTimeoutSeconds = 30;
        private const string KeyId = "haa-rsa-2026-01";
        private const string PublicModulusBase64 = "jvnv7JmJi5AB0wGk2FgfwfQ9tOIIafPJGRZvNOdA0kP7umh41EtGt7xhP7fhqToDatqlE/jtXCmVDGzXa/ZXjDIh2ceFK9jR1u3v7Y0oo8E+e21jnT5WVNb6cExcmb04v5P+X2/aTpiBPUrtl9uJAEM1f78jwLeWEk0Avp4grF/RPr/2l+VInJ3HXsEv2hy1oambtwuSnDGp/4DzQDFu2ItOkSUQZ7YsCQllgzagDij2zP8MVGaFVxMVrDPOsRQh+K3ZoQFwI6+UYFhuh485jldj7VlwDP8v7mOLp6AIlfbIGQUMWOe2fi8Kj3uHiMpergUfFPOQ6b6MvaRS3Kq4CQ==";
        private const string PublicExponentBase64 = "AQAB";

        private readonly MonoBehaviour _host;
        private readonly ManualLogSource _log;
        private readonly string _currentVersion;
        private readonly Func<string> _officialManifestUrl;
        private readonly Func<string> _mirrorManifestUrl;
        private readonly Func<bool> _allowMirror;
        private readonly string _downloadDirectory;
        private UpdateManifest _candidate;
        private bool _busy;
        private UpdateUiSnapshot _snapshot;

        private sealed class UpdateManifest
        {
            public string Version;
            public string AssetName;
            public string Sha256;
            public long Size;
            public string OfficialUrl;
            public string MirrorUrl;
            public string NotesUrl;
            public string PublishedUtc;
            public string Signature;
            public bool PreferMirror;
            public string SourcePackageUrl;
        }

        public SafeUpdater(
            MonoBehaviour host,
            ManualLogSource log,
            string currentVersion,
            Func<string> officialManifestUrl,
            Func<string> mirrorManifestUrl,
            Func<bool> allowMirror,
            string downloadDirectory)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _currentVersion = currentVersion ?? "0.0.0";
            _officialManifestUrl = officialManifestUrl ?? throw new ArgumentNullException(nameof(officialManifestUrl));
            _mirrorManifestUrl = mirrorManifestUrl ?? throw new ArgumentNullException(nameof(mirrorManifestUrl));
            _allowMirror = allowMirror ?? throw new ArgumentNullException(nameof(allowMirror));
            _downloadDirectory = downloadDirectory ?? throw new ArgumentNullException(nameof(downloadDirectory));
            _snapshot = new UpdateUiSnapshot(UpdateUiState.Idle, _currentVersion, string.Empty, string.Empty, string.Empty);
        }

        public UpdateUiSnapshot Snapshot => _snapshot;

        public IEnumerator AutoCheckAfterDelay(float seconds)
        {
            if (seconds > 0f) yield return new WaitForSecondsRealtime(seconds);
            if (!_busy) yield return CheckRoutine(false);
        }

        public void CheckNow()
        {
            if (_busy) return;
            _host.StartCoroutine(CheckRoutine(true));
        }

        public void DownloadAvailable()
        {
            if (_busy || _candidate == null || _snapshot.State != UpdateUiState.Available) return;
            _host.StartCoroutine(DownloadRoutine());
        }

        public void OpenReleasePage()
        {
            var url = _candidate?.NotesUrl;
            if (!IsHttpsUrl(url)) return;
            try { Application.OpenURL(url); }
            catch (Exception ex) { _log.LogWarning("HealthAutoArrange updater: could not open release page: " + ex.Message); }
        }

        private IEnumerator CheckRoutine(bool userInitiated)
        {
            _busy = true;
            _snapshot = new UpdateUiSnapshot(UpdateUiState.Checking, _currentVersion, string.Empty, string.Empty, string.Empty);
            _candidate = null;

            var sources = new List<Tuple<string, bool>>();
            var official = NormalizeUrl(_officialManifestUrl());
            var mirror = NormalizeUrl(_mirrorManifestUrl());
            if (IsHttpsUrl(official)) sources.Add(Tuple.Create(official, false));
            if (_allowMirror() && IsHttpsUrl(mirror) && !string.Equals(mirror, official, StringComparison.OrdinalIgnoreCase))
                sources.Add(Tuple.Create(mirror, true));

            if (sources.Count == 0)
            {
                Fail("No valid HTTPS update manifest source is configured.", userInitiated);
                yield break;
            }

            var requests = new List<Tuple<UnityWebRequest, bool>>();
            UpdateManifest firstValid = null;
            string lastError = string.Empty;
            var processed = new HashSet<UnityWebRequest>();

            // Outer try is try-finally ONLY (no catch) so that yield return is
            // permitted inside (CS1626 prohibits yield return in try-with-catch).
            // Per-frame work is wrapped in its own try-catch below; yield return
            // sits between frames, outside any try-catch.
            try
            {
                // Setup: fire off manifest requests. Wrapped in try-catch to
                // preserve the original "any exception -> Fail + cleanup" semantics.
                try
                {
                    foreach (var source in sources)
                    {
                        var request = UnityWebRequest.Get(source.Item1);
                        request.timeout = ManifestTimeoutSeconds;
                        request.redirectLimit = 4;
                        request.SendWebRequest();
                        requests.Add(Tuple.Create(request, source.Item2));
                    }
                }
                catch (Exception ex)
                {
                    Fail(ex.Message, userInitiated);
                    yield break;
                }

                while (true)
                {
                    bool allDone;
                    try
                    {
                        allDone = ProcessCheckFrame();
                    }
                    catch (Exception ex)
                    {
                        Fail(ex.Message, userInitiated);
                        yield break;
                    }
                    if (allDone) break;
                    yield return null;
                }

                // Post-processing: choose highest verified manifest or report failure.
                try
                {
                    if (firstValid != null)
                    {
                        AcceptManifest(firstValid);
                        yield break;
                    }

                    foreach (var item in requests)
                    {
                        var request = item.Item1;
                        if (!string.IsNullOrEmpty(request.error)) lastError = request.error;
                        else if (request.responseCode >= 400) lastError = "HTTP " + request.responseCode.ToString(CultureInfo.InvariantCulture);
                    }
                    Fail(string.IsNullOrEmpty(lastError) ? "No signed update manifest could be verified." : lastError, userInitiated);
                }
                catch (Exception ex)
                {
                    Fail(ex.Message, userInitiated);
                    yield break;
                }
            }
            finally
            {
                foreach (var item in requests) item.Item1.Dispose();
                _busy = false;
            }

            // Local function: processes one frame of the manifest check loop.
            // Closes over requests / processed / firstValid / lastError.
            // Returns true when all requests have finished (success or error).
            // Throws on unexpected error; the caller catches it per-frame.
            bool ProcessCheckFrame()
            {
                var allDone = true;
                foreach (var item in requests)
                {
                    var request = item.Item1;
                    if (!request.isDone)
                    {
                        if (request.downloadedBytes > MaxManifestBytes)
                        {
                            request.Abort();
                            processed.Add(request);
                            lastError = "Update manifest exceeded the 32 KiB safety limit.";
                        }
                        else
                        {
                            allDone = false;
                        }
                        continue;
                    }
                    if (!processed.Add(request)) continue;
                    if (request.downloadedBytes > MaxManifestBytes)
                    {
                        lastError = "Update manifest exceeded the 32 KiB safety limit.";
                        continue;
                    }
                    if (!RequestSucceeded(request))
                    {
                        lastError = string.IsNullOrEmpty(request.error)
                            ? "HTTP " + request.responseCode.ToString(CultureInfo.InvariantCulture)
                            : request.error;
                        continue;
                    }
                    if (request.downloadHandler == null) continue;
                    var text = request.downloadHandler.text;
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    UpdateManifest parsed;
                    string parseError;
                    if (TryParseAndVerifyManifest(text, out parsed, out parseError))
                    {
                        parsed.PreferMirror = item.Item2;
                        // Use request.url (post-redirect) so packages/<v>/<asset> is
                        // derived from the actual serving host, not the original source.
                        parsed.SourcePackageUrl = DeriveTransportPackageUrl(item.Item1.url, parsed.Version, parsed.AssetName);
                        if (firstValid == null || CompareVersions(parsed.Version, firstValid.Version) > 0)
                            firstValid = parsed;
                    }
                    else if (!string.IsNullOrEmpty(parseError)) lastError = parseError;
                }
                return allDone;
            }
        }

        private void AcceptManifest(UpdateManifest manifest)
        {
            _candidate = manifest;
            var comparison = CompareVersions(manifest.Version, _currentVersion);
            if (comparison > 0)
            {
                _snapshot = new UpdateUiSnapshot(UpdateUiState.Available, _currentVersion, manifest.Version,
                    string.Empty, string.Empty);
                _log.LogInfo("HealthAutoArrange updater: verified update v" + manifest.Version + " is available.");
            }
            else
            {
                _snapshot = new UpdateUiSnapshot(UpdateUiState.UpToDate, _currentVersion, manifest.Version,
                    string.Empty, string.Empty);
            }
            _busy = false;
        }

        private IEnumerator DownloadRoutine()
        {
            _busy = true;
            var manifest = _candidate;
            if (manifest == null)
            {
                _snapshot = new UpdateUiSnapshot(UpdateUiState.Failed, _currentVersion, string.Empty,
                    "No verified update is available to download.", string.Empty);
                _busy = false;
                yield break;
            }

            _snapshot = new UpdateUiSnapshot(UpdateUiState.Downloading, _currentVersion, manifest.Version, string.Empty, string.Empty);
            string destination;
            string temporary;
            try
            {
                Directory.CreateDirectory(_downloadDirectory);
                destination = Path.Combine(_downloadDirectory, manifest.AssetName);
                temporary = destination + ".download";
            }
            catch (Exception ex)
            {
                _snapshot = new UpdateUiSnapshot(UpdateUiState.Failed, _currentVersion, manifest.Version,
                    "Could not prepare the update staging directory: " + ex.Message, string.Empty);
                _log.LogWarning("HealthAutoArrange updater: " + _snapshot.Detail);
                _busy = false;
                yield break;
            }

            if (File.Exists(destination) && VerifyDownloadedFile(destination, manifest))
            {
                _snapshot = new UpdateUiSnapshot(UpdateUiState.Downloaded, _currentVersion, manifest.Version,
                    string.Empty, destination, 1f);
                _busy = false;
                yield break;
            }

            var urls = new List<string>();
            if (manifest.PreferMirror && _allowMirror())
            {
                AddUniqueHttps(urls, manifest.SourcePackageUrl);
                AddUniqueHttps(urls, manifest.MirrorUrl);
            }
            AddUniqueHttps(urls, manifest.OfficialUrl);
            if (!manifest.PreferMirror && _allowMirror())
            {
                AddUniqueHttps(urls, manifest.SourcePackageUrl);
                AddUniqueHttps(urls, manifest.MirrorUrl);
            }

            string lastError = string.Empty;
            foreach (var url in urls)
            {
                TryDelete(temporary);
                UnityWebRequest request = null;

                // Outer try is try-finally ONLY (no catch) so that yield return
                // is permitted inside (CS1626 prohibits yield return in try-with-catch).
                // Setup, per-frame work, and post-loop checks each have their own
                // try-catch; yield return sits outside any try-catch.
                try
                {
                    // 1) Setup + start request — try-catch (no yield)
                    UnityWebRequestAsyncOperation operation;
                    try
                    {
                        request = new UnityWebRequest(url, "GET");
                        request.timeout = DownloadTimeoutSeconds;
                        request.redirectLimit = 6;
                        request.downloadHandler = new DownloadHandlerFile(temporary);
                        operation = request.SendWebRequest();
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        continue;
                    }

                    // 2) Download loop — yield OUTSIDE try-catch, INSIDE outer try-finally
                    var exceededLimit = false;
                    while (!operation.isDone)
                    {
                        bool shouldBreak;
                        try
                        {
                            if (request.downloadedBytes > (ulong)MaxPackageBytes)
                            {
                                exceededLimit = true;
                                lastError = "Update package exceeded the 16 MiB safety limit.";
                                request.Abort();
                                shouldBreak = true;
                            }
                            else
                            {
                                var progress = request.downloadProgress;
                                _snapshot = new UpdateUiSnapshot(UpdateUiState.Downloading, _currentVersion, manifest.Version,
                                    string.Empty, string.Empty, progress < 0f ? 0f : progress);
                                shouldBreak = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            lastError = ex.Message;
                            shouldBreak = true;
                        }
                        if (shouldBreak) break;
                        yield return null;
                    }

                    // 3) Post-loop checks + move — try-catch (yield break is OK in try-catch)
                    try
                    {
                        if (exceededLimit || request.downloadedBytes > (ulong)MaxPackageBytes)
                        {
                            lastError = "Update package exceeded the 16 MiB safety limit.";
                        }
                        else if (!RequestSucceeded(request))
                        {
                            lastError = string.IsNullOrEmpty(request.error) ? "HTTP " + request.responseCode : request.error;
                        }
                        else if (!VerifyDownloadedFile(temporary, manifest))
                        {
                            lastError = "Downloaded package failed size or SHA-256 verification.";
                        }
                        else
                        {
                            TryDelete(destination);
                            File.Move(temporary, destination);
                            _snapshot = new UpdateUiSnapshot(UpdateUiState.Downloaded, _currentVersion, manifest.Version,
                                string.Empty, destination, 1f);
                            _log.LogInfo("HealthAutoArrange updater: verified update downloaded to " + destination);
                            _busy = false;
                            yield break;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }
                finally
                {
                    request?.Dispose();
                    if (File.Exists(temporary) && !VerifyDownloadedFile(temporary, manifest)) TryDelete(temporary);
                }
            }

            TryDelete(temporary);
            _snapshot = new UpdateUiSnapshot(UpdateUiState.Failed, _currentVersion, manifest.Version,
                string.IsNullOrEmpty(lastError) ? "Update download failed." : lastError, string.Empty);
            _log.LogWarning("HealthAutoArrange updater download failed: " + _snapshot.Detail);
            _busy = false;
        }

        private bool VerifyDownloadedFile(string path, UpdateManifest manifest)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != manifest.Size || info.Length <= 0 || info.Length > MaxPackageBytes) return false;
                using (var stream = File.OpenRead(path))
                using (var sha = SHA256.Create())
                {
                    var digest = sha.ComputeHash(stream);
                    var actual = ToHex(digest);
                    return string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private void Fail(string detail, bool userInitiated)
        {
            _snapshot = new UpdateUiSnapshot(UpdateUiState.Failed, _currentVersion, string.Empty, detail, string.Empty);
            _busy = false;
            if (userInitiated) _log.LogWarning("HealthAutoArrange updater check failed: " + detail);
            else _log.LogInfo("HealthAutoArrange updater auto-check unavailable: " + detail);
        }

        private static bool TryParseAndVerifyManifest(string text, out UpdateManifest manifest, out string error)
        {
            manifest = null;
            error = string.Empty;
            if (string.IsNullOrEmpty(text) || text.Length > 32768) { error = "Manifest is empty or too large."; return false; }
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var index = line.IndexOf('=');
                if (index <= 0) { error = "Malformed manifest line."; return false; }
                var key = line.Substring(0, index).Trim();
                var value = line.Substring(index + 1).Trim();
                if (values.ContainsKey(key)) { error = "Duplicate manifest field: " + key; return false; }
                values[key] = value;
            }

            string schema, keyId, version, assetName, sha256, sizeText, officialUrl, mirrorUrl, notesUrl, publishedUtc, signature;
            if (!values.TryGetValue("schema", out schema) || schema != ManifestSchema.ToString(CultureInfo.InvariantCulture)
                || !values.TryGetValue("keyId", out keyId) || keyId != KeyId
                || !values.TryGetValue("version", out version)
                || !values.TryGetValue("assetName", out assetName)
                || !values.TryGetValue("sha256", out sha256)
                || !values.TryGetValue("size", out sizeText)
                || !values.TryGetValue("officialUrl", out officialUrl)
                || !values.TryGetValue("mirrorUrl", out mirrorUrl)
                || !values.TryGetValue("notesUrl", out notesUrl)
                || !values.TryGetValue("publishedUtc", out publishedUtc)
                || !values.TryGetValue("signature", out signature))
            {
                error = "Manifest is missing a required field or uses an unsupported schema/key.";
                return false;
            }

            Version parsedVersion;
            long size;
            DateTimeOffset published;
            if (!TryParseStableVersion(version, out parsedVersion)) { error = "Invalid stable version."; return false; }
            if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size <= 0 || size > MaxPackageBytes)
            { error = "Invalid package size."; return false; }
            if (!IsSafeAssetName(assetName)) { error = "Unsafe asset name."; return false; }
            if (!IsSha256(sha256)) { error = "Invalid SHA-256 digest."; return false; }
            if (!IsHttpsUrl(officialUrl) || !IsHttpsUrl(mirrorUrl) || !IsHttpsUrl(notesUrl)) { error = "Manifest URLs must use HTTPS."; return false; }
            if (!DateTimeOffset.TryParse(publishedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out published))
            { error = "Invalid publishedUtc."; return false; }

            var canonical = CanonicalManifest(schema, keyId, version, assetName, sha256.ToLowerInvariant(), sizeText,
                officialUrl, mirrorUrl, notesUrl, publishedUtc);
            byte[] signatureBytes;
            try { signatureBytes = Convert.FromBase64String(signature); }
            catch { error = "Invalid manifest signature encoding."; return false; }

            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.ImportParameters(new RSAParameters
                    {
                        Modulus = Convert.FromBase64String(PublicModulusBase64),
                        Exponent = Convert.FromBase64String(PublicExponentBase64)
                    });
                    var data = Encoding.UTF8.GetBytes(canonical);
                    using (var sha = SHA256.Create())
                    {
                        var hash = sha.ComputeHash(data);
                        if (!rsa.VerifyHash(hash, CryptoConfig.MapNameToOID("SHA256"), signatureBytes))
                        { error = "Manifest signature verification failed."; return false; }
                    }
                }
            }
            catch (Exception ex)
            {
                error = "Manifest signature verification error: " + ex.Message;
                return false;
            }

            manifest = new UpdateManifest
            {
                Version = version,
                AssetName = assetName,
                Sha256 = sha256.ToLowerInvariant(),
                Size = size,
                OfficialUrl = officialUrl,
                MirrorUrl = mirrorUrl,
                NotesUrl = notesUrl,
                PublishedUtc = publishedUtc,
                Signature = signature
            };
            return true;
        }

        private static string CanonicalManifest(string schema, string keyId, string version, string assetName, string sha256,
            string size, string officialUrl, string mirrorUrl, string notesUrl, string publishedUtc)
        {
            return "schema=" + schema + "\n"
                + "keyId=" + keyId + "\n"
                + "version=" + version + "\n"
                + "assetName=" + assetName + "\n"
                + "sha256=" + sha256 + "\n"
                + "size=" + size + "\n"
                + "officialUrl=" + officialUrl + "\n"
                + "mirrorUrl=" + mirrorUrl + "\n"
                + "notesUrl=" + notesUrl + "\n"
                + "publishedUtc=" + publishedUtc;
        }

        private static void AddUniqueHttps(List<string> urls, string value)
        {
            if (!IsHttpsUrl(value)) return;
            foreach (var existing in urls)
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)) return;
            urls.Add(value);
        }

        private static string DeriveTransportPackageUrl(string manifestUrl, string version, string assetName)
        {
            if (!IsHttpsUrl(manifestUrl) || !TryParseStableVersion(version, out _) || !IsSafeAssetName(assetName))
                return string.Empty;
            try
            {
                var uri = new Uri(manifestUrl, UriKind.Absolute);
                var path = uri.AbsolutePath;
                var slash = path.LastIndexOf('/');
                if (slash < 0) return string.Empty;
                var basePath = path.Substring(0, slash + 1);
                var packagePath = basePath + "packages/" + Uri.EscapeDataString(version.TrimStart('v', 'V')) + "/" + Uri.EscapeDataString(assetName);
                var builder = new UriBuilder(uri)
                {
                    Path = packagePath,
                    Query = string.Empty,
                    Fragment = string.Empty
                };
                return IsHttpsUrl(builder.Uri.AbsoluteUri) ? builder.Uri.AbsoluteUri : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int CompareVersions(string left, string right)
        {
            Version a, b;
            if (!TryParseStableVersion(left, out a)) return -1;
            if (!TryParseStableVersion(right, out b)) return 1;
            return a.CompareTo(b);
        }

        private static bool TryParseStableVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var value = text.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            var parts = value.Split('.');
            if (parts.Length != 3) return false;
            int major, minor, patch;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor)
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch)
                || major < 0 || minor < 0 || patch < 0) return false;
            version = new Version(major, minor, patch);
            return true;
        }

        private static bool IsSafeAssetName(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.StartsWith("HealthAutoArrange-", StringComparison.Ordinal)
                && value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && value.IndexOf('/') < 0 && value.IndexOf('\\') < 0
                && value.IndexOf("..", StringComparison.Ordinal) < 0
                && value.Length <= 128;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        private static bool IsHttpsUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(uri.Host);
        }

        private static string NormalizeUrl(string value) => (value ?? string.Empty).Trim();

        private static bool RequestSucceeded(UnityWebRequest request)
        {
            return request != null && string.IsNullOrEmpty(request.error)
                && request.responseCode >= 200 && request.responseCode < 400
                && IsHttpsUrl(request.url);
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
