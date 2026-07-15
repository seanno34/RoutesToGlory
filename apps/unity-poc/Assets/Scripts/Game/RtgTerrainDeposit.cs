using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Tile-embedded resource deposits for Phase 2 — flush with terrain, minimal glow.
    /// Replaces floating glow-pad pins from <see cref="RtgGroundMarkerVisual"/> for resources.
    /// </summary>
    public static class RtgTerrainDeposit
    {
        public readonly struct BuildResult
        {
            public readonly float FootprintM;
            public readonly float LabelHeightM;

            public BuildResult(float footprintM, float labelHeightM)
            {
                FootprintM = footprintM;
                LabelHeightM = labelHeightM;
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
            float footprint = RichnessFootprint(richness);
            float bodyScale = footprint * 0.18f;
            float bodyHeight = bodyScale * BodyHeightScale(resourceId);
            float embedDepth = footprint * 0.06f;

            AddSubtleGlow(root, footprint * 0.55f, glowMaterial);
            AddEmbeddedBody(root, resourceId, biome, color, bodyMaterial, bodyScale, bodyHeight, embedDepth);

            float labelY = Mathf.Max(bodyHeight * 0.55f, footprint * 0.06f);
            return new BuildResult(footprint, labelY);
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

            float groundY = -embedDepth * 0.35f;

            switch (resourceId)
            {
                case "xenite":
                    AddPart(bodyRoot.transform, "Vein", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 1.4f, height * 0.35f, scale * 0.55f),
                        new Vector3(0f, groundY + height * 0.12f, 0f), Quaternion.Euler(8f, 22f, 4f));
                    AddPart(bodyRoot.transform, "Crystal", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.42f, height * 0.75f, scale * 0.42f),
                        new Vector3(scale * 0.18f, groundY + height * 0.38f, 0f), Quaternion.Euler(0f, 18f, 12f));
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

            _ = biome;
            _ = color;
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
