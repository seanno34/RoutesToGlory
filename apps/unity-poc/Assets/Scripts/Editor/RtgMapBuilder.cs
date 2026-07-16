using System.IO;
using System.Net.Http;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CesiumForUnity;
using RoutesToGlory.Game;

namespace RoutesToGlory.EditorTools
{
    /// <summary>
    /// Editor menu commands that build the Routes to Glory map foundation and its
    /// alien styling in the open scene. Primary workflow: Regenerate Playable World
    /// (biome terrain, echo sites, player ship art, Tripo hull, glider) → Play →
    /// Reset &amp; Reload World as needed. Save the scene (Cmd+S) after edits.
    /// </summary>
    public static class RtgMapBuilder
    {
        // --- POC map origin: Douglas, WY (real-world anchor for the alien world) ---
        private const double OriginLongitude = -105.3819; // degrees, -180..180
        private const double OriginLatitude = 42.7597;    // degrees, -90..90
        private const double OriginHeight = 1476.0;        // meters above ellipsoid (~ground)

        // Camera starts this many meters above the origin for a nice overview.
        private const double CameraStartAltitude = 1200.0;

        // Fixed fly speed (m/s) for the camera. We disable Cesium's dynamic speed
        // because it measures altitude by raycasting terrain colliders, and we build
        // the terrain with createPhysicsMeshes = false (no colliders) — so dynamic
        // speed collapses to 0 and WASD does nothing. A fixed speed + scroll-wheel
        // adjustment is predictable and needs no colliders.
        private const float CameraFlySpeed = 300.0f;

        // Cesium ion asset IDs
        private const long CesiumWorldTerrainAssetId = 1;

        private const string GeoreferenceName = "RTG Georeference";
        private const string TerrainName = "RTG Terrain";
        private const string CameraName = "RTG Fly Camera";
        private const string EchoSitesName = "RTG Echo Sites";
        private const string TerrainScatterName = "RTG Terrain Scatter";
        private const string PlayerName = "RTG Player";
        private const string AlienMaterialPath = "Assets/Materials/RTG_AlienTerrain.mat";
        private const string BiomeMaterialPath = "Assets/Materials/RTG_AlienTerrainBiome.mat";
        private const string AlienSkyboxPath = "Assets/Materials/RTG_AlienSky.mat";

        private const string TripoSourceFolder =
            "Assets/TripoModels/futuristic_fighter_3d_model";
        private const string TripoSourceFbx =
            TripoSourceFolder + "/futuristic_fighter_3d_model.fbx";
        private const string XeniteTripoResourcesFolder = "Assets/Resources/RTG_Deposits";
        private const string XeniteTripoPrefabName = "xenite_rift";

        // Prototype stylized tiles: CARTO "dark, no labels" — free, keyless, dark
        // basemap with no text. Cesium's {y} is TMS (south-origin), so standard
        // XYZ (north-origin) providers need {reverseY}.
        private const string PrototypeOverlayUrl =
            "https://basemaps.cartocdn.com/dark_nolabels/{z}/{x}/{reverseY}.png";

        // Custom (MapTiler Cloud) stylized tiles. The map id + API key live in a
        // gitignored config file (see TileSourceConfigPath) so the key never lands
        // in the repo or a committed scene. MapTiler raster XYZ is standard
        // north-origin y, so we use Cesium's {reverseY}.
        // Format: https://api.maptiler.com/maps/{mapId}/256/{z}/{x}/{y}.png?key=...
        private const string MapTilerUrlFormat =
            "https://api.maptiler.com/maps/{0}/256/{{z}}/{{x}}/{{reverseY}}.png?key={1}";

        // Read relative to the Unity project root (…/apps/unity-poc/), NOT Assets/,
        // so Unity doesn't import it as an asset. Gitignored; see tilesource.local.json.example.
        private const string TileSourceConfigFileName = "tilesource.local.json";

        // MapTiler custom maps rasterize/overzoom well past the vector source zoom.
        private const int CustomOverlayMaxLevel = 20;

        /// <summary>Local, gitignored MapTiler credentials (see tilesource.local.json.example).</summary>
        [System.Serializable]
        private class TileSourceConfig
        {
            public string mapId;
            public string key;
        }

        // ------------------------------------------------------------------ //
        // Build everything
        // ------------------------------------------------------------------ //

