using System.Collections.Generic;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Draws a glowing "Light Road" polyline that grows behind a moving target
    /// (the player marker). This is the game's signature mechanic made visible:
    /// real-world movement lays down a route. It samples the target's world
    /// position whenever it has moved at least <see cref="pointSpacingMeters"/>,
    /// so the trail is smooth regardless of frame rate or travel speed.
    ///
    /// The trail lives in world space, which is stable while the follow camera is
    /// active (that path disables Cesium's origin shift). For free-fly with origin
    /// shift enabled, a future version would re-anchor points via the georeference.
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

        [Tooltip("Meters to raise/lower each sampled point (the marker floats above ground, so this drops the road toward it).")]
        public float verticalOffset = 0f;

        [Tooltip("Cap on recorded points; oldest are dropped past this so the road stays bounded.")]
        public int maxPoints = 8000;

        [Tooltip("Glowing road color (bright reads as energy against the dark alien map).")]
        public Color roadColor = new Color(0.45f, 0.95f, 1.00f);

        // Recording is off until the owner confirms a real position fix, so we
        // never draw a stray segment from the origin to the first GPS point.
        private bool _recording;

        private LineRenderer _line;
        private readonly List<Vector3> _points = new();
        private bool _hasLast;
        private Vector3 _last;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            ConfigureLine();
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

            Vector3 pos = target.position + Vector3.up * verticalOffset;

            if (!_hasLast)
            {
                AppendPoint(pos);
                _last = pos;
                _hasLast = true;
                return;
            }

            if (Vector3.Distance(pos, _last) >= Mathf.Max(0.5f, pointSpacingMeters))
            {
                AppendPoint(pos);
                _last = pos;
            }
        }

        private void AppendPoint(Vector3 worldPoint)
        {
            _points.Add(worldPoint);
            if (_points.Count > Mathf.Max(2, maxPoints))
                _points.RemoveAt(0);

            // Point additions are infrequent (only every pointSpacingMeters of
            // travel), so rewriting the whole buffer here is cheap.
            _line.positionCount = _points.Count;
            _line.SetPositions(_points.ToArray());
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

            // Unlit so the road reads as a constant bright energy ribbon regardless
            // of the scene's alien lighting.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "RTG_LightRoad" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", roadColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", roadColor);
            _line.material = mat;

            // Slight fade along the trail: brightest at the head (player), softer tail.
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
