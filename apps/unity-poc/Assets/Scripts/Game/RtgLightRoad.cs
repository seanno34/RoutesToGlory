using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Draws a glowing "Light Road" polyline that grows behind a moving target
    /// (the player marker). Points are stored as lat/lng and re-projected onto
    /// Cesium terrain each frame so the ribbon stays above hills as samples arrive.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class RtgLightRoad : MonoBehaviour
    {
        [Tooltip("Transform whose world position is traced (the player marker).")]
        public Transform target;

        [Tooltip("Only record a new point after the target has moved this many meters. Smaller = smoother, more points.")]
        public float pointSpacingMeters = 12f;

        [Tooltip("Road width in meters.")]
        public float widthMeters = 8f;

        [Tooltip("Meters above sampled terrain for each road point.")]
        public float roadClearanceMeters = 3f;

        [Tooltip("Cap on recorded points; oldest are dropped past this so the road stays bounded.")]
        public int maxPoints = 8000;

        [Tooltip("Glowing road color (bright reads as energy against the dark alien map).")]
        public Color roadColor = new Color(0.45f, 0.95f, 1.00f);

        [Tooltip("How often to re-project all trail points onto terrain (seconds).")]
        public float terrainRefreshIntervalSeconds = 0.12f;

        // Recording is off until the owner confirms a real position fix, so we
        // never draw a stray segment from the origin to the first GPS point.
        private bool _recording;

        private LineRenderer _line;
        private RtgTerrainHeight _terrainHeight;
        private CesiumGlobeAnchor _targetAnchor;
        private CesiumGlobeAnchor _probeAnchor;
        private readonly List<GeoPoint> _points = new();
        private bool _hasLast;
        private Vector3 _lastWorld;
        private float _nextTerrainRefreshTime;

        private struct GeoPoint
        {
            public double Lat;
            public double Lng;
        }

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            ConfigureLine();
        }

        private void OnEnable()
        {
            EnsureTerrainRefs();
        }

        /// <summary>Begin tracing (call once the target holds a valid position).</summary>
        public void StartRecording()
        {
            _recording = true;
        }

        /// <summary>Clear the road (e.g. when a route session ends).</summary>
        public void ClearRoad()
        {
            _points.Clear();
            _hasLast = false;
            if (_line != null) _line.positionCount = 0;
        }

        private void LateUpdate()
        {
            if (!_recording || target == null) return;

            EnsureTerrainRefs();
            if (_targetAnchor == null)
                _targetAnchor = target.GetComponent<CesiumGlobeAnchor>();
            if (_targetAnchor == null) return;

            double lat = _targetAnchor.latitude;
            double lng = _targetAnchor.longitude;
            Vector3 worldPos = WorldPosAt(lat, lng);

            if (_terrainHeight != null)
                _terrainHeight.QueueSampleIfNeeded(lat, lng);

            bool appended = false;
            if (!_hasLast)
            {
                AppendGeoPoint(lat, lng);
                _lastWorld = worldPos;
                _hasLast = true;
                appended = true;
            }
            else if (Vector3.Distance(worldPos, _lastWorld) >= Mathf.Max(0.5f, pointSpacingMeters))
            {
                AppendGeoPoint(lat, lng);
                _lastWorld = worldPos;
                appended = true;
            }

            if (appended || Time.time >= _nextTerrainRefreshTime)
            {
                RefreshLinePositions();
                _nextTerrainRefreshTime = Time.time + Mathf.Max(0.03f, terrainRefreshIntervalSeconds);
            }
        }

        private void EnsureTerrainRefs()
        {
            if (_terrainHeight == null)
                _terrainHeight = RtgTerrainHeight.FindOrCreate();

            if (_probeAnchor != null) return;

            var probeGo = new GameObject("RTG_RoadProbe");
            probeGo.hideFlags = HideFlags.HideInHierarchy;
            probeGo.transform.SetParent(transform, false);
            _probeAnchor = probeGo.AddComponent<CesiumGlobeAnchor>();
        }

        private void AppendGeoPoint(double lat, double lng)
        {
            _points.Add(new GeoPoint { Lat = lat, Lng = lng });
            if (_points.Count > Mathf.Max(2, maxPoints))
                _points.RemoveAt(0);
        }

        private void RefreshLinePositions()
        {
            if (_line == null || _probeAnchor == null) return;

            int count = _points.Count;
            if (count == 0)
            {
                _line.positionCount = 0;
                return;
            }

            _line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                GeoPoint p = _points[i];
                if (_terrainHeight != null)
                    _terrainHeight.QueueSampleIfNeeded(p.Lat, p.Lng);
                _line.SetPosition(i, WorldPosAt(p.Lat, p.Lng));
            }
        }

        private Vector3 WorldPosAt(double lat, double lng)
        {
            double heightM = roadClearanceMeters;
            if (_terrainHeight != null)
                heightM = _terrainHeight.GetGroundHeight(lat, lng) + roadClearanceMeters;

            _probeAnchor.SetPositionLongitudeLatitudeHeight(lng, lat, heightM);
            return _probeAnchor.transform.position;
        }

        private void ConfigureLine()
        {
            _line.useWorldSpace = true;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.numCapVertices = 4;
            _line.numCornerVertices = 4;
            _line.widthMultiplier = widthMeters;
            _line.positionCount = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sortingOrder = 1;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "RTG_LightRoad" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", roadColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", roadColor);
            _line.material = mat;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(roadColor, 0f),
                    new GradientColorKey(roadColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.35f, 0f),
                    new GradientAlphaKey(1.0f, 1f),
                });
            _line.colorGradient = gradient;
        }
    }
}