        [MenuItem("Routes to Glory/Build Everything", priority = 0)]
        public static void BuildEverything()
        {
            CesiumGeoreference georeference = BuildBaseMapInternal();
            ApplyBiomeTerrainInternal(georeference);
            ApplyAtmosphereInternal();
            SetupFlyCameraInternal(georeference);
            MarkDirty(georeference);
            Debug.Log(
                "[RTG] Built everything (biome terrain + atmosphere + camera). " +
                "Enter Play mode to fly (WASD + mouse). Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Regenerate Playable World", priority = 3)]
        public static void RegeneratePlayableWorld()
        {
            CesiumGeoreference georeference = BuildBaseMapInternal();
            ApplyBiomeTerrainInternal(georeference);
            ApplyAtmosphereInternal();
            SetupFlyCameraInternal(georeference);

            RtgEchoSiteLoader loader = GetOrCreateEchoSiteLoader(georeference);
            if (loader.dataSource == RtgEchoSiteLoader.DataSource.SampleFile
                || string.IsNullOrWhiteSpace(loader.worldId))
            {
                loader.dataSource = RtgEchoSiteLoader.DataSource.SampleFile;
                loader.LoadSampleImmediate();
            }
            else if (Application.isPlaying)
            {
                loader.ReloadAfterWorldReset(preferSync: false);
            }
            else
            {
                loader.ReloadAfterWorldReset(preferSync: true);
            }

            bool syncedArt = RtgPlayerShipAssetSync.SyncShipArtFromSources();
            RtgPlayerLocation player = GetOrCreatePlayer(georeference);
            bool syncedHull = RtgPlayerShipAssetSync.SyncTripoHullFromSources(player);
            player.RegeneratePresentation();

            RtgTerrainScatter scatter = RtgTerrainScatter.Ensure(loader);
            if (scatter != null && Application.isPlaying)
                scatter.OnMapSpawned();

            Selection.activeGameObject = georeference.gameObject;
            MarkDirty(georeference);
            Debug.Log(
                "[RTG] Regenerated playable world — biome terrain, atmosphere, echo sites, " +
                "deposits, player ship art" + (syncedArt ? "" : " (art skipped)") +
                ", Tripo hull" + (syncedHull ? "" : " (hull skipped)") +
                ", and glider refreshed. Enter Play mode (or stay in Play) to test. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Reset & Reload World", priority = 5)]
        public static void ResetAndReloadWorld()
        {
            bool hasDevWorld = TryReadDevWorld(out RtgDevWorld dev);
            string message = hasDevWorld
                ? "Resets API progress (routes, claims, mines) for your dev world, clears " +
                  "markers/routes/fog, then reloads echo sites and resource deposits from " +
                  "the Echo Site loader's configured data source (Live API or sample file)."
                : "No rtg-dev-world.json found — API progress will NOT be reset. Clears " +
                  "markers/routes/fog and reloads from the Echo Site loader's current data source.";

            if (!EditorUtility.DisplayDialog(
                    "Reset & reload world?",
                    message + "\n\nTerrain, camera, and player are kept. Continue?",
                    "Reset & Reload",
                    "Cancel"))
            {
                return;
            }

            if (hasDevWorld && !CallResetProgressApi(dev))
                return;

            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;

            RtgEchoSiteLoader loader = FindByName<RtgEchoSiteLoader>(EchoSitesName);
            if (loader == null)
                loader = GetOrCreateEchoSiteLoader(georeference);

            ClearWorldMarkersRoutesAndFog(loader);
            bool reloaded = loader.ReloadAfterWorldReset(preferSync: true);
            RefreshPresentationAfterWorldReset(georeference);
            if (reloaded)
            {
                Debug.Log(
                    "[RTG] Reset & reload complete — " + loader.LastSpawnSummary +
                    (Application.isPlaying ? " Player, terrain, and camera refreshed." : " Save with Cmd+S."));
            }
            else
            {
                Debug.LogError(
                    "[RTG] Reset & reload finished but NO echo sites or deposits spawned. " +
                    "See errors above — verify API is running, worldId is set, or enable " +
                    "fallbackToSampleOnApiFailure on RTG Echo Sites.");
            }

            MarkDirty(georeference);
        }

        /// <summary>
        /// Keeps the glider, Cesium terrain material, and chase camera valid after marker reload.
        /// </summary>
        private static void RefreshPresentationAfterWorldReset(CesiumGeoreference georeference)
        {
            if (georeference == null) return;

            RtgPlayerLocation player = FindByName<RtgPlayerLocation>(PlayerName);
            if (player != null)
            {
                player.RegeneratePresentation();
            }
            else
            {
                Debug.LogWarning(
                    "[RTG] No RTG Player after reset — run Routes to Glory → Regenerate Playable World " +
                    "to restore the glider and camera follow.");
            }

            RefreshTerrainAfterWorldReset(georeference);
        }

        private static void RefreshTerrainAfterWorldReset(CesiumGeoreference georeference)
        {
            Cesium3DTileset terrain = GetOrCreateTerrain(georeference);
            if (terrain == null)
            {
                Debug.LogWarning(
                    "[RTG] RTG Terrain tileset missing — run Routes to Glory → Regenerate Playable World.");
                return;
            }

            ApplyBiomeTerrainInternal(georeference);
            Debug.Log("[RTG] Re-applied alien biome terrain after world reset.");
        }

        // ------------------------------------------------------------------ //
        // Scene setup (common)
        // ------------------------------------------------------------------ //

        [MenuItem("Routes to Glory/Connect Echo Sites to Live API", priority = 10)]
        public static void ConnectEchoSitesToLiveApi()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;

            if (!TryReadDevWorld(out RtgDevWorld dev))
            {
                Debug.LogError(
                    "[RTG] Could not read rtg-dev-world.json at the project root " +
                    "(apps/unity-poc/rtg-dev-world.json). Copy rtg-dev-world.json.example, " +
                    "seed a world in local MySQL, and paste its worldId.");
                return;
            }

