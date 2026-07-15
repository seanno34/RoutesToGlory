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
    /// Caches Cesium terrain heights for scatter, fog sheet, and player clearance.
    /// Uses async tile sampling plus a physics raycast fallback for live elevation.
    /// </summary>
    [DisallowMultipleComponent]
    public class RtgTerrainHeight : MonoBehaviour
    {
        private const double LatM = 111_320.0;
        private const int CacheQuantizeDigits = 4;
        private const float RaycastProbeHeightM = 6000f;
        private const float RaycastMaxDistanceM = 12000f;

        [Tooltip("Flat ellipsoid height (m) used before Cesium samples arrive.")]
        public double fallbackGroundHeightM = 1476.0;

        [Tooltip("Meters above sampled terrain for player / settlement clearance.")]
        public float markerClearanceM = 6f;

        [Tooltip("Look-ahead distance (m) when sampling terrain under the travel heading.")]
        public float forwardSampleDistanceM = 28f;

        [Tooltip("Seconds to ease between fallback and sampled terrain heights.")]
        public float heightBlendSeconds = 0.35f;

        [Tooltip("Use Physics.Raycast against Cesium terrain colliders when async samples are pending.")]
        public bool useRaycastFallback = true;

        private Cesium3DTileset _tileset;
        private CesiumGlobeAnchor _probeAnchor;
        private readonly Dictionary<string, double> _heightCache = new();
        private readonly HashSet<string> _pendingKeys = new();
        private readonly Queue<SampleJob> _sampleQueue = new();
        private bool _sampling;
        private double _smoothedClearanceHeight;
        private bool _hasSmoothedClearanceHeight;

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
            _smoothedClearanceHeight = groundHeightMeters + markerHeightMeters;
            _hasSmoothedClearanceHeight = true;
        }

        public double GetGroundHeight(double lat, double lng)
        {
            string key = CacheKey(lat, lng);
            if (_heightCache.TryGetValue(key, out double cached))
                return cached;

            if (useRaycastFallback && TryRaycastGroundHeight(lat, lng, out double rayHeight))
            {
                _heightCache[key] = rayHeight;
                return rayHeight;
            }

            return fallbackGroundHeightM;
        }

        public void QueueSampleIfNeeded(double lat, double lng)
        {
            QueueSample(lat, lng);
        }

        public void QueueForwardSamplesIfNeeded(double lat, double lng, float headingRad)
        {
            QueueSample(lat, lng);

            double northM = Math.Cos(headingRad) * forwardSampleDistanceM * 0.5;
            double eastM = Math.Sin(headingRad) * forwardSampleDistanceM * 0.5;
            OffsetMeters(lat, lng, northM, eastM, out double midLat, out double midLng);
            QueueSample(midLat, midLng);

            northM = Math.Cos(headingRad) * forwardSampleDistanceM;
            eastM = Math.Sin(headingRad) * forwardSampleDistanceM;
            OffsetMeters(lat, lng, northM, eastM, out double aheadLat, out double aheadLng);
            QueueSample(aheadLat, aheadLng);
        }

        public double GetClearancePlacementHeight(double lat, double lng, float headingRad)
        {
            double target = ResolveClearanceTarget(lat, lng, headingRad);
            if (!_hasSmoothedClearanceHeight)
            {
                _smoothedClearanceHeight = target;
                _hasSmoothedClearanceHeight = true;
                return target;
            }

            float blend = heightBlendSeconds > 0f
                ? 1f - Mathf.Exp(-Time.deltaTime / heightBlendSeconds)
                : 1f;
            _smoothedClearanceHeight += (target - _smoothedClearanceHeight) * blend;
            return _smoothedClearanceHeight;
        }

        private double ResolveClearanceTarget(double lat, double lng, float headingRad)
        {
            double best = GetGroundHeight(lat, lng);

            double northM = Math.Cos(headingRad) * forwardSampleDistanceM * 0.5;
            double eastM = Math.Sin(headingRad) * forwardSampleDistanceM * 0.5;
            OffsetMeters(lat, lng, northM, eastM, out double midLat, out double midLng);
            best = Math.Max(best, GetGroundHeight(midLat, midLng));

            northM = Math.Cos(headingRad) * forwardSampleDistanceM;
            eastM = Math.Sin(headingRad) * forwardSampleDistanceM;
            OffsetMeters(lat, lng, northM, eastM, out double aheadLat, out double aheadLng);
            best = Math.Max(best, GetGroundHeight(aheadLat, aheadLng));

            return best + markerClearanceM;
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
