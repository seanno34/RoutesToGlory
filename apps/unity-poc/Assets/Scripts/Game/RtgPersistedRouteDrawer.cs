using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.Networking;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Renders persisted routes from GET /worlds/:worldId/map. Each vertex sits on
    /// its own <see cref="CesiumGlobeAnchor"/> so lines follow the globe curvature
    /// and stay aligned with the live Light Road (no flat ENU drift).
    /// </summary>
    public class RtgPersistedRouteDrawer : MonoBehaviour
    {
        [Tooltip("Ellipsoid height fallback when terrain is not sampled yet.")]
        public double groundHeightMeters = 1476.0;

        [Tooltip("Meters above terrain for saved travel legs (below the live Light Road).")]
        public float travelHeightAboveTerrainM = 3f;

        [Tooltip("Meters above terrain for tap-claim connector lines.")]
        public float connectorHeightAboveTerrainM = 7f;

        [Header("Travel routes (multi-point GPS legs)")]
        public Color travelRouteColor = new Color(0.30f, 0.75f, 0.95f, 0.85f);
        public float travelWidthMeters = 7f;

        [Header("Connector routes (two-point tap claims)")]
        public Color connectorColor = new Color(1.00f, 0.78f, 0.22f, 0.95f);
        public float connectorWidthMeters = 6f;

        private Material _travelMat;
        private Material _connectorMat;
        private readonly Dictionary<string, RouteLine> _lines = new();
        private int _anonymousLineIndex;
        private int _refreshGeneration;

        private sealed class RouteLine
        {
            public GameObject root;
            public LineRenderer line;
            public Transform[] anchorTransforms;
            public Vector3[] positionBuffer;
            public ulong pathFingerprint;
            public bool isConnector;

            public void SyncLineToAnchors()
            {
                if (line == null || anchorTransforms == null || positionBuffer == null) return;
                int count = anchorTransforms.Length;
                if (positionBuffer.Length != count)
                    positionBuffer = new Vector3[count];

                for (int i = 0; i < count; i++)
                    positionBuffer[i] = anchorTransforms[i].position;

                line.positionCount = count;
                line.SetPositions(positionBuffer);
            }
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
            EnsureMaterials();
        }

        private void LateUpdate()
        {
            if (_lines.Count == 0 || (Time.frameCount & 1) != 0) return;

            foreach (RouteLine entry in _lines.Values)
                entry.SyncLineToAnchors();
        }

        public void DrawAll(RtgRoute[] routes)
        {
            SyncRoutes(routes);
        }

        public void SyncRoutes(RtgRoute[] routes)
        {
            ++_refreshGeneration;
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

            foreach (RouteLine entry in _lines.Values)
                entry.SyncLineToAnchors();

            if (drawn > 0)
                Debug.Log($"[RTG] Synced {drawn} persisted route(s).");
            else if (routes != null && routes.Length > 0)
                Debug.LogWarning($"[RTG] Map returned {routes.Length} route(s) but none were drawable (check path_json / status).");
        }

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
            UpsertRoute(route, forceConnector: true);
        }

        public IEnumerator RefreshFromApi(string apiBaseUrl, string worldId, string playerEmpireId = null)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(worldId))
                yield break;

            int generation = ++_refreshGeneration;
            string url = $"{apiBaseUrl.TrimEnd('/')}/worlds/{worldId}/map";
            using UnityWebRequest req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (generation != _refreshGeneration)
                yield break;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[RTG] Route refresh failed: {req.responseCode} {req.error}");
                yield break;
            }

            RtgWorldMap map = JsonUtility.FromJson<RtgWorldMap>(req.downloadHandler.text);
            if (map?.routes != null)
            {
                ApplyRouteSnapshotToLoader(map.routes);
                SyncRoutes(map.routes);
            }
            if (!string.IsNullOrWhiteSpace(playerEmpireId))
                RtgMapConnections.Apply(map, playerEmpireId);
        }

        private static void ApplyRouteSnapshotToLoader(RtgRoute[] routes)
        {
#if UNITY_2023_1_OR_NEWER
            RtgEchoSiteLoader loader = Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
            RtgEchoSiteLoader loader = Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
            loader?.ApplyRouteSnapshot(routes);

#if UNITY_2023_1_OR_NEWER
            RtgRouteSession session = Object.FindFirstObjectByType<RtgRouteSession>();
#else
            RtgRouteSession session = Object.FindObjectOfType<RtgRouteSession>();
#endif
            session?.InvalidateSnapCache();
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

        /// <summary>Gold connectors are tap-claim lines — not short 2-point travel spurs.</summary>
        private static bool IsConnectorRoute(RtgRoute route)
        {
            if (route?.path_json == null || route.path_json.Length != 2) return false;
            return !string.IsNullOrEmpty(route.from_settlement_id)
                && !string.IsNullOrEmpty(route.to_settlement_id);
        }

        private void UpsertRoute(RtgRoute route, bool forceConnector = false)
        {
            bool isConnector = forceConnector || IsConnectorRoute(route);
            RtgPathPoint[] displayPath = isConnector
                ? route.path_json
                : RtgRoutePathUtil.DecimatePathPointsForDisplay(route.path_json);
            ulong fingerprint = PathFingerprint(displayPath);

            if (_lines.TryGetValue(route.id, out RouteLine existing))
            {
                if (existing.pathFingerprint == fingerprint)
                    return;
                RemoveRoute(route.id);
            }

            RouteLine created = CreateRouteLine(route.id, displayPath, isConnector, route.empire_color, fingerprint);
            created.SyncLineToAnchors();
            _lines[route.id] = created;
        }

        private static ulong PathFingerprint(RtgPathPoint[] path)
        {
            if (path == null || path.Length == 0) return 0;

            unchecked
            {
                ulong hash = (ulong)path.Length;
                hash = hash * 31 + (ulong)System.BitConverter.DoubleToInt64Bits(path[0].lat);
                hash = hash * 31 + (ulong)System.BitConverter.DoubleToInt64Bits(path[0].lng);

                int mid = path.Length / 2;
                hash = hash * 31 + (ulong)System.BitConverter.DoubleToInt64Bits(path[mid].lat);
                hash = hash * 31 + (ulong)System.BitConverter.DoubleToInt64Bits(path[mid].lng);

                int last = path.Length - 1;
                hash = hash * 31 + (ulong)System.BitConverter.DoubleToInt64Bits(path[last].lat);
                hash = hash * 31 + (ulong)System.BitConverter.DoubleToInt64Bits(path[last].lng);
                return hash;
            }
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
            string empireColorHex,
            ulong fingerprint)
        {
            Color color = isConnector
                ? connectorColor
                : TryParseHexColor(empireColorHex, travelRouteColor);
            float width = isConnector ? connectorWidthMeters : travelWidthMeters;
            float heightOffset = isConnector ? connectorHeightAboveTerrainM : travelHeightAboveTerrainM;
            double pointHeightM = groundHeightMeters + heightOffset;

            var go = new GameObject(isConnector ? $"Connector {routeId}" : $"Route {routeId}");
            if (isConnector && routeId.StartsWith("connector-"))
                _anonymousLineIndex++;

            go.transform.SetParent(transform, false);

            var anchorTransforms = new Transform[displayPath.Length];
            for (int i = 0; i < displayPath.Length; i++)
            {
                var anchorGo = new GameObject($"p{i}");
                anchorGo.hideFlags = HideFlags.HideInHierarchy;
                anchorGo.transform.SetParent(go.transform, false);

                CesiumGlobeAnchor anchor = anchorGo.AddComponent<CesiumGlobeAnchor>();
                anchor.SetPositionLongitudeLatitudeHeight(
                    displayPath[i].lng,
                    displayPath[i].lat,
                    pointHeightM);

                anchorTransforms[i] = anchorGo.transform;
            }

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = displayPath.Length;
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = isConnector ? 2 : 1;
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
                anchorTransforms = anchorTransforms,
                positionBuffer = new Vector3[displayPath.Length],
                pathFingerprint = fingerprint,
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
