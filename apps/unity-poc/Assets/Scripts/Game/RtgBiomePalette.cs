using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Canonical alien biome colors and ids — keep in sync with
    /// packages/shared/src/map/terrain-biome.ts and TERRAIN_BIOME_TAXONOMY.md.
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

        public static readonly Color PlainsColor = new(0.72f, 0.55f, 0.22f);
        public static readonly Color WastelandColor = new(0.55f, 0.42f, 0.29f);
        public static readonly Color WetlandColor = new(0.18f, 0.35f, 0.42f);
        public static readonly Color ForestColor = new(0.12f, 0.60f, 0.29f);
        public static readonly Color HighlandColor = new(0.62f, 0.78f, 0.92f);
        public static readonly Color RiftColor = new(0.92f, 0.38f, 0.12f);
        public static readonly Color WaterColor = new(0.10f, 0.16f, 0.28f);

        private static readonly int PlainColorId = Shader.PropertyToID("_PlainColor");
        private static readonly int WastelandColorId = Shader.PropertyToID("_WastelandColor");
        private static readonly int WetlandColorId = Shader.PropertyToID("_WetlandColor");
        private static readonly int ForestColorId = Shader.PropertyToID("_ForestColor");
        private static readonly int HighlandColorId = Shader.PropertyToID("_HighlandColor");
        private static readonly int RiftColorId = Shader.PropertyToID("_RiftColor");
        private static readonly int WaterColorId = Shader.PropertyToID("_WaterColor");

        /// <summary>Apply locked taxonomy colors to a biome material instance.</summary>
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
        }
    }
}
