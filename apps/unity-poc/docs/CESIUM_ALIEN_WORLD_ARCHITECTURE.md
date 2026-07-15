# GPS-Based Alien World Architecture for Unity

**Source:** `Cesium_Alien_World_Architecture_For_Cursor.md` (authoritative path forward)

**POC status (2026-07):** Phase 1 terrain uses `AlienTerrainBiome.shader` + `RtgTerrainMaterialController` on `RTG Terrain`. Earth raster overlays (`RtgTerrainRasterOverlay`) are **deprecated**.

## Objective

Design a scalable mobile game where:

- The real Earth provides the coordinate system.
- Players expand an alien civilization through real-world GPS movement.
- The world appears completely fictional despite being geographically accurate.
- Performance is suitable for iOS and Android.

---

# Executive Recommendation

**Use Cesium as the geospatial engine—not the rendering engine.**

Cesium should provide:

- Latitude/Longitude → Unity transforms
- Globe precision
- Terrain streaming
- Elevation
- Origin shifting
- Camera stability

Unity should provide:

- Alien terrain appearance
- Civilization-style visuals
- Gameplay overlays
- Route rendering
- Biomes
- Resources
- Structures
- Effects

Think of Cesium as the operating system for geography.

Think of Unity as the game engine.

---

# Recommended Architecture

```
GPS
    │
    ▼
Backend
(Route Data)

    │
    ▼
Cesium
(Earth Coordinates)

    │
    ▼
Unity World

    ├── Alien Terrain Shader
    ├── Biomes
    ├── Structures
    ├── Roads / Routes
    ├── Territory
    ├── Resources
    └── Effects
```

---

# Responsibilities

## Cesium

Use Cesium only for:

- Earth coordinates
- Terrain mesh
- Elevation
- Streaming
- Double precision transforms
- Camera origin rebasing

Avoid relying on:

- Satellite imagery
- Photogrammetry
- Earth buildings

Those work against the desired art style.

---

## Terrain Rendering

Replace Earth's appearance with a custom terrain shader.

The shader should classify terrain using:

- Elevation
- Slope
- Latitude
- Noise
- Moisture
- Temperature
- Custom biome masks

Example:

```
Low elevation
    -> fungal swamp

Flat mid elevation
    -> alien plains

High elevation
    -> crystal mountains

Steep slopes
    -> volcanic rock
```

Blend multiple tileable textures instead of one large texture.

Recommended techniques:

- Triplanar mapping
- Height-based blending
- Detail normals
- Runtime biome masks

### POC implementation (Unity)

| File | Role |
|------|------|
| `Assets/Shaders/AlienTerrainBiome.shader` | Height + slope + noise biome blend |
| `Assets/Materials/RTG_AlienTerrainBiome.mat` | Material assigned to Cesium tileset `opaqueMaterial` |
| `RtgTerrainMaterialController.cs` | Applies material, disables raster overlays, tracks player height via global shader property |

**Note:** Cesium clones `opaqueMaterial` per tile primitive. Height reference uses `Shader.SetGlobalFloat` so all clones stay in sync.

---

# Gameplay Data

Never bake gameplay into terrain.

Store routes separately.

Example:

```csharp
class PlayerRoute
{
    Guid RouteId;
    Guid OwnerId;

    List<LatLon> Points;

    int Level;

    RouteState State;
}
```

Convert only nearby GPS points into Unity positions.

---

# Route Rendering

Generate route meshes in chunks.

Good:

```
Chunk_10_14
Chunk_10_15
Chunk_11_15
```

Bad:

```
Entire world route mesh
```

Only instantiate chunks inside the active area.

---

# Civilization Layer

Treat the visible world as several layers.

Layer 1 — Terrain

Layer 2 — Biomes

Layer 3 — Player territory

Layer 4 — Routes

Layer 5 — Structures

Layer 6 — Special effects

These should remain independent.

---

# Strategic Grid

Consider using a hex grid.

GPS movement remains continuous.

Gameplay resolves inside hexes.

Benefits:

- clearer strategy
- easier balancing
- simplified ownership
- easier fog of war
- Civilization-like readability

---

# Mobile Optimization

Use concentric streaming zones.

Example

```
0-2 km
High detail

2-10 km
Medium detail

10-50 km
Low detail

50+ km
Strategic icons only
```

Avoid rendering detailed geometry everywhere.

---

# Asset Streaming

Use Addressables.

Pool:

- structures
- VFX
- NPCs
- route meshes

Unload distant content aggressively.

---

# Terrain Material Goals

Alien but readable.

Avoid:

- realistic satellite imagery

Prefer:

- bold biome colors
- stylized materials
- exaggerated elevation
- glowing crystal regions
- volcanic areas
- fungal forests

The terrain should support gameplay readability over realism.

---

# Long-Term Scalability

Phase 1 — Prototype: Cesium terrain + custom shader + GPS routes

Phase 2 — Vertical Slice: territory, structures, biomes, optimization

Phase 3 — Beta: bandwidth profiling, tile cache, chunk streaming

Phase 4 — Launch: evaluate Cesium ion vs self-hosted terrain tiles

---

# Cursor Implementation Tasks

**Priority 1** — Separate geospatial code from rendering; wrap Cesium behind interfaces

**Priority 2** — `TerrainMaterialController` ✓ · `BiomeManager` · `RouteChunkRenderer` · `TerritoryOverlay` · `GPSCoordinateService` · `StreamingManager`

**Priority 3** — Terrain shader: triplanar, biome masks, height/slope blend (POC: height/slope/noise ✓)

**Priority 4** — Chunk-based route generation

**Priority 5** — Addressables for streamed assets

---

# Design Principle

**Cesium owns WHERE things are.**

**Unity owns WHAT the player sees.**

Keeping those responsibilities separate will make the project easier to optimize, easier to maintain, and easier to evolve into a polished GPS-driven strategy game.

**Related:** [REALISTIC_TERRAIN_POC.md](REALISTIC_TERRAIN_POC.md)
