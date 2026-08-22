using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using HealthAutoArrange.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace HealthAutoArrange.Plugin
{
    /// <summary>
    /// Launch-only, notification-only checker for a GitHub-hosted manifest.
    ///
    /// Security model (v1.1.8+):
    ///   - HTTPS-only manifest fetch (mandatory; UnityWebRequest enforces it).
    ///   - Manifest URL must resolve to github.com or raw.githubusercontent.com
    ///     (checked both before the request and after redirects).
    ///   - No RSA signature verification. The previous RSA-2048 trust anchor was
    ///     removed because:
    ///       1. The updater is notification-only since v1.1.7: it never downloads,
    ///          stages, or replaces DLLs. The worst case is "user is told a new
    ///          version is available and clicks a link to GitHub".
    ///       2. The private key was previously exposed in chat history, so the
    ///          RSA layer was providing zero actual security.
    ///       3. HTTPS + GitHub host whitelist + user-driven manual download from
    ///          the GitHub Release page covers the relevant threat surface for a
    ///          notification-only updater.
    ///   - Release URLs in the manifest must also be on github.com.
    /// </summary>
    internal sealed class SafeUpdater
    {
        private const int ManifestSchema = 2;
        private const ulong MaxManifestBytes = 32UL * 1024UL;
        private const int ManifestTimeoutSeconds = 8;

        private readonly ManualLogSource _log;
        private readonly string _currentVersion;
        private readonly Func<string> _githubManifestUrl;
        private readonly Action<string> _onUpdateAvailable;
        private UpdateManifest _candidate;
        private bool _started;
        private UpdateUiSnapshot _snapshot;

        private sealed class UpdateManifest
        {
            public string Version;
            public string NotesUrl;
            public string PublishedUtc;
        }

        public SafeUpdater(ManualLogSource log, string currentVersion,
            Func<string> githubManifestUrl, Action<string> onUpdateAvailable)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _currentVersion = currentVersion ?? "0.0.0";
            _githubManifestUrl = githubManifestUrl ?? throw new ArgumentNullException(nameof(githubManifestUrl));
            _onUpdateAvailable = onUpdateAvailable;
            _snapshot = new UpdateUiSnapshot(UpdateUiState.Idle, _currentVersion, string.Empty, string.Empty, string.Empty);
        }

        public UpdateUiSnapshot Snapshot => _snapshot;

        public IEnumerator AutoCheckAfterDelay(float seconds)
        {
            // One updater instance = one launch check. Do not allow accidental duplicate
            // calls from UI reloads or future lifecycle hooks.
            if (_started) yield break;
            _started = true;
            if (seconds > 0f) yield return new WaitForSecondsRealtime(seconds);
            yield return CheckRoutine();
        }

        public void OpenReleasePage()
        {
            var url = _candidate?.NotesUrl;
            if (!IsGitHubHttpsUrl(url)) return;
            try { Application.OpenURL(url); }
            catch (Exception ex) { _log.LogWarning("HealthAutoArrange update checker: could not open release page: " + ex.Message); }
        }

        private IEnumerator CheckRoutine()
        {
            _snapshot = new UpdateUiSnapshot(UpdateUiState.Checking, _currentVersion, string.Empty, string.Empty, string.Empty);
            _candidate = null;
            var source = NormalizeUrl(_githubManifestUrl());
            if (!IsGitHubHttpsUrl(source)) { Fail("The GitHub update manifest URL is invalid."); yield break; }

            UnityWebRequest request = null;
            try
            {
                try
                {
                    request = UnityWebRequest.Get(source);
                    request.timeout = ManifestTimeoutSeconds;
                    request.redirectLimit = 4;
                    request.SendWebRequest();
                }
                catch (Exception ex) { Fail(ex.Message); yield break; }

                while (!request.isDone)
                {
                    if (request.downloadedBytes > MaxManifestBytes)
                    {
                        request.Abort();
                        Fail("Update manifest exceeded the 32 KiB safety limit.");
                        yield break;
                    }
                    yield return null;
                }
                if (request.downloadedBytes > MaxManifestBytes) { Fail("Update manifest exceeded the 32 KiB safety limit."); yield break; }
                if (!RequestSucceeded(request))
                {
                    Fail(string.IsNullOrEmpty(request.error) ? "HTTP " + request.responseCode.ToString(CultureInfo.InvariantCulture) : request.error);
                    yield break;
                }

                UpdateManifest manifest;
                string parseError = string.Empty;
                if (request.downloadHandler == null || !TryParseManifest(request.downloadHandler.text, out manifest, out parseError))
                { Fail(string.IsNullOrEmpty(parseError) ? "GitHub returned an empty update manifest." : parseError); yield break; }

                _candidate = manifest;
                if (CompareVersions(manifest.Version, _currentVersion) > 0)
                {
                    _snapshot = new UpdateUiSnapshot(UpdateUiState.Available, _currentVersion, manifest.Version, string.Empty, string.Empty);
                    _log.LogInfo("HealthAutoArrange update checker: GitHub release v" + manifest.Version + " is available.");
                    try { _onUpdateAvailable?.Invoke(manifest.Version); }
                    catch (Exception ex) { _log.LogWarning("HealthAutoArrange update reminder failed: " + ex.Message); }
                }
                else _snapshot = new UpdateUiSnapshot(UpdateUiState.UpToDate, _currentVersion, manifest.Version, string.Empty, string.Empty);
            }
            finally { request?.Dispose(); }
        }

        private void Fail(string detail)
        {
            _snapshot = new UpdateUiSnapshot(UpdateUiState.Failed, _currentVersion, string.Empty, detail, string.Empty);
            _log.LogInfo("HealthAutoArrange launch update check unavailable: " + detail);
        }

        /// <summary>
        /// Parse a v2 manifest (no signature field). Required fields:
        ///   schema, version, notesUrl, publishedUtc
        /// Optional but ignored if present (for backward-read compatibility with v1 manifests):
        ///   assetName, sha256, size, officialUrl, mirrorUrl, keyId, signature
        /// </summary>
        private static bool TryParseManifest(string text, out UpdateManifest manifest, out string error)
        {
            manifest = null; error = string.Empty;
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

            string schema, version, notesUrl, publishedUtc;
            if (!values.TryGetValue("schema", out schema) || schema != ManifestSchema.ToString(CultureInfo.InvariantCulture))
            { error = "Manifest schema is missing or unsupported (expected schema=2)."; return false; }
            if (!values.TryGetValue("version", out version)) { error = "Manifest is missing required field: version."; return false; }
            if (!values.TryGetValue("notesUrl", out notesUrl)) { error = "Manifest is missing required field: notesUrl."; return false; }
            if (!values.TryGetValue("publishedUtc", out publishedUtc)) { error = "Manifest is missing required field: publishedUtc."; return false; }

            Version parsedVersion; DateTimeOffset published;
            if (!TryParseStableVersion(version, out parsedVersion)) { error = "Invalid stable version."; return false; }
            if (!IsGitHubHttpsUrl(notesUrl)) { error = "Release notes URL must use GitHub HTTPS."; return false; }
            if (!DateTimeOffset.TryParse(publishedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out published)) { error = "Invalid publishedUtc."; return false; }

            manifest = new UpdateManifest { Version = version, NotesUrl = notesUrl, PublishedUtc = publishedUtc };
            return true;
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
            var parts = value.Split('.'); int major, minor, patch;
            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor)
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
            version = new Version(major, minor, patch); return true;
        }

        private static bool IsGitHubHttpsUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUrl(string value) => (value ?? string.Empty).Trim();
        private static bool RequestSucceeded(UnityWebRequest request)
        {
            return request != null && string.IsNullOrEmpty(request.error) && request.responseCode >= 200
                && request.responseCode < 400 && IsGitHubHttpsUrl(request.url);
        }
    }
}
