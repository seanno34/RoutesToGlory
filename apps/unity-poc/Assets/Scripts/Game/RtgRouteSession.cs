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
    /// Streams movement to the API as route sessions. Every leg with ≥2 GPS samples
    /// is persisted as a travel route — node connection (Echo Sites, resources, etc.)
    /// is tracked separately for future bonuses, not required to save the path.
    /// </summary>
    public class RtgRouteSession : MonoBehaviour
    {
        [Tooltip("Base URL of @empire/api. Device default: production HTTPS. Editor override: http://localhost:3001/api")]
        public string apiBaseUrl = RtgApiHttp.PublicApiBaseUrl;

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

        [Tooltip("Auto-save the current leg and start a fresh session after this many meters of travel (keeps long drives durable even without closing the app). 0 = only save on shutdown or node connect.")]
        public float legCheckpointMeters = 1500f;

        [Tooltip("During Auto Pilot home testing: auto-save legs every N meters. 0 = only save when the app backgrounds (keeps repeated test loops from fragmenting the DB).")]
        public float autopilotCheckpointMeters = 0f;

        [Tooltip("Do not persist legs shorter than this (filters mode-switch spurs and bad samples).")]
        public float minLegLengthMeters = 120f;

        [Header("Route snap & cleanup")]
        [Tooltip("When near an existing owned route, snap movement and new points onto that corridor.")]
        public bool snapToRoutes = true;

        [Tooltip("Snap when within this many meters of an existing route line. Keep tight (~25 m) so parallel roads are not pulled together.")]
        public float snapProximityMeters = 25f;

        [Tooltip("How quickly the glider eases onto a nearby route corridor (higher = snappier).")]
        public float snapBlendSmoothing = 14f;

        [Tooltip("Douglas-Peucker tolerance when saving a route leg (meters).")]
        public float simplifyToleranceMeters = 12f;

        [Tooltip("Run one-time server cleanup for messy saved routes after the map loads.")]
        public bool cleanupRoutesOnMapLoad = false;

        [Tooltip("In the Editor, retry POST via 127.0.0.1 when the configured LAN IP fails.")]
        public bool editorLocalhostRetry = true;

        private const int MaxBatch = 20;

        public bool IsActive => _state == State.Active;
        public string StatusText { get; private set; } = "Route: idle";

        private enum State { Idle, Active }
        private State _state = State.Idle;
        private string _sessionId;
        private DateTime _startUtc;
        private double _cumulativeDistanceM;

        private double _lastLat, _lastLng;
        private double _lastRawLat, _lastRawLng;
        private bool _hasLast;
        private bool _hasLastRaw;
        private double _curLat, _curLng;
        private bool _hasCur;

        private readonly List<GpsPoint> _queue = new();
        private readonly List<PathPoint> _fullPath = new();
        private Coroutine _flushLoop;
        private bool _flushInFlight;
        private int _flushEpoch;

        // After an auto-connect, hold off starting the next leg until the player has
        // left the connected site's geofence (see resumeAfterMeters).
        private double _resumeLat, _resumeLng;
        private bool _hasResumeGate;
        private bool _checkpointSaving;
        private bool _shutdownSaveStarted;
        private bool _routeCleanupRequested;
        private RtgEchoSiteLoader _echoLoader;
        private bool _movementSnapEnabled = true;
        private bool _skipGeofenceConnect;
        private bool _autopilotTestMode;
        private float _autoBeginBlockedUntil;
        private float _apiUnreachableBlockedUntil;
        private bool _beginInFlight;
        private RtgRoute[] _cachedSnapRoutes;
        private List<IReadOnlyList<RtgRouteGeometry.LatLng>> _cachedSnapPaths;
        private bool _corridorSnapEngaged;
        private Coroutine _debouncedRouteRefresh;

        // ------------------------------------------------------------------ //
        // Public API (called by the player each frame + by the on-screen buttons)
        // ------------------------------------------------------------------ //

        private void Start()
        {
#if UNITY_2023_1_OR_NEWER
            _echoLoader = UnityEngine.Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
            _echoLoader = UnityEngine.Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
        }

        /// <summary>When enabled, blend the map pin toward nearby persisted route corridors.</summary>
        public void SetMovementSnapEnabled(bool enabled)
        {
            _movementSnapEnabled = enabled;
            if (!enabled)
                _corridorSnapEngaged = false;
        }

        /// <summary>
        /// Speed used to fabricate point timestamps (m/s). Match simulated autopilot
        /// speed so the server speed gate accepts flushed points.
        /// </summary>
        public void SetFabricatedGpsSpeedMps(float mps)
        {
            gpsSpeedMps = Mathf.Max(0.5f, mps);
        }

        /// <summary>
        /// Autopilot: skip server geofence auto-connect so legs are not cut short near Echo Sites.
        /// Manual: allow geofence connect for real drives.
        /// </summary>
        public void SetSkipGeofenceConnect(bool skip)
        {
            _skipGeofenceConnect = skip;
        }

        /// <summary>
        /// Home testing: when enabled, disables mid-drive checkpoint saves and uses
        /// autopilotCheckpointMeters instead of legCheckpointMeters. Off when real
        /// drive parity is on (Auto Pilot behaves like Manual).
        /// </summary>
        public void SetAutopilotTestMode(bool enabled)
        {
            _autopilotTestMode = enabled;
        }

        /// <summary>
        /// Called when Manual ↔ Auto Pilot teleports the pin — abandon the open leg
        /// so we never save perpendicular spur routes at the route origin.
        /// </summary>
        public void OnLocationSourceChanged()
        {
            AbandonActiveLeg("mode change");
        }

        /// <summary>
        /// Drop the in-progress leg without persisting (mode switch, test reset, world wipe).
        /// </summary>
        public void AbandonActiveLeg(string reason)
        {
            _autoBeginBlockedUntil = Time.time + 2f;
            _hasResumeGate = false;

            if (_flushLoop != null)
            {
                StopCoroutine(_flushLoop);
                _flushLoop = null;
            }

            _flushEpoch++;
            _flushInFlight = false;
            _queue.Clear();
            _fullPath.Clear();
            _state = State.Idle;
            _sessionId = null;
            _hasLast = false;
            _hasLastRaw = false;
            _cumulativeDistanceM = 0;
            _checkpointSaving = false;
            StatusText = $"Route: idle ({reason})";
            Debug.Log($"[RTG] Route leg abandoned — {reason}.");
        }

        /// <summary>
        /// Dev helper: wipe saved routes/sessions for this empire via reset-progress API.
        /// </summary>
        public void ResetWorldProgress(Action<bool, string> done = null)
        {
            if (string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(empireId))
            {
                done?.Invoke(false, "Needs live world (run '6b. Connect Echo Sites to Live API')");
                return;
            }

            StartCoroutine(ResetWorldProgressRoutine(done));
        }

        /// <summary>Drop cached corridor paths after the world map reloads.</summary>
        public void InvalidateSnapCache()
        {
            _cachedSnapRoutes = null;
            _cachedSnapPaths = null;
            _corridorSnapEngaged = false;
        }

        /// <summary>
        /// One-shot snap for route begin (instant placement on corridor).
        /// </summary>
        public bool TrySnapForMovement(ref double lat, ref double lng)
        {
            if (!_movementSnapEnabled || !snapToRoutes || snapProximityMeters <= 0f)
                return false;

            List<IReadOnlyList<RtgRouteGeometry.LatLng>> candidates = EnsureSnapPathCache();
            if (candidates == null || candidates.Count == 0)
                return false;

            double distanceM = RtgRoutePathUtil.MinDistanceToPaths(lat, lng, candidates);
            if (distanceM > snapProximityMeters)
                return false;

            RtgRouteGeometry.LatLng foot = RtgRouteGeometry.NearestPointOnAnyPath(lat, lng, candidates);
            lat = foot.lat;
            lng = foot.lng;
            return true;
        }

        /// <summary>
        /// When near a persisted route corridor, ease the glider onto that line.
        /// Blends every frame with hysteresis so the pin does not jerk sideways.
        /// </summary>
        public bool TryBlendSnapForMovement(ref double lat, ref double lng, float deltaTime)
        {
            if (!_movementSnapEnabled || !snapToRoutes || snapProximityMeters <= 0f)
            {
                _corridorSnapEngaged = false;
                return false;
            }

            List<IReadOnlyList<RtgRouteGeometry.LatLng>> candidates = EnsureSnapPathCache();
            if (candidates == null || candidates.Count == 0)
            {
                _corridorSnapEngaged = false;
                return false;
            }

            double distanceM = RtgRoutePathUtil.MinDistanceToPaths(lat, lng, candidates);
            float enterM = snapProximityMeters;
            float exitM = snapProximityMeters * 1.35f;

            if (_corridorSnapEngaged)
            {
                if (distanceM > exitM)
                    _corridorSnapEngaged = false;
            }
            else if (distanceM <= enterM)
            {
                _corridorSnapEngaged = true;
            }

            if (!_corridorSnapEngaged)
                return false;

            RtgRouteGeometry.LatLng foot = RtgRouteGeometry.NearestPointOnAnyPath(lat, lng, candidates);
            float speed = Mathf.Max(0.5f, snapBlendSmoothing);
            float t = 1f - Mathf.Exp(-speed * deltaTime);
            lat += (foot.lat - lat) * t;
            lng += (foot.lng - lng) * t;
            return true;
        }

        private List<IReadOnlyList<RtgRouteGeometry.LatLng>> EnsureSnapPathCache()
        {
            RtgRoute[] persisted = _echoLoader != null ? _echoLoader.LastMap?.routes : null;
            if (persisted == _cachedSnapRoutes && _cachedSnapPaths != null)
                return _cachedSnapPaths;

            _cachedSnapRoutes = persisted;
            _cachedSnapPaths = RtgRoutePathUtil.CollectNetworkPaths(
                null,
                persisted,
                empireId,
                RtgRoutePathUtil.MaxSnapCheckPoints);
            return _cachedSnapPaths;
        }

        public void NotifyPosition(double rawLat, double rawLng)
        {
            _curLat = rawLat;
            _curLng = rawLng;
            _hasCur = true;

            // Always-on capture: begin (or resume) a route automatically as the
            // player moves, so no manual Begin/End is ever needed.
            if (autoRecord && _state == State.Idle) TryAutoBegin();

            if (_state != State.Active) return;

            if (!_hasLastRaw)
            {
                _lastRawLat = rawLat;
                _lastRawLng = rawLng;
                _hasLastRaw = true;
                _lastLat = rawLat;
                _lastLng = rawLng;
                _hasLast = true;
                return;
            }

            double moved = Haversine(_lastRawLat, _lastRawLng, rawLat, rawLng);
            if (moved < sampleSpacingMeters) return;

            _cumulativeDistanceM += moved;
            _queue.Add(new GpsPoint
            {
                lat = rawLat,
                lng = rawLng,
                accuracyM = 8,
                recordedAt = Iso(_startUtc.AddSeconds(_cumulativeDistanceM / Mathf.Max(0.1f, gpsSpeedMps))),
            });
            _fullPath.Add(new PathPoint { lat = rawLat, lng = rawLng });
            _lastRawLat = rawLat;
            _lastRawLng = rawLng;
            _lastLat = rawLat;
            _lastLng = rawLng;
            MaybeCheckpointLeg();
        }

        public void ToggleFromButton()
        {
            if (IsActive) End();
            else Begin();
        }

        private void TryAutoBegin()
        {
            if (Time.time < _autoBeginBlockedUntil) return;
            if (Time.time < _apiUnreachableBlockedUntil) return;
            if (_beginInFlight) return;
            if (!_hasCur) return;
            SyncConfigFromEchoLoader();
            if (_echoLoader != null && _echoLoader.LoadedFromSampleFallback)
            {
                StatusText = "Route: offline (sample map)";
                return;
            }
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
            if (_state == State.Active || _beginInFlight) return;
            if (!_hasCur) { StatusText = "Route: waiting for GPS fix…"; return; }
            SyncConfigFromEchoLoader();
            if (_echoLoader != null && _echoLoader.LoadedFromSampleFallback)
            {
                StatusText = "Route: offline (sample map)";
                return;
            }
            if (string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(empireId))
            {
                StatusText = "Route: needs live world (run '6b. Connect Echo Sites to Live API')";
                return;
            }

            double startLat = _curLat;
            double startLng = _curLng;
            if (_movementSnapEnabled && snapToRoutes)
                TrySnapForMovement(ref startLat, ref startLng);

            StartCoroutine(BeginRoutine(startLat, startLng));
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

        /// <summary>Decimated active leg for lightweight claim payloads.</summary>
        public bool TryGetClaimPathHint(out List<RtgRouteGeometry.LatLng> path)
        {
            if (!TryGetRoutePath(out path) || path.Count < 1)
            {
                path = null;
                return false;
            }
            path = RtgRoutePathUtil.DecimateForClaim(path);
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
                path = RtgRoutePathUtil.DecimateForClaim(pathOverride);
            else if (!TryGetClaimPathHint(out path))
                path = null;

            var sb = new StringBuilder("{\"empireId\":\"").Append(empireId).Append("\",");
            if (!string.IsNullOrEmpty(_sessionId))
                sb.Append("\"sessionId\":\"").Append(_sessionId).Append("\",");
            sb.Append("\"useNetworkRoutes\":true");
            if (path != null && path.Count > 0)
            {
                sb.Append(",\"routePath\":[");
                for (int i = 0; i < path.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"lat\":").Append(D(path[i].lat)).Append(",\"lng\":").Append(D(path[i].lng)).Append('}');
                }
                sb.Append(']');
            }
            sb.Append(",\"targetKind\":\"").Append(marker.KindApiValue).Append("\",");
            sb.Append("\"targetId\":\"").Append(marker.targetId).Append("\"");
            // Pin position on the map (may be scatter-offset for tap testing).
            sb.Append(",\"approachLat\":").Append(D(marker.lat));
            sb.Append(",\"approachLng\":").Append(D(marker.lng));
            if (!string.IsNullOrEmpty(goodieChoice))
                sb.Append(",\"goodieChoice\":\"").Append(goodieChoice).Append("\"");
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
                    if (resp != null)
                    {
                        result.connectorRouteId = resp.connectorRouteId;
                        result.linkedRouteId = resp.linkedRouteId;
                    }
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
            public string connectorRouteId;
            public string linkedRouteId;
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
            _beginInFlight = true;
            StatusText = "Route: starting…";
            _cumulativeDistanceM = 0;
            _hasLast = false;
            _hasLastRaw = false;
            _flushEpoch++;
            _flushInFlight = false;
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
                    _state = State.Active;
                    var resp = JsonUtility.FromJson<BeginResp>(text);
                    _sessionId = resp.sessionId;
                    _lastLat = lat;
                    _lastLng = lng;
                    _hasLast = true;
                    _lastRawLat = _curLat;
                    _lastRawLng = _curLng;
                    _hasLastRaw = true;
                    _fullPath.Add(new PathPoint { lat = lat, lng = lng });
                    _hasResumeGate = false;
                    StatusText = "Route: recording";
                    if (_flushLoop == null) _flushLoop = StartCoroutine(FlushLoop());
                }
                else
                {
                    _state = State.Idle;
                    if (code == 0)
                    {
                        _apiUnreachableBlockedUntil = Time.time + 12f;
                        StatusText = "Route: API unreachable";
                        Debug.LogError(
                            $"[RTG] Begin route failed: API unreachable at {apiBaseUrl.TrimEnd('/')}/sessions. " +
                            RtgApiHttp.FormatUnreachableHint(apiBaseUrl, text));
                    }
                    else
                    {
                        StatusText = $"Route: begin failed ({code})";
                        Debug.LogError($"[RTG] Begin route failed: {code} {text}");
                    }
                }
            });

            _beginInFlight = false;
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
            if (_flushInFlight) yield break;
            if (_state != State.Active || string.IsNullOrEmpty(_sessionId)) yield break;
            if (_queue.Count == 0) yield break;

            _flushInFlight = true;
            int flushEpoch = _flushEpoch;
            int take = Mathf.Min(MaxBatch, _queue.Count);
            var batch = _queue.GetRange(0, take);

            var sb = new StringBuilder("{\"points\":[");
            for (int i = 0; i < batch.Count; i++)
            {
                GpsPoint p = batch[i];
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"lat\":{D(p.lat)},\"lng\":{D(p.lng)},\"accuracyM\":{D(p.accuracyM)},\"recordedAt\":\"{p.recordedAt}\"}}");
            }
            sb.Append(']');
            if (_skipGeofenceConnect)
                sb.Append(",\"skipGeofenceConnect\":true");
            sb.Append('}');

            yield return Post($"/sessions/{_sessionId}/points", sb.ToString(), (code, text, ok) =>
            {
                try
                {
                    if (!ok)
                    {
                        Debug.LogWarning($"[RTG] Points flush failed ({code}) — will retry.");
                        return;
                    }

                    if (flushEpoch != _flushEpoch)
                    {
                        Debug.Log("[RTG] Points flush ack ignored — route session was reset.");
                        return;
                    }

                    int removeCount = Mathf.Min(take, _queue.Count);
                    if (removeCount > 0)
                        _queue.RemoveRange(0, removeCount);
                    else if (take > 0)
                        Debug.LogWarning(
                            $"[RTG] Points flush ack but queue empty (take={take}) — skipping remove.");

                    var resp = JsonUtility.FromJson<PointsResp>(text);
                    NotifyExplorationDelta(resp?.exploration);
                    if (resp != null && resp.connected)
                    {
                        string name = resp.settlement != null ? resp.settlement.name : "settlement";
                        Debug.Log($"[RTG] Node connected at {name} — leg saved (route {resp.routeId}). Bonuses TBD.");
                        _state = State.Idle;
                        RefreshPersistedRoutes();

                        _resumeLat = _curLat;
                        _resumeLng = _curLng;
                        _hasResumeGate = true;
                        StatusText = autoRecord
                            ? $"Route: node connected → {name}"
                            : $"Route: connected → {name}";
                    }
                }
                finally
                {
                    _flushInFlight = false;
                }
            });

            // Post invokes the callback before returning; guard if callback was skipped.
            if (_flushInFlight)
                _flushInFlight = false;
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

            List<RtgRouteGeometry.LatLng> rawPath = new List<RtgRouteGeometry.LatLng>(_fullPath.Count);
            foreach (PathPoint p in _fullPath)
                rawPath.Add(new RtgRouteGeometry.LatLng(p.lat, p.lng));

            List<RtgRouteGeometry.LatLng> cleaned = RtgRoutePathUtil.CleanupForPersist(
                rawPath,
                simplifyToleranceMeters);
            if (cleaned.Count < 2 && rawPath.Count >= 2)
                cleaned = rawPath;

            if (cleaned.Count < 2)
            {
                _state = State.Idle;
                StatusText = "Route: too few points to save";
                Debug.LogWarning($"[RTG] Route end skipped — only {cleaned.Count} point(s) after cleanup.");
                yield break;
            }

            double legLengthM = PathLengthM(cleaned);
            if (legLengthM < minLegLengthMeters)
            {
                _state = State.Idle;
                StatusText = "Route: leg too short to save";
                Debug.LogWarning($"[RTG] Route end skipped — leg only {legLengthM:0} m (min {minLegLengthMeters:0} m).");
                yield break;
            }

            var sb = new StringBuilder("{\"path\":[");
            for (int i = 0; i < cleaned.Count; i++)
            {
                RtgRouteGeometry.LatLng p = cleaned[i];
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
                        ? "Route: travel leg saved"
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
            SyncConfigFromEchoLoader();

            string primaryBase = apiBaseUrl.TrimEnd('/');
            long code = 0;
            string text = null;
            bool ok = false;

            yield return PostOnce(primaryBase + path, json, (c, t, o) =>
            {
                code = c;
                text = t;
                ok = o;
            });

            if (ok)
            {
                done?.Invoke(code, text, true);
                yield break;
            }

            if (code == 0 && Application.isEditor && editorLocalhostRetry)
            {
                string localBase = LocalhostApiBase(apiBaseUrl).TrimEnd('/');
                if (!string.Equals(localBase, primaryBase, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning(
                        $"[RTG] API POST failed ({primaryBase}{path}). Retrying via editor localhost: {localBase}{path}");
                    yield return PostOnce(localBase + path, json, (c, t, o) =>
                    {
                        code = c;
                        text = t;
                        ok = o;
                    });
                    if (ok)
                    {
                        apiBaseUrl = localBase;
                        if (_echoLoader != null)
                            _echoLoader.apiBaseUrl = localBase;
                        done?.Invoke(code, text, true);
                        yield break;
                    }
                }
            }

            done?.Invoke(code, text, ok);
        }

        private static IEnumerator PostOnce(string path, string json, Action<long, string, bool> done)
        {
            using var req = new UnityWebRequest(path, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 8;

            yield return req.SendWebRequest();

            bool ok = req.result == UnityWebRequest.Result.Success;
            string text = ok
                ? req.downloadHandler.text
                : string.IsNullOrEmpty(req.error)
                    ? req.downloadHandler?.text ?? "request failed"
                    : req.error;
            done?.Invoke(req.responseCode, text, ok);
        }

        /// <summary>Keep api/world/empire ids aligned with the Echo Site loader after localhost retry.</summary>
        public void SyncConfigFromEchoLoader()
        {
            if (_echoLoader == null)
            {
#if UNITY_2023_1_OR_NEWER
                _echoLoader = UnityEngine.Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
                _echoLoader = UnityEngine.Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
            }

            if (_echoLoader == null) return;

            if (!string.IsNullOrWhiteSpace(_echoLoader.apiBaseUrl) &&
                !string.Equals(_echoLoader.apiBaseUrl, apiBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                apiBaseUrl = _echoLoader.apiBaseUrl;
                _apiUnreachableBlockedUntil = 0f;
            }

            // Always mirror loader IDs (including clear on Exit → login).
            worldId = _echoLoader.worldId ?? "";
            empireId = _echoLoader.empireId ?? "";
        }

        private static string LocalhostApiBase(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri))
                return "http://127.0.0.1:3001/api";

            int port = uri.Port > 0 ? uri.Port : 3001;
            string path = uri.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(path))
                path = "/api";
            return $"http://127.0.0.1:{port}{path}";
        }

        private void MaybeCheckpointLeg()
        {
            float checkpointThreshold = _autopilotTestMode ? autopilotCheckpointMeters : legCheckpointMeters;
            if (!autoRecord || checkpointThreshold <= 0f) return;
            if (_state != State.Active || _checkpointSaving || _shutdownSaveStarted) return;
            if (_cumulativeDistanceM < checkpointThreshold) return;
            if (_fullPath.Count < 2) return;

            _checkpointSaving = true;
            StartCoroutine(CheckpointSaveRoutine());
        }

        private IEnumerator CheckpointSaveRoutine()
        {
            Debug.Log($"[RTG] Checkpoint save after {_cumulativeDistanceM:0} m…");
            yield return EndRoutine();
            _checkpointSaving = false;
            if (autoRecord && _hasCur)
                TryAutoBegin();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) TryPersistActiveRouteOnShutdown();
        }

        private void OnApplicationQuit()
        {
            TryPersistActiveRouteOnShutdown();
        }

        private void OnDestroy()
        {
            TryPersistActiveRouteOnShutdown();
        }

        /// <summary>
        /// Flush and end the active leg when the app backgrounds or closes so
        /// real-world drives persist without needing an Echo Site geofence connect.
        /// </summary>
        private void TryPersistActiveRouteOnShutdown()
        {
            if (_shutdownSaveStarted || _state != State.Active) return;
            if (_fullPath.Count < 2 && _queue.Count == 0) return;

            _shutdownSaveStarted = true;
            StartCoroutine(ShutdownSaveRoutine());
        }

        private IEnumerator ShutdownSaveRoutine()
        {
            Debug.Log("[RTG] Saving in-progress route before shutdown…");
            int guard = 0;
            while (_state == State.Active && _queue.Count > 0 && guard++ < 50)
                yield return FlushOnce();

            if (_state == State.Active)
                yield return EndRoutine();
        }

        // ------------------------------------------------------------------ //
        // On-screen controls
        // ------------------------------------------------------------------ //

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (RtgGameSessionLogin.IsPlayBlocked()) return;

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
            ConfigurePersistedRouteDrawer(drawer);
            if (_debouncedRouteRefresh != null)
                StopCoroutine(_debouncedRouteRefresh);
            _debouncedRouteRefresh = StartCoroutine(DebouncedRouteRefresh(drawer));
        }

        private void ConfigurePersistedRouteDrawer(RtgPersistedRouteDrawer drawer)
        {
            if (drawer == null) return;

#if UNITY_2023_1_OR_NEWER
            RtgEchoSiteLoader loader = UnityEngine.Object.FindFirstObjectByType<RtgEchoSiteLoader>();
            RtgPlayerLocation player = UnityEngine.Object.FindFirstObjectByType<RtgPlayerLocation>();
#else
            RtgEchoSiteLoader loader = UnityEngine.Object.FindObjectOfType<RtgEchoSiteLoader>();
            RtgPlayerLocation player = UnityEngine.Object.FindObjectOfType<RtgPlayerLocation>();
#endif
            if (loader != null)
                drawer.groundHeightMeters = loader.groundHeightMeters;
            if (player != null)
                drawer.travelHeightAboveTerrainM = player.roadHeightMeters;
        }

        private IEnumerator DebouncedRouteRefresh(RtgPersistedRouteDrawer drawer)
        {
            yield return new WaitForSeconds(0.75f);
            yield return drawer.RefreshFromApi(apiBaseUrl, worldId, empireId);
            _debouncedRouteRefresh = null;
        }

        private void MaybeRequestRouteCleanup()
        {
            if (!cleanupRoutesOnMapLoad || _routeCleanupRequested) return;
            if (string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(empireId)) return;

            _routeCleanupRequested = true;
            StartCoroutine(CleanupRoutesRoutine());
        }

        /// <summary>Called after the world map loads so existing messy routes get simplified once.</summary>
        public void RequestRouteCleanupIfNeeded()
        {
            MaybeRequestRouteCleanup();
        }

        private IEnumerator ResetWorldProgressRoutine(Action<bool, string> done)
        {
            AbandonActiveLeg("world reset");

            string json =
                $"{{\"confirm\":true,\"empireId\":\"{empireId}\"}}";

            bool finished = false;
            bool success = false;
            string message = null;

            yield return Post($"/worlds/{worldId}/reset-progress", json, (code, text, ok) =>
            {
                finished = true;
                success = ok && code == 200;
                message = success ? "World routes cleared" : $"Reset failed ({code})";
                if (success)
                {
                    Debug.Log($"[RTG] World progress reset: {text}");
                    InvalidateSnapCache();
                    RefreshPersistedRoutes();
                    if (_echoLoader == null)
                    {
#if UNITY_2023_1_OR_NEWER
                        _echoLoader = UnityEngine.Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
                        _echoLoader = UnityEngine.Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
                    }

                    _echoLoader?.ReloadMarkersAfterReset();
#if UNITY_2023_1_OR_NEWER
                    RtgPlayerLocation player = UnityEngine.Object.FindFirstObjectByType<RtgPlayerLocation>();
#else
                    RtgPlayerLocation player = UnityEngine.Object.FindObjectOfType<RtgPlayerLocation>();
#endif
                    player?.RefreshAfterWorldReset();
                }
                else
                {
                    Debug.LogWarning($"[RTG] World progress reset failed ({code}): {text}");
                }
            });

            if (!finished) yield break;
            done?.Invoke(success, message);
        }

        private IEnumerator CleanupRoutesRoutine()
        {
            string json =
                $"{{\"empireId\":\"{empireId}\",\"toleranceM\":{D(simplifyToleranceMeters)}}}";

            bool done = false;
            long code = 0;
            string text = null;
            bool ok = false;

            yield return Post($"/worlds/{worldId}/routes/cleanup", json, (c, t, o) =>
            {
                code = c;
                text = t;
                ok = o;
                done = true;
            });

            if (!done) yield break;

            if (ok && code == 200)
            {
                Debug.Log($"[RTG] Route cleanup: {text}");
                RtgPersistedRouteDrawer drawer = RtgPersistedRouteDrawer.FindOrCreate();
                if (drawer != null)
                    drawer.StartCoroutine(drawer.RefreshFromApi(apiBaseUrl, worldId, empireId));
            }
            else
            {
                Debug.LogWarning($"[RTG] Route cleanup failed ({code}): {text}");
            }
        }

        private static double PathLengthM(List<RtgRouteGeometry.LatLng> path)
        {
            double total = 0;
            for (int i = 1; i < path.Count; i++)
            {
                total += Haversine(
                    path[i - 1].lat, path[i - 1].lng,
                    path[i].lat, path[i].lng);
            }
            return total;
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

        private static void NotifyExplorationDelta(ExplorationDelta delta)
        {
            if (delta == null || RtgWorldScanSettings.PreSurveyedWorld) return;
            RtgFogOfWar fog = RtgFogOfWar.Find();
            if (fog == null) return;
            fog.ApplyExplorationDelta(delta.newlyRevealedTileIds, delta.newResourceNodeIds);
        }

        [Serializable] private class BeginResp { public string sessionId; public string status; }
        [Serializable] private class PointsResp
        {
            public bool connected;
            public string routeId;
            public ConnSettlement settlement;
            public ExplorationDelta exploration;
        }

        [Serializable] private class ExplorationDelta
        {
            public string[] newlyRevealedTileIds;
            public string[] newResourceNodeIds;
        }
        [Serializable] private class ConnSettlement { public string name; }
        [Serializable] private class ClaimResp
        {
            public bool ok;
            public string message;
            public string connectorRouteId;
            public string linkedRouteId;
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
