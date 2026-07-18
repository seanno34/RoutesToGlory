using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Tile-embedded resource deposits for Phase 2 — flush with terrain, minimal glow.
    /// Xenite v1 spec: <c>docs/XENITE_DEPOSIT_DESIGN_BRIEF.md</c>
    /// Tripo / prefab spec: <c>apps/unity-poc/docs/XENITE_DEPOSIT_ASSET_BRIEF.md</c>
    /// Spawn handoff: <c>apps/unity-poc/docs/XENITE_SPAWN_HANDOFF.md</c>
    /// Guardrails: <see cref="RtgTerrainDepositGuards"/>
    ///
    /// XENITE TRIPO GUARDRAILS (Jul 2026 — do not regress; same class as ship TRIPO HULL):
    /// • Prefab mesh + material refs must stay under <c>Assets/Resources/RTG_Deposits/</c>.
    ///   Baking from <c>TripoModels/</c> alone → invisible deposits on device.
    /// • <see cref="IsRenderableDepositPrefab"/> requires mesh + non-null materials + albedo
    ///   (<c>_BaseMap</c> / <c>_MainTex</c>). A Renderer alone is not enough.
    /// • Do not leave MeshRenderer on FBX-embedded materials — Sync must
    ///   <c>PersistXeniteMaterialsToResources</c> before <c>SaveAsPrefabAsset</c>.
    /// • Flat albedo in Resources: <c>Xenite_Albedo.jpg</c> (like TripoHull_Albedo).
    /// • <see cref="ConfigureXenitePrefabRenderers"/> must NOT force fuel×2.2 emission or
    ///   orange base wash — that destroys Tripo albedo (solid yellow). Subtle textured
    ///   emission only. Re-bake via Routes to Glory → Sync Xenite Deposit (Tripo).
    /// </summary>
    public static class RtgTerrainDeposit
    {
        public readonly struct BuildResult
        {
            public readonly float FootprintM;
            public readonly float LabelHeightM;
            public readonly bool UsedTripoPrefab;

            public BuildResult(float footprintM, float labelHeightM, bool usedTripoPrefab = false)
            {
                FootprintM = footprintM;
                LabelHeightM = labelHeightM;
                UsedTripoPrefab = usedTripoPrefab;
            }
        }

        /// <summary>Build an embedded deposit rooted at local Y = 0 (terrain surface).</summary>
        /// <param name="claimed">
        /// Ownership flag. Xenite always gets ground-hugging orange vent vapor (mist + embers
        /// + soft point light); claimed slightly intensifies the vent light. Does not tint
        /// Tripo materials — VFX are separate children only.
        /// </param>
        public static BuildResult BuildEmbedded(
            Transform root,
            string resourceId,
            string richness,
            string biome,
            Color color,
            Material bodyMaterial,
            Material glowMaterial,
            bool claimed = false)
        {
            ClearDepositVisuals(root);
            float footprint = RichnessFootprint(richness);

            if (resourceId == "xenite"
                && TryBuildXeniteFromPrefab(root, biome, footprint, glowMaterial, out float prefabLabelY))
            {
                RtgXeniteClaimedVentVfx.EnsureBuilt(root);
                if (claimed)
                    RtgXeniteClaimedVentVfx.IntensifyForClaim(root);
                return new BuildResult(footprint, prefabLabelY, usedTripoPrefab: true);
            }

            if (resourceId == "xenite")
            {
                UnityEngine.Debug.LogWarning(
                    "[RTG] Xenite using procedural placeholder — Tripo prefab not found. " +
                    "Run Routes to Glory → Sync Xenite Deposit (Tripo), or import " +
                    "glowing_lava_crystal_3d_model under Assets/.");
            }

            float bodyScale = footprint * 0.18f;
            float bodyHeight = bodyScale * BodyHeightScale(resourceId);
            // Root anchor = terrain surface; geometry builds upward from Y=0 (not buried).
            float embedDepth = footprint * 0.02f;

            // Xenite always gets vent vapor; unclaimed procedural also keeps the subtle ground ring.
            // Claimed xenite: vapor only (no green extractor pad — that settlement is skipped at spawn).
            if (resourceId == "xenite")
            {
                RtgXeniteClaimedVentVfx.EnsureBuilt(root);
                if (claimed)
                    RtgXeniteClaimedVentVfx.IntensifyForClaim(root);
            }

            if (resourceId != "xenite" || !claimed)
                AddSubtleGlow(root, footprint * 0.55f, glowMaterial);

            AddEmbeddedBody(root, resourceId, biome, color, bodyMaterial, bodyScale, bodyHeight, embedDepth);

            float labelY = Mathf.Max(bodyHeight * 0.55f, footprint * 0.06f);
            return new BuildResult(footprint, labelY);
        }

        /// <summary>
        /// After a live tap-connect (no map reload): ensure vent vapor exists, then intensify light.
        /// Idempotent — vapor is already present from spawn; claim must not recreate playing systems.
        /// </summary>
        public static void ApplyClaimedHaloForMarker(RtgMapMarker marker)
        {
            if (marker == null || marker.kind != RtgMapMarker.Kind.Resource)
                return;
            RtgXeniteClaimedVentVfx.IntensifyForClaim(marker.transform);
        }

        private static void ClearDepositVisuals(Transform root)
        {
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
                DestroyDepositObject(root.GetChild(i).gameObject);
        }

        private static void DestroyDepositObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }

        /// <summary>
        /// Loads Tripo/commissioned prefab from Resources when present.
        /// See <c>docs/XENITE_DEPOSIT_ASSET_BRIEF.md</c> §9.
        /// XENITE TRIPO GUARDRAILS: configure renderers then gate on
        /// <see cref="IsRenderableDepositInstance"/> — never treat a non-albedo prefab as success.
        /// </summary>
        private static bool TryBuildXeniteFromPrefab(
            Transform root,
            string biome,
            float footprintM,
            Material glowMaterial,
            out float labelHeightM)
        {
            labelHeightM = footprintM * 0.06f;
            GameObject prefab = ResolveXenitePrefab(biome);
            if (prefab == null)
                return false;

            float richnessScale = footprintM / Mathf.Max(1f, RtgTerrainDepositGuards.XeniteAuthoringFootprintM);
            GameObject instance = Object.Instantiate(prefab, root, false);
            instance.name = "XeniteDepositPrefab";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(RtgXeniteDepositTuningConfig.RuntimeEulerOffset);
            instance.transform.localScale = Vector3.one;
            FitXenitePrefabToAuthoringFootprint(instance.transform);
            instance.transform.localScale *= richnessScale;
            ConfigureXenitePrefabRenderers(instance);

            if (!IsRenderableDepositInstance(instance))
            {
                UnityEngine.Debug.LogWarning(
                    $"[RTG] Xenite prefab '{prefab.name}' instantiated without a usable mesh/material — " +
                    "falling back to procedural deposit. Re-run Routes to Glory → Sync Xenite Deposit (Tripo).");
                DestroyDepositObject(instance);
                return false;
            }

            labelHeightM = Mathf.Max(ResolveVisualHeightM(instance.transform), footprintM * 0.06f);
            UnityEngine.Debug.Log(
                $"[RTG] Xenite deposit using Tripo prefab ({prefab.name}) — footprint {footprintM:0.#}m.");
            return true;
        }

        /// <summary>
        /// XENITE TRIPO GUARDRAILS — prefabs that load but reference missing TripoModels
        /// mesh/material GUIDs look fine in Resources.Load logs and still render nothing —
        /// same class of bug as TripoGlider. Requires mesh + materials + albedo.
        /// </summary>
        public static bool IsRenderableDepositPrefab(GameObject prefab)
        {
            if (prefab == null)
                return false;

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (filters == null || filters.Length == 0)
                return false;

            bool hasMesh = false;
            foreach (MeshFilter filter in filters)
            {
                if (filter != null && filter.sharedMesh != null)
                {
                    hasMesh = true;
                    break;
                }
            }

            if (!hasMesh)
                return false;

            bool hasAlbedo = false;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    return false;
                foreach (Material material in materials)
                {
                    if (material == null)
                        return false;
                    if (ExtractAlbedoTexture(material) != null)
                        hasAlbedo = true;
                }
            }

            // Textured Tripo skin is required — untextured URP Lit reads as flat yellow/gray.
            if (!hasAlbedo)
            {
                UnityEngine.Debug.LogWarning(
                    $"[RTG] Xenite prefab '{prefab.name}' has materials but no albedo map — " +
                    "re-run Routes to Glory → Sync Xenite Deposit (Tripo).");
                return false;
            }

            return true;
        }

        private static bool IsRenderableDepositInstance(GameObject instance) =>
            IsRenderableDepositPrefab(instance);

        private static GameObject ResolveXenitePrefab(string biome)
        {
            string resourcesPath = RtgTerrainDepositGuards.ResolveXenitePrefabResourcesPath(biome);
            GameObject prefab = TryLoadRenderableResourcesPrefab(resourcesPath);
            if (prefab != null)
                return prefab;

            if (biome != RtgBiomePalette.Rift)
            {
                prefab = TryLoadRenderableResourcesPrefab(RtgTerrainDepositGuards.XeniteRiftPrefabResourcesPath);
                if (prefab != null)
                    return prefab;
            }

            // Prefer the Resources FBX copy (stable GUIDs) over a baked prefab that still
            // points at TripoModels paths outside Resources.
            prefab = TryLoadRenderableResourcesPrefab(RtgTerrainDepositGuards.XeniteTripoResourcesFallbackPath);
            if (prefab != null)
                return prefab;

#if UNITY_EDITOR
            prefab = FindXeniteTripoImportInEditor();
            if (IsRenderableDepositPrefab(prefab))
                return prefab;
#endif

            UnityEngine.Debug.LogWarning(
                $"[RTG] Xenite prefab not found or not renderable (tried Resources/{resourcesPath}, " +
                $"{RtgTerrainDepositGuards.XeniteRiftPrefabResourcesPath}, " +
                $"{RtgTerrainDepositGuards.XeniteTripoResourcesFallbackPath}). " +
                "Run Routes to Glory → Sync Xenite Deposit (Tripo).");
            return null;
        }

        private static GameObject TryLoadRenderableResourcesPrefab(string resourcesPath)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcesPath);
            if (!IsRenderableDepositPrefab(prefab))
            {
                if (prefab != null)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[RTG] Skipping Xenite asset Resources/{resourcesPath} — missing mesh or material refs.");
                }

                return null;
            }

            return prefab;
        }

