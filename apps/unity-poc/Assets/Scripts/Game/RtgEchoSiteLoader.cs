using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.Networking;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Loads a world map from @empire/api (or a local sample file) and spawns each
    /// Echo Site (settlement) and resource node as a georeferenced, glowing beacon
    /// under the Cesium georeference.
    ///
    /// Data source is a single switch: <see cref="dataSource"/>. In Play mode the
    /// live path uses UnityWebRequest; the editor "Load Echo Sites" menu command
    /// uses the synchronous sample path so you can preview placement without Play
    /// mode or a running backend. The parsing + spawning code is identical for both,
    /// so flipping to the live API once MySQL is up needs no code changes.
    /// </summary>
    public class RtgEchoSiteLoader : MonoBehaviour
    {
        public enum DataSource { SampleFile, LiveApi }

        [Header("Data source")]
        public DataSource dataSource = DataSource.SampleFile;

        [Tooltip("Base URL of @empire/api, e.g. http://localhost:3001/api")]
        public string apiBaseUrl = "http://localhost:3001/api";

        [Tooltip("World id (UUID) to load when dataSource = LiveApi")]
        public string worldId = "";

        [Tooltip("Empire id (UUID) for the player; used by route sessions. Set by Connect Echo Sites to Live API.")]
        public string empireId = "";

        [Tooltip("File under Assets/StreamingAssets/ to load when dataSource = SampleFile")]
        public string sampleFileName = "sample-world-map.json";

        [Tooltip("When LiveApi is unreachable, load the sample map so Play mode still works offline.")]
        public bool fallbackToSampleOnApiFailure = true;

        [Tooltip("In the Editor, retry map fetch via 127.0.0.1 when the configured LAN IP fails.")]
        public bool editorLocalhostRetry = true;

        [Header("Placement")]
        [Tooltip("Approx. ground height (m above ellipsoid) for the POC area near Douglas, WY.")]
        public double groundHeightMeters = 1476.0;

        [Tooltip("Meters above ellipsoid ground height for settlement marker anchors.")]
        public float groundMarkerClearanceM = 6f;

        [Tooltip("When true, resource deposits sample Cesium terrain height and sit flush on the surface.")]
        public bool anchorDepositsToTerrain = true;

        [Tooltip("When true, Echo Site settlements sample Cesium terrain height (matches deposit anchoring).")]
        public bool anchorSettlementsToTerrain = true;

        [Tooltip("Meters above resolved terrain ground for deposit roots (prevents mesh burial).")]
        public float depositSurfaceClearanceM = RtgTerrainDepositGuards.DefaultDepositSurfaceClearanceM;

        [Tooltip("Label size multiplier for ground-anchored markers.")]
        public float groundLabelSizeFactor = 0.09f;

        [Tooltip("Label foreground color (a black outline is drawn behind it for contrast).")]
        public Color labelColor = new Color(0.90f, 0.98f, 1.00f); // pale alien cyan-white

        [Tooltip("Load automatically when entering Play mode.")]
        public bool loadOnPlay = true;

        [Header("World presentation")]
        [Tooltip("Pre-surveyed worlds show the full map at mission start — no fog of war. Frees GPU for terrain tiles.")]
        public bool preSurveyedWorld = true;

        [Header("Tap-to-connect test layout")]
        [Tooltip("Offset nearby markers off the tour corridor so you can test tap-claim (near) and reject (far).")]
        public bool scatterForTapTest = true;

        [Tooltip("Only scatter items within this radius of the play center (Douglas). Far metro sites stay put.")]
        public float scatterRadiusMeters = 8000f;

        public double scatterCenterLat = 42.7597;
        public double scatterCenterLng = -105.3819;

        [Tooltip("East offset (m) for 'near' markers — should be within minConnectDistanceM of the corridor.")]
        public float nearTapOffsetM = 450f;

        [Tooltip("East offset (m) for 'far' markers — should exceed minConnectDistanceM from the corridor.")]
        public float farTapOffsetM = 1800f;

        [Tooltip("Goodie huts are pinned on the corridor tour north leg (matches RtgPlayerLocation.CorridorTourLoop).")]
        public float goodieHutCorridorFraction = 0.55f;

        private const string MarkerContainerName = "Markers";
        private int _scatterIndex;
        private string _corridorGoodieId;
        private readonly List<MarkerAnchorPending> _pendingTerrainAnchors = new();

        private const double CorridorDLat = 0.012;

        /// <summary>
        /// The most recently loaded/parsed world map, or null before the first load.
        /// Exposed so other systems (e.g. the player's "tour nearby sites" route) can
        /// reuse the same data without re-fetching.
        /// </summary>
        public RtgWorldMap LastMap { get; private set; }

        /// <summary>True when LiveApi fell back to the sample file (API unreachable).</summary>
        public bool LoadedFromSampleFallback { get; private set; }

        /// <summary>Human-readable counts from the most recent SpawnAll (for editor menus).</summary>
        public string LastSpawnSummary { get; private set; } = "no markers spawned";

        /// <summary>
        /// When false (default until join), Play-mode loads / sample fallback / SpawnAll are blocked.
        /// Set by <see cref="RtgGameSessionLogin"/> after Join or Editor sample.
        /// </summary>
        private bool _sessionLoadAllowed;

        /// <summary>Allow or deny Play-mode world loads until login confirms a session.</summary>
        public void SetSessionLoadAllowed(bool allowed)
        {
            _sessionLoadAllowed = allowed;
            if (!allowed)
            {
                loadOnPlay = false;
                ClearMarkers();
                LastMap = null;
                LoadedFromSampleFallback = false;
                LastSpawnSummary = "waiting for login";
            }
        }

        /// <summary>Clear the cached map without spawning (used while login gates play).</summary>
        public void ClearCachedMap()
        {
            LastMap = null;
            LoadedFromSampleFallback = false;
        }

        /// <summary>Keep in-memory map routes aligned after incremental API refreshes.</summary>
        public void ApplyRouteSnapshot(RtgRoute[] routes)
        {
            if (LastMap == null) return;
            LastMap.routes = routes;
        }

        private bool CanLoadWorldInPlay()
        {
            if (!Application.isPlaying) return true;
            if (_sessionLoadAllowed) return true;
            // No login component → legacy scenes may still load.
            return !RtgGameSessionLogin.IsBlockingWorldLoad(this);
        }

        // Cache runtime-created emissive materials by color so we don't leak one per marker.
        private readonly Dictionary<Color, Material> _materialCache = new();
        private readonly Dictionary<Color, Material> _glowPadCache = new();
        private readonly Dictionary<Color, Material> _depositGlowCache = new();

        private bool _reloadingAfterWorldReset;

        private struct MarkerAnchorPending
        {
            public GameObject Root;
            public double Longitude;
            public double Latitude;
            public float SurfaceClearanceM;
        }

        private void Awake()
        {
            // Gate before dev-config / Start so sample or LiveApi never auto-loads
            // while the join overlay is up.
            loadOnPlay = false;
            _sessionLoadAllowed = false;
            RtgDevWorldConfig.TryApplyTo(this);
            RtgWorldScanSettings.Apply(preSurveyedWorld);
            RtgXeniteDepositTuningConfig.TryLoad(out _);
            // Access-code login gates loadOnPlay until a session is applied.
            RtgGameSessionLogin.EnsureOn(this);
        }

        private void Start()
        {
            if (!Application.isPlaying || !loadOnPlay) return;
            if (!CanLoadWorldInPlay())
            {
                loadOnPlay = false;
                return;
            }
            StartCoroutine(LoadRoutine());
        }

        /// <summary>Editor entry point: loads the sample file synchronously and spawns markers.</summary>
        public void LoadSampleImmediate()
        {
            if (Application.isPlaying && !CanLoadWorldInPlay())
            {
                Debug.Log("[RTG] Sample world load blocked until login Join (or Editor sample button).");
                return;
            }
            string json = ReadSampleFile();
            if (string.IsNullOrEmpty(json)) return;
            SpawnAll(Parse(json));
        }

        /// <summary>Parse JSON from a live map fetch and respawn all markers (editor menu helper).</summary>
        public void SpawnFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (Application.isPlaying && !CanLoadWorldInPlay())
            {
                Debug.Log("[RTG] Map spawn blocked until login Join.");
                return;
            }
            SpawnAll(Parse(json));
        }

        /// <summary>
        /// Reload markers from the configured <see cref="dataSource"/>.
        /// In play mode LiveApi uses a coroutine; sample file loads synchronously.
        /// </summary>
        public void ReloadFromConfiguredSource()
        {
            if (Application.isPlaying && !CanLoadWorldInPlay())
            {
                Debug.Log("[RTG] World reload blocked until login Join.");
                return;
            }

            if (dataSource == DataSource.SampleFile)
            {
                LoadSampleImmediate();
                return;
            }

            if (Application.isPlaying)
                StartCoroutine(ReloadFromApi());
            else
                Debug.LogWarning(
                    "[RTG] Live API reload in Edit mode must be driven by the editor menu " +
                    "(fetch + SpawnFromJson). Press Play or use Reset & Reload World.");
        }

        private IEnumerator LoadRoutine()
        {
            if (!CanLoadWorldInPlay()) yield break;
            yield return FetchAndSpawn();
        }

        /// <summary>Re-fetch the live map and respawn markers (e.g. after founding a goodie hut).</summary>
        public IEnumerator ReloadFromApi()
        {
            if (dataSource != DataSource.LiveApi) yield break;
            if (Application.isPlaying && !CanLoadWorldInPlay()) yield break;
            LastMap = null;
            yield return FetchAndSpawn();
        }

        /// <summary>
        /// After world reset — discard cached map and respawn from the configured source.
        /// <paramref name="preferSync"/> uses blocking HTTP (editor menu); otherwise play mode
        /// uses a coroutine so UnityWebRequest can run on the main thread.
        /// </summary>
        /// <returns>True when markers were spawned (sync path), or when an async reload was started.</returns>
        public bool ReloadAfterWorldReset(bool preferSync = false)
        {
            LastMap = null;
            _reloadingAfterWorldReset = true;

            if (dataSource == DataSource.SampleFile)
            {
                LoadSampleImmediate();
                return LogReloadOutcome(HasActiveMarkers(), "sample file");
            }

            if (string.IsNullOrWhiteSpace(worldId))
            {
                Debug.LogError(
                    "[RTG] Cannot reload live map — worldId is empty. " +
                    "Run Routes to Glory → Connect Echo Sites to Live API.");
                if (fallbackToSampleOnApiFailure)
                    return TryFallbackSample("missing worldId");
                return false;
            }

            if (preferSync)
                return TryReloadLiveApiSync();

            if (Application.isPlaying)
            {
                StartCoroutine(ReloadFromApiThenValidate());
                return true;
            }

            Debug.LogWarning(
                "[RTG] Live API reload in Edit mode must use preferSync=true " +
                "(Routes to Glory → Reset & Reload World).");
            return false;
        }

        private IEnumerator ReloadFromApiThenValidate()
        {
            yield return ReloadFromApi();
            if (HasActiveMarkers())
                Debug.Log($"[RTG] World reload complete from live API — {LastSpawnSummary}.");
            else
                LogEmptyReloadFailure("live API coroutine");
        }

        private bool TryReloadLiveApiSync()
        {
            string json = FetchLiveMapJsonBlocking();
            if (!string.IsNullOrEmpty(json))
            {
                LoadedFromSampleFallback = false;
                SpawnFromJson(json);
                return LogReloadOutcome(HasActiveMarkers(), "live API");
            }

            if (fallbackToSampleOnApiFailure)
                return TryFallbackSample("live API unreachable");

            Debug.LogError(
                "[RTG] Live map reload failed and fallbackToSampleOnApiFailure is disabled. " +
                "Is @empire/api running (pnpm --filter @empire/api dev)?");
            return false;
        }

        private bool TryFallbackSample(string reason)
        {
            if (Application.isPlaying && !CanLoadWorldInPlay())
            {
                Debug.Log($"[RTG] Skipping sample fallback ({reason}) — waiting for access-code Join.");
                return false;
            }

            Debug.LogWarning(
                $"[RTG] Live map reload failed ({reason}) — falling back to {sampleFileName}. " +
                "Start @empire/api or update apiBaseUrl in rtg-dev-world.json.");
            LoadedFromSampleFallback = true;
            LoadSampleImmediate();
            return LogReloadOutcome(HasActiveMarkers(), "sample fallback");
        }

        private string FetchLiveMapJsonBlocking()
        {
            string primaryUrl = BuildMapUrl(apiBaseUrl);
            string json = TryFetchMapJsonBlocking(primaryUrl);
            if (!string.IsNullOrEmpty(json))
                return json;

            if (Application.isEditor && editorLocalhostRetry)
            {
                string localBase = LocalhostApiBase(apiBaseUrl);
                if (!string.Equals(localBase, apiBaseUrl.TrimEnd('/'), System.StringComparison.OrdinalIgnoreCase))
                {
                    string localUrl = BuildMapUrl(localBase);
                    Debug.LogWarning(
                        $"[RTG] Live map fetch failed ({primaryUrl}). Retrying via editor localhost: {localUrl}");
                    json = TryFetchMapJsonBlocking(localUrl);
                    if (!string.IsNullOrEmpty(json))
                    {
                        apiBaseUrl = localBase;
                        return json;
                    }
                }
            }

            return null;
        }

        private static string TryFetchMapJsonBlocking(string url)
        {
            try
            {
                using var client = new HttpClient { Timeout = System.TimeSpan.FromSeconds(8) };
                return client.GetStringAsync(url).GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RTG] Map fetch failed: {ex.Message} ({url})");
                return null;
            }
        }

        private bool HasActiveMarkers()
        {
            Transform container = transform.Find(MarkerContainerName);
            return container != null && container.childCount > 0;
        }

        private bool LogReloadOutcome(bool spawned, string source)
        {
            if (spawned)
            {
                Debug.Log($"[RTG] World reload complete from {source} — {LastSpawnSummary}.");
                return true;
            }

            LogEmptyReloadFailure(source);
            return false;
        }

        private void LogEmptyReloadFailure(string source)
        {
            Debug.LogError(
                $"[RTG] World reload from {source} finished with NO markers spawned. " +
                "Check Console for fetch/parse errors, verify rtg-dev-world.json worldId, " +
                "and ensure sample-world-map.json exists under StreamingAssets.");
        }

        private IEnumerator FetchAndSpawn()
        {
            if (Application.isPlaying && !CanLoadWorldInPlay())
                yield break;

            string json = null;
            LoadedFromSampleFallback = false;

            if (dataSource == DataSource.SampleFile)
            {
                json = ReadSampleFile();
            }
            else
            {
                yield return FetchLiveMapJson(result => json = result);
            }

            if (Application.isPlaying && !CanLoadWorldInPlay())
                yield break;

            if (!string.IsNullOrEmpty(json)) SpawnAll(Parse(json));
        }

        private IEnumerator FetchLiveMapJson(System.Action<string> done)
        {
            string primaryUrl = BuildMapUrl(apiBaseUrl);
            string json = null;
            yield return TryFetchMapJson(primaryUrl, result => json = result);
            if (!string.IsNullOrEmpty(json))
            {
                LoadedFromSampleFallback = false;
                done?.Invoke(json);
                yield break;
            }

            if (Application.isEditor && editorLocalhostRetry)
            {
                string localBase = LocalhostApiBase(apiBaseUrl);
                if (!string.Equals(localBase, apiBaseUrl.TrimEnd('/'), System.StringComparison.OrdinalIgnoreCase))
                {
                    string localUrl = BuildMapUrl(localBase);
                    Debug.LogWarning(
                        $"[RTG] Echo Site load failed ({primaryUrl}). Retrying via editor localhost: {localUrl}");
                    yield return TryFetchMapJson(localUrl, result => json = result);
                    if (!string.IsNullOrEmpty(json))
                    {
                        apiBaseUrl = localBase;
                        LoadedFromSampleFallback = false;
                        done?.Invoke(json);
                        yield break;
                    }
                }
            }

            if (fallbackToSampleOnApiFailure && CanLoadWorldInPlay())
            {
                Debug.LogWarning(
                    $"[RTG] Echo Site load failed ({primaryUrl}). Falling back to {sampleFileName}. " +
                    "Start @empire/api (pnpm --filter @empire/api dev) or update apiBaseUrl in rtg-dev-world.json.");
                LoadedFromSampleFallback = true;
                done?.Invoke(ReadSampleFile());
                yield break;
            }

            if (fallbackToSampleOnApiFailure && !CanLoadWorldInPlay())
            {
                Debug.Log(
                    "[RTG] Skipping sample fallback — waiting for access-code Join.");
            }

            Debug.LogError(
                $"[RTG] Echo Site load failed ({primaryUrl}). " +
                "Is @empire/api running (pnpm --filter @empire/api dev)? " +
                "On iPhone, apiBaseUrl must be your Mac LAN IP, not localhost. " +
                "Check rtg-dev-world.json / RTG Echo Sites in the scene.");
            done?.Invoke(null);
        }

        private static IEnumerator TryFetchMapJson(string url, System.Action<string> done)
        {
            using UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = 8;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[RTG] Map fetch failed: {req.responseCode} {req.error} ({url})");
                done?.Invoke(null);
                yield break;
            }

            done?.Invoke(req.downloadHandler.text);
        }

        private string BuildMapUrl(string baseUrl) =>
            $"{baseUrl.TrimEnd('/')}/worlds/{worldId}/map";

        private static string LocalhostApiBase(string baseUrl)
        {
            if (!System.Uri.TryCreate(baseUrl, System.UriKind.Absolute, out System.Uri uri))
                return "http://127.0.0.1:3001/api";

            int port = uri.Port > 0 ? uri.Port : 3001;
            string path = uri.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(path))
                path = "/api";
            return $"http://127.0.0.1:{port}{path}";
        }

        private string ReadSampleFile()
        {
            string path = Path.Combine(Application.streamingAssetsPath, sampleFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[RTG] Sample world map not found at {path}");
                return null;
            }
            return File.ReadAllText(path);
        }

        private static RtgWorldMap Parse(string json)
        {
            RtgWorldMap map = JsonUtility.FromJson<RtgWorldMap>(json);
            if (map == null) Debug.LogError("[RTG] Failed to parse world map JSON.");
            return map;
        }

        // ------------------------------------------------------------------ //
        // Spawning
        // ------------------------------------------------------------------ //

        public void SpawnAll(RtgWorldMap map)
        {
            if (map == null) return;
            if (Application.isPlaying && !CanLoadWorldInPlay())
            {
                Debug.Log("[RTG] SpawnAll blocked until login Join.");
                return;
            }

            LastMap = map;
            Transform container = ResetContainer();
            _scatterIndex = 0;
            _pendingTerrainAnchors.Clear();
            _corridorGoodieId = SelectCorridorGoodieTarget(map);
            int settlements = 0, resources = 0, xeniteTripo = 0, xeniteProcedural = 0;

            if (map.settlements != null)
            {
                foreach (RtgSettlement s in map.settlements)
                {
                    SpawnSettlement(s, container);
                    settlements++;
                }
            }

            if (map.resources != null)
            {
                int skippedDeposits = 0;
                foreach (RtgResourceNode r in map.resources)
                {
                    if (!RtgTerrainDepositGuards.IsActivePocDeposit(r.resource_id))
                    {
                        skippedDeposits++;
                        continue;
                    }

                    bool usedTripo = SpawnResource(r, container);
                    resources++;
                    if (r.resource_id == "xenite")
                    {
                        if (usedTripo) xeniteTripo++;
                        else xeniteProcedural++;
                    }
                }

                if (skippedDeposits > 0)
                {
                    Debug.Log(
                        $"[RTG] Skipped {skippedDeposits} resource deposit(s) — POC only spawns: " +
                        string.Join(", ", RtgTerrainDepositGuards.ActivePocDepositResourceIds));
                }
            }

            LastSpawnSummary = FormatSpawnSummary(settlements, resources, xeniteTripo, xeniteProcedural);
            Debug.Log($"[RTG] {LastSpawnSummary}");
            if (settlements == 0 && resources == 0)
            {
                Debug.LogError(
                    "[RTG] Map parsed but contains no spawnable echo sites or deposits. " +
                    "After reset-progress the API map may be empty — try Reload or check MySQL seed data.");
            }
            else if (settlements == 0 && resources > 0)
            {
                Debug.LogWarning(
                    "[RTG] Map has deposits but no echo sites — verify world seed/MySQL data or enable " +
                    "fallbackToSampleOnApiFailure on RTG Echo Sites.");
            }
            else if (resources == 0 && map.resources != null && map.resources.Length > 0)
            {
                Debug.LogWarning(
                    $"[RTG] Map has {map.resources.Length} resource node(s) but 0 POC deposits spawned " +
                    $"(active ids: {string.Join(", ", RtgTerrainDepositGuards.ActivePocDepositResourceIds)}).");
            }
            else if (resources > 0)
            {
                WarnIfNoDepositsNearPlayCenter(map);
            }
            DrawPersistedRoutes(map);
            InvalidateRouteSnapCache();
            NotifyRouteCleanup();
            RtgMapMarkerRegistry.Refresh();
            RtgMapConnections.Apply(map, empireId);
            RtgWorldScanSettings.Apply(preSurveyedWorld);
            if (preSurveyedWorld)
                ShutdownFogOfWar();
            else
                SetupFogOfWar(container, map?.routes);
            EnsureAllMarkersVisible(container);
            if (_reloadingAfterWorldReset)
            {
                RtgFogOfWar fog = RtgFogOfWar.Find();
                fog?.RevealAllMarkersAfterWorldReset(container);
                _reloadingAfterWorldReset = false;
            }
            SetupTerrainScatter();

            if (ShouldAnchorMarkersToTerrain())
                StartTerrainAnchorRoutine();
        }

        private static string FormatSpawnSummary(
            int settlements, int resources, int xeniteTripo, int xeniteProcedural)
        {
            string xeniteDetail = xeniteTripo + xeniteProcedural == 0
                ? "0 xenite"
                : $"{xeniteTripo + xeniteProcedural} xenite ({xeniteTripo} Tripo, {xeniteProcedural} procedural)";
            return $"Spawned {settlements} settlements, {resources} deposits ({xeniteDetail})";
        }

        /// <summary>
        /// Catches worlds seeded far from the Unity play camera (e.g. Denver default vs Orin POC).
        /// </summary>
        private void WarnIfNoDepositsNearPlayCenter(RtgWorldMap map)
        {
            if (map?.resources == null) return;

            int near = 0;
            foreach (RtgResourceNode r in map.resources)
            {
                if (r == null || !RtgTerrainDepositGuards.IsActivePocDeposit(r.resource_id))
                    continue;
                if (HaversineM(scatterCenterLat, scatterCenterLng, r.lat, r.lng) <= scatterRadiusMeters)
                    near++;
            }

            if (near == 0)
            {
                Debug.LogWarning(
                    $"[RTG] Xenite deposits spawned but none within {scatterRadiusMeters:0} m of play center " +
                    $"({scatterCenterLat:F4}, {scatterCenterLng:F4}). World was likely seeded at a different " +
                    "spawn (use New Game after the Orin spawn fix, or Join a Douglas-area session).");
            }
        }

        private bool ShouldAnchorMarkersToTerrain()
        {
            if (_pendingTerrainAnchors.Count == 0) return false;
            if (!Application.isPlaying) return false;
            return anchorDepositsToTerrain || anchorSettlementsToTerrain;
        }

        private void StartTerrainAnchorRoutine()
        {
            if (Application.isPlaying)
                StartCoroutine(AnchorMarkersToTerrain());
        }

        private IEnumerator AnchorMarkersToTerrain()
        {
            if (_pendingTerrainAnchors.Count == 0) yield break;

            RtgTerrainHeight terrainHeight = RtgTerrainHeight.FindOrCreate();
            int anchoredCount = 0;

            for (int attempt = 0; attempt < RtgTerrainDepositGuards.DepositAnchorMaxAttempts; attempt++)
            {
                if (attempt > 0)
                    yield return new WaitForSeconds(RtgTerrainDepositGuards.DepositAnchorRetryDelaySeconds);

                Cesium3DTileset tileset = RtgTerrainHeightSampler.ResolveTileset();
                if (tileset == null) continue;

                var requests = new RtgTerrainHeightSampler.SampleRequest[_pendingTerrainAnchors.Count];
                for (int i = 0; i < _pendingTerrainAnchors.Count; i++)
                {
                    MarkerAnchorPending pending = _pendingTerrainAnchors[i];
                    requests[i] = new RtgTerrainHeightSampler.SampleRequest
                    {
                        Longitude = pending.Longitude,
                        Latitude = pending.Latitude,
                        FallbackHeightM = groundHeightMeters,
                    };
                }

                double[] heights = null;
                yield return RtgTerrainHeightSampler.SampleHeightsCoroutine(
                    tileset,
                    requests,
                    sampled => heights = sampled);

                if (heights == null) continue;

                anchoredCount = 0;
                for (int i = 0; i < _pendingTerrainAnchors.Count && i < heights.Length; i++)
                {
                    MarkerAnchorPending pending = _pendingTerrainAnchors[i];
                    if (pending.Root == null) continue;

                    double groundM = terrainHeight != null
                        ? terrainHeight.ResolveDepositGroundHeight(
                            pending.Latitude, pending.Longitude, heights[i])
                        : heights[i];
                    double anchorHeightM = groundM + pending.SurfaceClearanceM;
                    AnchorAt(pending.Root, pending.Longitude, pending.Latitude, anchorHeightM);
                    anchoredCount++;
                }

                if (anchoredCount > 0)
                {
                    Debug.Log(
                        $"[RTG] Anchored {anchoredCount} marker(s) to terrain " +
                        $"(attempt {attempt + 1}/{RtgTerrainDepositGuards.DepositAnchorMaxAttempts}).");
                    break;
                }
            }

            if (anchoredCount == 0)
            {
                Debug.LogWarning(
                    "[RTG] Terrain anchoring failed — markers remain at ellipsoid fallback height. " +
                    "Enter Play mode and wait for Cesium tiles, or run Build Everything.");
            }

            _pendingTerrainAnchors.Clear();
        }

        private static void EnsureAllMarkersVisible(Transform container)
        {
            if (container == null) return;
            foreach (RtgMapMarker marker in container.GetComponentsInChildren<RtgMapMarker>(true))
                marker.gameObject.SetActive(true);
        }

        private static void ShutdownFogOfWar()
        {
            RtgFogOfWar fog = RtgFogOfWar.Find();
            if (fog == null) return;
            fog.ShutdownForPreSurvey();
            fog.enabled = false;
        }

        private void SetupTerrainScatter()
        {
            if (!Application.isPlaying) return;
            RtgTerrainScatter scatter = RtgTerrainScatter.Ensure(this);
            scatter?.OnMapSpawned();
        }

        private void SetupFogOfWar(Transform markersContainer, RtgRoute[] routes = null)
        {
            if (!Application.isPlaying) return;
            RtgFogOfWar fog = RtgFogOfWar.Ensure(this);
            if (fog != null) fog.OnMapSpawned(markersContainer, routes);
        }

        private void DrawPersistedRoutes(RtgWorldMap map)
        {
            RtgPersistedRouteDrawer drawer = RtgPersistedRouteDrawer.FindOrCreate();
            if (drawer == null) return;
            int routeCount = map?.routes?.Length ?? 0;
            drawer.SyncRoutes(map?.routes);
            if (routeCount > 0)
                Debug.Log($"[RTG] Drew {routeCount} persisted route(s) from map load.");
        }

        private static void NotifyRouteCleanup()
        {
#if UNITY_2023_1_OR_NEWER
            RtgRouteSession session = Object.FindFirstObjectByType<RtgRouteSession>();
#else
            RtgRouteSession session = Object.FindObjectOfType<RtgRouteSession>();
#endif
            session?.RequestRouteCleanupIfNeeded();
        }

        private static void InvalidateRouteSnapCache()
        {
#if UNITY_2023_1_OR_NEWER
            RtgRouteSession session = Object.FindFirstObjectByType<RtgRouteSession>();
#else
            RtgRouteSession session = Object.FindObjectOfType<RtgRouteSession>();
#endif
            session?.InvalidateSnapCache();
        }

        public void ClearMarkers()
        {
            Transform existing = transform.Find(MarkerContainerName);
            if (existing != null) DestroyObject(existing.gameObject);
            RtgPersistedRouteDrawer drawer = RtgPersistedRouteDrawer.FindOrCreate();
            drawer?.Clear();
        }

        /// <summary>Respawns resource deposits only (e.g. after xenite rotation tuning).</summary>
        public void RefreshResourceDepositsOnly()
        {
            if (LastMap?.resources == null)
            {
                Debug.LogWarning("[RTG] Cannot refresh deposits — no map loaded.");
                return;
            }

            Transform container = transform.Find(MarkerContainerName);
            if (container == null)
            {
                Debug.LogWarning("[RTG] Cannot refresh deposits — no marker container.");
                return;
            }

            var toDestroy = new List<GameObject>();
            foreach (RtgMapMarker marker in container.GetComponentsInChildren<RtgMapMarker>(true))
            {
                if (marker.kind == RtgMapMarker.Kind.Resource)
                    toDestroy.Add(marker.gameObject);
            }

            for (int i = _pendingTerrainAnchors.Count - 1; i >= 0; i--)
            {
                GameObject root = _pendingTerrainAnchors[i].Root;
                if (root == null || toDestroy.Contains(root))
                    _pendingTerrainAnchors.RemoveAt(i);
            }

            foreach (GameObject go in toDestroy)
                DestroyObject(go);

            int resources = 0, xeniteTripo = 0, xeniteProcedural = 0;
            foreach (RtgResourceNode r in LastMap.resources)
            {
                if (!RtgTerrainDepositGuards.IsActivePocDeposit(r.resource_id))
                    continue;

                bool usedTripo = SpawnResource(r, container);
                resources++;
                if (r.resource_id == "xenite")
                {
                    if (usedTripo) xeniteTripo++;
                    else xeniteProcedural++;
                }
            }

            RtgMapMarkerRegistry.Refresh();
            if (ShouldAnchorMarkersToTerrain())
                StartTerrainAnchorRoutine();

            string xeniteDetail = xeniteTripo + xeniteProcedural == 0
                ? "0 xenite"
                : $"{xeniteTripo + xeniteProcedural} xenite ({xeniteTripo} Tripo, {xeniteProcedural} procedural)";
            Debug.Log($"[RTG] Refreshed {resources} resource deposit(s) ({xeniteDetail}).");
        }

        /// <summary>After world reset — always re-fetch for LiveApi (never stale LastMap).</summary>
        public void ReloadMarkersAfterReset()
        {
            ReloadAfterWorldReset(preferSync: !Application.isPlaying);
        }

        private Transform ResetContainer()
        {
            ClearMarkers();
            var go = new GameObject(MarkerContainerName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private void SpawnSettlement(RtgSettlement s, Transform container)
        {
            Color color = AlignmentColor(s.alignment, s.is_goodie_hut != 0);

            double lat = s.lat, lng = s.lng;
            bool isGoodieHut = s.is_goodie_hut != 0 || s.tier == "goodie_hut";
            bool pinOnCorridor = isGoodieHut && s.id == _corridorGoodieId;
            string tapTag = ApplyTapTestScatter(ref lat, ref lng, pinOnCorridor, s.id);

            GameObject root = CreateMarkerRoot($"Echo Site — {s.name} ({s.tier})", container);
            RtgGroundMarkerVisual.BuildResult visual = RtgGroundMarkerVisual.BuildSettlement(
                root.transform,
                s.tier,
                isGoodieHut,
                color,
                GetEmissiveMaterial(color),
                GetGlowPadMaterial(color));
            AnchorAt(root, lng, lat, groundHeightMeters + groundMarkerClearanceM);
            if (anchorSettlementsToTerrain)
            {
                _pendingTerrainAnchors.Add(new MarkerAnchorPending
                {
                    Root = root,
                    Longitude = lng,
                    Latitude = lat,
                    SurfaceClearanceM = groundMarkerClearanceM,
                });
            }
            AddLabel(root.transform, $"{s.name}\n{TierLabel(s.tier)} · {s.alignment}{tapTag}",
                visual.LabelHeightM, groundLabelSizeFactor);
            root.AddComponent<RtgMapMarker>().Configure(
                RtgMapMarker.Kind.Settlement, s.id, s.name, s.tier, lat, lng);
            RtgMapMarkerRegistry.Register(root.GetComponent<RtgMapMarker>());
        }

        private bool SpawnResource(RtgResourceNode r, Transform container)
        {
            Color color = ResourceColor(r.resource_id);
            if (r.resource_id == "xenite")
                RtgTerrainDepositGuards.WarnIfXeniteColorDrift(color);

            double lat = r.lat, lng = r.lng;
            string tapTag = ApplyTapTestScatter(ref lat, ref lng, false, r.id);

            GameObject root = CreateMarkerRoot($"Resource — {r.resource_id} ({r.richness})", container);
            RtgTerrainDeposit.BuildResult visual = RtgTerrainDeposit.BuildEmbedded(
                root.transform,
                r.resource_id,
                r.richness,
                r.biome,
                color,
                GetEmissiveMaterial(color),
                GetDepositGlowMaterial(color));
            AnchorAt(root, lng, lat, groundHeightMeters);
            if (anchorDepositsToTerrain)
            {
                _pendingTerrainAnchors.Add(new MarkerAnchorPending
                {
                    Root = root,
                    Longitude = lng,
                    Latitude = lat,
                    SurfaceClearanceM = depositSurfaceClearanceM,
                });
            }

            string biomeTag = string.IsNullOrEmpty(r.biome) ? "" : $"\n{BiomeLabel(r.biome)}";
            AddLabel(root.transform, $"{ResourceName(r.resource_id)}\n{r.richness}{biomeTag}{tapTag}",
                visual.LabelHeightM, groundLabelSizeFactor);
            root.AddComponent<RtgMapMarker>().Configure(
                RtgMapMarker.Kind.Resource, r.id, ResourceName(r.resource_id), r.richness, lat, lng);
            RtgMapMarkerRegistry.Register(root.GetComponent<RtgMapMarker>());
            return visual.UsedTripoPrefab;
        }

        private string SelectCorridorGoodieTarget(RtgWorldMap map)
        {
            if (map?.settlements == null) return null;

            string bestId = null;
            double bestDist = double.MaxValue;
            foreach (RtgSettlement s in map.settlements)
            {
                if (s == null || (s.is_goodie_hut == 0 && s.tier != "goodie_hut")) continue;
                double dist = HaversineM(scatterCenterLat, scatterCenterLng, s.lat, s.lng);
                if (dist > scatterRadiusMeters || dist >= bestDist) continue;
                bestDist = dist;
                bestId = s.id;
            }
            return bestId;
        }

        /// <summary>
        /// Cycles markers into on-corridor / near / far buckets for tap-to-connect testing.
        /// The nearest Douglas goodie hut is pinned on the simulated tour's north corridor leg.
        /// Returns a short label suffix.
        /// </summary>
        private string ApplyTapTestScatter(
            ref double lat, ref double lng, bool isGoodieHut = false, string stableId = null)
        {
            if (isGoodieHut)
            {
                lat = scatterCenterLat + CorridorDLat * goodieHutCorridorFraction;
                lng = scatterCenterLng;
                return scatterForTapTest ? "\n◎ goodie hut · on route" : "";
            }

            if (!scatterForTapTest) return "";

            double dist = HaversineM(scatterCenterLat, scatterCenterLng, lat, lng);
            if (dist > scatterRadiusMeters)
                return "";

            // Stable bucket per marker so reload/claim does not reshuffle positions.
            int bucket = 0;
            if (!string.IsNullOrEmpty(stableId))
                bucket = Mathf.Abs(stableId.GetHashCode()) % 3;

            double eastM = bucket switch
            {
                1 => nearTapOffsetM,
                2 => farTapOffsetM,
                _ => 0,
            };

            if (eastM > 0)
            {
                double lngM = 111_320.0 * System.Math.Cos(lat * System.Math.PI / 180.0);
                lng += eastM / lngM;
            }

            return bucket switch
            {
                0 => "\n◎ tap: map",
                1 => "\n◎ tap: near",
                2 => "\n✕ tap: far",
                _ => "",
            };
        }

        private static double HaversineM(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6_371_000;
            double ToRad(double d) => d * System.Math.PI / 180.0;
            double dLat = ToRad(lat2 - lat1);
            double dLng = ToRad(lng2 - lng1);
            double a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
                       System.Math.Cos(ToRad(lat1)) * System.Math.Cos(ToRad(lat2)) *
                       System.Math.Sin(dLng / 2) * System.Math.Sin(dLng / 2);
            return 2 * R * System.Math.Asin(System.Math.Sqrt(a));
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        // Each marker is an unscaled root (which carries the CesiumGlobeAnchor) with
        // two children: the scaled beacon mesh and a billboarded text label. Keeping
        // the root at scale 1 means the label's text size is independent of the
        // (large) beacon scale.
        private static GameObject CreateMarkerRoot(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root;
        }

        // Outline offsets (8-way) for the faux text outline. Legacy TextMesh has no
        // outline, so we draw black copies around a lighter main copy.
        private static readonly Vector2[] OutlineDirections =
        {
            new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f),
            new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f),
        };

        private void AddLabel(Transform root, string text, float beaconSize, float sizeFactor)
        {
            var pivot = new GameObject("Label");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, beaconSize, 0f);
            pivot.AddComponent<RtgBillboard>();

            float charSize = Mathf.Max(1f, beaconSize * sizeFactor);
            float outline = charSize * 0.08f;

            // Black outline copies at z = 0, offset in the label plane.
            foreach (Vector2 dir in OutlineDirections)
            {
                Vector2 offset = dir.normalized * outline;
                CreateTextMesh(pivot.transform, text, charSize, Color.black,
                    new Vector3(offset.x, offset.y, 0f));
            }

            // Main text pulled slightly toward the camera (local -z) so the transparent
            // sort draws it on top of the outline copies.
            CreateTextMesh(pivot.transform, text, charSize, labelColor,
                new Vector3(0f, 0f, -charSize * 0.5f));
        }

        private static TextMesh CreateTextMesh(
            Transform parent, string text, float charSize, Color color, Vector3 localPosition)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = text;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = charSize;
            tm.color = color;

            var mr = go.GetComponent<MeshRenderer>();
            if (font != null) mr.sharedMaterial = font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return tm;
        }

        /// <summary>Adds a CesiumGlobeAnchor and positions the marker at lon/lat/height.</summary>
        private static void AnchorAt(GameObject go, double lon, double lat, double height)
        {
            CesiumGlobeAnchor anchor = go.GetComponent<CesiumGlobeAnchor>();
            if (anchor == null) anchor = go.AddComponent<CesiumGlobeAnchor>();
            anchor.SetPositionLongitudeLatitudeHeight(lon, lat, height);
        }

        private Material GetEmissiveMaterial(Color color)
        {
            if (_materialCache.TryGetValue(color, out Material cached) && cached != null)
                return cached;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = $"RTG_Beacon_{ColorUtility.ToHtmlStringRGB(color)}"
            };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.6f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2.2f); // glow
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            _materialCache[color] = mat;
            return mat;
        }

        private Material GetGlowPadMaterial(Color color)
        {
            if (_glowPadCache.TryGetValue(color, out Material cached) && cached != null)
                return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader)
            {
                name = $"RTG_GlowPad_{ColorUtility.ToHtmlStringRGB(color)}"
            };

            Color glow = color;
            glow.a = 0.42f;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", glow);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", glow);
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
            }

            _glowPadCache[color] = mat;
            return mat;
        }

        private Material GetDepositGlowMaterial(Color color)
        {
            if (_depositGlowCache.TryGetValue(color, out Material cached) && cached != null)
                return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader)
            {
                name = $"RTG_DepositGlow_{ColorUtility.ToHtmlStringRGB(color)}"
            };

            Color glow = color;
            glow.a = 0.18f;
            RtgTerrainDepositGuards.WarnIfDepositGlowTooStrong(glow.a);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", glow);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", glow);
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
            }

            _depositGlowCache[color] = mat;
            return mat;
        }

        private static Color AlignmentColor(string alignment, bool isGoodieHut)
        {
            if (isGoodieHut) return new Color(1.00f, 0.82f, 0.25f);   // gold
            switch (alignment)
            {
                case "friendly":      return new Color(0.35f, 1.00f, 0.55f); // green
                case "hostile":       return new Color(1.00f, 0.35f, 0.28f); // red-orange
                case "alien_enclave": return new Color(0.85f, 0.30f, 1.00f); // magenta
                default:              return new Color(0.35f, 0.85f, 1.00f); // neutral cyan
            }
        }

        private static Color ResourceColor(string resourceId)
        {
            switch (resourceId)
            {
                case "xenite":         return RtgTerrainDepositGuards.XeniteCanonicalColor;
                case "solari_dust":    return new Color(1.00f, 0.85f, 0.30f);
                case "ferracite":      return new Color(1.00f, 0.55f, 0.25f);
                case "lumin_spring":   return new Color(0.40f, 0.95f, 1.00f);
                case "quantium_shard": return new Color(0.65f, 0.45f, 1.00f);
                case "voidglass":      return new Color(0.70f, 0.80f, 1.00f);
                case "mycelium_core":  return new Color(0.70f, 1.00f, 0.35f);
                case "chrono_moss":    return new Color(0.35f, 0.90f, 0.75f);
                case "aegis_bark":     return new Color(0.70f, 0.60f, 0.35f);
                case "nebula_pearl":   return new Color(1.00f, 0.55f, 0.85f);
                default:               return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        private static string TierLabel(string tier)
        {
            switch (tier)
            {
                case "super_city": return "Super City";
                case "city":       return "City";
                case "town":       return "Town";
                case "settlement": return "Settlement";
                case "goodie_hut": return "Goodie Hut";
                default:           return tier;
            }
        }

        private static string BiomeLabel(string biome)
        {
            switch (biome)
            {
                case RtgBiomePalette.Plains: return "Xeno Plains";
                case RtgBiomePalette.Wasteland: return "Xeno Wasteland";
                case RtgBiomePalette.Wetland: return "Xeno Wetland";
                case RtgBiomePalette.FungalForest: return "Fungal Forest";
                case RtgBiomePalette.Highland: return "Xeno Highland";
                case RtgBiomePalette.Rift: return "Xeno Rift";
                case RtgBiomePalette.Water: return "Xeno Water";
                default: return biome;
            }
        }

        private static string ResourceName(string resourceId)
        {
            switch (resourceId)
            {
                case "xenite":         return "Xenite";
                case "solari_dust":    return "Solari Dust";
                case "ferracite":      return "Ferracite";
                case "lumin_spring":   return "Lumin Spring";
                case "quantium_shard": return "Quantium Shard";
                case "voidglass":      return "Voidglass";
                case "mycelium_core":  return "Mycelium Core";
                case "chrono_moss":    return "Chrono Moss";
                case "aegis_bark":     return "Aegis Bark";
                case "nebula_pearl":   return "Nebula Pearl";
                default:               return resourceId;
            }
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            DestroyImmediate(obj);
        }
    }
}
