using CesiumForUnity;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Applies the alien biome terrain shader to Cesium World Terrain.
    /// Cesium owns WHERE (mesh + elevation); Unity owns WHAT (stylized biomes).
    /// See docs/CESIUM_ALIEN_WORLD_ARCHITECTURE.md.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public class RtgTerrainMaterialController : MonoBehaviour
    {
        private static readonly int HeightReferenceYId = Shader.PropertyToID("_RTG_HeightReferenceY");

        [Header("Target")]
        public Cesium3DTileset terrainTileset;

        [Tooltip("Alien biome material (RoutesToGlory/AlienTerrainBiome). Loaded from Resources if empty.")]
        public Material biomeMaterial;

        [Header("Height reference")]
        [Tooltip("World Y written globally so all Cesium tile material clones share the same height band.")]
        public bool followPlayerHeight = true;

        [Tooltip("Optional manual world-Y reference when followPlayerHeight is off.")]
        public float manualHeightReferenceY;

        [Header("Raster overlay")]
        [Tooltip("Remove any CesiumUrlTemplateRasterOverlay — Earth imagery is not the art path.")]
        public bool disableRasterOverlays = true;

        private bool _applied;

        private void Awake()
        {
            if (terrainTileset == null)
                terrainTileset = GetComponent<Cesium3DTileset>();

            ResolveBiomeMaterial();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void Start()
        {
            // Tileset may finish wiring after OnEnable; re-apply once more on the first frame.
            Apply();
        }

        private void LateUpdate()
        {
            if (!followPlayerHeight)
            {
                Shader.SetGlobalFloat(HeightReferenceYId, manualHeightReferenceY);
                return;
            }

            if (TryGetPlayerWorldY(out float worldY))
                Shader.SetGlobalFloat(HeightReferenceYId, worldY);
        }

        /// <summary>Apply biome material and disable Earth raster overlays.</summary>
        public void Apply()
        {
            if (terrainTileset == null)
            {
                Debug.LogError("[RTG] Terrain material — no Cesium3DTileset.");
                return;
            }

            Material material = ResolveBiomeMaterial();
            if (material == null)
            {
                Debug.LogError(
                    "[RTG] Terrain material — AlienTerrainBiome shader/material missing. " +
                    "Assign RTG_AlienTerrainBiome.mat on RTG Terrain.");
                return;
            }

            if (!ValidateShader(material))
                return;

            RebindShader(material);

            if (disableRasterOverlays)
                DisableRasterOverlays();

            // Assign the asset directly — Cesium clones it per primitive at tile load.
            if (terrainTileset.opaqueMaterial != material)
                terrainTileset.opaqueMaterial = material;

            float heightRef = followPlayerHeight && TryGetPlayerWorldY(out float worldY)
                ? worldY
                : manualHeightReferenceY;
            Shader.SetGlobalFloat(HeightReferenceYId, heightRef);

            if (!_applied)
            {
                _applied = true;
                Debug.Log(
                    $"[RTG] Alien terrain biome material applied (shader-based, no Earth raster). " +
                    $"Shader={material.shader.name}, supported={material.shader.isSupported}");
            }
        }

        private Material ResolveBiomeMaterial()
        {
            if (biomeMaterial != null)
                return biomeMaterial;

            biomeMaterial = Resources.Load<Material>("RTG_AlienTerrainBiome");
            if (biomeMaterial != null)
                return biomeMaterial;

            Shader shader = Shader.Find("RoutesToGlory/AlienTerrainBiome");
            if (shader == null)
                return null;

            biomeMaterial = new Material(shader) { name = "RTG_AlienTerrainBiome_Runtime" };
            return biomeMaterial;
        }

        private static void RebindShader(Material material)
        {
            Shader expected = Shader.Find("RoutesToGlory/AlienTerrainBiome");
            if (expected == null)
            {
                Debug.LogError(
                    "[RTG] Terrain material — Shader.Find could not locate RoutesToGlory/AlienTerrainBiome. " +
                    "Ensure Assets/Shaders/AlienTerrainBiome.shader exists and Unity has finished importing.");
                return;
            }

            if (material.shader != expected)
            {
                material.shader = expected;
                Debug.Log("[RTG] Rebound RTG_AlienTerrainBiome material to AlienTerrainBiome shader.");
            }
        }

        private static bool ValidateShader(Material material)
        {
            Shader shader = material.shader;
            if (shader == null)
            {
                Debug.LogError("[RTG] Terrain material — material has no shader assigned.");
                return false;
            }

            if (!shader.isSupported)
            {
                Debug.LogError(
                    $"[RTG] Terrain material — shader not supported on this platform: {shader.name}. " +
                    "Select Assets/Shaders/AlienTerrainBiome.shader in the Inspector and check compile errors.");
                return false;
            }

            if (shader.name.Contains("InternalErrorShader"))
            {
                Debug.LogError(
                    $"[RTG] Terrain material — shader failed to compile: {shader.name}. " +
                    "Open AlienTerrainBiome.shader in the Inspector for details.");
                return false;
            }

            return true;
        }

        private void DisableRasterOverlays()
        {
            CesiumUrlTemplateRasterOverlay[] overlays =
                GetComponents<CesiumUrlTemplateRasterOverlay>();
            foreach (CesiumUrlTemplateRasterOverlay overlay in overlays)
            {
                if (overlay != null)
                    overlay.enabled = false;
            }

            RtgTerrainRasterOverlay legacy = GetComponent<RtgTerrainRasterOverlay>();
            if (legacy != null)
                legacy.enabled = false;
        }

        private bool TryGetPlayerWorldY(out float worldY)
        {
            worldY = 0f;

#if UNITY_2023_1_OR_NEWER
            RtgPlayerLocation player = Object.FindFirstObjectByType<RtgPlayerLocation>();
#else
            RtgPlayerLocation player = Object.FindObjectOfType<RtgPlayerLocation>();
#endif
            if (player == null) return false;

            Transform marker = player.transform.Find("Player Marker");
            if (marker == null) return false;

            worldY = marker.position.y;
            return true;
        }
    }
}
