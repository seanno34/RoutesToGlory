using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CesiumForUnity;
using RoutesToGlory.Game;

namespace RoutesToGlory.EditorTools
{
    /// <summary>
    /// Editor menu commands that build the Routes to Glory map foundation and its
    /// alien styling in the open scene. Run from the "Routes to Glory" menu, then
    /// save the scene (Cmd+S) to persist. Everything is authored via the Cesium C#
    /// API + RenderSettings so the scene stays reproducible and reviewable.
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
        private const string AlienMaterialPath = "Assets/Materials/RTG_AlienTerrain.mat";
        private const string AlienSkyboxPath = "Assets/Materials/RTG_AlienSky.mat";

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
            ApplyAlienMaterialInternal(georeference);
            ApplyAtmosphereInternal();
            SetupFlyCameraInternal(georeference);
            MarkDirty(georeference);
            Debug.Log("[RTG] Built everything. Enter Play mode to fly (WASD + mouse). Save with Cmd+S.");
        }

        // ------------------------------------------------------------------ //
        // Individual steps
        // ------------------------------------------------------------------ //

        [MenuItem("Routes to Glory/1. Build Base Map", priority = 20)]
        public static void BuildBaseMap()
        {
            CesiumGeoreference georeference = BuildBaseMapInternal();
            Selection.activeGameObject = georeference.gameObject;
            FrameSelectedInSceneView();
            MarkDirty(georeference);
            Debug.Log("[RTG] Base map built (raw terrain, no imagery). Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/2. Apply Alien Material", priority = 21)]
        public static void ApplyAlienMaterial()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;
            ApplyAlienMaterialInternal(georeference);
            MarkDirty(georeference);
            Debug.Log("[RTG] Alien material applied to terrain. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/3. Apply Atmosphere & Lighting", priority = 22)]
        public static void ApplyAtmosphere()
        {
            ApplyAtmosphereInternal();
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[RTG] Atmosphere, fog, and lighting applied. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/4. Setup Fly Camera", priority = 23)]
        public static void SetupFlyCamera()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;
            SetupFlyCameraInternal(georeference);
            MarkDirty(georeference);
            Debug.Log("[RTG] Fly camera set up. Enter Play mode to move (WASD + mouse). Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/5. Apply Stylized Overlay (prototype)", priority = 24)]
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

        [MenuItem("Routes to Glory/5b. Apply Custom (MapTiler) Overlay", priority = 25)]
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

        [MenuItem("Routes to Glory/Remove Stylized Overlay", priority = 26)]
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

        [MenuItem("Routes to Glory/6. Load Echo Sites (sample)", priority = 27)]
        public static void LoadEchoSites()
        {
            CesiumGeoreference georeference = RequireGeoreference();
            if (georeference == null) return;

            RtgEchoSiteLoader loader = GetOrCreateEchoSiteLoader(georeference);
            loader.LoadSampleImmediate();
            MarkDirty(georeference);
            Debug.Log(
                "[RTG] Echo Sites + resource nodes loaded from sample-world-map.json. " +
                "Fly the camera to see the beacons. Save with Cmd+S. To use live data, set the " +
                "RTG Echo Sites object's Data Source to LiveApi + fill in the World id.");
        }

        [MenuItem("Routes to Glory/Clear Echo Sites", priority = 41)]
        public static void ClearEchoSites()
        {
            RtgEchoSiteLoader loader = FindByName<RtgEchoSiteLoader>(EchoSitesName);
            if (loader == null) { Debug.Log("[RTG] No Echo Sites to clear."); return; }
            loader.ClearMarkers();
            MarkDirty(loader.GetComponentInParent<CesiumGeoreference>());
            Debug.Log("[RTG] Cleared Echo Site markers. Save with Cmd+S.");
        }

        [MenuItem("Routes to Glory/Clear Map", priority = 40)]
        public static void ClearMap()
        {
            CesiumGeoreference existing = FindByName<CesiumGeoreference>(GeoreferenceName);
            if (existing == null)
            {
                Debug.Log("[RTG] No RTG map found to clear.");
                return;
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[RTG] Cleared RTG map. Save with Cmd+S.");
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
                Debug.LogWarning("[RTG] No base map found. Run 'Build Base Map' first.");
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
            if (georeference != null)
                EditorSceneManager.MarkSceneDirty(georeference.gameObject.scene);
        }

        private static void FrameSelectedInSceneView()
        {
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
        }
    }
}
