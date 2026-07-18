using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Canonical alien biome ids + unified POC map skin colors — keep in sync with
    /// packages/shared/src/map/terrain-biome.ts and TERRAIN_BIOME_TAXONOMY.md.
    ///
    /// Visual skin (Jul 2026): one coherent dark blackish-purple ground with neon pink
    /// veins. Biome ids still drive gameplay hooks; color variation stays within the
    /// purple family so the world reads as a single alien skin from glider altitude.
    /// </summary>
    public static class RtgBiomePalette
    {
        public const string Plains = "xeno_plains";
        public const string Wasteland = "xeno_wasteland";
        public const string Wetland = "xeno_wetland";
        public const string FungalForest = "xeno_fungal_forest";
        public const string Highland = "xeno_highland";
        public const string Rift = "xeno_rift";
        public const string Water = "xeno_water";

        // Unified blackish-purple family (subtle variation only).
        public static readonly Color PlainsColor = new(0.09f, 0.045f, 0.14f);
        public static readonly Color WastelandColor = new(0.11f, 0.06f, 0.13f);
        public static readonly Color WetlandColor = new(0.05f, 0.04f, 0.11f);
        public static readonly Color ForestColor = new(0.08f, 0.055f, 0.16f);
        public static readonly Color HighlandColor = new(0.16f, 0.10f, 0.24f);
        public static readonly Color RiftColor = new(0.20f, 0.05f, 0.18f);
        public static readonly Color WaterColor = new(0.025f, 0.02f, 0.07f);

        /// <summary>Neon pink / magenta vein accent — readable from glider altitude.</summary>
        public static readonly Color VeinColor = new(1.0f, 0.22f, 0.65f);

        private static readonly int PlainColorId = Shader.PropertyToID("_PlainColor");
        private static readonly int WastelandColorId = Shader.PropertyToID("_WastelandColor");
        private static readonly int WetlandColorId = Shader.PropertyToID("_WetlandColor");
        private static readonly int ForestColorId = Shader.PropertyToID("_ForestColor");
        private static readonly int HighlandColorId = Shader.PropertyToID("_HighlandColor");
        private static readonly int RiftColorId = Shader.PropertyToID("_RiftColor");
        private static readonly int WaterColorId = Shader.PropertyToID("_WaterColor");
        private static readonly int VeinColorId = Shader.PropertyToID("_VeinColor");
        private static readonly int VeinCellSizeId = Shader.PropertyToID("_VeinCellSizeM");
        private static readonly int VeinWidthId = Shader.PropertyToID("_VeinWidthM");
        private static readonly int VeinWarpId = Shader.PropertyToID("_VeinWarp");
        private static readonly int VeinDensityId = Shader.PropertyToID("_VeinDensity");
        private static readonly int VeinSharpnessId = Shader.PropertyToID("_VeinSharpness");
        private static readonly int VeinEmissionId = Shader.PropertyToID("_VeinEmission");
        private static readonly int VeinBlendId = Shader.PropertyToID("_VeinBlend");
        private static readonly int VeinFilamentScaleId = Shader.PropertyToID("_VeinFilamentScaleM");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        private static readonly int DetailStrengthId = Shader.PropertyToID("_DetailStrength");

        /// <summary>Apply locked taxonomy colors + vein skin to a biome material instance.</summary>
        public static void ApplyToMaterial(Material material)
        {
            if (material == null) return;

            material.SetColor(PlainColorId, PlainsColor);
            material.SetColor(WastelandColorId, WastelandColor);
            material.SetColor(WetlandColorId, WetlandColor);
            material.SetColor(ForestColorId, ForestColor);
            material.SetColor(HighlandColorId, HighlandColor);
            material.SetColor(RiftColorId, RiftColor);
            material.SetColor(WaterColorId, WaterColor);
            material.SetColor(VeinColorId, VeinColor);

            // Procedural vein defaults — faint neon cracks (not bold blotches).
            // Note: higher _VeinDensity = sparser network (presence threshold).
            if (material.HasProperty(VeinCellSizeId))
                material.SetFloat(VeinCellSizeId, 720f);
            if (material.HasProperty(VeinWidthId))
                material.SetFloat(VeinWidthId, 9f);
            if (material.HasProperty(VeinWarpId))
                material.SetFloat(VeinWarpId, 0.48f);
            if (material.HasProperty(VeinDensityId))
                material.SetFloat(VeinDensityId, 0.68f);
            if (material.HasProperty(VeinSharpnessId))
                material.SetFloat(VeinSharpnessId, 8.5f);
            if (material.HasProperty(VeinEmissionId))
                material.SetFloat(VeinEmissionId, 0.38f);
            if (material.HasProperty(VeinBlendId))
                material.SetFloat(VeinBlendId, 0.52f);
            if (material.HasProperty(VeinFilamentScaleId))
                material.SetFloat(VeinFilamentScaleId, 140f);
            if (material.HasProperty(SaturationId))
                material.SetFloat(SaturationId, 1.08f);
            if (material.HasProperty(DetailStrengthId))
                material.SetFloat(DetailStrengthId, 0.1f);
        }
    }
}
