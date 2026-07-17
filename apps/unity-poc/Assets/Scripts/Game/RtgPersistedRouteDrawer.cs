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
    ///
    /// <para><b>Heighting</b> — path_json is lat/lng only. Vertices are placed at
    /// Cesium-sampled terrain + a small clearance (same idea as deposits / live
    /// <see cref="RtgLightRoad"/>). Do <b>not</b> use fixed
    /// <c>groundHeightMeters + offset</c> alone — that floats saved routes above the
    /// terrain-following glider whenever real ground is below the Douglas fallback.</para>
    ///
    /// <para><b>Clearance stack</b> (see <see cref="RtgTerrainElevationGuards"/>):
    /// travel / live Light Road ≈ +3 m, connectors ≈ +7 m, glider ≈ +15 m.
    /// Prefer fixing route elevation near terrain over raising the glider.</para>
    /// </summary>
    public class RtgPersistedRouteDrawer : MonoBehaviour
    {
        private const int TerrainSampleBatchSize = 32;
        private const int TerrainAnchorMaxAttempts = 5;
        private const float TerrainAnchorRetryDelaySeconds = 1.25f;

        [Tooltip("Ellipsoid height fallback while Cesium samples are pending (Douglas, WY).")]
        public double groundHeightMeters = 1476.0;

        // Keep travel clearance == live Light Road; glider GliderClearanceM (~15 m) stays above.
        [Tooltip("Meters above sampled terrain for saved travel legs (match live Light Road).")]
        public float travelHeightAboveTerrainM = RtgTerrainElevationGuards.TravelRoadClearanceM;

        [Tooltip("Meters above sampled terrain for tap-claim connector lines.")]
        public float connectorHeightAboveTerrainM = RtgTerrainElevationGuards.ConnectorClearanceM;

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
        private static readonly HashSet<string> _loggedSkippedRouteIds = new();

        private sealed class RouteLine
        {
            public string id;
            public GameObject root;
            public LineRenderer line;
            public Transform[] anchorTransforms;
            public CesiumGlobeAnchor[] anchors;
            public RtgPathPoint[] path;
            public float clearanceM;
            public Vector3[] positionBuffer;
            public ulong pathFingerprint;
            public bool isConnector;
            public int terrainGeneration;

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
            // Scene may contain editor-baked Route/Connector children with null
            // materials (Unity magenta). They are not in _lines — purge them.
            DestroyUnmanagedChildren();
        }

        private void OnDestroy()
        {
            DestroyObject(_travelMat);
            DestroyObject(_connectorMat);
            _travelMat = null;
            _connectorMat = null;
        }

        private void LateUpdate()
        {
            if (_lines.Count == 0 || (Time.frameCount & 1) != 0) return;

            foreach (RouteLine entry in _lines.Values)
            {
                EnsureRouteLineMaterial(entry);
                entry.SyncLineToAnchors();
            }
        }

        public void DrawAll(RtgRoute[] routes)
        {
            SyncRoutes(routes);
        }

        public void SyncRoutes(RtgRoute[] routes)
        {
            ++_refreshGeneration;
            DestroyUnmanagedChildren();
            var seen = new HashSet<string>();
            int drawn = 0;
            int skipped = 0;

            if (routes != null)
            {
                foreach (RtgRoute route in routes)
                {
                    if (!IsDrawable(route))
                    {
                        if (route != null)
                        {
                            skipped++;
                            LogSkippedRouteOnce(route);
                        }
                        continue;
                    }
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
            {
                EnsureRouteLineMaterial(entry);
                entry.SyncLineToAnchors();
            }

            if (drawn > 0)
                Debug.Log($"[RTG] Synced {drawn} persisted route(s).");
            else if (routes != null && routes.Length > 0)
                Debug.LogWarning($"[RTG] Map returned {routes.Length} route(s) but none were drawable (check path_json / status).");
            if (skipped > 0)
                Debug.Log($"[RTG] Skipped {skipped} invalid persisted route(s).");
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
                if (kv.Value != null)
                    kv.Value.terrainGeneration++;
                if (kv.Value?.root != null)
                    DestroyObject(kv.Value.root);
            }
            _lines.Clear();
            _anonymousLineIndex = 0;
            DestroyUnmanagedChildren();
        }

        private static bool IsDrawable(RtgRoute route)
        {
            if (route?.path_json == null || route.path_json.Length < 2) return false;
            if (!(string.IsNullOrEmpty(route.status) || route.status == "active")) return false;
            return HasDistinctPathEndpoints(route.path_json);
        }

        private static bool HasDistinctPathEndpoints(RtgPathPoint[] path)
        {
            if (path == null || path.Length < 2) return false;
            RtgPathPoint a = path[0];
            RtgPathPoint b = path[path.Length - 1];
            return System.Math.Abs(a.lat - b.lat) > 1e-9
                || System.Math.Abs(a.lng - b.lng) > 1e-9;
        }

        private static void LogSkippedRouteOnce(RtgRoute route)
        {
            string id = string.IsNullOrEmpty(route.id) ? "(no-id)" : route.id;
            if (!_loggedSkippedRouteIds.Add(id)) return;

            int pts = route.path_json?.Length ?? 0;
            Debug.Log(
                $"[RTG] Skipping persisted route '{id}' " +
                $"(status={route.status ?? "null"}, pathPts={pts}, " +
                $"from={route.from_settlement_id ?? "null"}, to={route.to_settlement_id ?? "null"}).");
        }

        /// <summary>
        /// Removes LineRenderer children not tracked in <see cref="_lines"/>.
        /// SampleScene historically saved editor play-mode draws
        /// (<c>Route route-sample-leg</c> / <c>Connector route-sample-connector</c>)
        /// with null materials — Unity renders those as magenta orphans.
        /// </summary>
        private void DestroyUnmanagedChildren()
        {
            var tracked = new HashSet<GameObject>();
            foreach (RouteLine entry in _lines.Values)
            {
                if (entry?.root != null)
                    tracked.Add(entry.root);
            }

            int removed = 0;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child == null) continue;
                if (tracked.Contains(child.gameObject)) continue;
                if (child.GetComponent<LineRenderer>() == null) continue;

                removed++;
                Debug.Log(
                    $"[RTG] Destroying unmanaged route child '{child.name}' " +
                    "(scene-baked or leaked LineRenderer — often null-material magenta).");
                DestroyObject(child.gameObject);
            }

            if (removed > 0)
                Debug.Log($"[RTG] Purged {removed} unmanaged persisted route LineRenderer(s).");
        }

        private void EnsureRouteLineMaterial(RouteLine entry)
        {
            if (entry?.line == null) return;
            EnsureMaterials();

            Material want = entry.isConnector ? _connectorMat : _travelMat;
            Color color = entry.isConnector ? connectorColor : travelRouteColor;
            if (want == null) return;

            Material current = entry.line.sharedMaterial;
            if (!IsUsableMaterial(current) || current != want)
                entry.line.sharedMaterial = want;

            ApplyLineColor(entry.line, color, fadeStart: !entry.isConnector);
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

            RouteLine created = CreateRouteLine(route.id, displayPath, isConnector, fingerprint);
            if (created == null) return;
            created.SyncLineToAnchors();
            _lines[route.id] = created;

            if (Application.isPlaying)
                StartCoroutine(AnchorRouteToTerrain(created));
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
            entry.terrainGeneration++;
            if (entry.root != null)
                DestroyObject(entry.root);
            _lines.Remove(routeId);
        }

        private RouteLine CreateRouteLine(
            string routeId,
            RtgPathPoint[] displayPath,
            bool isConnector,
            ulong fingerprint)
        {
            // Intended palette only: cyan travel / gold connector.
            // Do not tint from empire_color — a missing LineRenderer material already
            // reads as Unity magenta and looks like a "pink route type."
            EnsureMaterials();

            Color color = isConnector ? connectorColor : travelRouteColor;
            Material lineMat = isConnector ? _connectorMat : _travelMat;
            if (lineMat == null)
            {
                Debug.LogError(
                    $"[RTG] Persisted route '{routeId}' skipped — LineRenderer material unavailable " +
                    "(would render magenta).");
                return null;
            }

            float width = isConnector ? connectorWidthMeters : travelWidthMeters;
            float heightOffset = isConnector ? connectorHeightAboveTerrainM : travelHeightAboveTerrainM;
            // Immediate fallback only — AnchorRouteToTerrain replaces with sampled ground.
            double fallbackHeightM = groundHeightMeters + heightOffset;

            var go = new GameObject(isConnector ? $"Connector {routeId}" : $"Route {routeId}");
            if (isConnector && routeId.StartsWith("connector-"))
                _anonymousLineIndex++;

            go.transform.SetParent(transform, false);

            var anchorTransforms = new Transform[displayPath.Length];
            var anchors = new CesiumGlobeAnchor[displayPath.Length];
            for (int i = 0; i < displayPath.Length; i++)
            {
                var anchorGo = new GameObject($"p{i}");
                anchorGo.hideFlags = HideFlags.HideInHierarchy;
                anchorGo.transform.SetParent(go.transform, false);

                CesiumGlobeAnchor anchor = anchorGo.AddComponent<CesiumGlobeAnchor>();
                anchor.SetPositionLongitudeLatitudeHeight(
                    displayPath[i].lng,
                    displayPath[i].lat,
                    fallbackHeightM);

                anchors[i] = anchor;
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
            // sharedMaterial keeps the template alive when route GameObjects are destroyed
            // during save reload / SyncRoutes (assigning .material can destroy the template).
            line.sharedMaterial = lineMat;
            ApplyLineColor(line, color, fadeStart: !isConnector);

            return new RouteLine
            {
                id = routeId,
                root = go,
                line = line,
                anchorTransforms = anchorTransforms,
                anchors = anchors,
                path = displayPath,
                clearanceM = heightOffset,
                positionBuffer = new Vector3[displayPath.Length],
                pathFingerprint = fingerprint,
                isConnector = isConnector,
                terrainGeneration = 0,
            };
        }

        private static void ApplyLineColor(LineRenderer line, Color color, bool fadeStart)
        {
            if (line == null) return;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(color, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(fadeStart ? color.a * 0.45f : color.a, 0f),
                    new GradientAlphaKey(color.a, 1f),
                });
            line.colorGradient = gradient;
            line.startColor = color;
            line.endColor = color;
        }

        /// <summary>
        /// Sample Cesium terrain per vertex (deposit-style) and re-anchor just above ground.
        /// REGRESSION: do not leave routes at fixed ellipsoid fallback — that sits above the glider.
        /// </summary>
        private IEnumerator AnchorRouteToTerrain(RouteLine route)
        {
            if (route?.path == null || route.anchors == null || route.path.Length == 0)
                yield break;

            int generation = ++route.terrainGeneration;
            RtgTerrainHeight terrainHeight = RtgTerrainHeight.FindOrCreate();
            bool applied = false;

            for (int attempt = 0; attempt < TerrainAnchorMaxAttempts; attempt++)
            {
                if (generation != route.terrainGeneration || route.root == null)
                    yield break;

                if (attempt > 0)
                    yield return new WaitForSeconds(TerrainAnchorRetryDelaySeconds);

                if (generation != route.terrainGeneration || route.root == null)
                    yield break;

                Cesium3DTileset tileset = RtgTerrainHeightSampler.ResolveTileset();
                if (tileset == null)
                    continue;

                int count = route.path.Length;
                var heights = new double[count];
                for (int i = 0; i < count; i++)
                    heights[i] = groundHeightMeters;

                for (int start = 0; start < count; start += TerrainSampleBatchSize)
                {
                    if (generation != route.terrainGeneration || route.root == null)
                        yield break;

                    int batchLen = Mathf.Min(TerrainSampleBatchSize, count - start);
                    var requests = new RtgTerrainHeightSampler.SampleRequest[batchLen];
                    for (int i = 0; i < batchLen; i++)
                    {
                        RtgPathPoint p = route.path[start + i];
                        requests[i] = new RtgTerrainHeightSampler.SampleRequest
                        {
                            Longitude = p.lng,
                            Latitude = p.lat,
                            FallbackHeightM = groundHeightMeters,
                        };
                    }

                    double[] batchHeights = null;
                    yield return RtgTerrainHeightSampler.SampleHeightsCoroutine(
                        tileset,
                        requests,
                        sampled => batchHeights = sampled);

                    if (generation != route.terrainGeneration || route.root == null)
                        yield break;

                    if (batchHeights == null)
                        continue;

                    for (int i = 0; i < batchLen && i < batchHeights.Length; i++)
                        heights[start + i] = batchHeights[i];
                }

                for (int i = 0; i < count; i++)
                {
                    if (route.anchors[i] == null) continue;

                    RtgPathPoint p = route.path[i];
                    double groundM = terrainHeight != null
                        ? terrainHeight.ResolveDepositGroundHeight(p.lat, p.lng, heights[i])
                        : heights[i];
                    route.anchors[i].SetPositionLongitudeLatitudeHeight(
                        p.lng,
                        p.lat,
                        groundM + route.clearanceM);
                }

                route.SyncLineToAnchors();
                EnsureRouteLineMaterial(route);
                applied = true;
                break;
            }

            if (applied && generation == route.terrainGeneration && route.root != null)
            {
                EnsureRouteLineMaterial(route);
                Debug.Log(
                    $"[RTG] Terrain-anchored persisted route '{route.id}' " +
                    $"({route.path.Length} pts, clearance {route.clearanceM:0.#} m).");
            }
            else if (generation == route.terrainGeneration && route.root != null)
            {
                Debug.LogWarning(
                    $"[RTG] Terrain anchoring failed for route '{route.id}' — " +
                    "vertices remain at ellipsoid fallback until Cesium tiles load.");
            }
        }

        private void EnsureMaterials()
        {
            if (IsUsableMaterial(_travelMat) && IsUsableMaterial(_connectorMat))
            {
                ApplyMaterialColor(_travelMat, travelRouteColor);
                ApplyMaterialColor(_connectorMat, connectorColor);
                return;
            }

            if (_travelMat != null && !IsUsableMaterial(_travelMat))
            {
                DestroyObject(_travelMat);
                _travelMat = null;
            }
            if (_connectorMat != null && !IsUsableMaterial(_connectorMat))
            {
                DestroyObject(_connectorMat);
                _connectorMat = null;
            }

            Shader shader = ResolveLineShader();
            if (shader == null)
            {
                Debug.LogError(
                    "[RTG] No usable LineRenderer shader found — persisted routes would render magenta. " +
                    "Include Universal Render Pipeline/Unlit (or Sprites/Default) in the build.");
                return;
            }

            if (_travelMat == null)
            {
                _travelMat = new Material(shader) { name = "RTG_RoutePersisted", hideFlags = HideFlags.HideAndDontSave };
                ApplyMaterialColor(_travelMat, travelRouteColor);
            }

            if (_connectorMat == null)
            {
                _connectorMat = new Material(shader) { name = "RTG_ConnectorPersisted", hideFlags = HideFlags.HideAndDontSave };
                ApplyMaterialColor(_connectorMat, connectorColor);
            }
        }

        private static Shader ResolveLineShader()
        {
            return Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
        }

        private static bool IsUsableMaterial(Material material)
        {
            // Unity fake-null: destroyed materials compare equal to null.
            if (material == null) return false;
            Shader shader = material.shader;
            if (shader == null) return false;
            string name = shader.name;
            return !string.IsNullOrEmpty(name)
                && !name.Contains("Hidden/InternalErrorShader")
                && !name.Contains("InternalErrorShader");
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static void DestroyObject(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
