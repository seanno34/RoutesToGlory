# Terrain Skin POC — Dark Purple + Neon Veins

**Status:** Active (Jul 2026)  
**Related:** [TERRAIN_BIOME_TAXONOMY.md](TERRAIN_BIOME_TAXONOMY.md) · [CESIUM_ALIEN_WORLD_ARCHITECTURE.md](CESIUM_ALIEN_WORLD_ARCHITECTURE.md)

## Look

One coherent map skin for the Unity POC:

| Layer | Read |
|-------|------|
| **Base** | Deep blackish-purple alien ground (all biomes stay in this family; subtle shade shifts only) |
| **Accent** | Faint neon pink / magenta veins — sparse thin cracks + sparse filaments (not bold blotches) |
| **Sky** | True night — deep blackish-purple skybox with procedural starfield + soft milky band; Tripo horizon planets via `RtgCelestialBodies` (Regenerate / Apply Atmosphere) |

Biome **ids** (`xeno_plains`, `xeno_rift`, …) still drive deposits / gameplay. Visuals no longer use competing ochre / green / orange biomes.

## Pipeline (unchanged)

1. Cesium World Terrain = elevation mesh  
2. `AlienTerrainBiome.shader` + `RTG_AlienTerrainBiome.mat` via `RtgTerrainMaterialController`  
3. `RtgBiomePalette.ApplyToMaterial` pushes locked colors + vein defaults on apply / regenerate  
4. `RtgMapBuilder.ApplyAtmosphereInternal` pushes night skybox / fog / dim moonlight / horizon planets  
5. Sky asset: `Assets/Shaders/AlienNightSky.shader` → `Assets/Materials/RTG_AlienSky.mat` (stars + milky band); planets: Tripo FBX under `CelestialBodies` via `RtgCelestialBodies`

## How to refresh in Unity

1. Open `apps/unity-poc/`
2. **Routes to Glory → Regenerate Playable World** (or **Apply Biome Terrain** + **Advanced → Apply Atmosphere & Lighting**)
3. **Play** — confirm dark purple ground, faint pink crack veins, and from glider altitude: starry night sky with the green ringed planet on one horizon and the smaller blue planet opposite

No second map system. Tune veins on the material: `_VeinDensity` (higher = sparser), `_VeinWidthM`, `_VeinEmission`, `_VeinBlend`, `_VeinColor`.

### Night sky tuning

On `RTG_AlienSky` (or re-run **Apply Atmosphere** / **Regenerate** to reset defaults):

| Property | Effect |
|----------|--------|
| `_StarDensity` / `_StarThreshold` / `_StarBrightness` | How dense / sharp / bright the starfield reads |
| `_BandColor` / `_BandStrength` / `_BandWidth` | Soft milky / nebula band across the sky |
| `_ZenithColor` / `_HorizonColor` | Night gradient (keep near-black purple for POC) |

Directional light stays low moonlight so it does not wash out the night sky.

### Horizon planets (`CelestialBodies`)

Select `RTG Georeference → CelestialBodies` (`RtgCelestialBodies`). Models are the Tripo PrefabInstances `green_ringed_planet_3d_model` (Planet A) and `earth_planet_3d_model` (Planet B).

| Field | Effect |
|-------|--------|
| `distanceMeters` | Celestial-sphere radius (default ~88 km; keep under camera far clip) |
| `ringedPlanet` / `ringlessPlanet` → `scale` | Multiplier on apparent-diameter sizing |
| `apparentDiameterDegrees` | Angular size (moon ≈ 0.5°; A default ~5° ≈ 10× moon) |
| `elevationDegrees` / `azimuthDegrees` | Horizon height and compass bearing (0=N, 90=E) |
| `rotationDegrees` / `ringAngleDegrees` | Spin / ring tilt (ringed only) |
| `brightness` / `tint` | Exposure and color grade |
| `horizonHaze` / `rimGlow` | Soft horizon fade + faint edge bloom |

Re-run **Apply Atmosphere** / **Regenerate** after deleting the hierarchy so placement is rebuilt.

## Guardrails

Do **not** change glider, Xenite Tripo sync, Light Roads, or Echo Sites for this skin. Terrain-only.
