using System;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Glowing Light Road trail behind the player.
    ///
    /// <para><b>Regression guardrails</b> (see <see cref="RtgTerrainElevationGuards"/>):</para>
    /// <list type="bullet">
    /// <item>New points: <see cref="ResolveGroundForNewPoint"/> → read-only glider floor +
    /// cached Cesium at that lat/lng.</item>
    /// <item>Older points: <see cref="LiftStoredGroundForPoint"/> — lift-only via cached samples;
    /// never lower stored heights.</item>
    /// <item>Do NOT call <see cref="RtgTerrainHeight.EvaluateCorridorGroundHeight"/>,
    /// <see cref="RtgTerrainHeight.GetGroundHeight"/> per frame, or periodically reproject
    /// the full trail from raw heights (caused bounce + hill clipping).</item>
    /// <item>Do NOT run before player terrain pass — keep
    /// <see cref="RtgTerrainElevationGuards.LightRoadExecutionOrder"/>.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    [DefaultExecutionOrder(RtgTerrainElevationGuards.LightRoadExecutionOrder)]
    public class RtgLightRoad : MonoBehaviour
    {
        [Tooltip("Transform whose world position is traced (the player marker).")]
        public Transform target;

        [Tooltip("Only record a new point after the target has moved this many meters.")]
        public float pointSpacingMeters = 12f;

        [Tooltip("Road width in meters.")]
        public float widthMeters = 8f;

        [Tooltip("Meters above committed corridor ground for each road point.")]
        public float roadClearanceMeters = 3f;

        [Tooltip("Cap on recorded points; oldest are dropped past this so the road stays bounded.")]
        public int maxPoints = 8000;

        [Tooltip("How often (s) to re-check older trail points against cached terrain (lift only). " +
                 "Lowering intervals reprojects often — keep lift-only logic if you change this.")]
        public float trailLiftIntervalSeconds = 0.5f;

        [Tooltip("Glowing road color (bright reads as energy against the dark alien map).")]
        public Color roadColor = new Color(0.45f, 0.95f, 1.00f);

        private bool _recording;

        private LineRenderer _line;
        private RtgTerrainHeight _terrainHeight;
        private CesiumGlobeAnchor _targetAnchor;
        private CesiumGlobeAnchor _probeAnchor;
        private readonly List<GeoPoint> _points = new();
        private bool _hasLast;
        private Vector3 _lastWorld;
        private float _travelHeadingRad;
        private float _nextTrailLiftTime;

        private struct GeoPoint
        {
            public double Lat;
            public double Lng;
            public double GroundHeightM;
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

        public void StartRecording()
        {
            _recording = true;
        }

        public void ClearRoad()
        {
            _points.Clear();
            _hasLast = false;
            if (_line != null) _line.positionCount = 0;
        }

        /// <summary>Heading for corridor sample queue (glider updates committed height separately).</summary>
        public void SetMovementContext(float headingRad, float groundSpeedMps)
        {
            _travelHeadingRad = headingRad;
            // groundSpeedMps intentionally unused — corridor min-distance is applied on glider only.
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
            Vector3 worldPos = WorldPosAt(lat, lng, ResolveGroundForNewPoint(lat, lng));

            if (_terrainHeight != null)
            {
                _terrainHeight.QueueSampleIfNeeded(lat, lng);
                _terrainHeight.QueueCorridorSamplesIfNeeded(lat, lng, _travelHeadingRad);
            }

            if (!_hasLast)
            {
                AppendGeoPoint(lat, lng);
                _lastWorld = worldPos;
                _hasLast = true;
                SyncLineToPoints();
                return;
            }

            if (Vector3.Distance(worldPos, _lastWorld) >= Mathf.Max(0.5f, pointSpacingMeters))
            {
                AppendGeoPoint(lat, lng);
                _lastWorld = worldPos;
                SyncLineToPoints();
            }
            else
            {
                LiftLastPointIfNeeded();
            }

            MaybeRefreshTrailLift();
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

        /// <summary>
        /// Ground for a new trail point at the player. REGRESSION: read-only glider floor +
        /// cached Cesium — not EvaluateCorridorGroundHeight or GetGroundHeight.
        /// </summary>
        private double ResolveGroundForNewPoint(double lat, double lng)
        {
            if (_terrainHeight == null)
                return 0;

            double ground = _terrainHeight.GetTrailReferenceGroundHeight();
            if (_terrainHeight.TryGetCachedGroundHeight(lat, lng, out double cached))
                ground = Math.Max(ground, cached);

            return ground;
        }

        /// <summary>
        /// Lift older points using cached terrain at each point. REGRESSION: never lower —
        /// use <see cref="RtgTerrainElevationGuards.LiftGroundMonotonic"/> only.
        /// </summary>
        private double LiftStoredGroundForPoint(double lat, double lng, double storedGroundM)
        {
            if (_terrainHeight == null)
                return storedGroundM;

            if (!_terrainHeight.TryGetCachedGroundHeight(lat, lng, out double cached))
                return storedGroundM;

            return RtgTerrainElevationGuards.LiftGroundMonotonic(storedGroundM, cached);
        }

        private void AppendGeoPoint(double lat, double lng)
        {
            double ground = ResolveGroundForNewPoint(lat, lng);
            _points.Add(new GeoPoint { Lat = lat, Lng = lng, GroundHeightM = ground });
            if (_points.Count > Mathf.Max(2, maxPoints))
                _points.RemoveAt(0);
        }

        private void LiftLastPointIfNeeded()
        {
            if (_points.Count == 0 || _line == null || _probeAnchor == null) return;

            int last = _points.Count - 1;
            GeoPoint p = _points[last];
            double before = p.GroundHeightM;
            double ground = ResolveGroundForNewPoint(p.Lat, p.Lng);
            double lifted = RtgTerrainElevationGuards.LiftGroundMonotonic(before, ground);
            if (lifted <= before + RtgTerrainElevationGuards.LiftEpsilonM)
                return;

            p.GroundHeightM = lifted;
            _points[last] = p;
            _line.SetPosition(last, WorldPosAt(p.Lat, p.Lng, p.GroundHeightM));
        }

        private void MaybeRefreshTrailLift()
        {
            if (_points.Count < 2 || _terrainHeight == null) return;
            if (Time.time < _nextTrailLiftTime) return;

            _nextTrailLiftTime = Time.time + Mathf.Max(0.1f, trailLiftIntervalSeconds);
            bool changed = false;

            int last = _points.Count - 1;
            for (int i = 0; i < last; i++)
            {
                GeoPoint p = _points[i];
                double before = p.GroundHeightM;
                double lifted = LiftStoredGroundForPoint(p.Lat, p.Lng, p.GroundHeightM);
                RtgTerrainElevationGuards.WarnIfTrailGroundLowered(before, lifted, $"point {i}");
                if (lifted <= before + RtgTerrainElevationGuards.LiftEpsilonM)
                    continue;

                p.GroundHeightM = lifted;
                _points[i] = p;
                changed = true;
            }

            if (changed)
                SyncLineToPoints();
        }

        /// <summary>
        /// Writes line vertices from stored geo points. REGRESSION: no full-trail raw height refresh.
        /// </summary>
        private void SyncLineToPoints()
        {
            if (_line == null || _probeAnchor == null) return;

            int count = _points.Count;
            _line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                GeoPoint p = _points[i];
                double before = p.GroundHeightM;
                double ground = i == count - 1
                    ? ResolveGroundForNewPoint(p.Lat, p.Lng)
                    : LiftStoredGroundForPoint(p.Lat, p.Lng, p.GroundHeightM);

                double lifted = RtgTerrainElevationGuards.LiftGroundMonotonic(before, ground);
                RtgTerrainElevationGuards.WarnIfTrailGroundLowered(before, lifted, $"sync point {i}");
                if (lifted > before + RtgTerrainElevationGuards.LiftEpsilonM)
                {
                    p.GroundHeightM = lifted;
                    _points[i] = p;
                }

                _line.SetPosition(i, WorldPosAt(p.Lat, p.Lng, p.GroundHeightM));
            }
        }

        private Vector3 WorldPosAt(double lat, double lng, double groundHeightM)
        {
            double heightM = groundHeightM + roadClearanceMeters;
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
