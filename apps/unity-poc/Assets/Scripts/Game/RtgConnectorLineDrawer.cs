using CesiumForUnity;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Draws persisted connector routes (route anchor → tapped object) after a
    /// successful tap-to-connect claim. Distinct from the cyan Light Road trail.
    /// </summary>
    public class RtgConnectorLineDrawer : MonoBehaviour
    {
        [Tooltip("Height above ellipsoid for connector endpoints (match map markers).")]
        public double groundHeightMeters = 1476.0;

        [Tooltip("Extra meters above ground so the line clears terrain.")]
        public float heightAboveGroundM = 8f;

        public Color connectorColor = new Color(1.00f, 0.78f, 0.22f);
        public float widthMeters = 7f;

        private Transform _geoRoot;

        public static RtgConnectorLineDrawer FindOrCreate()
        {
#if UNITY_2023_1_OR_NEWER
            var existing = Object.FindFirstObjectByType<RtgConnectorLineDrawer>();
#else
            var existing = Object.FindObjectOfType<RtgConnectorLineDrawer>();
#endif
            if (existing != null) return existing;

            CesiumGeoreference geo = Object.FindObjectOfType<CesiumGeoreference>();
            if (geo == null) return null;

            var go = new GameObject("RTG Connector Lines");
            go.transform.SetParent(geo.transform, false);
            return go.AddComponent<RtgConnectorLineDrawer>();
        }

        private void Awake()
        {
            _geoRoot = transform.parent != null ? transform.parent : transform;
        }

        /// <summary>Draw a two-point connector (anchor on route → map object).</summary>
        public void DrawConnector(double anchorLat, double anchorLng, double targetLat, double targetLng)
        {
            if (_geoRoot == null) _geoRoot = transform.parent;

            double h = groundHeightMeters + heightAboveGroundM;
            Vector3 a = WorldFromLatLng(anchorLng, anchorLat, h);
            Vector3 b = WorldFromLatLng(targetLng, targetLat, h);

            var go = new GameObject($"Connector {_lineIndex++}");
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.widthMultiplier = widthMeters;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "RTG_Connector" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", connectorColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", connectorColor);
            line.material = mat;

            Debug.Log($"[RTG] Drew connector line (anchor → target).");
        }

        private int _lineIndex;

        private Vector3 WorldFromLatLng(double lng, double lat, double heightM)
        {
            var probe = new GameObject("_geo_probe");
            probe.transform.SetParent(_geoRoot, false);
            var anchor = probe.AddComponent<CesiumGlobeAnchor>();
            anchor.SetPositionLongitudeLatitudeHeight(lng, lat, heightM);
            Vector3 world = probe.transform.position;
            if (Application.isPlaying) Destroy(probe);
            else DestroyImmediate(probe);
            return world;
        }
    }
}
