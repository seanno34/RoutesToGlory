using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CesiumForUnity;

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
        private const double CameraStartAltitude = 3000.0;

        // Cesium ion asset IDs
        private const long CesiumWorldTerrainAssetId = 1;

        private const string GeoreferenceName = "RTG Georeference";
        private const string TerrainName = "RTG Terrain";
        private const string CameraName = "RTG Fly Camera";
        private const string AlienMaterialPath = "Assets/Materials/RTG_AlienTerrain.mat";
        private const string AlienSkyboxPath = "Assets/Materials/RTG_AlienSky.mat";

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
            if (camGo.GetComponent<CesiumCameraController>() == null)
                camGo.AddComponent<CesiumCameraController>();

            anchor.SetPositionLongitudeLatitudeHeight(
                OriginLongitude, OriginLatitude, OriginHeight + CameraStartAltitude);
            camGo.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);
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
