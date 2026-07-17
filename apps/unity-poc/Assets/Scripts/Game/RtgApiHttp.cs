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

        public static string JoinUrl(string baseUrl, string relativePath)
        {
            string b = (baseUrl ?? "").TrimEnd('/');
            string p = (relativePath ?? "").TrimStart('/');
            return $"{b}/{p}";
        }
    }
}