#if UNITY_EDITOR
        private static GameObject FindXeniteTripoImportInEditor()
        {
            foreach (string path in RtgTerrainDepositGuards.XeniteTripoImportCandidatePaths)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null)
                {
                    UnityEngine.Debug.Log($"[RTG] Xenite Tripo import resolved at {path}");
                    return asset;
                }
            }

            string[] guids = AssetDatabase.FindAssets("glowing_lava_crystal");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", System.StringComparison.Ordinal))
                    continue;
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null)
                {
                    UnityEngine.Debug.Log($"[RTG] Xenite Tripo import resolved via search at {path}");
                    return asset;
                }
            }

            return null;
        }
#endif

        /// <summary>Normalize Tripo import to 10 m authoring footprint with pivot on terrain surface.</summary>
        private static void FitXenitePrefabToAuthoringFootprint(Transform instance)
        {
            Bounds bounds = CalculateMeshLocalBounds(instance);
            float horizontalSpan = Mathf.Max(bounds.size.x, bounds.size.z, 0.001f);
            float fitScale = RtgTerrainDepositGuards.XeniteAuthoringFootprintM / horizontalSpan;
            instance.localScale = Vector3.one * fitScale;

            bounds = CalculateMeshLocalBounds(instance);
            instance.localPosition = new Vector3(0f, -bounds.min.y, 0f);
        }

        /// <summary>
        /// Mesh.bounds in root-local space — avoids world-AABB corner bugs under Cesium /
        /// non-axis rotations that previously collapsed scale to near-zero or buried the mesh.
        /// </summary>
        private static Bounds CalculateMeshLocalBounds(Transform root)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            foreach (MeshFilter filter in filters)
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;

                Bounds meshBounds = filter.sharedMesh.bounds;
                Matrix4x4 localToRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3[] corners =
                {
                    meshBounds.min,
                    new Vector3(meshBounds.min.x, meshBounds.min.y, meshBounds.max.z),
                    new Vector3(meshBounds.min.x, meshBounds.max.y, meshBounds.min.z),
                    new Vector3(meshBounds.min.x, meshBounds.max.y, meshBounds.max.z),
                    new Vector3(meshBounds.max.x, meshBounds.min.y, meshBounds.min.z),
                    new Vector3(meshBounds.max.x, meshBounds.min.y, meshBounds.max.z),
                    new Vector3(meshBounds.max.x, meshBounds.max.y, meshBounds.min.z),
                    meshBounds.max,
                };

                foreach (Vector3 corner in corners)
                {
                    Vector3 local = localToRoot.MultiplyPoint3x4(corner);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>
        /// XENITE TRIPO GUARDRAILS — keep Tripo albedo/maps readable.
        /// Only fix broken shaders and add a low-intensity textured glow for night pass-over —
        /// never flat fuel emission or base-color wash.
        /// REGRESSION: fuel×2.2 emission + orange base lerp made Tripo deposits solid yellow.
        /// </summary>
        private static void ConfigureXenitePrefabRenderers(GameObject depositRoot)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            Color fuel = RtgTerrainDepositGuards.XeniteCanonicalColor;
            // Low HDR so lava albedo still dominates; emission map carries texture detail.
            const float subtleEmissionIntensity = 0.22f;

            foreach (Renderer renderer in depositRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Material[] shared = renderer.sharedMaterials;
                if (shared == null)
                    continue;

                var runtime = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    Material source = shared[i];
                    if (source == null)
                    {
                        runtime[i] = null;
                        continue;
                    }

                    Material material = new Material(source);
                    Texture albedo = ExtractAlbedoTexture(material);

                    if (urpLit != null
                        && (material.shader == null
                            || material.shader.name.Contains("Hidden/InternalErrorShader")
                            || material.shader.name == "Standard"))
                    {
                        material.shader = urpLit;
                        ApplyAlbedoTexture(material, albedo);
                        if (material.HasProperty("_BaseColor"))
                            material.SetColor("_BaseColor", Color.white);
                    }

                    // Strip washout emission baked onto Resources materials by older sync/runtime.
                    if (material.HasProperty("_EmissionColor"))
                    {
                        Color existing = material.GetColor("_EmissionColor");
                        float luminance = existing.r * 0.299f + existing.g * 0.587f + existing.b * 0.114f;
                        if (luminance > 0.75f || existing.maxColorComponent > 1.2f)
                        {
                            material.SetColor("_EmissionColor", Color.black);
                            material.DisableKeyword("_EMISSION");
                        }
                    }

                    if (albedo == null)
                        albedo = TryLoadXeniteAlbedoFromResources();

                    if (albedo != null)
                        ApplyAlbedoTexture(material, albedo);

                    // Subtle night glow: reuse albedo as emission map so crystal detail remains.
                    if (material.HasProperty("_EmissionColor") && albedo != null)
                    {
                        if (material.HasProperty("_EmissionMap")
                            && material.GetTexture("_EmissionMap") == null)
                        {
                            material.SetTexture("_EmissionMap", albedo);
                        }

                        material.EnableKeyword("_EMISSION");
                        Color emission = Color.Lerp(Color.white, fuel, 0.4f) * subtleEmissionIntensity;
                        material.SetColor("_EmissionColor", emission);
                        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }

                    runtime[i] = material;
                }

                renderer.materials = runtime;
            }
        }

        private static Texture ExtractAlbedoTexture(Material source)
        {
            if (source == null)
                return null;

            if (source.mainTexture != null)
                return source.mainTexture;

            string[] texturePropertyNames =
            {
                "_BaseMap",
                "_MainTex",
                "_BaseColorMap",
                "_DiffuseMap",
            };

            foreach (string propertyName in texturePropertyNames)
            {
                if (!source.HasProperty(propertyName))
                    continue;

                Texture texture = source.GetTexture(propertyName);
                if (texture != null)
                    return texture;
            }

            return null;
        }

        private static void ApplyAlbedoTexture(Material material, Texture albedo)
        {
            if (material == null || albedo == null)
                return;

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", albedo);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", albedo);
        }

        private static Texture TryLoadXeniteAlbedoFromResources()
        {
            // Flat copy written by Sync (mirrors TripoHull_Albedo) — preferred device path.
            Texture2D texture = Resources.Load<Texture2D>("RTG_Deposits/Xenite_Albedo");
            if (texture != null)
                return texture;

            // Resources path omits extension; JPEG lives beside the FBX under .fbm.
            texture = Resources.Load<Texture2D>(
                "RTG_Deposits/glowing_lava_crystal_3d_model_basecolor");
            if (texture != null)
                return texture;

            return Resources.Load<Texture2D>(
                "RTG_Deposits/glowing_lava_crystal_3d_model.fbm/glowing_lava_crystal_3d_model_basecolor");
        }

        private static float ResolveVisualHeightM(Transform depositRoot)
        {
            Renderer[] renderers = depositRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return 0f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 topLocal = depositRoot.InverseTransformPoint(bounds.max);
            return Mathf.Max(0.5f, topLocal.y);
        }

        private static void AddSubtleGlow(Transform root, float diameterM, Material glowMaterial)
        {
            var ring = RtgMeshPrimitives.CreateMeshObject(
                "DepositGlow", RtgMeshPrimitives.GroundQuad, glowMaterial, root);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.transform.localScale = new Vector3(diameterM, 1f, diameterM);
        }

        private static void AddEmbeddedBody(
            Transform root,
            string resourceId,
            string biome,
            Color color,
            Material material,
            float scale,
            float height,
            float embedDepth)
        {
            var bodyRoot = new GameObject("EmbeddedDeposit");
            bodyRoot.transform.SetParent(root, false);
            bodyRoot.transform.localPosition = Vector3.zero;

            // Shelf bases sit on the anchored surface; only a sliver sinks for embed read.
            float groundY = -embedDepth * 0.15f;

            switch (resourceId)
            {
                case "xenite":
                    // v1 — biome-aware vent/vein (see docs/XENITE_DEPOSIT_DESIGN_BRIEF.md).
                    // REGRESSION: embedded at local Y≈0; no floating pin; orange fuel read.
                    BuildXeniteDeposit(bodyRoot.transform, biome, material, scale, height, groundY);
                    RtgTerrainDepositGuards.WarnIfDepositUsesFloatingPinPattern(resourceId, bodyRoot.transform.localPosition.y);
                    break;
                case "solari_dust":
                    AddPart(bodyRoot.transform, "DustBed", RtgMeshPrimitives.GroundQuad, material,
                        new Vector3(scale * 1.25f, 1f, scale * 1.25f), new Vector3(0f, groundY + 0.02f, 0f), Quaternion.identity);
                    AddPart(bodyRoot.transform, "Mound", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.9f, height * 0.18f, scale * 0.9f),
                        new Vector3(0f, groundY + height * 0.09f, 0f), Quaternion.Euler(0f, 31f, 0f));
                    break;
                case "ferracite":
                    AddPart(bodyRoot.transform, "Outcrop", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 1.1f, height * 0.42f, scale * 0.85f),
                        new Vector3(0f, groundY + height * 0.18f, 0f), Quaternion.Euler(14f, 28f, 6f));
                    AddPart(bodyRoot.transform, "Ore", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.55f, height * 0.28f, scale * 0.45f),
                        new Vector3(scale * 0.32f, groundY + height * 0.12f, scale * 0.1f), Quaternion.Euler(-6f, 40f, 0f));
                    break;
                case "lumin_spring":
                    AddPart(bodyRoot.transform, "Pool", RtgMeshPrimitives.GroundQuad, material,
                        new Vector3(scale * 1.2f, 1f, scale * 1.2f), new Vector3(0f, groundY + 0.03f, 0f), Quaternion.identity);
                    AddPart(bodyRoot.transform, "Spring", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.28f, height * 0.22f, scale * 0.28f),
                        new Vector3(0f, groundY + height * 0.11f, 0f), Quaternion.identity);
                    break;
                case "quantium_shard":
                    AddPart(bodyRoot.transform, "ShardA", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.28f, height * 0.82f, scale * 0.28f),
                        new Vector3(0f, groundY + height * 0.38f, 0f), Quaternion.Euler(0f, 12f, 16f));
                    AddPart(bodyRoot.transform, "ShardB", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.2f, height * 0.5f, scale * 0.2f),
                        new Vector3(scale * 0.2f, groundY + height * 0.22f, 0f), Quaternion.Euler(0f, -20f, -10f));
                    break;
                case "voidglass":
                    AddPart(bodyRoot.transform, "Glass", RtgMeshPrimitives.VerticalQuad, material,
                        new Vector3(scale * 0.75f, height * 0.55f, scale * 0.12f),
                        new Vector3(0f, groundY + height * 0.25f, 0f), Quaternion.Euler(12f, 0f, 0f));
                    AddPart(bodyRoot.transform, "Shard", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.35f, height * 0.15f, scale * 0.35f),
                        new Vector3(0f, groundY + height * 0.05f, scale * 0.15f), Quaternion.Euler(22f, 0f, 0f));
                    bodyRoot.AddComponent<RtgBillboard>();
                    break;
                case "mycelium_core":
                    AddPart(bodyRoot.transform, "Colony", RtgMeshPrimitives.GroundQuad, material,
                        new Vector3(scale * 1.1f, 1f, scale * 1.1f), new Vector3(0f, groundY + 0.02f, 0f), Quaternion.identity);
                    AddPart(bodyRoot.transform, "Cap", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.75f, height * 0.14f, scale * 0.75f),
                        new Vector3(0f, groundY + height * 0.07f, 0f), Quaternion.identity);
                    break;
                case "chrono_moss":
                    AddPart(bodyRoot.transform, "Moss", RtgMeshPrimitives.GroundQuad, material,
                        new Vector3(scale * 1.2f, 1f, scale * 1.2f), new Vector3(0f, groundY + 0.02f, 0f), Quaternion.identity);
                    AddPart(bodyRoot.transform, "Tuft", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.32f, height * 0.14f, scale * 0.32f),
                        new Vector3(0f, groundY + height * 0.07f, 0f), Quaternion.identity);
                    break;
                case "aegis_bark":
                    AddPart(bodyRoot.transform, "Stump", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.65f, height * 0.22f, scale * 0.65f),
                        new Vector3(0f, groundY + height * 0.08f, 0f), Quaternion.identity);
                    AddPart(bodyRoot.transform, "Bark", RtgMeshPrimitives.VerticalQuad, material,
                        new Vector3(scale * 0.38f, height * 0.42f, scale * 0.08f),
                        new Vector3(0f, groundY + height * 0.24f, 0f), Quaternion.Euler(0f, 18f, 0f));
                    break;
                case "nebula_pearl":
                    AddPart(bodyRoot.transform, "Pearl", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.48f, height * 0.22f, scale * 0.48f),
                        new Vector3(0f, groundY + height * 0.08f, 0f), Quaternion.identity);
                    AddPart(bodyRoot.transform, "Ripple", RtgMeshPrimitives.GroundQuad, material,
                        new Vector3(scale * 0.9f, 1f, scale * 0.9f), new Vector3(0f, groundY + 0.01f, 0f), Quaternion.identity);
                    break;
                default:
                    AddPart(bodyRoot.transform, "Deposit", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.65f, height * 0.32f, scale * 0.65f),
                        new Vector3(0f, groundY + height * 0.14f, 0f), Quaternion.Euler(0f, 35f, 0f));
                    break;
            }

            _ = color;
        }

        private static void BuildXeniteDeposit(
            Transform bodyRoot,
            string biome,
            Material material,
            float scale,
            float height,
            float groundY)
        {
            switch (biome)
            {
                case RtgBiomePalette.Highland:
                    AddPart(bodyRoot, "VeinShelf", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 1.35f, height * 0.22f, scale * 0.75f),
                        new Vector3(0f, groundY + height * 0.09f, 0f), Quaternion.Euler(10f, 34f, 3f));
                    AddPart(bodyRoot, "CrystalShard", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.34f, height * 0.68f, scale * 0.34f),
                        new Vector3(scale * 0.14f, groundY + height * 0.3f, 0f), Quaternion.Euler(0f, 12f, 14f));
                    break;

                case RtgBiomePalette.Wasteland:
                    AddPart(bodyRoot, "BuriedMound", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 1.1f, height * 0.16f, scale * 0.95f),
                        new Vector3(0f, groundY + height * 0.06f, 0f), Quaternion.Euler(4f, 18f, 0f));
                    AddPart(bodyRoot, "ScrapA", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.38f, height * 0.2f, scale * 0.28f),
                        new Vector3(scale * 0.22f, groundY + height * 0.08f, scale * 0.08f), Quaternion.Euler(-8f, 40f, 6f));
                    AddPart(bodyRoot, "ScrapB", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.28f, height * 0.14f, scale * 0.22f),
                        new Vector3(-scale * 0.18f, groundY + height * 0.05f, -scale * 0.06f), Quaternion.Euler(6f, -24f, 0f));
                    break;

                default:
                    // xeno_rift signature — vent shelf + fuel crystal spires.
                    AddPart(bodyRoot, "VentShelf", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 1.45f, height * 0.28f, scale * 0.62f),
                        new Vector3(0f, groundY + height * 0.1f, 0f), Quaternion.Euler(8f, 22f, 4f));
                    AddPart(bodyRoot, "SpireA", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.36f, height * 0.72f, scale * 0.36f),
                        new Vector3(0f, groundY + height * 0.34f, 0f), Quaternion.Euler(0f, 18f, 12f));
                    AddPart(bodyRoot, "SpireB", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.28f, height * 0.55f, scale * 0.28f),
                        new Vector3(scale * 0.2f, groundY + height * 0.24f, scale * 0.04f), Quaternion.Euler(4f, -28f, 8f));
                    AddPart(bodyRoot, "SpireC", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.24f, height * 0.48f, scale * 0.24f),
                        new Vector3(-scale * 0.16f, groundY + height * 0.2f, -scale * 0.08f), Quaternion.Euler(-6f, 36f, -6f));
                    break;
            }
        }

        private static void AddPart(
            Transform parent,
            string name,
            Mesh mesh,
            Material material,
            Vector3 scale,
            Vector3 localPosition,
            Quaternion rotation)
        {
            GameObject go = RtgMeshPrimitives.CreateMeshObject(name, mesh, material, parent);
            go.transform.localScale = scale;
            go.transform.localPosition = localPosition;
            go.transform.localRotation = rotation;
        }

        private static float RichnessFootprint(string richness)
        {
            switch (richness)
            {
                case "rich": return 56f;
                case "moderate": return 40f;
                case "sparse": return 28f;
                default: return 36f;
            }
        }

        private static float BodyHeightScale(string resourceId)
        {
            switch (resourceId)
            {
                case "voidglass": return 1.2f;
                case "quantium_shard": return 1.15f;
                case "chrono_moss": return 0.5f;
                case "solari_dust": return 0.6f;
                default: return 0.95f;
            }
        }
    }
}
