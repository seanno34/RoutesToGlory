using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Cesium terrain height cache + glider corridor commitment.
    ///
    /// <para><b>Regression guardrails</b> (see <see cref="RtgTerrainElevationGuards"/>):</para>
    /// <list type="bullet">
    /// <item>Glider: <see cref="GetClearancePlacementHeight"/> only — blends
    /// <c>_committedGroundHeight</c> when corridor ahead is consistent.</item>
    /// <item>Corridor samples: <see cref="GetCorridorSampleHeight"/> — cached Cesium only;
    /// never raycast (frame-volatile tile noise caused bounce).</item>
    /// <item>Light Road: <see cref="GetTrailReferenceGroundHeight"/> + <see cref="TryGetCachedGroundHeight"/>
    /// read-only; do not mutate <c>_committedGroundHeight</c> from trail code.</item>
    /// <item><see cref="GetGroundHeight"/> may raycast — OK for deposits/scatter, not glider/trail.</item>
    /// <item>Do not cache raycast results; do not snap committed height instantly per frame.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public class RtgTerrainHeight : MonoBehaviour
    {
        private const double LatM = 111_320.0;
        private const int CacheQuantizeDigits = 4;
        private const float RaycastProbeHeightM = 6000f;
        private const float RaycastMaxDistanceM = 12000f;
        private const int MaxCorridorSamples = 16;

        [Tooltip("Flat ellipsoid height (m) used before Cesium samples arrive.")]
        public double fallbackGroundHeightM = 1476.0;

        [Tooltip("Meters above committed corridor ground for player clearance. " +
                 "Keep above TravelRoadClearanceM / ConnectorClearanceM so the ship reads above roads.")]
        public float markerClearanceM = RtgTerrainElevationGuards.GliderClearanceM;

        [Tooltip("Spacing (m) between terrain samples along the travel corridor.")]
        public float corridorSampleSpacingM = 12f;

        [Tooltip("How far ahead (m) to sample the corridor.")]
        public float corridorLookAheadM = 72f;

        [Tooltip("Max height spread (m) in a sample run to count as a flat plateau.")]
        public float consistencyBandM = 1.25f;

        [Tooltip("Flat/monotone corridor must span this distance (m) at low speed before height changes.")]
        public float minConsistentDistanceSlowM = 36f;

        [Tooltip("Flat/monotone corridor span (m) required at high speed.")]
        public float minConsistentDistanceFastM = 18f;

        [Tooltip("Ground speed (m/s) where fast min-distance fully applies.")]
        public float consistencyFullSpeedMps = 25f;

        [Tooltip("Minimum change (m) in committed ground before blending toward a new plateau.")]
        public float minLevelChangeM = 1.25f;

        [Tooltip("Seconds to ease committed ground upward.")]
        public float committedBlendUpSeconds = 0.4f;

        [Tooltip("Seconds to ease committed ground downward.")]
        public float committedBlendDownSeconds = 0.55f;

        [Tooltip("Use Physics.Raycast when async samples are pending (not cached — volatile). " +
                 "OK for deposits/scatter via GetGroundHeight. NEVER use for corridor/glider/trail.")]
        public bool useRaycastFallback = true;

        // Glider-only committed corridor ground. Light Road reads via GetTrailReferenceGroundHeight;
        // trail code must not write this field.
        private double _committedGroundHeight;
        private bool _hasCommittedGroundHeight;

        private Cesium3DTileset _tileset;
        private CesiumGlobeAnchor _probeAnchor;
        // Cesium SampleHeightMostDetailed only — never store raycast results (volatile → bounce).
        private readonly Dictionary<string, double> _heightCache = new();
        private readonly HashSet<string> _pendingKeys = new();
        private readonly Queue<SampleJob> _sampleQueue = new();
        private readonly double[] _corridorHeights = new double[MaxCorridorSamples];
        private int _corridorCount;
        private bool _sampling;

        private struct SampleJob
        {
            public double Longitude;
            public double Latitude;
            public string Key;
        }

        public static RtgTerrainHeight FindOrCreate()
        {
#if UNITY_2023_1_OR_NEWER
            RtgTerrainHeight existing = UnityEngine.Object.FindFirstObjectByType<RtgTerrainHeight>();
#else
            RtgTerrainHeight existing = UnityEngine.Object.FindObjectOfType<RtgTerrainHeight>();
#endif
            if (existing != null) return existing;

            CesiumGeoreference geo = UnityEngine.Object.FindObjectOfType<CesiumGeoreference>();
            if (geo == null) return null;

            var go = new GameObject("RTG Terrain Height");
            go.transform.SetParent(geo.transform, false);
            return go.AddComponent<RtgTerrainHeight>();
        }

        public void Configure(double groundHeightMeters, float markerHeightMeters)
        {
            fallbackGroundHeightM = groundHeightMeters;
            markerClearanceM = markerHeightMeters;
            _committedGroundHeight = groundHeightMeters;
            _hasCommittedGroundHeight = true;
        }

        public void ResetHeightSmoothing()
        {
            _hasCommittedGroundHeight = false;
        }

        /// <summary>Raw height for deposits/scatter — may raycast. Not for glider or Light Road.</summary>
        public double GetGroundHeight(double lat, double lng)
        {
            string key = CacheKey(lat, lng);
            if (_heightCache.TryGetValue(key, out double cached))
                return cached;

            if (useRaycastFallback && TryRaycastGroundHeight(lat, lng, out double rayHeight))
                return rayHeight;

            return fallbackGroundHeightM;
        }

        /// <summary>
        /// Best static ground height for embedded deposit anchors — max of Cesium sample,
        /// cache, and one-shot raycast. Does not use corridor smoothing.
        /// </summary>
        public double ResolveDepositGroundHeight(double lat, double lng, double cesiumSampleM)
        {
            QueueSample(lat, lng);
            double best = cesiumSampleM;

            if (TryGetCachedGroundHeight(lat, lng, out double cached))
                best = Math.Max(best, cached);

            if (useRaycastFallback && TryRaycastGroundHeight(lat, lng, out double rayHeight))
                best = Math.Max(best, rayHeight);

            return best;
        }

        public void QueueSampleIfNeeded(double lat, double lng)
        {
            QueueSample(lat, lng);
        }

        public void QueueCorridorSamplesIfNeeded(double lat, double lng, float headingRad)
        {
            FillCorridorSamples(lat, lng, headingRad);
        }

        /// <summary>Legacy hook — queues corridor samples along heading.</summary>
        public void QueueForwardSamplesIfNeeded(double lat, double lng, float headingRad)
        {
            QueueCorridorSamplesIfNeeded(lat, lng, headingRad);
        }

        /// <summary>Player corridor ground (read-only). Light road anchors here — does not mutate glider state.</summary>
        public double GetTrailReferenceGroundHeight()
        {
            return _hasCommittedGroundHeight ? _committedGroundHeight : fallbackGroundHeightM;
        }

        /// <summary>Stable cached Cesium sample only (no raycast). False when not sampled yet.</summary>
        public bool TryGetCachedGroundHeight(double lat, double lng, out double heightM)
        {
            return _heightCache.TryGetValue(CacheKey(lat, lng), out heightM);
        }

        /// <summary>
        /// <b>Glider placement API.</b> Updates committed corridor and returns ellipsoid height.
        /// Called from <see cref="RtgPlayerLocation"/> LateUpdate only — do not call from Light Road.
        /// </summary>
        public double GetClearancePlacementHeight(
            double lat,
            double lng,
            float headingRad,
            float groundSpeedMps = 0f)
        {
            UpdateCommittedCorridor(lat, lng, headingRad, groundSpeedMps);
            return _committedGroundHeight + markerClearanceM;
        }

        /// <summary>
        /// One-shot corridor evaluation for non-trail consumers (e.g. pathfinder clearance).
        /// <b>Do not use for Light Road</b> — trail must use <see cref="GetTrailReferenceGroundHeight"/>.
        /// </summary>
        public double EvaluateCorridorGroundHeight(
            double lat,
            double lng,
            float headingRad,
            float groundSpeedMps,
            double holdGroundM)
        {
            FillCorridorSamples(lat, lng, headingRad);
            if (TryResolveCorridorPlateau(groundSpeedMps, out double plateau))
                return plateau;
            return holdGroundM;
        }

        public double GetGroundClearanceHeight(double lat, double lng, float headingRad, float clearanceM)
        {
            double hold = _hasCommittedGroundHeight ? _committedGroundHeight : fallbackGroundHeightM;
            return EvaluateCorridorGroundHeight(lat, lng, headingRad, consistencyFullSpeedMps, hold) + clearanceM;
        }

        private void UpdateCommittedCorridor(
            double lat,
            double lng,
            float headingRad,
            float groundSpeedMps)
        {
            if (!_hasCommittedGroundHeight)
            {
                _committedGroundHeight = GetGroundHeight(lat, lng);
                _hasCommittedGroundHeight = true;
                return;
            }

            FillCorridorSamples(lat, lng, headingRad);
            if (!TryResolveCorridorPlateau(groundSpeedMps, out double plateau))
                return;

            BlendCommittedToward(plateau);
        }

        private void BlendCommittedToward(double targetGround)
        {
            // REGRESSION: exponential blend only — no instant snap (caused visible bounce).
            double delta = targetGround - _committedGroundHeight;
            if (Math.Abs(delta) < minLevelChangeM)
                return;

            float blendSeconds = delta > 0 ? committedBlendUpSeconds : committedBlendDownSeconds;
            float blend = blendSeconds > 0f
                ? 1f - Mathf.Exp(-Time.deltaTime / blendSeconds)
                : 1f;
            _committedGroundHeight += delta * blend;
        }

        private enum CorridorPlateauKind
        {
            None,
            Flat,
            Climb,
            Descent,
        }

        private void FillCorridorSamples(double lat, double lng, float headingRad)
        {
            float spacing = Mathf.Max(4f, corridorSampleSpacingM);
            float lookAhead = Mathf.Max(spacing, corridorLookAheadM);
            _corridorCount = 0;

            for (float distanceM = 0f;
                 distanceM <= lookAhead + 0.01f && _corridorCount < MaxCorridorSamples;
                 distanceM += spacing)
            {
                double northM = Math.Cos(headingRad) * distanceM;
                double eastM = Math.Sin(headingRad) * distanceM;
                OffsetMeters(lat, lng, northM, eastM, out double sampleLat, out double sampleLng);
                QueueSample(sampleLat, sampleLng);
                _corridorHeights[_corridorCount++] = GetCorridorSampleHeight(sampleLat, sampleLng);
            }
        }

        /// <summary>
        /// Corridor height sample — cached Cesium only. REGRESSION: do not call GetGroundHeight
        /// or raycast here; see <see cref="RtgTerrainElevationGuards"/>.
        /// </summary>
        private double GetCorridorSampleHeight(double lat, double lng)
        {
            string key = CacheKey(lat, lng);
            bool hasCached = _heightCache.TryGetValue(key, out double cached);
            double hold = _hasCommittedGroundHeight ? _committedGroundHeight : double.MinValue;
            return RtgTerrainElevationGuards.CorridorSampleHeightOrHold(
                cached,
                hasCached,
                hold,
                fallbackGroundHeightM);
        }

        private bool TryResolveCorridorPlateau(float groundSpeedMps, out double plateauHeight)
        {
            plateauHeight = 0;
            if (_corridorCount <= 0)
                return false;

            float minDistanceM = ResolveMinConsistentDistance(groundSpeedMps);
            int bestEnd = -1;
            CorridorPlateauKind bestKind = CorridorPlateauKind.None;

            for (int end = 0; end < _corridorCount; end++)
            {
                float spanM = end * Mathf.Max(4f, corridorSampleSpacingM);
                if (spanM < minDistanceM)
                    continue;

                if (IsFlatPrefix(end))
                {
                    bestEnd = end;
                    bestKind = CorridorPlateauKind.Flat;
                }
                else if (IsMonotonicClimbPrefix(end))
                {
                    bestEnd = end;
                    bestKind = CorridorPlateauKind.Climb;
                }
                else if (IsMonotonicDescentPrefix(end))
                {
                    bestEnd = end;
                    bestKind = CorridorPlateauKind.Descent;
                }
            }

            if (bestEnd < 0 || bestKind == CorridorPlateauKind.None)
                return false;

            plateauHeight = ResolvePlateauHeight(bestEnd, bestKind);
            return true;
        }

        private double ResolvePlateauHeight(int end, CorridorPlateauKind kind)
        {
            if (kind == CorridorPlateauKind.Flat)
                return _corridorHeights[0];

            double extreme = kind == CorridorPlateauKind.Climb ? double.MinValue : double.MaxValue;
            for (int i = 0; i <= end; i++)
            {
                if (kind == CorridorPlateauKind.Climb)
                    extreme = Math.Max(extreme, _corridorHeights[i]);
                else
                    extreme = Math.Min(extreme, _corridorHeights[i]);
            }

            return extreme;
        }

        private float ResolveMinConsistentDistance(float groundSpeedMps)
        {
            float t = consistencyFullSpeedMps > 0f
                ? Mathf.Clamp01(groundSpeedMps / consistencyFullSpeedMps)
                : 1f;
            return Mathf.Lerp(minConsistentDistanceSlowM, minConsistentDistanceFastM, t);
        }

        private bool IsFlatPrefix(int end)
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            for (int i = 0; i <= end; i++)
            {
                double h = _corridorHeights[i];
                if (h < min) min = h;
                if (h > max) max = h;
            }

            return max - min <= consistencyBandM;
        }

        private bool IsMonotonicClimbPrefix(int end)
        {
            if (end < 1)
                return false;

            const double dipToleranceM = 0.35;
            for (int i = 1; i <= end; i++)
            {
                if (_corridorHeights[i] + dipToleranceM < _corridorHeights[i - 1])
                    return false;
            }

            return _corridorHeights[end] - _corridorHeights[0] >= minLevelChangeM;
        }

        private bool IsMonotonicDescentPrefix(int end)
        {
            if (end < 1)
                return false;

            const double bumpToleranceM = 0.35;
            for (int i = 1; i <= end; i++)
            {
                if (_corridorHeights[i] - bumpToleranceM > _corridorHeights[i - 1])
                    return false;
            }

            return _corridorHeights[0] - _corridorHeights[end] >= minLevelChangeM;
        }

        private void Awake()
        {
            _tileset = RtgTerrainHeightSampler.ResolveTileset();
            EnsureProbeAnchor();
        }

        private void OnEnable()
        {
            if (_tileset == null)
                _tileset = RtgTerrainHeightSampler.ResolveTileset();
            EnsureProbeAnchor();
        }

        private void EnsureProbeAnchor()
        {
            if (_probeAnchor != null) return;

            var probeGo = new GameObject("RTG_HeightProbe");
            probeGo.hideFlags = HideFlags.HideInHierarchy;
            probeGo.transform.SetParent(transform, false);
            _probeAnchor = probeGo.AddComponent<CesiumGlobeAnchor>();
        }

        private bool TryRaycastGroundHeight(double lat, double lng, out double heightM)
        {
            // REGRESSION: raycasts are frame-volatile — only for GetGroundHeight (deposits/scatter).
            // Corridor, glider commitment, and Light Road must never call this path.
            heightM = fallbackGroundHeightM;
            if (_probeAnchor == null) return false;

            _probeAnchor.SetPositionLongitudeLatitudeHeight(lng, lat, fallbackGroundHeightM + RaycastProbeHeightM);
            Vector3 origin = _probeAnchor.transform.position;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, RaycastMaxDistanceM))
            {
                _probeAnchor.transform.position = hit.point;
                heightM = _probeAnchor.height;
                return true;
            }

            return false;
        }

        private void QueueSample(double lat, double lng)
        {
            string key = CacheKey(lat, lng);
            if (_heightCache.ContainsKey(key) || _pendingKeys.Contains(key))
                return;

            _pendingKeys.Add(key);
            _sampleQueue.Enqueue(new SampleJob
            {
                Longitude = lng,
                Latitude = lat,
                Key = key,
            });

            if (!_sampling && isActiveAndEnabled)
                StartCoroutine(ProcessSampleQueue());
        }

        private IEnumerator ProcessSampleQueue()
        {
            _sampling = true;

            while (_sampleQueue.Count > 0)
            {
                if (_tileset == null)
                {
                    _tileset = RtgTerrainHeightSampler.ResolveTileset();
                    if (_tileset == null)
                        break;
                }

                int batchSize = Math.Min(16, _sampleQueue.Count);
                var jobs = new SampleJob[batchSize];
                var positions = new double3[batchSize];

                for (int i = 0; i < batchSize; i++)
                {
                    jobs[i] = _sampleQueue.Dequeue();
                    positions[i] = new double3(
                        jobs[i].Longitude,
                        jobs[i].Latitude,
                        fallbackGroundHeightM);
                }

                Task task = _tileset.SampleHeightMostDetailed(positions);
                yield return new WaitForTask(task);

                if (RtgTerrainHeightSampler.TryGetSampleHeightResult(task, out CesiumSampleHeightResult sample))
                {
                    for (int i = 0; i < batchSize; i++)
                    {
                        double height = fallbackGroundHeightM;
                        if (sample.sampleSuccess != null &&
                            i < sample.sampleSuccess.Length &&
                            sample.sampleSuccess[i] &&
                            sample.longitudeLatitudeHeightPositions != null &&
                            i < sample.longitudeLatitudeHeightPositions.Length)
                        {
                            height = sample.longitudeLatitudeHeightPositions[i].z;
                        }

                        _heightCache[jobs[i].Key] = height;
                        _pendingKeys.Remove(jobs[i].Key);
                    }
                }

                for (int i = 0; i < batchSize; i++)
                    _pendingKeys.Remove(jobs[i].Key);
            }

            _sampling = false;
        }

        private static string CacheKey(double lat, double lng)
        {
            double q = Math.Pow(10, CacheQuantizeDigits);
            return $"{Math.Round(lat * q) / q}:{Math.Round(lng * q) / q}";
        }

        private static void OffsetMeters(
            double lat,
            double lng,
            double northM,
            double eastM,
            out double outLat,
            out double outLng)
        {
            double lngM = LatM * Math.Cos(lat * Math.PI / 180.0);
            outLat = lat + northM / LatM;
            outLng = lng + eastM / lngM;
        }
    }
}
