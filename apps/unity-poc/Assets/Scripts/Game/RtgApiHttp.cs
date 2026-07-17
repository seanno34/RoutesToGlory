using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Shared UnityWebRequest helpers for POC API calls (saved worlds, by-code join, create world).
    /// Mirrors the timeout / error pattern used by <see cref="RtgEchoSiteLoader"/>.
    /// </summary>
    public static class RtgApiHttp
    {
        public const int DefaultTimeoutSeconds = 8;
        public const int CreateWorldTimeoutSeconds = 60;
        public const string DefaultApiBaseUrl = "http://localhost:3001/api";

        /// <summary>Trim + uppercase access codes to match web <c>getWorldByCode</c>.</summary>
        public static string NormalizeAccessCode(string code) =>
            string.IsNullOrWhiteSpace(code) ? "" : code.Trim().ToUpperInvariant();

        /// <summary>Normalize to 4-digit PIN (<c>0000</c>–<c>9999</c>) or empty if not exactly 4 digits.</summary>
        public static string NormalizeUserPin(string pin)
        {
            if (string.IsNullOrWhiteSpace(pin)) return "";
            var sb = new StringBuilder(4);
            foreach (char c in pin)
            {
                if (c >= '0' && c <= '9')
                    sb.Append(c);
            }
            return sb.Length == 4 ? sb.ToString() : "";
        }

        public static string JoinUrl(string baseUrl, string relativePath)
        {
            string b = (baseUrl ?? "").TrimEnd('/');
            string p = (relativePath ?? "").TrimStart('/');
            return $"{b}/{p}";
        }

        /// <summary>
        /// Editor localhost fallback base (same idea as <see cref="RtgEchoSiteLoader"/>):
        /// rewrite host to 127.0.0.1 so LAN-IP configs still work when the API is only
        /// reachable on loopback. If the URL already uses 127.0.0.1, try <c>localhost</c>.
        /// </summary>
        public static string EditorLocalhostRetryBase(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri))
                return "http://127.0.0.1:3001/api";

            int port = uri.Port > 0 ? uri.Port : 3001;
            string path = uri.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(path))
                path = "/api";

            string host = uri.Host ?? "";
            if (string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
                return $"http://localhost:{port}{path}";

            return $"http://127.0.0.1:{port}{path}";
        }

        /// <summary>True when UnityWebRequest failed before getting an HTTP status (code 0 / connect errors).</summary>
        public static bool IsUnreachableError(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;
            if (error.StartsWith("0 ", StringComparison.Ordinal) || error == "0")
                return true;

            return error.IndexOf("Cannot connect", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("Could not connect", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("Unable to connect", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("Connection refused", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("No route to host", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("Network is unreachable", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Human-readable join/list/create failure when the host cannot be reached.
        /// </summary>
        public static string FormatUnreachableHint(string apiBaseUrl, string rawError = null)
        {
            string baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? DefaultApiBaseUrl : apiBaseUrl.TrimEnd('/');
            string detail = string.IsNullOrWhiteSpace(rawError) ? "Cannot connect to destination host" : rawError.Trim();
            bool looksLocal =
                baseUrl.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0
                || baseUrl.IndexOf("127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0;

            string deviceHint = looksLocal
                ? "On a phone/tablet use your Mac LAN IP (e.g. http://192.168.x.x:3001/api), not localhost."
                : "Confirm the Mac and device share Wi‑Fi and the API listens on 0.0.0.0 (pnpm dev / pnpm dev:field).";

            return
                $"API unreachable at {baseUrl} ({detail}). " +
                "Start the API (pnpm dev or pnpm dev:field). " +
                deviceHint +
                " Editor: localhost/127.0.0.1 is fine; set API base in the join panel or rtg-dev-world.json.";
        }

        /// <summary>
        /// GET <paramref name="url"/>; invokes <paramref name="done"/> with body text on
        /// success, or (null, errorMessage) on failure.
        /// </summary>
        public static IEnumerator Get(string url, Action<string, string> done, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                done?.Invoke(null, "URL is empty");
                yield break;
            }

            using UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = Mathf.Max(1, timeoutSeconds);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = string.IsNullOrEmpty(req.error)
                    ? $"HTTP {req.responseCode}"
                    : $"{req.responseCode} {req.error}";
                Debug.LogWarning($"[RTG] API GET failed: {err} ({url})");
                // Pass body when present so callers can surface API { error: "..." } (e.g. 404).
                string body = req.downloadHandler?.text;
                done?.Invoke(string.IsNullOrEmpty(body) ? null : body, err);
                yield break;
            }

            done?.Invoke(req.downloadHandler?.text, null);
        }

        /// <summary>
        /// GET relative to <paramref name="apiBaseUrl"/>; in the Editor, on unreachable
        /// failure, retry via localhost↔127.0.0.1 (same pattern as map fetch).
        /// <paramref name="done"/> receives (body, error, workingApiBaseUrl).
        /// </summary>
        public static IEnumerator GetWithEditorLocalhostRetry(
            string apiBaseUrl,
            string relativePath,
            Action<string, string, string> done,
            bool editorLocalhostRetry = true,
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            string primaryBase = string.IsNullOrWhiteSpace(apiBaseUrl)
                ? DefaultApiBaseUrl
                : apiBaseUrl.TrimEnd('/');
            string primaryUrl = JoinUrl(primaryBase, relativePath);
            string body = null;
            string error = null;
            yield return Get(primaryUrl, (b, e) =>
            {
                body = b;
                error = e;
            }, timeoutSeconds);

            if (string.IsNullOrEmpty(error))
            {
                done?.Invoke(body, null, primaryBase);
                yield break;
            }

            if (!(Application.isEditor && editorLocalhostRetry && IsUnreachableError(error)))
            {
                done?.Invoke(body, error, primaryBase);
                yield break;
            }

            string retryBase = EditorLocalhostRetryBase(primaryBase).TrimEnd('/');
            if (string.Equals(retryBase, primaryBase, StringComparison.OrdinalIgnoreCase))
            {
                done?.Invoke(body, error, primaryBase);
                yield break;
            }

            string retryUrl = JoinUrl(retryBase, relativePath);
            Debug.LogWarning(
                $"[RTG] API GET failed ({primaryUrl}). Retrying via editor localhost: {retryUrl}");
            string retryBody = null;
            string retryError = null;
            yield return Get(retryUrl, (b, e) =>
            {
                retryBody = b;
                retryError = e;
            }, timeoutSeconds);

            if (string.IsNullOrEmpty(retryError))
            {
                done?.Invoke(retryBody, null, retryBase);
                yield break;
            }

            done?.Invoke(retryBody ?? body, retryError ?? error, primaryBase);
        }

        /// <summary>
        /// POST JSON <paramref name="jsonBody"/> to <paramref name="url"/>.
        /// </summary>
        public static IEnumerator PostJson(
            string url,
            string jsonBody,
            Action<string, string> done,
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                done?.Invoke(null, "URL is empty");
                yield break;
            }

            byte[] raw = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            using UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Mathf.Max(1, timeoutSeconds);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string err = string.IsNullOrEmpty(req.error)
                    ? $"HTTP {req.responseCode}"
                    : $"{req.responseCode} {req.error}";
                Debug.LogWarning($"[RTG] API POST failed: {err} ({url})");
                string body = req.downloadHandler?.text;
                done?.Invoke(string.IsNullOrEmpty(body) ? null : body, err);
                yield break;
            }

            done?.Invoke(req.downloadHandler?.text, null);
        }

        /// <summary>
        /// POST JSON relative to <paramref name="apiBaseUrl"/> with Editor localhost retry.
        /// </summary>
        public static IEnumerator PostJsonWithEditorLocalhostRetry(
            string apiBaseUrl,
            string relativePath,
            string jsonBody,
            Action<string, string, string> done,
            bool editorLocalhostRetry = true,
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            string primaryBase = string.IsNullOrWhiteSpace(apiBaseUrl)
                ? DefaultApiBaseUrl
                : apiBaseUrl.TrimEnd('/');
            string primaryUrl = JoinUrl(primaryBase, relativePath);
            string body = null;
            string error = null;
            yield return PostJson(primaryUrl, jsonBody, (b, e) =>
            {
                body = b;
                error = e;
            }, timeoutSeconds);

            if (string.IsNullOrEmpty(error))
            {
                done?.Invoke(body, null, primaryBase);
                yield break;
            }

            if (!(Application.isEditor && editorLocalhostRetry && IsUnreachableError(error)))
            {
                done?.Invoke(body, error, primaryBase);
                yield break;
            }

            string retryBase = EditorLocalhostRetryBase(primaryBase).TrimEnd('/');
            if (string.Equals(retryBase, primaryBase, StringComparison.OrdinalIgnoreCase))
            {
                done?.Invoke(body, error, primaryBase);
                yield break;
            }

            string retryUrl = JoinUrl(retryBase, relativePath);
            Debug.LogWarning(
                $"[RTG] API POST failed ({primaryUrl}). Retrying via editor localhost: {retryUrl}");
            string retryBody = null;
            string retryError = null;
            yield return PostJson(retryUrl, jsonBody, (b, e) =>
            {
                retryBody = b;
                retryError = e;
            }, timeoutSeconds);

            if (string.IsNullOrEmpty(retryError))
            {
                done?.Invoke(retryBody, null, retryBase);
                yield break;
            }

            done?.Invoke(retryBody ?? body, retryError ?? error, primaryBase);
        }
    }
}
