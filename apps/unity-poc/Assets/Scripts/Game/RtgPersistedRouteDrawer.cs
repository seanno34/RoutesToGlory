using System.Collections;
using System.Collections.Generic;
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
        private RtgGeoWorld _geoWorld;
        private Material _travelMat;
        private Material _connectorMat;
        private readonly Dictionary<string, RouteLine> _lines = new();
        private int _anonymousLineIndex;

        private sealed class RouteLine
        {
            public GameObject root;
            public LineRenderer line;
            public int pointCount;
            public bool isConnector;
        }

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
            _geoWorld = new RtgGeoWorld(_geoRoot, _terrainHeight, groundHeightMeters);
            EnsureMaterials();
        }

        private void OnDestroy()
        {
            _geoWorld?.Dispose();
        }

        /// <summary>Replace all drawn routes with the given map snapshot.</summary>
        public void DrawAll(RtgRoute[] routes)
        {
            SyncRoutes(routes);
        }

        /// <summary>Incrementally add/update/remove routes without clearing the whole network.</summary>
        public void SyncRoutes(RtgRoute[] routes)
        {
            var seen = new HashSet<string>();
            int drawn = 0;

            if (routes != null)
            {
                foreach (RtgRoute route in routes)
                {
                    if (!IsDrawable(route)) continue;
                    seen.Add(route.id);
                    UpsertRoute(route);
                    drawn++;
                }
            }

            var stale = new List<string>();
            foreach (KeyValuePair<string, RouteLine> kv in _lines)
            {
                if (!seen.Contains(kv.Key))
                    stale.Add(kv.Key);
            }
            foreach (string id in stale)
                RemoveRoute(id);

            if (drawn > 0)
                Debug.Log($"[RTG] Synced {drawn} persisted route(s).");
        }

        /// <summary>Draw a single connector returned by POST /claim without refetching the full map.</summary>
        public void AppendConnector(
            string routeId,
            double anchorLat,
            double anchorLng,
            double targetLat,
            double targetLng)
        {
            var route = new RtgRoute
            {
                id = string.IsNullOrEmpty(routeId) ? $"connector-{_anonymousLineIndex}" : routeId,
                path_json = new[]
                {
                    new RtgPathPoint { lat = anchorLat, lng = anchorLng },
                    new RtgPathPoint { lat = targetLat, lng = targetLng },
                },
            };
            UpsertRoute(route);
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
                SyncRoutes(map.routes);
            if (!string.IsNullOrWhiteSpace(playerEmpireId))
                RtgMapConnections.Apply(map, playerEmpireId);
        }

        public void Clear()
        {
            foreach (KeyValuePair<string, RouteLine> kv in _lines)
            {
                if (kv.Value?.root != null)
                    DestroyObject(kv.Value.root);
            }
            _lines.Clear();
            _anonymousLineIndex = 0;
        }

        private static bool IsDrawable(RtgRoute route)
        {
            if (route?.path_json == null || route.path_json.Length < 2) return false;
            return string.IsNullOrEmpty(route.status) || route.status == "active";
        }

        private void UpsertRoute(RtgRoute route)
        {
            bool isConnector = route.path_json.Length == 2;
            RtgPathPoint[] displayPath = isConnector
                ? route.path_json
                : RtgRoutePathUtil.DecimatePathPointsForDisplay(route.path_json);

            if (_lines.TryGetValue(route.id, out RouteLine existing))
            {
                if (existing.pointCount == displayPath.Length)
                    return;
                DestroyObject(existing.root);
                _lines.Remove(route.id);
            }

            _lines[route.id] = CreateRouteLine(route.id, displayPath, isConnector, route.empire_color);
        }

        private void RemoveRoute(string routeId)
        {
            if (!_lines.TryGetValue(routeId, out RouteLine entry)) return;
            if (entry.root != null)
                DestroyObject(entry.root);
            _lines.Remove(routeId);
        }

        private RouteLine CreateRouteLine(
            string routeId,
            RtgPathPoint[] displayPath,
            bool isConnector,
            string empireColorHex)
        {
            Color color = isConnector
                ? connectorColor
                : TryParseHexColor(empireColorHex, travelRouteColor);
            float width = isConnector ? connectorWidthMeters : travelWidthMeters;
            float heightOffset = isConnector ? connectorHeightAboveTerrainM : travelHeightAboveTerrainM;

            var positions = new Vector3[displayPath.Length];
            _geoWorld.FillWorldPositions(displayPath, heightOffset, positions);

            var go = new GameObject(isConnector ? $"Connector {routeId}" : $"Route {routeId}");
            if (isConnector && routeId.StartsWith("connector-"))
                _anonymousLineIndex++;

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
            line.material = isConnector ? _connectorMat : _travelMat;

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

            return new RouteLine
            {
                root = go,
                line = line,
                pointCount = displayPath.Length,
                isConnector = isConnector,
            };
        }

        private void EnsureMaterials()
        {
            if (_travelMat != null && _connectorMat != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");

            _travelMat = new Material(shader) { name = "RTG_RoutePersisted" };
            if (_travelMat.HasProperty("_BaseColor")) _travelMat.SetColor("_BaseColor", travelRouteColor);
            if (_travelMat.HasProperty("_Color")) _travelMat.SetColor("_Color", travelRouteColor);

            _connectorMat = new Material(shader) { name = "RTG_ConnectorPersisted" };
            if (_connectorMat.HasProperty("_BaseColor")) _connectorMat.SetColor("_BaseColor", connectorColor);
            if (_connectorMat.HasProperty("_Color")) _connectorMat.SetColor("_Color", connectorColor);
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

        private static void DestroyObject(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
