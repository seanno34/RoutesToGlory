using System.IO;
using UnityEditor;
using UnityEngine;
using RoutesToGlory.Game;

namespace RoutesToGlory.EditorTools
{
    /// <summary>
    /// Copies player ship art and Tripo hull into Resources so iOS/Android builds
    /// render the full glider without manual editor menu steps.
    ///
    /// TRIPO DEVICE PIPELINE (Jul 2026 — do not regress):
    /// 1. <see cref="SyncTripoHullFromSources"/> copies TripoModels → Resources/TripoGlider (incl. textures).
    /// 2. <see cref="EnsureTripoAlbedoInResources"/> writes TripoHull_Albedo.png for runtime load.
    /// 3. <see cref="EnsureResourcesHullPrefab"/> bakes ONLY from <see cref="TripoResourcesFbx"/> — never
    ///    TripoSourceFbx (mesh refs outside Resources break on device).
    /// 4. <see cref="PersistHullMaterialsToResources"/> assigns albedo to TripoHull.mat before SaveAsPrefabAsset.
    /// 5. <see cref="LoadDeviceHullAsset"/> / <see cref="EnsureScenePlayersUseResourcesHull"/> point builds at
    ///    device assets; <see cref="LoadResourcesHullAsset"/> may still prefer TripoModels for editor tuning.
    /// 6. <see cref="ValidateDeviceAssets"/> fails the build if hull albedo is missing.
    /// </summary>
    public static class RtgPlayerShipAssetSync
    {
        public const string TripoSourceFolder =
            "Assets/TripoModels/futuristic_fighter_3d_model";
        public const string TripoSourceFbx =
            TripoSourceFolder + "/futuristic_fighter_3d_model.fbx";
        public const string TripoResourcesFbx =
            "Assets/Resources/RTG_PlayerShip/TripoGlider/futuristic_fighter_3d_model.fbx";
        public const string TripoResourcesPrefab =
            "Assets/Resources/RTG_PlayerShip/TripoGlider/TripoGlider.prefab";
        public const string ResourcesHullLoadPath =
            "RTG_PlayerShip/TripoGlider/TripoGlider";
        public const string GliderTextureAssetPath =
            "Assets/Resources/RTG_PlayerShip/glider_01.png";

        /// <summary>Sync art + hull from source folders, then verify device-ready Resources.</summary>
        public static bool PrepareForDeviceBuild(out string error)
        {
            bool syncedArt = SyncShipArtFromSources();
            bool syncedHull = SyncTripoHullFromSources(null);
            EnsureScenePlayersUseResourcesHull();
            if (!ValidateDeviceAssets(out error))
                return false;

            Debug.Log(
                "[RTG] Player ship assets ready for device build" +
                (syncedArt ? " (art synced)" : "") +
                (syncedHull ? " (Tripo hull synced)" : "") +
                ".");
            return true;
        }

