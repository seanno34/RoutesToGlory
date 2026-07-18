# Terrain Skin POC — Dark Purple + Neon Veins

**Status:** Active (Jul 2026)  
**Related:** [TERRAIN_BIOME_TAXONOMY.md](TERRAIN_BIOME_TAXONOMY.md) · [CESIUM_ALIEN_WORLD_ARCHITECTURE.md](CESIUM_ALIEN_WORLD_ARCHITECTURE.md)

## Look

One coherent map skin for the Unity POC:

| Layer | Read |
|-------|------|
| **Base** | Deep blackish-purple alien ground (all biomes stay in this family; subtle shade shifts only) |
| **Accent** | Faint neon pink / magenta veins — sparse thin cracks + sparse filaments (not bold blotches) |
| **Sky** | True night — deep blackish-purple skybox with procedural starfield + medium alien planets (2 with rings); no sun disc (via Regenerate / Apply Atmosphere) |

Biome **ids** (`xeno_plains`, `xeno_rift`, …) still drive deposits / gameplay. Visuals no longer use competing ochre / green / orange biomes.

## Pipeline (unchanged)

1. Cesium World Terrain = elevation mesh  
2. `AlienTerrainBiome.shader` + `RTG_AlienTerrainBiome.mat` via `RtgTerrainMaterialController`  
3. `RtgBiomePalette.ApplyToMaterial` pushes locked colors + vein defaults on apply / regenerate  
4. `RtgMapBuilder.ApplyAtmosphereInternal` pushes night skybox / fog / dim moonlight  
5. Sky asset: `Assets/Shaders/AlienNightSky.shader` → `Assets/Materials/RTG_AlienSky.mat` (stars + medium planets / rings; no sun disc)

## How to refresh in Unity

1. Open `apps/unity-poc/`
2. **Routes to Glory → Regenerate Playable World** (or **Apply Biome Terrain** + **Advanced → Apply Atmosphere & Lighting**)
3. **Play** — confirm dark purple ground, faint pink crack veins, and from glider altitude: starry night sky + medium colored planets (look for rings on A / E; no large sun disc)

No second map system. Tune veins on the material: `_VeinDensity` (higher = sparser), `_VeinWidthM`, `_VeinEmission`, `_VeinBlend`, `_VeinColor`.

### Night sky tuning

On `RTG_AlienSky` (or re-run **Apply Atmosphere** / **Regenerate** to reset defaults):

| Property | Effect |
|----------|--------|
| `_StarDensity` / `_StarThreshold` / `_StarBrightness` | How dense / sharp / bright the starfield reads |
| `_PlanetA–E Dir/Color/Size/Bright` | Five alien bodies (skybox direction, medium disc size, tint) |
| `_PlanetA/E Ring` / `RingWidth` / `RingBright` | Procedural ellipse rings (A rose + E lavender default on) |
| `_ZenithColor` / `_HorizonColor` | Night gradient (keep near-black purple for POC) |

The former large amber Planet C “sun” was removed (replaced by a smaller violet body). Directional light stays low moonlight so it does not wash out the night sky.

## Guardrails

Do **not** change glider, Xenite Tripo sync, Light Roads, or Echo Sites for this skin. Terrain-only.