            RtgEchoSiteLoader loader = GetOrCreateEchoSiteLoader(georeference);
            loader.dataSource = RtgEchoSiteLoader.DataSource.LiveApi;
            loader.apiBaseUrl = string.IsNullOrWhiteSpace(dev.apiBaseUrl)
                ? "http://localhost:3001/api"
                : dev.apiBaseUrl;
            loader.worldId = dev.worldId;
            loader.empireId = dev.empireId;
            loader.loadOnPlay = true;
            loader.ClearMarkers();

            Selection.activeGameObject = loader.gameObject;
            MarkDirty(georeference);
            Debug.Log(
                $"[RTG] Echo Sites bound to LIVE API {loader.apiBaseUrl} (world {dev.worldId}" +
                (string.IsNullOrEmpty(dev.accessCode) ? "" : $", code {dev.accessCode}") +
                "). Press Play or run Reset & Reload World to spawn beacons from MySQL. " +
                "Make sure the API is running (pnpm --filter @empire/api dev). Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Load Echo Sites (sample)", priority = 11)]
        public static void LoadEchoSites()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;

            RtgEchoSiteLoader loader = GetOrCreateEchoSiteLoader(georeference);
            loader.dataSource = RtgEchoSiteLoader.DataSource.SampleFile;
            loader.LoadSampleImmediate();
            EditorUtility.SetDirty(loader);
            MarkDirty(georeference);
            Debug.Log(
                "[RTG] Echo Sites + resource nodes loaded from sample-world-map.json. " +
                "Fly the camera to see the beacons. Save with Cmd+S. To use live data, run " +
                "Connect Echo Sites to Live API.");
        }

        [MenuItem("Routes to Glory/Setup Player (GPS)", priority = 15)]
        public static void SetupPlayer()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;

            RtgPlayerLocation player = GetOrCreatePlayer(georeference);
            player.RegeneratePresentation();
            Selection.activeGameObject = player.gameObject;
            MarkDirty(georeference);
            Debug.Log(
                "[RTG] Player (GPS) set up at the route start. Enter Play mode to watch it " +
                "walk the simulated route. Switch Source to Auto Pilot for the auto-drive test route, " +
                "or Manual for real on-device GPS. Tick 'Follow With Camera' to chase it. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Sync Xenite Deposit (Tripo)", priority = 16)]
        public static void SyncXeniteDeposit()
        {
            string sourceAssetPath = ResolveXeniteTripoSourceAssetPath();
            if (string.IsNullOrEmpty(sourceAssetPath))
            {
                EditorUtility.DisplayDialog(
                    "Xenite deposit missing",
                    "Import the Xenite vent from Tripo first.\n\n" +
                    "Expected under Assets/ as glowing_lava_crystal_3d_model (FBX/GLB).",
                    "OK");
                return;
            }

            string sourceFolder = Path.GetDirectoryName(sourceAssetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(sourceFolder))
            {
                Debug.LogError("[RTG] Could not resolve Xenite Tripo source folder.");
                return;
            }

            string targetFolder = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Resources/RTG_Deposits"));
            Directory.CreateDirectory(targetFolder);

            CopyTripoHullDirectory(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", sourceFolder)),
                targetFolder);
            AssetDatabase.Refresh();

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourceAssetPath);
            if (source == null)
            {
                Debug.LogError($"[RTG] Could not load Xenite Tripo asset from {sourceAssetPath}");
                return;
            }

            string prefabPath = $"{XeniteTripoResourcesFolder}/{XeniteTripoPrefabName}.prefab";
            GameObject instance = Object.Instantiate(source);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError($"[RTG] Failed to create Xenite prefab at {prefabPath}");
                    return;
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AssetDatabase.Refresh();
            Debug.Log(
                $"[RTG] Xenite deposit synced — source={sourceAssetPath} prefab={prefabPath}. " +
                "Enter Play mode (or Reset & Reload World) to respawn deposits at r-xenite-1.");
        }

        [MenuItem("Routes to Glory/Sync Player Ship Art", priority = 17)]
        public static void SyncPlayerShipArt()
        {
            if (!RtgPlayerShipAssetSync.SyncShipArtFromSources())
                return;

            RtgPlayerLocation player = FindByName<RtgPlayerLocation>(PlayerName);
            if (player != null)
                player.RefreshMarkerVisual();

            Debug.Log(
                "[RTG] Synced glider image(s) to Resources/RTG_PlayerShip. " +
                "glider_01 = map pin; glider_cockpit_01 / glider_cockpit_portrait_01 = cockpit overlays. Re-export to Xcode after changes.");
        }

        [MenuItem("Routes to Glory/Sync Tripo Ship Hull", priority = 18)]
        public static void SyncTripoShipHull()
        {
            string sourcePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", TripoSourceFbx));
            if (!File.Exists(sourcePath)
                && !File.Exists(Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", RtgPlayerShipAssetSync.TripoResourcesFbx))))
            {
                EditorUtility.DisplayDialog(
                    "Tripo hull missing",
                    "Import the glider from Tripo first.\n\nExpected:\n" + TripoSourceFbx,
                    "OK");
                return;
            }

            RtgPlayerLocation player = FindByName<RtgPlayerLocation>(PlayerName);
            if (player == null)
            {
                if (!RtgPlayerShipAssetSync.SyncTripoHullFromSources(null))
                    return;
                Debug.LogWarning(
                    "[RTG] Tripo hull copied to Resources, but no RTG Player was found. " +
                    "Run Setup Player (GPS) or Regenerate Playable World.");
                return;
            }

            if (!RtgPlayerShipAssetSync.SyncTripoHullFromSources(player))
                return;

            player.RefreshMarkerVisual();
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(player);
                MarkDirty(player.GetComponentInParent<CesiumGeoreference>());
            }

            Debug.Log(
                "[RTG] Tripo hull synced to Resources and assigned on RTG Player. Rebuild for device.");
        }

        [MenuItem("Routes to Glory/Apply Ship Tuning to Player", priority = 19)]
        public static void ApplyShipTuningToPlayer()
        {
            if (!RtgShipTuningConfig.TryLoad(out RtgShipTuningConfig.ShipTuningFile tuning))
            {
                EditorUtility.DisplayDialog(
                    "Ship tuning missing",
                    "No rtg-ship-tuning.json found.\n\n" +
                    "Tune in Play mode (Settings → Hull orientation → Save tuning), or copy " +
                    "rtg-ship-tuning.json.example to rtg-ship-tuning.json.",
                    "OK");
                return;
            }

            RtgPlayerLocation player = FindByName<RtgPlayerLocation>(PlayerName);
            if (player == null)
            {
                Debug.LogWarning("[RTG] No RTG Player found. Run Setup Player (GPS) first.");
                return;
            }

            if (!Application.isPlaying)
                Undo.RecordObject(player, "Apply Ship Tuning");
            RtgShipTuningConfig.ApplyTo(player, tuning);
            if (Application.isPlaying)
                player.RefreshMarkerVisual();
            else
            {
                EditorUtility.SetDirty(player);
                MarkDirty(player.GetComponentInParent<CesiumGeoreference>());
            }

            Debug.Log(
                $"[RTG] Applied {RtgShipTuningConfig.FileName} to RTG Player — " +
                $"euler={tuning.hullEulerOffset} heading={tuning.headingOffsetDegrees} " +
                $"autoOrient={tuning.autoOrientImportedHull} " +
                $"customPorts={tuning.useCustomEnginePorts}");
        }

        // ------------------------------------------------------------------ //
        // Advanced / individual build steps
        // ------------------------------------------------------------------ //

        [MenuItem("Routes to Glory/Advanced/Apply Biome Terrain", priority = 20)]
        public static void ApplyBiomeTerrain()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;
            ApplyBiomeTerrainInternal(georeference);
            MarkDirty(georeference);
            Debug.Log("[RTG] Alien biome terrain applied (Voronoi regions). Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Advanced/Build Base Map", priority = 21)]
        public static void BuildBaseMap()
        {
            CesiumGeoreference georeference = BuildBaseMapInternal();
            Selection.activeGameObject = georeference.gameObject;
            FrameSelectedInSceneView();
            MarkDirty(georeference);
            Debug.Log("[RTG] Base map built (raw terrain, no imagery). Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Advanced/Apply Alien Material", priority = 22)]
        public static void ApplyAlienMaterial()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;
            ApplyAlienMaterialInternal(georeference);
            MarkDirty(georeference);
            Debug.Log("[RTG] Alien material applied to terrain. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Advanced/Apply Atmosphere & Lighting", priority = 23)]
        public static void ApplyAtmosphere()
        {
            ApplyAtmosphereInternal();
            if (!Application.isPlaying)
                EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[RTG] Atmosphere, fog, and lighting applied. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Advanced/Setup Fly Camera", priority = 24)]
        public static void SetupFlyCamera()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;
            SetupFlyCameraInternal(georeference);
            MarkDirty(georeference);
            Debug.Log("[RTG] Fly camera set up. Enter Play mode to move (WASD + mouse). Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Advanced/Apply Stylized Overlay (prototype)", priority = 24)]
        public static void ApplyStylizedOverlay()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;
            ApplyStylizedOverlayInternal(georeference);
            MarkDirty(georeference);
            Debug.Log(
                "[RTG] Stylized raster overlay applied (CARTO dark, no labels). " +
                "Terrain material override cleared so the overlay renders. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Advanced/Apply Custom (MapTiler) Overlay", priority = 25)]
        public static void ApplyCustomOverlay()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;

            TileSourceConfig config = LoadTileSourceConfig();
            if (config == null) return; // LoadTileSourceConfig already logged why.

            string url = string.Format(MapTilerUrlFormat, config.mapId, config.key);
            SetRasterOverlayInternal(georeference, url, CustomOverlayMaxLevel);
            MarkDirty(georeference);
            Debug.Log(
                $"[RTG] Custom MapTiler overlay applied (map '{config.mapId}'). " +
                "Terrain material override cleared so the overlay renders. Save with Cmd+S. " +
                "Do NOT commit the scene — it embeds your API key.");
        }

        [MenuItem("Routes to Glory/Advanced/Remove Stylized Overlay", priority = 26)]
        public static void RemoveStylizedOverlay()
        {
            CesiumGeoreference georeference = FindByName<CesiumGeoreference>(GeoreferenceName);
            if (georeference == null) { Debug.Log("[RTG] No RTG map found."); return; }

            Cesium3DTileset terrain = GetOrCreateTerrain(georeference);
            var overlay = terrain.GetComponent<CesiumUrlTemplateRasterOverlay>();
            if (overlay != null) Undo.DestroyObjectImmediate(overlay);
            terrain.RecreateTileset();
            MarkDirty(georeference);
            Debug.Log("[RTG] Removed stylized overlay. Run 'Apply Alien Material' to restore the tint.");
        }

        [MenuItem("Routes to Glory/Advanced/Setup Fog Of War", priority = 27)]
        public static void SetupFogOfWar()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;

            RtgEchoSiteLoader loader = FindByName<RtgEchoSiteLoader>(EchoSitesName);
            RtgFogOfWar fog = RtgFogOfWar.Ensure(loader);
            if (fog == null)
            {
                Debug.LogError("[RTG] Could not create fog of war — georeference missing?");
                return;
            }

            Selection.activeGameObject = fog.gameObject;
            MarkDirty(georeference);
            Debug.Log(
                "[RTG] Fog of war component added. In Play mode it fetches explored tiles " +
                "from the API and renders shader fog over hidden areas. Run Connect Echo Sites " +
                "to Live API first for live data. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Advanced/Setup Terrain Scatter", priority = 28)]
        public static void SetupTerrainScatter()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;

            RtgEchoSiteLoader loader = FindByName<RtgEchoSiteLoader>(EchoSitesName);
            RtgTerrainScatter scatter = RtgTerrainScatter.Ensure(loader);
            if (scatter == null)
            {
                Debug.LogError("[RTG] Could not create terrain scatter — georeference missing?");
                return;
            }

            Selection.activeGameObject = scatter.gameObject;
            MarkDirty(georeference);
            Debug.Log(
                "[RTG] Terrain scatter added. In Play mode it dresses revealed tiles around " +
                "the player with procedural alien trees, rocks, and brush. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Advanced/Setup Pathfinder Beam", priority = 29)]
        public static void SetupPathfinderBeam()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;

            RtgPlayerLocation player = GetOrCreatePlayer(georeference);
            RtgPathfinderBeam beam = RtgPathfinderBeam.Ensure(player);
            if (beam == null)
            {
                Debug.LogError("[RTG] Could not create Pathfinder beam — player missing?");
                return;
            }

            player.EditorApplyPathfinderBeamSettings();
            Selection.activeGameObject = player.gameObject;
            MarkDirty(georeference);
            Debug.Log(
                "[RTG] Pathfinder beam added to the player. The corridor lance activates " +
                "when scatter props enter detection range (~115 m). Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Clear Echo Sites", priority = 40)]
        public static void ClearEchoSites()
        {
            RtgEchoSiteLoader loader = FindByName<RtgEchoSiteLoader>(EchoSitesName);
            if (loader == null) { Debug.Log("[RTG] No Echo Sites to clear."); return; }
            loader.ClearMarkers();
            MarkDirty(loader.GetComponentInParent<CesiumGeoreference>());
            Debug.Log("[RTG] Cleared Echo Site markers. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Clear Map", priority = 41)]
        public static void ClearMap()
        {
            CesiumGeoreference existing = FindByName<CesiumGeoreference>(GeoreferenceName);
            if (existing == null)
            {
                Debug.Log("[RTG] No RTG map found to clear.");
                return;
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
            if (!Application.isPlaying)
                EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[RTG] Cleared RTG map. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Remove Player", priority = 42)]
        public static void RemovePlayer()
        {
            RtgPlayerLocation player = FindByName<RtgPlayerLocation>(PlayerName);
            if (player == null) { Debug.Log("[RTG] No player to remove."); return; }
            CesiumGeoreference georeference = player.GetComponentInParent<CesiumGeoreference>();
            Undo.DestroyObjectImmediate(player.gameObject);
            MarkDirty(georeference);
            Debug.Log("[RTG] Removed player. Save with Cmd+S.");
        }

        private static string ResolveXeniteTripoSourceAssetPath()
        {
            foreach (string path in RtgTerrainDepositGuards.XeniteTripoImportCandidatePaths)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    return path;
            }

            string[] guids = AssetDatabase.FindAssets("glowing_lava_crystal");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", System.StringComparison.Ordinal))
                    continue;
                if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                        return path;
                }
            }

            return null;
        }

        private static void CopyTripoHullDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = dir.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(targetDir, relative));
            }

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta"))
                    continue;

                string relative = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
                string destination = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? targetDir);
                File.Copy(file, destination, true);
            }
        }

        private static bool CallResetProgressApi(RtgDevWorld dev)
        {
            string apiBase = string.IsNullOrWhiteSpace(dev.apiBaseUrl)
                ? "http://localhost:3001/api"
                : dev.apiBaseUrl.TrimEnd('/');
            string url = $"{apiBase}/worlds/{dev.worldId}/reset-progress";
            string json = $"{{\"confirm\":true,\"empireId\":\"{dev.empireId}\"}}";

            try
            {
                using var client = new HttpClient();
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = client.PostAsync(url, content).GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    Debug.Log($"[RTG] Dev world reset complete: {body}");
                    return true;
                }

                Debug.LogError($"[RTG] Dev world reset failed ({(int)response.StatusCode}): {body}");
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogError(
                    $"[RTG] Dev world reset failed: {ex.Message}. " +
                    "Is the API running (pnpm --filter @empire/api dev)?");
                return false;
            }
        }

        private static void ClearWorldMarkersRoutesAndFog(RtgEchoSiteLoader loader)
        {
            if (loader == null) return;

            loader.ClearMarkers();

            RtgPersistedRouteDrawer drawer = FindPersistedRouteDrawer();
            drawer?.Clear();

            RtgFogOfWar fog = RtgFogOfWar.Find();
            fog?.ClearFog();
        }

        private static RtgPersistedRouteDrawer FindPersistedRouteDrawer()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<RtgPersistedRouteDrawer>();
