using System;
using System.Collections;
using System.Threading.Tasks;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Samples Cesium terrain height at the spaceship's lat/lng and returns a smoothed
    /// ellipsoid height for marker / route placement. Falls back to a constant when
    /// tiles are not loaded yet.
    /// </summary>
    public class RtgTerrainHeight : MonoBehaviour
    {
        private const double MetersPerDegreeLat = 111_320.0;

        [Tooltip("Terrain tileset to sample (Cesium World Terrain). Auto-filled if empty.")]
        public Cesium3DTileset terrainTileset;

        [Tooltip("Ellipsoid height (m) used until the first successful terrain sample.")]
        public double fallbackHeightMeters = 1476.0;

        [Tooltip("Meters above sampled terrain for the spaceship anchor.")]
        public float markerOffsetMeters = 15f;

        [Tooltip("Request a new terrain sample after moving this many meters.")]
        public float resampleMoveMeters = 8f;

        [Tooltip("How quickly sampled height catches up (higher = snappier).")]
        public float heightSmoothing = 10f;

        private double _displayTerrainH;
        private double _targetTerrainH;
        private bool _hasSample;
        private double _lastSampleLat;
        private double _lastSampleLng;
        private bool _hasLastSamplePos;
        private bool _sampling;
        private double _pendingLat;
        private double _pendingLng;
        private bool _hasPending;

        public static RtgTerrainHeight FindOrCreate()
        {
#if UNITY_2023_1_OR_NEWER
            RtgTerrainHeight existing = UnityEngine.Object.FindFirstObjectByType<RtgTerrainHeight>();
#else
            RtgTerrainHeight existing = UnityEngine.Object.FindObjectOfType<RtgTerrainHeight>();
#endif
            if (existing != null) return existing;

#if UNITY_2023_1_OR_NEWER
            Cesium3DTileset tileset = UnityEngine.Object.FindFirstObjectByType<Cesium3DTileset>();
#else
            Cesium3DTileset tileset = UnityEngine.Object.FindObjectOfType<Cesium3DTileset>();
#endif
            if (tileset == null) return null;

            return tileset.gameObject.AddComponent<RtgTerrainHeight>();
        }

        private void Awake()
        {
            if (terrainTileset == null)
                terrainTileset = GetComponent<Cesium3DTileset>();

            _displayTerrainH = fallbackHeightMeters;
            _targetTerrainH = fallbackHeightMeters;
        }

        public void Configure(double fallbackMeters, float markerOffset)
        {
            fallbackHeightMeters = fallbackMeters;
            markerOffsetMeters = markerOffset;
            if (!_hasSample)
            {
                _displayTerrainH = fallbackHeightMeters;
                _targetTerrainH = fallbackHeightMeters;
            }
        }

        /// <summary>Ellipsoid height for CesiumGlobeAnchor (terrain + marker offset).</summary>
        public double GetPlacementHeight(double lat, double lng) =>
            GetGroundHeight(lat, lng) + markerOffsetMeters;

        /// <summary>Sampled terrain height (m above ellipsoid), without marker offset.</summary>
        public double GetGroundHeight(double lat, double lng)
        {
            QueueSampleIfNeeded(lat, lng);
            return _hasSample ? _displayTerrainH : fallbackHeightMeters;
        }

        public void QueueSampleIfNeeded(double lat, double lng)
        {
            if (terrainTileset == null) return;

            if (!_hasLastSamplePos || DistanceMeters(_lastSampleLat, _lastSampleLng, lat, lng) >= resampleMoveMeters)
            {
                _pendingLat = lat;
                _pendingLng = lng;
                _hasPending = true;
                _lastSampleLat = lat;
                _lastSampleLng = lng;
                _hasLastSamplePos = true;

                if (!_sampling)
                    StartCoroutine(SampleLoop());
            }
        }

        private void Update()
        {
            if (!_hasSample) return;

            double t = heightSmoothing > 0f
                ? 1.0 - Math.Exp(-heightSmoothing * Time.deltaTime)
                : 1.0;
            _displayTerrainH += (_targetTerrainH - _displayTerrainH) * t;
        }

        private IEnumerator SampleLoop()
        {
            _sampling = true;

            while (_hasPending)
            {
                double lat = _pendingLat;
                double lng = _pendingLng;
                _hasPending = false;

                yield return SampleAt(lat, lng);
            }

            _sampling = false;
        }

        private IEnumerator SampleAt(double lat, double lng)
        {
            if (terrainTileset == null) yield break;

            // Cesium: X = longitude (deg), Y = latitude (deg), Z = height (ignored).
            var positions = new[] { new double3(lng, lat, 0.0) };

            Task<CesiumSampleHeightResult> task =
                terrainTileset.SampleHeightMostDetailed(positions);

            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning($"[RTG] Terrain sample failed at {lat:F5}, {lng:F5}: {task.Exception?.Message}");
                yield break;
            }

            CesiumSampleHeightResult result = task.Result;
            if (result.longitudeLatitudeHeightPositions == null ||
                result.longitudeLatitudeHeightPositions.Length == 0)
            {
                Debug.LogWarning($"[RTG] Terrain sample returned no data at {lat:F5}, {lng:F5}.");
                yield break;
            }

            double sampled = result.longitudeLatitudeHeightPositions[0].z;
            if (double.IsNaN(sampled) || double.IsInfinity(sampled))
                yield break;

            _targetTerrainH = sampled;
            if (!_hasSample)
                _displayTerrainH = sampled;
            _hasSample = true;
        }

        private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            double avgLatRad = (lat1 + lat2) * 0.5 * Math.PI / 180.0;
            double metersPerDegLng = MetersPerDegreeLat * Math.Cos(avgLatRad);
            double dLat = (lat2 - lat1) * MetersPerDegreeLat;
            double dLng = (lng2 - lng1) * metersPerDegLng;
            return Math.Sqrt(dLat * dLat + dLng * dLng);
        }
    }
}