        public static bool SyncShipArtFromSources()
        {
            string srcDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../images"));
            string dstDir = Path.Combine(Application.dataPath, "Resources/RTG_PlayerShip");

            if (!Directory.Exists(srcDir))
            {
                Debug.LogWarning($"[RTG] Player ship art skipped — source folder not found: {srcDir}");
                return false;
            }

            Directory.CreateDirectory(dstDir);
            int copied = 0;

            foreach (string srcPath in Directory.GetFiles(srcDir, "glider_*.png"))
            {
                string fileName = Path.GetFileName(srcPath);
                if (fileName.EndsWith("_src.png"))
                    continue;
                File.Copy(srcPath, Path.Combine(dstDir, fileName), overwrite: true);
                copied++;
            }

            if (copied == 0)
            {
                Debug.LogWarning(
                    $"[RTG] Player ship art skipped — no glider_*.png files in {srcDir}.");
                return false;
            }

            AssetDatabase.Refresh();

            string processScript = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../scripts/process-cockpit-transparency.py"));
            if (File.Exists(processScript))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{processScript}\"",
                    UseShellExecute = false,
                })?.WaitForExit();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[RTG] Synced {copied} glider image(s) to Resources/RTG_PlayerShip.");
            return true;
        }

        public static bool SyncTripoHullFromSources(RtgPlayerLocation player)
        {
            string sourcePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", TripoSourceFbx));
            if (File.Exists(sourcePath))
            {
                CopyDirectory(
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..", TripoSourceFolder)),
                    Path.GetFullPath(Path.Combine(Application.dataPath, "Resources/RTG_PlayerShip/TripoGlider")));
                AssetDatabase.Refresh();
                EnsureTripoAlbedoInResources();
            }
            else if (!File.Exists(Path.GetFullPath(
                         Path.Combine(Application.dataPath, "..", TripoResourcesFbx)))
                     && !File.Exists(Path.GetFullPath(
                         Path.Combine(Application.dataPath, "..", TripoResourcesPrefab))))
            {
                Debug.LogWarning(
                    "[RTG] Tripo hull skipped — import the glider from Tripo first. Expected: "
                    + TripoSourceFbx);
                return false;
            }

            if (!EnsureResourcesHullPrefab())
            {
                Debug.LogError("[RTG] Could not bake Tripo hull prefab into Resources.");
                return false;
            }

            GameObject hull = LoadResourcesHullAsset();
            if (hull == null)
            {
                Debug.LogError("[RTG] Could not load Tripo hull from Resources after sync.");
                return false;
            }

            ApplyHullToPlayer(player, hull);
            Debug.Log("[RTG] Tripo hull synced to Resources/RTG_PlayerShip/TripoGlider.");
            return true;
        }

        public static GameObject LoadResourcesHullAsset()
        {
#if UNITY_EDITOR
            GameObject hull = AssetDatabase.LoadAssetAtPath<GameObject>(TripoSourceFbx);
            if (RtgPlayerShipVisual.IsValidHullPrefab(hull))
                return hull;
#endif

            return LoadDeviceHullAsset();
        }

        /// <summary>Resources prefab/FBX safe to ship in iOS/Android builds (not TripoModels source).</summary>
        public static GameObject LoadDeviceHullAsset()
        {
            GameObject hull = AssetDatabase.LoadAssetAtPath<GameObject>(TripoResourcesPrefab);
            if (RtgPlayerShipVisual.IsDeviceReadyHullPrefab(hull))
                return hull;

            hull = AssetDatabase.LoadAssetAtPath<GameObject>(TripoResourcesFbx);
            if (RtgPlayerShipVisual.IsValidHullPrefab(hull))
                return hull;

            return null;
        }

        /// <summary>
        /// Bakes TripoGlider.prefab into Resources. REGRESSION: baking from TripoSourceFbx left mesh guids
        /// outside Resources; skipping PersistHullMaterialsToResources left fileID:0 materials on device.
        /// </summary>
        private static bool EnsureResourcesHullPrefab()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(TripoResourcesFbx);
            if (!RtgPlayerShipVisual.IsValidHullPrefab(source))
            {
                Debug.LogError(
                    "[RTG] Cannot bake TripoGlider.prefab — copy the hull to "
                    + TripoResourcesFbx + " first (Regenerate Playable World).");
                return false;
            }

            GameObject instance = Object.Instantiate(source);
            try
            {
                instance.name = "TripoGlider";
                RtgPlayerShipVisual.PrepareImportedHullInstance(instance);
                RtgPlayerShipVisual.BakeHullScaleForDevice(instance.transform, 24f);
                PersistHullMaterialsToResources(instance);

                string directory = Path.GetDirectoryName(TripoResourcesPrefab)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
                {
                    string parent = "Assets/Resources/RTG_PlayerShip/TripoGlider";
                    if (!AssetDatabase.IsValidFolder(parent))
                        return false;
                }

                bool success = PrefabUtility.SaveAsPrefabAsset(
                    instance,
                    TripoResourcesPrefab,
                    out bool saved);
                if (!success || !saved)
                    return false;

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return RtgPlayerShipVisual.IsDeviceReadyHullPrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(TripoResourcesPrefab));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Writes TripoHull.mat with albedo from source FBX sub-assets or TripoHull_Albedo.png.
        /// REGRESSION: reusing existing TripoHull.mat without ApplyAlbedoToMaterial kept empty _BaseMap on device.
        /// </summary>
        private static void PersistHullMaterialsToResources(GameObject hullRoot)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            Texture albedo = FindAlbedoForBake(hullRoot);
            foreach (MeshRenderer renderer in hullRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] sourceMaterials = renderer.sharedMaterials;
                Material[] persistedMaterials = new Material[sourceMaterials.Length];
                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    string assetPath = i == 0
                        ? "Assets/Resources/RTG_PlayerShip/TripoGlider/TripoHull.mat"
                        : $"Assets/Resources/RTG_PlayerShip/TripoGlider/TripoHull_{i}.mat";

                    Material existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                    if (existing != null)
                    {
                        RtgPlayerShipVisual.ApplyAlbedoToMaterial(existing, albedo);
                        EditorUtility.SetDirty(existing);
                        persistedMaterials[i] = existing;
                        continue;
                    }

                    Material material = sourceMaterials[i];
                    if (material == null)
                        material = urpLit != null ? new Material(urpLit) : new Material(Shader.Find("Standard"));
                    else if (AssetDatabase.GetAssetPath(material) != assetPath)
                        material = new Material(material);

                    material.name = Path.GetFileNameWithoutExtension(assetPath);
                    RtgPlayerShipVisual.ApplyAlbedoToMaterial(material, albedo);
                    AssetDatabase.CreateAsset(material, assetPath);
                    persistedMaterials[i] = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                }

                renderer.sharedMaterials = persistedMaterials;
            }
        }

        private static Texture FindAlbedoForBake(GameObject hullRoot)
        {
            foreach (MeshRenderer renderer in hullRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Texture albedo = RtgPlayerShipVisual.ExtractAlbedoTexture(material);
                    if (albedo != null)
                        return albedo;
                }
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(TripoSourceFbx))
            {
                if (asset is Material sourceMaterial)
                {
                    Texture albedo = RtgPlayerShipVisual.ExtractAlbedoTexture(sourceMaterial);
                    if (albedo != null)
                        return albedo;
                }

                if (asset is Texture2D texture)
                    return texture;
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(TripoResourcesFbx))
            {
                if (asset is Texture2D texture)
                    return texture;
            }

            return RtgPlayerShipVisual.TryLoadHullAlbedoFromResources();
        }

        private static void EnsureTripoAlbedoInResources()
        {
            string resourcesDir = Path.Combine(Application.dataPath, "Resources/RTG_PlayerShip/TripoGlider");
            string namedPath = Path.Combine(resourcesDir, "TripoHull_Albedo.png");
            if (File.Exists(namedPath))
                return;

            string sourceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", TripoSourceFolder));
            if (!Directory.Exists(sourceDir))
                return;

            string bestPath = null;
            foreach (string file in Directory.GetFiles(sourceDir, "*.png", SearchOption.AllDirectories))
            {
                string lower = file.ToLowerInvariant();
                if (lower.Contains("normal")
                    || lower.Contains("rough")
                    || lower.Contains("metallic")
                    || lower.Contains("ao")
                    || lower.Contains("height")
                    || lower.Contains("mask"))
                {
                    continue;
                }

                bestPath = file;
                if (lower.Contains("base")
                    || lower.Contains("color")
                    || lower.Contains("albedo")
                    || lower.Contains("diffuse"))
                {
                    break;
                }
            }

            if (bestPath == null)
                return;

            Directory.CreateDirectory(resourcesDir);
            File.Copy(bestPath, namedPath, overwrite: true);
            AssetDatabase.Refresh();
        }

        public static void ApplyHullToPlayer(RtgPlayerLocation player, GameObject hull)
        {
            if (player == null || hull == null)
                return;

            if (!Application.isPlaying)
                Undo.RecordObject(player, "Assign Tripo Ship Hull");

            player.shipHullPrefab = hull;
            player.shipSizeMeters = 24f;
            if (RtgShipTuningConfig.TryLoad(out RtgShipTuningConfig.ShipTuningFile tuning))
                RtgShipTuningConfig.ApplyTo(player, tuning);
            else
                player.shipHullEulerOffset = Vector3.zero;

            if (!Application.isPlaying)
                EditorUtility.SetDirty(player);
        }

        /// <summary>Point scene RTG Player components at the Resources hull (device-safe).</summary>
        public static void EnsureScenePlayersUseResourcesHull()
        {
            GameObject hull = LoadDeviceHullAsset();
            if (hull == null)
                return;

#if UNITY_2023_1_OR_NEWER
            RtgPlayerLocation[] players =
                Object.FindObjectsByType<RtgPlayerLocation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            RtgPlayerLocation[] players = Object.FindObjectsOfType<RtgPlayerLocation>(true);
#endif
            foreach (RtgPlayerLocation player in players)
                ApplyHullToPlayer(player, hull);
        }

        public static bool ValidateDeviceAssets(out string error)
        {
            error = null;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string gliderPath = Path.Combine(projectRoot, GliderTextureAssetPath);
            string hullPath = Path.Combine(projectRoot, TripoResourcesFbx);

            Texture2D gliderTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Resources/RTG_PlayerShip/glider_01");
            if (gliderTex == null && !File.Exists(gliderPath))
            {
                error =
                    "Missing device ship texture at " + GliderTextureAssetPath + ". " +
                    "Add glider_01.png to apps/images/ or Resources/RTG_PlayerShip/, then rebuild.";
                return false;
            }

            GameObject hull = AssetDatabase.LoadAssetAtPath<GameObject>(TripoResourcesPrefab);
            if (!RtgPlayerShipVisual.IsDeviceReadyHullPrefab(hull))
                hull = AssetDatabase.LoadAssetAtPath<GameObject>(TripoResourcesFbx);
            if (!RtgPlayerShipVisual.IsValidHullPrefab(hull) && !File.Exists(hullPath))
            {
                error =
                    "Missing or invalid device Tripo hull at " + TripoResourcesPrefab + " (or " + TripoResourcesFbx + "). " +
                    "Run Routes to Glory → Regenerate Playable World, then rebuild.";
                return false;
            }

            Material hullMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/RTG_PlayerShip/TripoGlider/TripoHull.mat");
            Texture2D hullAlbedo = RtgPlayerShipVisual.TryLoadHullAlbedoFromResources();
            if (hullMaterial != null
                && RtgPlayerShipVisual.ExtractAlbedoTexture(hullMaterial) == null
                && hullAlbedo == null)
            {
                error =
                    "Tripo hull material is missing its albedo texture. Run Routes to Glory → Regenerate Playable World " +
                    "to copy Tripo textures into Resources/RTG_PlayerShip/TripoGlider/, then rebuild.";
                return false;
            }

            Material shipMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/RTG_PlayerShip/PlayerShip.mat");
            if (shipMat == null || shipMat.shader == null)
            {
                error =
                    "PlayerShip.mat is missing or has no shader at Assets/Resources/RTG_PlayerShip/PlayerShip.mat.";
                return false;
            }

            return true;
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
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
    }
}
