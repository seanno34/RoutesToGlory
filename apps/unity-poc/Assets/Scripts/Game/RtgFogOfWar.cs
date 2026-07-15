using System;
using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.Networking;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Global fog of war: one sliding lat/lng sheet follows the view (player or panned
    /// focus). Everything defaults to fog; the live pin bubble and permanently revealed
    /// tile stamps clear it. One mesh + one draw call — no planet-wide tile grid.
    /// </summary>
    public class RtgFogOfWar : MonoBehaviour
    {
        private const float DefaultLiveRevealM = 60f;
        private const int RevealTexSize = 256;
        private const float DefaultFogSheetSizeM = 14_000f;

        [Header("API (filled by Echo Site loader / 6b)")]
        public string apiBaseUrl = "http://localhost:3001/api";
        public string worldId = "";
        public string empireId = "";

        [Header("Placement")]
        public double groundHeightMeters = 1476.0;
        public float fogHeightAboveGround = 12f;

        [Header("Fog sheet")]
        [Tooltip("Square fog overlay (m) centered on the view. Recenters when focus drifts.")]
        public float fogSheetSizeM = DefaultFogSheetSizeM;

        [Header("Pin reveal (POC)")]
        [Tooltip("Clear bubble radius around the pin in real-world meters.")]
        public float liveRevealRadiusM = DefaultLiveRevealM;

        [Header("Visuals")]
        public float tileSizeM = 400f;
        public float unexploredOpacity = 0.92f;
        public int resourceShimmerDurationMs = 8000;

        private readonly Dictionary<string, RevealAabb> _permanentReveal = new();
        private readonly HashSet<string> _shimmeringResources = new();
        private readonly HashSet<string> _seenMarkers = new();
        private readonly Color32[] _revealPixels = new Color32[RevealTexSize * RevealTexSize];

        private Texture2D _revealTexture;
        private bool _revealTextureDirty = true;

        private Transform _fogRoot;
        private Transform _markerRoot;
        private Material _fogMaterial;
        private MaterialPropertyBlock _fogPropertyBlock;
        private CesiumGeoreference _georeference;
        private CesiumGlobeAnchor _sheetAnchor;
        private MeshRenderer _sheetRenderer;
        private RtgTerrainHeight _terrainHeight;
        private bool _ready;
        private bool _initializing;
        private bool _sheetSpawned;
        private double _sheetCenterLat;
        private double _sheetCenterLng;
        private double _focusLat, _focusLng;
        private bool _hasFocus;
        private RtgRoute[] _pendingRoutes;

        private static readonly int PlayerLatLngId = Shader.PropertyToID("_PlayerLatLng");
        private static readonly int LiveRevealRadiusId = Shader.PropertyToID("_LiveRevealRadiusM");
        private static readonly int FogSheetBoundsId = Shader.PropertyToID("_FogSheetBounds");
        private static readonly int RevealTexId = Shader.PropertyToID("_RevealTex");

        private struct RevealAabb
        {
            public double South, West, North, East;
            public bool IsEmpty => South > North || West > East;

            public bool IntersectsSheet(double sheetSouth, double sheetWest, double sheetNorth, double sheetEast) =>
                !(South > sheetNorth || North < sheetSouth || West > sheetEast || East < sheetWest);
        }

        public static RtgFogOfWar Find()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<RtgFogOfWar>();
#else
            return UnityEngine.Object.FindObjectOfType<RtgFogOfWar>();
#endif
        }

        public static RtgFogOfWar Ensure(RtgEchoSiteLoader loader)
        {
            RtgFogOfWar fog = Find();
            if (fog == null)
            {
                CesiumGeoreference geo = UnityEngine.Object.FindObjectOfType<CesiumGeoreference>();
                if (geo == null) return null;
                var go = new GameObject("RTG Fog Of War");
                go.transform.SetParent(geo.transform, false);
                fog = go.AddComponent<RtgFogOfWar>();
            }

            if (loader != null)
            {
                fog.apiBaseUrl = loader.apiBaseUrl;
                fog.worldId = loader.worldId;
                fog.empireId = loader.empireId;
                fog.groundHeightMeters = loader.groundHeightMeters;
            }

            return fog;
        }

        private void Awake()
        {
            if (RtgWorldScanSettings.PreSurveyedWorld)
            {
                enabled = false;
                return;
            }

#if UNITY_2023_1_OR_NEWER
            _georeference = GetComponentInParent<CesiumGeoreference>()
                ?? UnityEngine.Object.FindFirstObjectByType<CesiumGeoreference>();
#else
            _georeference = GetComponentInParent<CesiumGeoreference>()
                ?? UnityEngine.Object.FindObjectOfType<CesiumGeoreference>();
#endif

            EnsureFogMaterial();
            _fogRoot = new GameObject("Fog Sheet").transform;
            if (_georeference != null)
                _fogRoot.SetParent(_georeference.transform, false);
            else
                _fogRoot.SetParent(transform, false);
        }

        private void Start()
        {
            if (RtgWorldScanSettings.PreSurveyedWorld) return;
            if (!Application.isPlaying) return;
            StartCoroutine(DelayedBootstrap());
        }

        private IEnumerator DelayedBootstrap()
        {
            // Echo sites normally trigger init; this fallback covers slow/failed map loads.
            yield return new WaitForSeconds(3f);
            if (_ready || _initializing) yield break;

            SyncFromEchoLoader();
            Debug.LogWarning(
                "[RTG] Fog bootstrap — echo sites did not initialize fog; starting standalone.");

            _initializing = true;
            yield return InitializeRoutine();
        }

        private void SyncFromEchoLoader()
        {
#if UNITY_2023_1_OR_NEWER
            RtgEchoSiteLoader loader = UnityEngine.Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
            RtgEchoSiteLoader loader = UnityEngine.Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
            if (loader == null) return;

            apiBaseUrl = loader.apiBaseUrl;
            worldId = loader.worldId;
            empireId = loader.empireId;
            groundHeightMeters = loader.groundHeightMeters;
        }

        public void OnMapSpawned(Transform markersContainer, RtgRoute[] routes = null)
        {
            if (RtgWorldScanSettings.PreSurveyedWorld) return;

            _markerRoot = markersContainer;
            if (!Application.isPlaying) return;

            if (_ready)
            {
                RevealAlongPersistedRoutes(routes);
                RefreshMarkerVisibility();
                return;
            }

            if (_initializing) return;
            _initializing = true;
            _pendingRoutes = routes;
            StartCoroutine(InitializeRoutine());
        }

        private IEnumerator InitializeRoutine()
        {
            bool live = !string.IsNullOrWhiteSpace(worldId) && !string.IsNullOrWhiteSpace(empireId);
            if (live)
            {
                yield return FetchFogConfig();
                yield return FetchExplorationState();
            }

            RevealAlongPersistedRoutes(_pendingRoutes);
            _pendingRoutes = null;

            yield return WaitForInitialFocus();

            double startLat = _hasFocus ? _focusLat : 42.7597;
            double startLng = _hasFocus ? _focusLng : -105.3819;

            if (_fogMaterial == null)
            {
                Debug.LogError("[RTG] Fog material missing — overlay will not render.");
                _initializing = false;
                yield break;
            }

            EnsureFogSheet(startLat, startLng);

            if (!_sheetSpawned)
            {
                Debug.LogError("[RTG] Fog sheet failed to spawn.");
                _initializing = false;
                yield break;
            }

            if (TryGetPlayerLatLng(out double pinLat, out double pinLng))
                TryRevealEntireTile(RtgFogTileMath.LatLngToTileId(pinLat, pinLng, tileSizeM));
            else
                TryRevealEntireTile(RtgFogTileMath.LatLngToTileId(startLat, startLng, tileSizeM));

            _initializing = false;
            _ready = true;
            UpdateFogShaderUniforms(startLat, startLng);
            RefreshMarkerVisibility();
            Debug.Log(
                $"[RTG] Fog ready — global sheet {fogSheetSizeM:0} m, " +
                $"{_permanentReveal.Count} permanent reveal(s), live bubble {liveRevealRadiusM} m.");
        }

        private IEnumerator WaitForInitialFocus()
        {
            float timeout = 12f;
            while (timeout > 0f && !TryGetFocusLatLng(out _, out _))
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (TryGetFocusLatLng(out double lat, out double lng))
            {
                _focusLat = lat;
                _focusLng = lng;
                _hasFocus = true;
            }
        }

        private IEnumerator FetchExplorationState()
        {
            string url = $"{apiBaseUrl.TrimEnd('/')}/worlds/{worldId}/exploration/{empireId}";
            using UnityWebRequest req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[RTG] Exploration fetch failed ({req.responseCode}): {req.error}");
                yield break;
            }

            var resp = JsonUtility.FromJson<ExplorationResp>(req.downloadHandler.text);
            if (resp?.exploredTileIds == null || resp.exploredTileIds.Length == 0)
            {
                Debug.LogWarning("[RTG] Exploration fetch returned no explored tiles.");
                yield break;
            }

            foreach (string tileId in resp.exploredTileIds)
            {
                if (!TryRevealEntireTile(tileId))
                    Debug.LogWarning($"[RTG] Skipped invalid explored tile id: {tileId}");
            }

            Debug.Log($"[RTG] Restored {resp.exploredTileIds.Length} explored tile(s) from server.");
        }

        private IEnumerator FetchFogConfig()
        {
            string url = $"{apiBaseUrl.TrimEnd('/')}/config/public";
            using UnityWebRequest req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[RTG] Fog config fetch failed ({req.responseCode}): {req.error}");
                yield break;
            }

            var resp = JsonUtility.FromJson<ConfigPublicResp>(req.downloadHandler.text);
            if (resp?.fogOfWar != null)
                ApplyFogConfig(resp.fogOfWar);
        }

        private void ApplyFogConfig(FogConfig cfg)
        {
            if (cfg.tileSizeM > 0) tileSizeM = cfg.tileSizeM;
            if (cfg.revealRadiusM > 0) liveRevealRadiusM = cfg.revealRadiusM;
            if (cfg.unexploredOpacity >= 0) unexploredOpacity = cfg.unexploredOpacity;
            if (cfg.resourceShimmerDurationMs > 0) resourceShimmerDurationMs = cfg.resourceShimmerDurationMs;
            if (_fogMaterial != null)
                _fogMaterial.SetFloat("_Opacity", unexploredOpacity);
        }

        public void ApplyExplorationDelta(string[] newlyRevealedTileIds, string[] newResourceNodeIds)
        {
            if (RtgWorldScanSettings.PreSurveyedWorld) return;

            if (newlyRevealedTileIds != null)
            {
                foreach (string tileId in newlyRevealedTileIds)
                    RevealEntireTile(tileId);

                if (newlyRevealedTileIds.Length > 0)
                {
                    RtgTerrainScatter scatter = RtgTerrainScatter.Find();
                    scatter?.NotifyExplorationChanged();
                }
            }

            if (!_ready) return;

            if (newResourceNodeIds != null)
            {
                foreach (string nodeId in newResourceNodeIds)
                    ShimmerResourceById(nodeId);
            }
        }

        /// <summary>
        /// True when terrain scatter may dress this fog tile (permanently revealed or near the pin).
        /// </summary>
        public bool IsTileDressable(string tileId)
        {
            if (RtgWorldScanSettings.PreSurveyedWorld) return !string.IsNullOrEmpty(tileId);
            if (string.IsNullOrEmpty(tileId)) return false;
            if (_permanentReveal.ContainsKey(tileId)) return true;
            if (!_hasFocus) return false;

            RtgFogTileMath.TileIdToCenter(tileId, tileSizeM, out double cLat, out double cLng);
            return DistanceMeters(cLat, cLng, _focusLat, _focusLng) <= liveRevealRadiusM * 2.5f;
        }

        private void RevealEntireTile(string tileId)
        {
            if (_permanentReveal.ContainsKey(tileId)) return;
            if (!TryRevealEntireTile(tileId))
                Debug.LogWarning($"[RTG] Could not reveal tile: {tileId}");
        }

        private bool TryRevealEntireTile(string tileId)
        {
            if (string.IsNullOrEmpty(tileId)) return false;

            string[] parts = tileId.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out _) || !int.TryParse(parts[1], out _))
                return false;

            if (_permanentReveal.ContainsKey(tileId))
                return false;

            RtgFogTileMath.TileIdToCenter(tileId, tileSizeM, out double cLat, out double cLng);
            ComputeTileBounds(cLat, cLng, out float south, out float west, out float north, out float east);

            _permanentReveal[tileId] = new RevealAabb { South = south, West = west, North = north, East = east };
            _revealTextureDirty = true;
            if (_ready)
                ShimmerResourcesInTile(tileId);
            return true;
        }

        private void LateUpdate()
        {
            if (RtgWorldScanSettings.PreSurveyedWorld) return;
            if (!_ready || !_sheetSpawned) return;

            if (!TryGetPlayerLatLng(out double playerLat, out double playerLng))
                return;

            if (!TryGetSheetCenterLatLng(out double sheetLat, out double sheetLng))
            {
                sheetLat = playerLat;
                sheetLng = playerLng;
            }

            _focusLat = playerLat;
            _focusLng = playerLng;
            _hasFocus = true;

            MaybeRecenterFogSheet(sheetLat, sheetLng);
            SyncFogSheetHeight();
            CommitPermanentReveal(playerLat, playerLng);
            EnsurePinOccupiedTileRevealed(playerLat, playerLng);
            // Bounds must match the anchored mesh center, not the live view center.
            UpdateFogShaderUniforms(playerLat, playerLng);
            RefreshMarkerVisibility();
        }

        private void MaybeRecenterFogSheet(double lat, double lng)
        {
            double threshold = fogSheetSizeM * 0.35;
            if (DistanceMeters(lat, lng, _sheetCenterLat, _sheetCenterLng) < threshold)
                return;

            _sheetCenterLat = lat;
            _sheetCenterLng = lng;
            _revealTextureDirty = true;
            SyncFogSheetHeight();
        }

        private void SyncFogSheetHeight()
        {
            if (_sheetAnchor == null) return;

            double heightM = GetFogSheetEllipsoidHeight(_sheetCenterLat, _sheetCenterLng);
            _sheetAnchor.SetPositionLongitudeLatitudeHeight(
                _sheetCenterLng, _sheetCenterLat, heightM);
        }

        private double GetFogSheetEllipsoidHeight(double lat, double lng)
        {
            if (_terrainHeight == null)
                _terrainHeight = RtgTerrainHeight.FindOrCreate();

            if (_terrainHeight != null)
                return _terrainHeight.GetGroundHeight(lat, lng) + fogHeightAboveGround;

            return groundHeightMeters + fogHeightAboveGround;
        }

        private void UpdateFogShaderUniforms(double playerLat, double playerLng)
        {
            if (_fogMaterial == null) return;

            ComputeSheetBounds(_sheetCenterLat, _sheetCenterLng,
                out float south, out float west, out float north, out float east);

            _fogMaterial.SetVector(PlayerLatLngId, new Vector4((float)playerLat, (float)playerLng, 0f, 0f));
            _fogMaterial.SetFloat(LiveRevealRadiusId, liveRevealRadiusM);
            _fogMaterial.SetVector(FogSheetBoundsId, new Vector4(south, west, north, east));

            if (_revealTextureDirty)
                RebuildRevealTexture(south, west, north, east);

            ApplyRevealTextureToRenderer();
        }

        private void ApplyRevealTextureToRenderer()
        {
            if (_sheetRenderer == null || _revealTexture == null) return;

            _fogPropertyBlock ??= new MaterialPropertyBlock();
            _sheetRenderer.GetPropertyBlock(_fogPropertyBlock);
            _fogPropertyBlock.SetTexture(RevealTexId, _revealTexture);
            _sheetRenderer.SetPropertyBlock(_fogPropertyBlock);

            if (_fogMaterial != null)
                _fogMaterial.SetTexture(RevealTexId, _revealTexture);
        }

        private void RebuildRevealTexture(float sheetSouth, float sheetWest, float sheetNorth, float sheetEast)
        {
            EnsureRevealTexture();

            for (int i = 0; i < _revealPixels.Length; i++)
                _revealPixels[i] = new Color32(0, 0, 0, 255);

            double latSpan = sheetNorth - sheetSouth;
            double lngSpan = sheetEast - sheetWest;
            if (latSpan <= 0 || lngSpan <= 0)
            {
                _revealTexture.SetPixels32(_revealPixels);
                _revealTexture.Apply(false, false);
                _revealTextureDirty = false;
                return;
            }

            foreach (RevealAabb aabb in _permanentReveal.Values)
            {
                if (aabb.IsEmpty) continue;
                FillAabbInTexture(
                    aabb.South, aabb.West, aabb.North, aabb.East,
                    sheetSouth, sheetWest, sheetNorth, sheetEast);
            }

            _revealTexture.SetPixels32(_revealPixels);
            _revealTexture.Apply(false, false);
            _revealTextureDirty = false;
        }

        private void FillAabbInTexture(
            double south, double west, double north, double east,
            float sheetSouth, float sheetWest, float sheetNorth, float sheetEast)
        {
            if (north <= sheetSouth || south >= sheetNorth || east <= sheetWest || west >= sheetEast)
                return;

            double latSpan = sheetNorth - sheetSouth;
            double lngSpan = sheetEast - sheetWest;
            if (latSpan <= 0 || lngSpan <= 0) return;

            double clampSouth = Math.Max(south, sheetSouth);
            double clampNorth = Math.Min(north, sheetNorth);
            double clampWest = Math.Max(west, sheetWest);
            double clampEast = Math.Min(east, sheetEast);

            int size = RevealTexSize;
            int px0 = Math.Max(0, (int)Math.Floor((clampWest - sheetWest) / lngSpan * (size - 1)));
            int px1 = Math.Min(size - 1, (int)Math.Ceiling((clampEast - sheetWest) / lngSpan * (size - 1)));
            int py0 = Math.Max(0, (int)Math.Floor((clampSouth - sheetSouth) / latSpan * (size - 1)));
            int py1 = Math.Min(size - 1, (int)Math.Ceiling((clampNorth - sheetSouth) / latSpan * (size - 1)));

            var white = new Color32(255, 255, 255, 255);
            for (int py = py0; py <= py1; py++)
            {
                int row = py * size;
                for (int px = px0; px <= px1; px++)
                    _revealPixels[row + px] = white;
            }
        }

        private void EnsureRevealTexture()
        {
            if (_revealTexture != null) return;

            _revealTexture = new Texture2D(
                RevealTexSize,
                RevealTexSize,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "RTG_FogReveal",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        /// <summary>
        /// The tile under the pin is always fully revealed — even when the ship is
        /// stationary (e.g. first GPS fix on load). Movement still expands via the
        /// live bubble in <see cref="CommitPermanentReveal"/>.
        /// </summary>
        private void EnsurePinOccupiedTileRevealed(double lat, double lng)
        {
            string tileId = RtgFogTileMath.LatLngToTileId(lat, lng, tileSizeM);
            TryRevealEntireTile(tileId);
        }

        private void CommitPermanentReveal(double lat, double lng)
        {
            List<string> tiles = RtgFogTileMath.TilesInRadius(
                lat, lng, liveRevealRadiusM, tileSizeM);

            foreach (string tileId in tiles)
                TryRevealEntireTile(tileId);
        }

        public void RevealAlongPersistedRoutes(RtgRoute[] routes)
        {
            if (routes == null) return;

            int revealed = 0;
            foreach (RtgRoute route in routes)
            {
                if (route?.path_json == null || route.path_json.Length < 1) continue;
                if (!string.IsNullOrEmpty(route.status) && route.status != "active") continue;

                revealed += RevealAlongPath(route.path_json);
            }

            if (revealed > 0)
                Debug.Log($"[RTG] Revealed fog along {revealed} persisted route corridor sample(s).");
        }

        private int RevealAlongPath(RtgPathPoint[] path)
        {
            if (path == null || path.Length == 0) return 0;

            int samples = 0;
            RevealTilesNearPoint(path[0].lat, path[0].lng);
            samples++;

            for (int i = 1; i < path.Length; i++)
            {
                RtgPathPoint from = path[i - 1];
                RtgPathPoint to = path[i];
                double dist = DistanceMeters(from.lat, from.lng, to.lat, to.lng);
                double stepM = Math.Max(tileSizeM * 0.5, liveRevealRadiusM);
                int steps = Math.Max(1, (int)Math.Ceiling(dist / stepM));

                for (int s = 1; s <= steps; s++)
                {
                    double t = s / (double)steps;
                    double sampleLat = from.lat + (to.lat - from.lat) * t;
                    double sampleLng = from.lng + (to.lng - from.lng) * t;
                    RevealTilesNearPoint(sampleLat, sampleLng);
                    samples++;
                }
            }

            return samples;
        }

        private void RevealTilesNearPoint(double lat, double lng)
        {
            foreach (string tileId in RtgFogTileMath.TilesInRadius(
                         lat, lng, liveRevealRadiusM, tileSizeM))
                TryRevealEntireTile(tileId);
        }

        private bool IsPermanentlyRevealed(double lat, double lng)
        {
            string tileId = RtgFogTileMath.LatLngToTileId(lat, lng, tileSizeM);
            if (!_permanentReveal.TryGetValue(tileId, out RevealAabb aabb) || aabb.IsEmpty)
                return false;

            return lat >= aabb.South && lat <= aabb.North &&
                   lng >= aabb.West && lng <= aabb.East;
        }

        private void EnsureFogSheet(double centerLat, double centerLng)
        {
            if (_sheetSpawned || _fogMaterial == null) return;

            _sheetCenterLat = centerLat;
            _sheetCenterLng = centerLng;

            var go = new GameObject("Fog Sheet Quad");
            go.transform.SetParent(_fogRoot, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(fogSheetSizeM, fogSheetSizeM, 1f);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = BuildFogQuadMesh();

            _sheetRenderer = go.AddComponent<MeshRenderer>();
            _sheetRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _sheetRenderer.receiveShadows = false;
            _sheetRenderer.allowOcclusionWhenDynamic = false;
            _sheetRenderer.material = _fogMaterial;

            _sheetAnchor = go.AddComponent<CesiumGlobeAnchor>();
            _sheetAnchor.SetPositionLongitudeLatitudeHeight(
                centerLng, centerLat, GetFogSheetEllipsoidHeight(centerLat, centerLng));

            _sheetSpawned = true;
        }

        private static Mesh BuildFogQuadMesh()
        {
            var mesh = new Mesh { name = "RTG_FogSheetQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 50_000f);
            return mesh;
        }

        private void ComputeSheetBounds(
            double centerLat, double centerLng,
            out float south, out float west, out float north, out float east)
        {
            double halfLat = (fogSheetSizeM * 0.5) / RtgFogTileMath.LatM;
            double lngM = RtgFogTileMath.LatM * Math.Cos(centerLat * Math.PI / 180.0);
            double halfLng = (fogSheetSizeM * 0.5) / lngM;
            south = (float)(centerLat - halfLat);
            west = (float)(centerLng - halfLng);
            north = (float)(centerLat + halfLat);
            east = (float)(centerLng + halfLng);
        }

        private void ComputeTileBounds(
            double centerLat, double centerLng,
            out float south, out float west, out float north, out float east)
        {
            double halfLat = (tileSizeM * 0.5) / RtgFogTileMath.LatM;
            double lngM = RtgFogTileMath.LatM * Math.Cos(centerLat * Math.PI / 180.0);
            double halfLng = (tileSizeM * 0.5) / lngM;
            south = (float)(centerLat - halfLat);
            west = (float)(centerLng - halfLng);
            north = (float)(centerLat + halfLat);
            east = (float)(centerLng + halfLng);
        }

        private void RefreshMarkerVisibility()
        {
            if (_markerRoot == null || !_hasFocus) return;

            foreach (RtgMapMarker marker in _markerRoot.GetComponentsInChildren<RtgMapMarker>(true))
            {
                bool inLiveBubble = DistanceMeters(_focusLat, _focusLng, marker.lat, marker.lng) <= liveRevealRadiusM;
                bool visible = inLiveBubble || IsPermanentlyRevealed(marker.lat, marker.lng);
                marker.gameObject.SetActive(visible);

                if (visible && marker.kind == RtgMapMarker.Kind.Resource && _seenMarkers.Add(marker.targetId))
                    BeginShimmer(marker);
            }
        }

        private void ShimmerResourcesInTile(string tileId)
        {
            if (_markerRoot == null) return;
            foreach (RtgMapMarker marker in _markerRoot.GetComponentsInChildren<RtgMapMarker>(true))
            {
                if (marker.kind != RtgMapMarker.Kind.Resource) continue;
                string mTile = RtgFogTileMath.LatLngToTileId(marker.lat, marker.lng, tileSizeM);
                if (mTile == tileId)
                    BeginShimmer(marker);
            }
        }

        private void ShimmerResourceById(string nodeId)
        {
            if (_markerRoot == null || string.IsNullOrEmpty(nodeId)) return;
            foreach (RtgMapMarker marker in _markerRoot.GetComponentsInChildren<RtgMapMarker>(true))
            {
                if (marker.kind == RtgMapMarker.Kind.Resource && marker.targetId == nodeId)
                    BeginShimmer(marker);
            }
        }

        private void BeginShimmer(RtgMapMarker marker)
        {
            if (!_shimmeringResources.Add(marker.targetId)) return;
            RtgResourceShimmer shimmer = marker.GetComponent<RtgResourceShimmer>();
            if (shimmer == null) shimmer = marker.gameObject.AddComponent<RtgResourceShimmer>();
            shimmer.Begin(resourceShimmerDurationMs / 1000f);
        }

        private bool TryGetPlayerLatLng(out double lat, out double lng)
        {
#if UNITY_2023_1_OR_NEWER
            RtgPlayerLocation player = UnityEngine.Object.FindFirstObjectByType<RtgPlayerLocation>();
#else
            RtgPlayerLocation player = UnityEngine.Object.FindObjectOfType<RtgPlayerLocation>();
#endif
            if (player != null && player.TryGetPlayerLatLng(out lat, out lng))
                return true;

            return TryGetFocusLatLng(out lat, out lng);
        }

        private bool TryGetSheetCenterLatLng(out double lat, out double lng)
        {
#if UNITY_2023_1_OR_NEWER
            RtgPlayerLocation player = UnityEngine.Object.FindFirstObjectByType<RtgPlayerLocation>();
#else
            RtgPlayerLocation player = UnityEngine.Object.FindObjectOfType<RtgPlayerLocation>();
#endif
            if (player != null && player.TryGetViewCenterLatLng(out lat, out lng))
                return true;

            return TryGetPlayerLatLng(out lat, out lng);
        }

        private bool TryGetFocusLatLng(out double lat, out double lng)
        {
#if UNITY_2023_1_OR_NEWER
            RtgPlayerLocation player = UnityEngine.Object.FindFirstObjectByType<RtgPlayerLocation>();
#else
            RtgPlayerLocation player = UnityEngine.Object.FindObjectOfType<RtgPlayerLocation>();
#endif
            if (player != null)
            {
                Transform marker = player.transform.Find("Player Marker");
                if (marker != null)
                {
                    var anchor = marker.GetComponent<CesiumGlobeAnchor>();
                    if (anchor != null)
                    {
                        lat = anchor.latitude;
                        lng = anchor.longitude;
                        return true;
                    }
                }
            }

            lat = _focusLat;
            lng = _focusLng;
            return _hasFocus;
        }

        private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            double avgLatRad = (lat1 + lat2) * 0.5 * Math.PI / 180.0;
            double metersPerDegLng = RtgFogTileMath.LatM * Math.Cos(avgLatRad);
            double dLat = (lat2 - lat1) * RtgFogTileMath.LatM;
            double dLng = (lng2 - lng1) * metersPerDegLng;
            return Math.Sqrt(dLat * dLat + dLng * dLng);
        }

        private void EnsureFogMaterial()
        {
            if (_fogMaterial != null) return;

            Shader shader = Shader.Find("RoutesToGlory/FogOfWarOverlay");
            if (shader == null)
            {
                Debug.LogError("[RTG] FogOfWarOverlay shader not found.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                Debug.LogError("[RTG] No fallback shader for fog of war.");
                return;
            }

            _fogMaterial = new Material(shader) { name = "RTG_FogOfWar_Runtime" };
            _fogMaterial.SetColor("_FogColor", new Color(0.0588f, 0.0902f, 0.1647f, 1f));
            _fogMaterial.SetFloat("_Opacity", unexploredOpacity);
            _fogMaterial.SetFloat("_NoiseScale", 0.015f);
            _fogMaterial.SetFloat("_PulseSpeed", 0.35f);
            _fogMaterial.SetFloat("_EdgeShimmer", 0f);
            _fogMaterial.SetFloat(LiveRevealRadiusId, liveRevealRadiusM);
            EnsureRevealTexture();
            _fogMaterial.SetTexture(RevealTexId, _revealTexture);
        }

        public void ClearFog()
        {
            if (_fogRoot != null)
            {
                foreach (Transform child in _fogRoot)
                    if (child != null) Destroy(child.gameObject);
            }

            _sheetAnchor = null;
            _sheetRenderer = null;
            _permanentReveal.Clear();
            _ready = false;
            _initializing = false;
            _sheetSpawned = false;
            _shimmeringResources.Clear();
            _seenMarkers.Clear();
            _revealTextureDirty = true;
            if (_revealTexture != null)
            {
                Destroy(_revealTexture);
                _revealTexture = null;
            }
        }

        /// <summary>
        /// Tear down fog mesh, reveal texture, and coroutines when the world is pre-surveyed.
        /// </summary>
        public void ShutdownForPreSurvey()
        {
            StopAllCoroutines();
            ClearFog();
            if (_fogMaterial != null)
            {
                Destroy(_fogMaterial);
                _fogMaterial = null;
            }
        }

        public IEnumerator ReloadFromApi()
        {
            ClearFog();
            _initializing = true;
            yield return InitializeRoutine();
        }

        [Serializable]
        private class ExplorationResp
        {
            public string[] exploredTileIds;
        }

        [Serializable]
        private class ConfigPublicResp
        {
            public FogConfig fogOfWar;
        }

        [Serializable]
        private class FogConfig
        {
            public float tileSizeM;
            public float revealRadiusM;
            public float unexploredOpacity;
            public int resourceShimmerDurationMs;
        }
    }
}
