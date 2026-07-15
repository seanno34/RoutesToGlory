using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Ground-anchored Echo Site / resource marker visuals for Phase 2 POC.
    /// Replaces hovering orbs with a glow pad on the terrain and a small local prop.
    /// </summary>
    public static class RtgGroundMarkerVisual
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

        public static BuildResult BuildResource(
            Transform root,
            string resourceId,
            string richness,
            Color color,
            Material bodyMaterial,
            Material glowMaterial)
        {
            float footprint = RichnessFootprint(richness);
            float bodyScale = footprint * 0.22f;
            float bodyHeight = bodyScale * BodyHeightScale(resourceId);

            AddGlowPad(root, footprint, glowMaterial);
            AddResourceBody(root, resourceId, color, bodyMaterial, bodyScale, bodyHeight);

            float labelY = bodyHeight + footprint * 0.08f;
            return new BuildResult(footprint, labelY);
        }

        public static BuildResult BuildSettlement(
            Transform root,
            string tier,
            bool isGoodieHut,
            Color color,
            Material bodyMaterial,
            Material glowMaterial)
        {
            float footprint = TierFootprint(tier, isGoodieHut);
            float spireHeight = footprint * (isGoodieHut ? 0.28f : 0.38f);

            AddGlowPad(root, footprint, glowMaterial);
            if (isGoodieHut)
                AddGoodieHut(root, color, bodyMaterial, footprint * 0.18f);
            else
                AddSettlementSpire(root, tier, color, bodyMaterial, footprint * 0.14f, spireHeight);

            float labelY = spireHeight + footprint * 0.12f;
            return new BuildResult(footprint, labelY);
        }

        private static void AddGlowPad(Transform root, float diameterM, Material glowMaterial)
        {
            var pad = RtgMeshPrimitives.CreateMeshObject(
                "GlowPad", RtgMeshPrimitives.GroundQuad, glowMaterial, root);
            pad.transform.localPosition = Vector3.zero;
            pad.transform.localScale = new Vector3(diameterM, 1f, diameterM);

            var halo = RtgMeshPrimitives.CreateMeshObject(
                "GlowHalo", RtgMeshPrimitives.GroundQuad, glowMaterial, root);
            halo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            float haloScale = diameterM * 1.28f;
            halo.transform.localScale = new Vector3(haloScale, 1f, haloScale);
        }

        private static void AddResourceBody(
            Transform root,
            string resourceId,
            Color color,
            Material material,
            float scale,
            float height)
        {
            var bodyRoot = new GameObject("Deposit");
            bodyRoot.transform.SetParent(root, false);
            bodyRoot.transform.localPosition = Vector3.zero;

            switch (resourceId)
            {
                case "xenite":
                    AddMeshPart(bodyRoot.transform, "Crystal", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.5f, height * 0.85f, scale * 0.5f),
                        new Vector3(0f, height * 0.42f, 0f), Quaternion.Euler(0f, 24f, 0f));
                    break;
                case "solari_dust":
                    AddMeshPart(bodyRoot.transform, "Dust", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 1.15f, height * 0.22f, scale * 1.15f),
                        new Vector3(0f, height * 0.11f, 0f), Quaternion.Euler(0f, 18f, 0f));
                    break;
                case "ferracite":
                    AddMeshPart(bodyRoot.transform, "Ore", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.95f, height * 0.55f, scale * 0.75f),
                        new Vector3(0f, height * 0.27f, 0f), Quaternion.Euler(12f, 33f, 8f));
                    break;
                case "lumin_spring":
                    AddMeshPart(bodyRoot.transform, "Pool", RtgMeshPrimitives.GroundQuad, material,
                        new Vector3(scale * 1.1f, 1f, scale * 1.1f), Vector3.zero, Quaternion.identity);
                    AddMeshPart(bodyRoot.transform, "Core", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.35f, height * 0.35f, scale * 0.35f),
                        new Vector3(0f, height * 0.18f, 0f), Quaternion.identity);
                    break;
                case "quantium_shard":
                    AddMeshPart(bodyRoot.transform, "ShardA", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.32f, height * 0.9f, scale * 0.32f),
                        new Vector3(0f, height * 0.45f, 0f), Quaternion.Euler(0f, 18f, 18f));
                    AddMeshPart(bodyRoot.transform, "ShardB", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.24f, height * 0.6f, scale * 0.24f),
                        new Vector3(scale * 0.22f, height * 0.3f, 0f), Quaternion.Euler(0f, -24f, -12f));
                    break;
                case "voidglass":
                    AddMeshPart(bodyRoot.transform, "Glass", RtgMeshPrimitives.VerticalQuad, material,
                        new Vector3(scale * 0.85f, height * 0.75f, scale * 0.15f),
                        new Vector3(0f, height * 0.38f, 0f), Quaternion.identity);
                    bodyRoot.AddComponent<RtgBillboard>();
                    break;
                case "mycelium_core":
                    AddMeshPart(bodyRoot.transform, "Cap", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.95f, height * 0.18f, scale * 0.95f),
                        new Vector3(0f, height * 0.09f, 0f), Quaternion.identity);
                    AddMeshPart(bodyRoot.transform, "Stem", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.3f, height * 0.28f, scale * 0.3f),
                        new Vector3(0f, height * 0.14f, 0f), Quaternion.identity);
                    break;
                case "chrono_moss":
                    AddMeshPart(bodyRoot.transform, "Moss", RtgMeshPrimitives.GroundQuad, material,
                        new Vector3(scale * 1.15f, 1f, scale * 1.15f), Vector3.zero, Quaternion.identity);
                    AddMeshPart(bodyRoot.transform, "Tuft", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.4f, height * 0.2f, scale * 0.4f),
                        new Vector3(0f, height * 0.1f, 0f), Quaternion.identity);
                    break;
                case "aegis_bark":
                    AddMeshPart(bodyRoot.transform, "Stump", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.72f, height * 0.35f, scale * 0.72f),
                        new Vector3(0f, height * 0.17f, 0f), Quaternion.identity);
                    AddMeshPart(bodyRoot.transform, "Bark", RtgMeshPrimitives.VerticalQuad, material,
                        new Vector3(scale * 0.42f, height * 0.55f, scale * 0.1f),
                        new Vector3(0f, height * 0.35f, 0f), Quaternion.Euler(0f, 20f, 0f));
                    break;
                case "nebula_pearl":
                    AddMeshPart(bodyRoot.transform, "Pearl", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.55f, height * 0.28f, scale * 0.55f),
                        new Vector3(0f, height * 0.14f, 0f), Quaternion.identity);
                    break;
                default:
                    AddMeshPart(bodyRoot.transform, "Deposit", RtgMeshPrimitives.Cube, material,
                        new Vector3(scale * 0.72f, height * 0.45f, scale * 0.72f),
                        new Vector3(0f, height * 0.22f, 0f), Quaternion.Euler(0f, 35f, 0f));
                    break;
            }

            _ = color;
        }

        private static void AddSettlementSpire(
            Transform root,
            string tier,
            Color color,
            Material material,
            float baseScale,
            float height)
        {
            var spireRoot = new GameObject("Spire");
            spireRoot.transform.SetParent(root, false);
            spireRoot.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);

            AddMeshPart(spireRoot.transform, "Base", RtgMeshPrimitives.Cube, material,
                new Vector3(baseScale * 2.2f, height * 0.18f, baseScale * 2.2f), Quaternion.identity);

            if (tier == "super_city" || tier == "city")
            {
                AddMeshPart(spireRoot.transform, "TowerA", RtgMeshPrimitives.Cube, material,
                    new Vector3(baseScale * 0.9f, height * 0.95f, baseScale * 0.9f), Quaternion.identity);
                AddMeshPart(spireRoot.transform, "TowerB", RtgMeshPrimitives.Cube, material,
                    new Vector3(baseScale * 0.55f, height * 0.65f, baseScale * 0.55f),
                    new Vector3(baseScale * 1.1f, height * 0.15f, 0f));
            }
            else
            {
                AddMeshPart(spireRoot.transform, "Tower", RtgMeshPrimitives.Cube, material,
                    new Vector3(baseScale, height, baseScale), Quaternion.identity);
            }

            AddMeshPart(spireRoot.transform, "Beacon", RtgMeshPrimitives.Cube, material,
                new Vector3(baseScale * 0.35f, baseScale * 0.35f, baseScale * 0.35f),
                new Vector3(0f, height * 0.52f, 0f), Quaternion.identity);

            _ = color;
        }

        private static void AddGoodieHut(
            Transform root,
            Color color,
            Material material,
            float radius)
        {
            var hut = new GameObject("Hut");
            hut.transform.SetParent(root, false);
            hut.transform.localPosition = new Vector3(0f, radius * 0.65f, 0f);

            AddMeshPart(hut.transform, "Base", RtgMeshPrimitives.Cube, material,
                new Vector3(radius * 2.4f, radius * 0.35f, radius * 2.4f), Quaternion.identity);
            AddMeshPart(hut.transform, "Dome", RtgMeshPrimitives.Sphere, material,
                new Vector3(radius * 2.1f, radius * 1.2f, radius * 2.1f), Quaternion.identity);

            _ = color;
        }

        private static void AddMeshPart(
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

        private static void AddMeshPart(
            Transform parent,
            string name,
            Mesh mesh,
            Material material,
            Vector3 scale,
            Quaternion rotation)
        {
            AddMeshPart(parent, name, mesh, material, scale, Vector3.zero, rotation);
        }

        private static void AddMeshPart(
            Transform parent,
            string name,
            Mesh mesh,
            Material material,
            Vector3 scale,
            Vector3 localPosition)
        {
            AddMeshPart(parent, name, mesh, material, scale, localPosition, Quaternion.identity);
        }

        private static float RichnessFootprint(string richness)
        {
            switch (richness)
            {
                case "rich": return 72f;
                case "moderate": return 52f;
                case "sparse": return 36f;
                default: return 48f;
            }
        }

        private static float TierFootprint(string tier, bool isGoodieHut)
        {
            if (isGoodieHut) return 48f;
            switch (tier)
            {
                case "super_city": return 140f;
                case "city": return 110f;
                case "town": return 82f;
                case "settlement": return 62f;
                default: return 70f;
            }
        }

        private static float BodyHeightScale(string resourceId)
        {
            switch (resourceId)
            {
                case "voidglass": return 1.35f;
                case "quantium_shard": return 1.25f;
                case "chrono_moss": return 0.55f;
                case "solari_dust": return 0.65f;
                default: return 1f;
            }
        }
    }
}
