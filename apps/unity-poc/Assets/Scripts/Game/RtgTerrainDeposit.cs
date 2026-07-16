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
    /// Guardrails: <see cref="RtgTerrainDepositGuards"/>
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
        public static BuildResult BuildEmbedded(
            Transform root,
            string resourceId,
            string richness,
            string biome,
            Color color,
            Material bodyMaterial,
            Material glowMaterial)
        {
            ClearDepositVisuals(root);
            float footprint = RichnessFootprint(richness);

            if (resourceId == "xenite"
                && TryBuildXeniteFromPrefab(root, biome, footprint, glowMaterial, out float prefabLabelY))
            {
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

            AddSubtleGlow(root, footprint * 0.55f, glowMaterial);
            AddEmbeddedBody(root, resourceId, biome, color, bodyMaterial, bodyScale, bodyHeight, embedDepth);

            float labelY = Mathf.Max(bodyHeight * 0.55f, footprint * 0.06f);
            return new BuildResult(footprint, labelY);
        }

        private static void ClearDepositVisuals(Transform root)
        {
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);
        }

        /// <summary>
        /// Loads Tripo/commissioned prefab from Resources when present.
        /// See <c>docs/XENITE_DEPOSIT_ASSET_BRIEF.md</c> §9.
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

            labelHeightM = Mathf.Max(ResolveVisualHeightM(instance.transform), footprintM * 0.06f);
            UnityEngine.Debug.Log(
                $"[RTG] Xenite deposit using Tripo prefab ({prefab.name}) — footprint {footprintM:0.#}m.");
            return true;
        }

        private static GameObject ResolveXenitePrefab(string biome)
        {
            string resourcesPath = RtgTerrainDepositGuards.ResolveXenitePrefabResourcesPath(biome);
            GameObject prefab = Resources.Load<GameObject>(resourcesPath);
            if (prefab != null)
                return prefab;

            if (biome != RtgBiomePalette.Rift)
            {
                prefab = Resources.Load<GameObject>(RtgTerrainDepositGuards.XeniteRiftPrefabResourcesPath);
                if (prefab != null)
                    return prefab;
            }

            prefab = Resources.Load<GameObject>(RtgTerrainDepositGuards.XeniteTripoResourcesFallbackPath);
            if (prefab != null)
                return prefab;

#if UNITY_EDITOR
            prefab = FindXeniteTripoImportInEditor();
            if (prefab != null)
                return prefab;
#endif

            UnityEngine.Debug.LogWarning(
                $"[RTG] Xenite prefab not found (tried Resources/{resourcesPath}, " +
                $"{RtgTerrainDepositGuards.XeniteRiftPrefabResourcesPath}, " +
                $"{RtgTerrainDepositGuards.XeniteTripoResourcesFallbackPath}). " +
                "Run Routes to Glory → Sync Xenite Deposit (Tripo).");
            return null;
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
            Bounds bounds = CalculateLocalBounds(instance);
            float horizontalSpan = Mathf.Max(bounds.size.x, bounds.size.z, 0.001f);
            float fitScale = RtgTerrainDepositGuards.XeniteAuthoringFootprintM / horizontalSpan;
            instance.localScale = Vector3.one * fitScale;

            bounds = CalculateLocalBounds(instance);
            instance.localPosition = new Vector3(0f, -bounds.min.y, 0f);
        }

        private static Bounds CalculateLocalBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Bounds bounds = new Bounds(root.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                bounds.Encapsulate(root.InverseTransformPoint(world.min));
                bounds.Encapsulate(root.InverseTransformPoint(world.max));
            }

            return bounds;
        }

        private static void ConfigureXenitePrefabRenderers(GameObject depositRoot)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (Renderer renderer in depositRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                if (urpLit == null)
                    continue;

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null)
                        continue;
                    if (material.shader.name.Contains("Hidden/InternalErrorShader")
                        || material.shader.name == "Standard")
                    {
                        material.shader = urpLit;
                    }
                }
            }
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
