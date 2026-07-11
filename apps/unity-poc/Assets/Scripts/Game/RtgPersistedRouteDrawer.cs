using System.Collections;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.Networking;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Renders persisted routes from GET /worlds/:worldId/map (saved Light Roads
    /// and tap-to-connect gold connectors). Distinct from the live cyan trail on
    /// <see cref="RtgLightRoad"/>, which only shows the current recording leg.
    /// </summary>
    public class RtgPersistedRouteDrawer : MonoBehaviour
    {
        [Tooltip("Ellipsoid height fallback when terrain is not sampled yet.")]
        public double groundHeightMeters = 1476.0;

        [Tooltip("Meters above terrain for saved travel legs (below the live Light Road).")]
        public float travelHeightAboveTerrainM = 2f;

        [Tooltip("Meters above terrain for tap-claim connector lines.")]
        public float connectorHeightAboveTerrainM = 7f;

        [Header("Travel routes (multi-point GPS legs)")]
        public Color travelRouteColor = new Color(0.30f, 0.75f, 0.95f, 0.85f);
        public float travelWidthMeters = 7f;

        [Header("Connector routes (two-point tap claims)")]
        public Color connectorColor = new Color(1.00f, 0.78f, 0.22f, 0.95f);
        public float connectorWidthMeters = 6f;

        private Transform _geoRoot;
        private RtgTerrainHeight _terrainHeight;
        private int _lineIndex;

        public static RtgPersistedRouteDrawer FindOrCreate()
        {
#if UNITY_2023_1_OR_NEWER
            var existing = Object.FindFirstObjectByType<RtgPersistedRouteDrawer>();
#else
            var existing = Object.FindObjectOfType<RtgPersistedRouteDrawer>();
#endif
            if (existing != null) return existing;

            CesiumGeoreference geo = Object.FindObjectOfType<CesiumGeoreference>();
            if (geo == null) return null;

            var go = new GameObject("RTG Persisted Routes");
            go.transform.SetParent(geo.transform, false);
            return go.AddComponent<RtgPersistedRouteDrawer>();
        }

        private void Awake()
        {
            _geoRoot = transform.parent != null ? transform.parent : transform;
            _terrainHeight = RtgTerrainHeight.FindOrCreate();
        }

        /// <summary>Replace all drawn routes with the given map snapshot.</summary>
        public void DrawAll(RtgRoute[] routes)
        {
            Clear();
            if (routes == null) return;

            int drawn = 0;
            foreach (RtgRoute route in routes)
            {
                if (route?.path_json == null || route.path_json.Length < 2) continue;
                if (!string.IsNullOrEmpty(route.status) && route.status != "active") continue;
                DrawRoute(route);
                drawn++;
            }

            if (drawn > 0)
                Debug.Log($"[RTG] Drew {drawn} persisted route(s) from map.");
        }

        /// <summary>Re-fetch routes from the API and redraw (after connect / claim).</summary>
        public IEnumerator RefreshFromApi(string apiBaseUrl, string worldId, string playerEmpireId = null)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(worldId))
                yield break;

            string url = $"{apiBaseUrl.TrimEnd('/')}/worlds/{worldId}/map";
            using UnityWebRequest req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[RTG] Route refresh failed: {req.responseCode} {req.error}");
                yield break;
            }

            RtgWorldMap map = JsonUtility.FromJson<RtgWorldMap>(req.downloadHandler.text);
            if (map?.routes != null)
                DrawAll(map.routes);
            if (!string.IsNullOrWhiteSpace(playerEmpireId))
                RtgMapConnections.Apply(map, playerEmpireId);
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyObject(transform.GetChild(i).gameObject);
            _lineIndex = 0;
        }

        private void DrawRoute(RtgRoute route)
        {
            bool isConnector = route.path_json.Length == 2;
            Color color = isConnector
                ? connectorColor
                : TryParseHexColor(route.empire_color, travelRouteColor);
            float width = isConnector ? connectorWidthMeters : travelWidthMeters;
            float heightOffset = isConnector ? connectorHeightAboveTerrainM : travelHeightAboveTerrainM;

            var positions = new Vector3[route.path_json.Length];
            for (int i = 0; i < route.path_json.Length; i++)
            {
                RtgPathPoint p = route.path_json[i];
                positions[i] = WorldFromLatLng(p.lng, p.lat, heightOffset);
            }

            var go = new GameObject(isConnector ? $"Connector {_lineIndex}" : $"Route {_lineIndex}");
            _lineIndex++;
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = positions.Length;
            line.SetPositions(positions);
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = isConnector ? 2 : 0;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = isConnector ? "RTG_ConnectorPersisted" : "RTG_RoutePersisted" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            line.material = mat;

            if (!isConnector)
            {
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(color, 0f),
                        new GradientColorKey(color, 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(color.a * 0.45f, 0f),
                        new GradientAlphaKey(color.a, 1f),
                    });
                line.colorGradient = gradient;
            }
        }

        private static Color TryParseHexColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            hex = hex.Trim();
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length != 6 && hex.Length != 8) return fallback;

            if (!byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r))
                return fallback;
            if (!byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g))
                return fallback;
            if (!byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                return fallback;

            float a = 1f;
            if (hex.Length == 8 &&
                byte.TryParse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out byte aByte))
            {
                a = aByte / 255f;
            }

            return new Color(r / 255f, g / 255f, b / 255f, a);
        }

        private Vector3 WorldFromLatLng(double lng, double lat, float heightAboveTerrainM)
        {
            if (_geoRoot == null) _geoRoot = transform.parent;
            if (_terrainHeight == null)
                _terrainHeight = RtgTerrainHeight.FindOrCreate();

            double heightM = _terrainHeight != null
                ? _terrainHeight.GetGroundHeight(lat, lng) + heightAboveTerrainM
                : groundHeightMeters + heightAboveTerrainM;

            var probe = new GameObject("_geo_probe");
            probe.transform.SetParent(_geoRoot, false);
            var anchor = probe.AddComponent<CesiumGlobeAnchor>();
            anchor.SetPositionLongitudeLatitudeHeight(lng, lat, heightM);
            Vector3 world = probe.transform.position;
            if (Application.isPlaying) Destroy(probe);
            else DestroyImmediate(probe);
            return world;
        }

        private static void DestroyObject(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
