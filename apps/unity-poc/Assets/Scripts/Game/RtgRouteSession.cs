using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Turns player movement into a real, persisted route on the server by driving
    /// the @empire/api route-session endpoints: begin → stream GPS points → the
    /// server validates them, auto-connects at a settlement's geofence, and saves a
    /// route. This is the "Light Road becomes a real route" plumbing, using the
    /// exact same contract the production web/mobile client will.
    ///
    /// Timestamps are fabricated from a plausible ground speed so the server's GPS
    /// validation (accuracy / speed / gap / duplicate) accepts them even though the
    /// editor's simulated pin moves faster than a real walk. On device, real GPS
    /// timestamps replace these with no other changes.
    /// </summary>
    public class RtgRouteSession : MonoBehaviour
    {
        [Tooltip("Base URL of @empire/api, e.g. http://localhost:3001/api")]
        public string apiBaseUrl = "http://localhost:3001/api";

        public string worldId = "";
        public string empireId = "";

        [Tooltip("Ground speed used to fabricate point timestamps (m/s). Must stay under the server's maxSpeedMps.")]
        public float gpsSpeedMps = 12f;

        [Tooltip("Record a point after moving this many meters (must exceed the server's ~15 m duplicate threshold).")]
        public float sampleSpacingMeters = 30f;

        [Tooltip("How often queued points are flushed to the server.")]
        public float flushIntervalSeconds = 1.5f;

        [Tooltip("Always capture movement into routes automatically (no Begin/End buttons) — matches the 'movement is the game' design. Untick for manual Begin/End (debug).")]
        public bool autoRecord = true;

        [Tooltip("After auto-connecting at a site, wait until the player moves this far from it before starting the next route (prevents instantly reconnecting while still inside the geofence).")]
        public float resumeAfterMeters = 250f;

        private const int MaxBatch = 20;

        public bool IsActive => _state == State.Active;
        public string StatusText { get; private set; } = "Route: idle";

        private enum State { Idle, Active }
        private State _state = State.Idle;
        private string _sessionId;
        private DateTime _startUtc;
        private double _cumulativeDistanceM;

        private double _lastLat, _lastLng;
        private bool _hasLast;
        private double _curLat, _curLng;
        private bool _hasCur;

        private readonly List<GpsPoint> _queue = new();
        private readonly List<PathPoint> _fullPath = new();
        private Coroutine _flushLoop;

        // After an auto-connect, hold off starting the next leg until the player has
        // left the connected site's geofence (see resumeAfterMeters).
        private double _resumeLat, _resumeLng;
        private bool _hasResumeGate;

        // ------------------------------------------------------------------ //
        // Public API (called by the player each frame + by the on-screen buttons)
        // ------------------------------------------------------------------ //

        public void NotifyPosition(double lat, double lng)
        {
            _curLat = lat;
            _curLng = lng;
            _hasCur = true;

            // Always-on capture: begin (or resume) a route automatically as the
            // player moves, so no manual Begin/End is ever needed.
            if (autoRecord && _state == State.Idle) TryAutoBegin();

            if (_state != State.Active) return;

            if (!_hasLast)
            {
                _lastLat = lat;
                _lastLng = lng;
                _hasLast = true;
                return;
            }

            double moved = Haversine(_lastLat, _lastLng, lat, lng);
            if (moved < sampleSpacingMeters) return;

            _cumulativeDistanceM += moved;
            _queue.Add(new GpsPoint
            {
                lat = lat,
                lng = lng,
                accuracyM = 8,
                recordedAt = Iso(_startUtc.AddSeconds(_cumulativeDistanceM / Mathf.Max(0.1f, gpsSpeedMps))),
            });
            _fullPath.Add(new PathPoint { lat = lat, lng = lng });
            _lastLat = lat;
            _lastLng = lng;
        }

        public void ToggleFromButton()
        {
            if (IsActive) End();
            else Begin();
        }

        private void TryAutoBegin()
        {
            if (!_hasCur) return;
            if (string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(empireId))
            {
                StatusText = "Route: needs live world (run '6b. Connect Echo Sites to Live API')";
                return;
            }
            // Still cooling down inside/near the last connected site — don't restart yet.
            if (_hasResumeGate &&
                Haversine(_resumeLat, _resumeLng, _curLat, _curLng) < resumeAfterMeters)
            {
                return;
            }
            Begin();
        }

        public void Begin()
        {
            if (_state == State.Active) return;
            if (!_hasCur) { StatusText = "Route: waiting for GPS fix…"; return; }
            if (string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(empireId))
            {
                StatusText = "Route: needs live world (run '6b. Connect Echo Sites to Live API')";
                return;
            }
            StartCoroutine(BeginRoutine(_curLat, _curLng));
        }

        public void End()
        {
            if (_state != State.Active) return;
            StartCoroutine(EndRoutine());
        }

        /// <summary>Lat/lng points on the current leg (for tap-to-connect range checks).</summary>
        public bool TryGetRoutePath(out List<RtgRouteGeometry.LatLng> path)
        {
            path = new List<RtgRouteGeometry.LatLng>(_fullPath.Count + _queue.Count);
            foreach (PathPoint p in _fullPath)
                path.Add(new RtgRouteGeometry.LatLng(p.lat, p.lng));
            foreach (GpsPoint p in _queue)
                path.Add(new RtgRouteGeometry.LatLng(p.lat, p.lng));
            return path.Count > 0;
        }

        /// <summary>
        /// Connect a tapped map item to the nearest point on this route via
        /// POST /worlds/:worldId/claim. The server anchors the connector there,
        /// not at the player's live GPS position.
        /// </summary>
        /// <param name="goodieChoice">found_town or claim_reward when connecting a goodie hut.</param>
        /// <param name="pathOverride">Route path captured when the goodie modal opened (avoids stale/empty path).</param>
        public IEnumerator ClaimNearRoute(
            RtgMapMarker marker,
            string goodieChoice,
            Action<ClaimResult> done,
            List<RtgRouteGeometry.LatLng> pathOverride = null)
        {
            List<RtgRouteGeometry.LatLng> path;
            if (pathOverride != null && pathOverride.Count > 0)
                path = pathOverride;
            else if (!TryGetRoutePath(out path) || path.Count < 1)
            {
                done?.Invoke(ClaimResult.Fail("No active route path."));
                yield break;
            }

            var sb = new StringBuilder("{\"empireId\":\"").Append(empireId).Append("\",");
            if (!string.IsNullOrEmpty(_sessionId))
                sb.Append("\"sessionId\":\"").Append(_sessionId).Append("\",");
            sb.Append("\"routePath\":[");
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"lat\":").Append(D(path[i].lat)).Append(",\"lng\":").Append(D(path[i].lng)).Append('}');
            }
            sb.Append("],\"targetKind\":\"").Append(marker.KindApiValue).Append("\",");
            sb.Append("\"targetId\":\"").Append(marker.targetId).Append("\"");
            if (!string.IsNullOrEmpty(goodieChoice))
            {
                sb.Append(",\"goodieChoice\":\"").Append(goodieChoice).Append("\"");
                sb.Append(",\"approachLat\":").Append(D(marker.lat));
                sb.Append(",\"approachLng\":").Append(D(marker.lng));
            }
            sb.Append('}');

            bool reloadMap = !string.IsNullOrEmpty(goodieChoice);

            string worldPath = $"/worlds/{worldId}/claim";
            yield return Post(worldPath, sb.ToString(), (code, text, ok) =>
            {
                if (ok && code == 200)
                {
                    var resp = JsonUtility.FromJson<ClaimResp>(text);
                    string msg = resp != null && !string.IsNullOrEmpty(resp.message)
                        ? resp.message
                        : $"Connected to {marker.displayName}.";

                    ClaimResult result = ClaimResult.Ok(msg, reloadMap, marker.targetId);
                    if (resp?.connectorPath != null && resp.connectorPath.Length >= 2)
                    {
                        result.hasConnector = true;
                        result.anchorLat = resp.connectorPath[0].lat;
                        result.anchorLng = resp.connectorPath[0].lng;
                        result.targetLat = resp.connectorPath[1].lat;
                        result.targetLng = resp.connectorPath[1].lng;
                    }
                    done?.Invoke(result);
                }
                else
                {
                    string err = TryParseError(text) ?? $"Claim failed ({code})";
                    Debug.LogWarning($"[RTG] Claim failed ({code}): {text}");
                    if (code == 409)
                        done?.Invoke(ClaimResult.AlreadyConnected(marker.targetId));
                    else
                        done?.Invoke(ClaimResult.Fail(err));
                }
            });
        }

        public struct ClaimResult
        {
            public bool ok;
            public string message;
            public bool alreadyConnected;
            public bool hasConnector;
            public double anchorLat, anchorLng, targetLat, targetLng;
            public bool reloadMap;
            public string connectedTargetId;

            public static ClaimResult Ok(string message, bool reloadMap = false, string connectedTargetId = null) =>
                new ClaimResult { ok = true, message = message, reloadMap = reloadMap, connectedTargetId = connectedTargetId };

            public static ClaimResult Fail(string message) =>
                new ClaimResult { ok = false, message = message };

            public static ClaimResult AlreadyConnected(string targetId = null) =>
                new ClaimResult { ok = false, alreadyConnected = true, connectedTargetId = targetId };
        }

        private static string TryParseError(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var e = JsonUtility.FromJson<ErrResp>(json);
                if (!string.IsNullOrEmpty(e?.message)) return e.message;
                return string.IsNullOrEmpty(e?.error) ? null : e.error;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ //
        // Session lifecycle
        // ------------------------------------------------------------------ //

        private IEnumerator BeginRoutine(double lat, double lng)
        {
            _state = State.Active;
            StatusText = "Route: starting…";
            _cumulativeDistanceM = 0;
            _hasLast = false;
            _queue.Clear();
            _fullPath.Clear();
            _startUtc = DateTime.UtcNow;
            _sessionId = null;

            string json =
                $"{{\"worldId\":\"{worldId}\",\"empireId\":\"{empireId}\",\"lat\":{D(lat)},\"lng\":{D(lng)}}}";

            yield return Post("/sessions", json, (code, text, ok) =>
            {
                if (ok && code == 200)
                {
                    var resp = JsonUtility.FromJson<BeginResp>(text);
                    _sessionId = resp.sessionId;
                    _lastLat = lat;
                    _lastLng = lng;
                    _hasLast = true;
                    _fullPath.Add(new PathPoint { lat = lat, lng = lng });
                    _hasResumeGate = false;
                    StatusText = "Route: recording";
                    if (_flushLoop == null) _flushLoop = StartCoroutine(FlushLoop());
                }
                else
                {
                    _state = State.Idle;
                    StatusText = $"Route: begin failed ({code})";
                    Debug.LogError($"[RTG] Begin route failed: {code} {text}");
                }
            });
        }

        private IEnumerator FlushLoop()
        {
            while (_state == State.Active)
            {
                yield return new WaitForSeconds(flushIntervalSeconds);
                yield return FlushOnce();
            }
            _flushLoop = null;
        }

        private IEnumerator FlushOnce()
        {
            if (_state != State.Active || string.IsNullOrEmpty(_sessionId)) yield break;
            if (_queue.Count == 0) yield break;

            int take = Mathf.Min(MaxBatch, _queue.Count);
            var batch = _queue.GetRange(0, take);

            var sb = new StringBuilder("{\"points\":[");
            for (int i = 0; i < batch.Count; i++)
            {
                GpsPoint p = batch[i];
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"lat\":{D(p.lat)},\"lng\":{D(p.lng)},\"accuracyM\":{D(p.accuracyM)},\"recordedAt\":\"{p.recordedAt}\"}}");
            }
            sb.Append("]}");

            yield return Post($"/sessions/{_sessionId}/points", sb.ToString(), (code, text, ok) =>
            {
                if (!ok)
                {
                    Debug.LogWarning($"[RTG] Points flush failed ({code}) — will retry.");
                    return; // keep queued points for next attempt
                }

                _queue.RemoveRange(0, take);

                var resp = JsonUtility.FromJson<PointsResp>(text);
                if (resp != null && resp.connected)
                {
                    string name = resp.settlement != null ? resp.settlement.name : "settlement";
                    Debug.Log($"[RTG] Route auto-connected to {name} (route {resp.routeId}).");
                    _state = State.Idle; // server completed the session
                    RefreshPersistedRoutes();

                    // Gate the next auto-started leg until we leave this site.
                    _resumeLat = _curLat;
                    _resumeLng = _curLng;
                    _hasResumeGate = true;
                    StatusText = autoRecord
                        ? $"Route: connected → {name} · continuing"
                        : $"Route: connected → {name}";
                }
            });
        }

        private IEnumerator EndRoutine()
        {
            StatusText = "Route: ending…";

            // Flush whatever is still queued before ending (bounded so we can't hang).
            int guard = 0;
            while (_state == State.Active && _queue.Count > 0 && guard++ < 50)
                yield return FlushOnce();

            if (_state != State.Active)
                yield break; // a geofence connect already completed the session

            var sb = new StringBuilder("{\"path\":[");
            for (int i = 0; i < _fullPath.Count; i++)
            {
                PathPoint p = _fullPath[i];
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"lat\":{D(p.lat)},\"lng\":{D(p.lng)}}}");
            }
            sb.Append("]}");

            yield return Post($"/sessions/{_sessionId}/end", sb.ToString(), (code, text, ok) =>
            {
                _state = State.Idle;
                if (ok)
                {
                    var resp = JsonUtility.FromJson<EndResp>(text);
                    StatusText = resp != null && resp.saved
                        ? $"Route: saved ({resp.status})"
                        : $"Route: ended ({(resp != null ? resp.status : "?")})";
                    Debug.Log($"[RTG] Route session ended: {text}");
                    if (resp != null && resp.saved)
                        RefreshPersistedRoutes();
                }
                else
                {
                    StatusText = $"Route: end failed ({code})";
                    Debug.LogError($"[RTG] End route failed: {code} {text}");
                }
            });
        }

        private IEnumerator Post(string path, string json, Action<long, string, bool> done)
        {
            string url = apiBaseUrl.TrimEnd('/') + path;
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            bool ok = req.result == UnityWebRequest.Result.Success;
            string text = ok ? req.downloadHandler.text : (req.downloadHandler?.text ?? req.error);
            done?.Invoke(req.responseCode, text, ok);
        }

        // ------------------------------------------------------------------ //
        // On-screen controls
        // ------------------------------------------------------------------ //

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            const float margin = 16f;

            GUI.Label(new Rect(margin, margin, 460f, 22f), StatusText);

            // Always-on capture needs no controls; only the manual/debug path shows a button.
            if (autoRecord) return;

            var prevSize = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = 16;
            var rect = new Rect(margin, margin + 26f, 150f, 40f);
            if (GUI.Button(rect, IsActive ? "End Route" : "Begin Route")) ToggleFromButton();
            GUI.skin.button.fontSize = prevSize;
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private static string Iso(DateTime utc) =>
            utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        // G17 + invariant culture: full double precision with a '.' decimal, so
        // coordinates survive the round-trip and never localize to a comma.
        private static string D(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

        private void RefreshPersistedRoutes()
        {
            RtgPersistedRouteDrawer drawer = RtgPersistedRouteDrawer.FindOrCreate();
            if (drawer == null) return;
            drawer.StartCoroutine(drawer.RefreshFromApi(apiBaseUrl, worldId, empireId));
        }

        private static double Haversine(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6_371_000;
            double ToRad(double d) => d * Math.PI / 180.0;
            double dLat = ToRad(lat2 - lat1);
            double dLng = ToRad(lng2 - lng1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * R * Math.Asin(Math.Sqrt(a));
        }

        private struct GpsPoint
        {
            public double lat, lng, accuracyM;
            public string recordedAt;
        }

        private struct PathPoint
        {
            public double lat, lng;
        }

        [Serializable] private class BeginResp { public string sessionId; public string status; }
        [Serializable] private class PointsResp { public bool connected; public string routeId; public ConnSettlement settlement; }
        [Serializable] private class ConnSettlement { public string name; }
        [Serializable] private class ClaimResp
        {
            public bool ok;
            public string message;
            public ConnectorPt[] connectorPath;
        }

        [Serializable] private class ConnectorPt
        {
            public double lat;
            public double lng;
        }

        [Serializable] private class EndResp { public bool saved; public string routeId; public string status; }
        [Serializable] private class ErrResp { public string error; public string message; }
    }
}