#else
            return Object.FindObjectOfType<RtgPersistedRouteDrawer>();
#endif
        }

        // ------------------------------------------------------------------ //
        // Implementation
        // ------------------------------------------------------------------ //

        private static CesiumGeoreference BuildBaseMapInternal()
        {
            CesiumGeoreference georeference = GetOrCreateGeoreference();
            georeference.SetOriginLongitudeLatitudeHeight(
                OriginLongitude, OriginLatitude, OriginHeight);

            Cesium3DTileset terrain = GetOrCreateTerrain(georeference);
            terrain.tilesetSource = CesiumDataSource.FromCesiumIon;
            terrain.ionAssetID = CesiumWorldTerrainAssetId;
            // No imagery overlay on purpose: raw terrain = alien canvas.
            // Skip collision meshes: not needed for a visual POC, and baking them
            // spams PhysX "large triangle" warnings on terrain tiles.
            terrain.createPhysicsMeshes = false;
            return georeference;
        }

        private static void ApplyAlienMaterialInternal(CesiumGeoreference georeference)
        {
            Material alien = GetOrCreateAlienMaterial();
            Cesium3DTileset terrain = GetOrCreateTerrain(georeference);
            terrain.opaqueMaterial = alien;
            terrain.RecreateTileset();
        }

        /// <summary>
        /// Applies the Voronoi biome shader (xeno_rift, crystal flats, etc.) instead of flat teal.
        /// </summary>
        private static void ApplyBiomeTerrainInternal(CesiumGeoreference georeference)
        {
            Cesium3DTileset terrain = GetOrCreateTerrain(georeference);
            terrain.tilesetSource = CesiumDataSource.FromCesiumIon;
            terrain.ionAssetID = CesiumWorldTerrainAssetId;
            terrain.createPhysicsMeshes = false;

            RtgTerrainMaterialController controller = terrain.GetComponent<RtgTerrainMaterialController>();
            if (controller == null)
            {
                if (!Application.isPlaying)
                    controller = Undo.AddComponent<RtgTerrainMaterialController>(terrain.gameObject);
                else
                    controller = terrain.gameObject.AddComponent<RtgTerrainMaterialController>();
            }

            Material biomeMat = AssetDatabase.LoadAssetAtPath<Material>(BiomeMaterialPath);
            if (biomeMat != null)
                controller.biomeMaterial = biomeMat;

            controller.terrainTileset = terrain;
            controller.disableRasterOverlays = true;
            controller.applyTaxonomyPalette = true;
            controller.followPlayerHeight = true;

            CesiumUrlTemplateRasterOverlay overlay =
                terrain.GetComponent<CesiumUrlTemplateRasterOverlay>();
            if (overlay != null)
            {
                if (!Application.isPlaying)
                    Undo.DestroyObjectImmediate(overlay);
                else
                    Object.Destroy(overlay);
            }

            controller.Apply();
            terrain.RecreateTileset();
        }

        private static void ApplyStylizedOverlayInternal(CesiumGeoreference georeference)
        {
            SetRasterOverlayInternal(georeference, PrototypeOverlayUrl, 18);
        }

        /// <summary>
        /// Drapes a web-mercator XYZ raster overlay on the terrain. Clears the flat
        /// alien material (which overrides Cesium's shader and would hide the overlay)
        /// so Cesium's default material samples and renders the tiles.
        /// </summary>
        private static void SetRasterOverlayInternal(
            CesiumGeoreference georeference, string templateUrl, int maximumLevel)
        {
            Cesium3DTileset terrain = GetOrCreateTerrain(georeference);
            terrain.opaqueMaterial = null;

            CesiumUrlTemplateRasterOverlay overlay =
                terrain.GetComponent<CesiumUrlTemplateRasterOverlay>();
            if (overlay == null)
                overlay = terrain.gameObject.AddComponent<CesiumUrlTemplateRasterOverlay>();

            overlay.projection = CesiumUrlTemplateRasterOverlayProjection.WebMercator;
            overlay.maximumLevel = maximumLevel;
            overlay.templateUrl = templateUrl;

            terrain.RecreateTileset();
        }

        /// <summary>
        /// Loads MapTiler credentials from the gitignored tilesource.local.json at the
        /// Unity project root. Returns null (and logs a helpful error) if it's missing
        /// or not filled in, so the API key never has to be hardcoded.
        /// </summary>
        private static TileSourceConfig LoadTileSourceConfig()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string path = Path.Combine(projectRoot, TileSourceConfigFileName);

            if (!File.Exists(path))
            {
                Debug.LogError(
                    $"[RTG] Missing {TileSourceConfigFileName} at project root. " +
                    $"Copy {TileSourceConfigFileName}.example to {TileSourceConfigFileName} " +
                    "and fill in your MapTiler mapId + key.");
                return null;
            }

            TileSourceConfig config;
            try
            {
                config = JsonUtility.FromJson<TileSourceConfig>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RTG] Failed to parse {TileSourceConfigFileName}: {e.Message}");
                return null;
            }

            if (config == null || string.IsNullOrWhiteSpace(config.mapId)
                || string.IsNullOrWhiteSpace(config.key))
            {
                Debug.LogError(
                    $"[RTG] {TileSourceConfigFileName} is missing 'mapId' or 'key'. " +
                    "Fill both in from your MapTiler Cloud account.");
                return null;
            }

            return config;
        }

        private static void ApplyAtmosphereInternal()
        {
            // Alien sky (procedural skybox tinted violet, dark-teal ground).
            RenderSettings.skybox = GetOrCreateAlienSkybox();

            // Moody alien haze. Linear fog is predictable at terrain (km) scale;
            // fog color is matched to the sky horizon so they blend.
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.34f, 0.22f, 0.46f); // violet haze
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 1500f;
            RenderSettings.fogEndDistance = 18000f;

            // Derive ambient light from the alien sky for a cohesive mood.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;

            Light sun = FindMainDirectionalLight();
            if (sun != null)
            {
                // Use plain Color mode so the tint applies directly (not blended
                // through a Kelvin color-temperature filter).
                sun.useColorTemperature = false;
                sun.color = new Color(0.80f, 0.60f, 1.00f); // alien sun tint
                sun.intensity = 1.15f;
                sun.transform.rotation = Quaternion.Euler(28f, 150f, 0f);
            }

            // Recompute ambient/reflection probes from the new skybox.
            DynamicGI.UpdateEnvironment();
        }

        private static void SetupFlyCameraInternal(CesiumGeoreference georeference)
        {
            GameObject camGo = GetOrCreateCameraObject(georeference);

            Camera cam = camGo.GetComponent<Camera>();
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 100000f);
            cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, 1f);

            // CesiumOriginShift requires a CesiumGlobeAnchor (added automatically),
            // but add it first so we can position the camera precisely.
            CesiumGlobeAnchor anchor = camGo.GetComponent<CesiumGlobeAnchor>();
            if (anchor == null) anchor = camGo.AddComponent<CesiumGlobeAnchor>();

            if (camGo.GetComponent<CesiumOriginShift>() == null)
                camGo.AddComponent<CesiumOriginShift>();

            CesiumCameraController controller = camGo.GetComponent<CesiumCameraController>();
            if (controller == null) controller = camGo.AddComponent<CesiumCameraController>();
            // See CameraFlySpeed: dynamic speed needs terrain colliders we don't bake.
            controller.enableDynamicSpeed = false;
            controller.defaultMaximumSpeed = CameraFlySpeed;

            if (camGo.GetComponent<AudioListener>() == null)
                camGo.AddComponent<AudioListener>();

            anchor.SetPositionLongitudeLatitudeHeight(
                OriginLongitude, OriginLatitude, OriginHeight + CameraStartAltitude);
            // Steeper initial tilt so "W" heads toward the ground, not the horizon.
            camGo.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private static CesiumGeoreference GetOrCreateGeoreference()
        {
            CesiumGeoreference existing = FindByName<CesiumGeoreference>(GeoreferenceName);
            if (existing != null) return existing;

            var go = new GameObject(GeoreferenceName);
            Undo.RegisterCreatedObjectUndo(go, "Create RTG Georeference");
            return go.AddComponent<CesiumGeoreference>();
        }

        private static Cesium3DTileset GetOrCreateTerrain(CesiumGeoreference georeference)
        {
            foreach (Transform child in georeference.transform)
            {
                var existing = child.GetComponent<Cesium3DTileset>();
                if (existing != null && child.name == TerrainName) return existing;
            }

            var go = new GameObject(TerrainName);
            Undo.RegisterCreatedObjectUndo(go, "Create RTG Terrain");
            go.transform.SetParent(georeference.transform, false);
            return go.AddComponent<Cesium3DTileset>();
        }

        private static GameObject GetOrCreateCameraObject(CesiumGeoreference georeference)
        {
            GameObject existing = null;
            foreach (Transform child in georeference.transform)
            {
                if (child.name == CameraName)
                {
                    existing = child.gameObject;
                    break;
                }
            }

            if (existing == null)
            {
                // Reuse the scene's main camera if present; otherwise make one.
                Camera main = Camera.main;
                if (main != null)
                {
                    existing = main.gameObject;
                    existing.name = CameraName;
                }
                else
                {
                    existing = new GameObject(CameraName);
                    Undo.RegisterCreatedObjectUndo(existing, "Create RTG Camera");
                    existing.AddComponent<Camera>();
                    existing.tag = "MainCamera";
                }
            }

            existing.transform.SetParent(georeference.transform, false);
            return existing;
        }

        private static RtgEchoSiteLoader GetOrCreateEchoSiteLoader(CesiumGeoreference georeference)
        {
            Transform existing = georeference.transform.Find(EchoSitesName);
            GameObject go = existing != null ? existing.gameObject : null;
            if (go == null)
            {
                go = new GameObject(EchoSitesName);
                Undo.RegisterCreatedObjectUndo(go, "Create RTG Echo Sites");
                go.transform.SetParent(georeference.transform, false);
            }

            RtgEchoSiteLoader loader = go.GetComponent<RtgEchoSiteLoader>();
            if (loader == null) loader = go.AddComponent<RtgEchoSiteLoader>();
            return loader;
        }

        // Local, gitignored pointer to a world seeded in the developer's own MySQL.
        // Lives at the Unity project root (apps/unity-poc/rtg-dev-world.json), one
        // level above Assets/.
        [System.Serializable]
        private struct RtgDevWorld
        {
            public string apiBaseUrl;
            public string worldId;
            public string empireId;
            public string accessCode;
            public string slug;
        }

        private static bool TryReadDevWorld(out RtgDevWorld dev)
        {
            dev = default;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "rtg-dev-world.json"));
            if (!File.Exists(path)) return false;

            try
            {
                dev = JsonUtility.FromJson<RtgDevWorld>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RTG] Failed to parse rtg-dev-world.json: {e.Message}");
                return false;
            }

            return !string.IsNullOrWhiteSpace(dev.worldId);
        }

        private static RtgPlayerLocation GetOrCreatePlayer(CesiumGeoreference georeference)
        {
            Transform existing = georeference.transform.Find(PlayerName);
            GameObject go = existing != null ? existing.gameObject : null;
            if (go == null)
            {
                go = new GameObject(PlayerName);
                Undo.RegisterCreatedObjectUndo(go, "Create RTG Player");
                go.transform.SetParent(georeference.transform, false);
            }

            RtgPlayerLocation player = go.GetComponent<RtgPlayerLocation>();
            if (player == null) player = go.AddComponent<RtgPlayerLocation>();
            return player;
        }

        private static Material GetOrCreateAlienMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(AlienMaterialPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.14f, 0.40f, 0.44f)); // alien teal
            mat.SetFloat("_Smoothness", 0.35f);
            mat.SetFloat("_Metallic", 0.10f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.04f, 0.16f, 0.20f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            AssetDatabase.CreateAsset(mat, AlienMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static Material GetOrCreateAlienSkybox()
        {
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(AlienSkyboxPath);
            if (sky == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, AlienSkyboxPath);
            }

            // Re-apply settings every run so tuning takes effect on rebuild.
            sky.SetColor("_SkyTint", new Color(0.42f, 0.30f, 0.62f));    // violet sky
            sky.SetColor("_GroundColor", new Color(0.10f, 0.13f, 0.14f)); // dark teal
            sky.SetFloat("_AtmosphereThickness", 1.35f);                  // thicker haze
            sky.SetFloat("_Exposure", 1.1f);
            sky.SetFloat("_SunSize", 0.045f);
            AssetDatabase.SaveAssets();
            return sky;
        }

        private static Light FindMainDirectionalLight()
        {
            Light[] lights = Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Light l in lights)
            {
                if (l.type == LightType.Directional) return l;
            }
            return null;
        }

        private static CesiumGeoreference RequireGeoreference()
        {
            CesiumGeoreference georeference = FindByName<CesiumGeoreference>(GeoreferenceName);
            if (georeference == null)
            {
                Debug.LogWarning("[RTG] No base map found. Run Advanced/Build Base Map or Build Everything first.");
            }
            return georeference;
        }

        private static T FindByName<T>(string name) where T : Component
        {
            T[] all = Object.FindObjectsByType<T>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (T candidate in all)
            {
                if (candidate.gameObject.name == name) return candidate;
            }
            return null;
        }

        private static void MarkDirty(CesiumGeoreference georeference)
        {
            if (Application.isPlaying || georeference == null)
                return;

            EditorSceneManager.MarkSceneDirty(georeference.gameObject.scene);
        }

        private static void FrameSelectedInSceneView()
        {
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
        }
    }
}
