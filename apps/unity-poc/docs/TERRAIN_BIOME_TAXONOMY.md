# Terrain Biome Taxonomy

**Status:** Locked for Phase 1 (shader + shared types)  
**Related:** [REALISTIC_TERRAIN_POC.md](REALISTIC_TERRAIN_POC.md) · [CESIUM_ALIEN_WORLD_ARCHITECTURE.md](CESIUM_ALIEN_WORLD_ARCHITECTURE.md)

Players read **alien fiction names** at pass-over altitude. Server classifiers use **Earth geography signals** underneath. Visuals must stay Civ-bold: large homogeneous regions, sharp edges.

---

## Design rules

1. **8–10 types max** at POC — more types reduce pass-over readability.
2. **Ground texture leads** — scatter and deposits reinforce later (Phase 2–3).
3. **Cesium = WHERE** — elevation mesh only. **Unity shader = WHAT** — biome colors from height, slope, and procedural region noise (Step 1). Server tile classification (Step 2) uses the same type ids.
4. **Water is special** — rivers/lakes/ocean share `xeno_water` visually; gameplay may split shore vs deep later.

---

## Earth signal → alien type

| Earth concept | Classification signals (POC heuristic) | Alien type | Player-facing name | Visual language |
|---------------|----------------------------------------|------------|-------------------|-----------------|
| Plains / grassland | Mid elevation, low slope, moderate wetness | `xeno_plains` | Alien Plains | Bold ochre-gold, sparse noise |
| Desert / barren | Mid elevation, dry region noise, low wetness | `xeno_wasteland` | Dust Expanse | Cracked tan-violet, sparse |
| Marsh / wetland | Low elevation band, high wetness | `xeno_wetland` | Fungal Marsh | Blue-green pools, reeds |
| Forest / woodland | Mid elevation, high region noise patch | `xeno_fungal_forest` | Fungal Forest | Saturated alien green patches |
| Hills / rolling | Positive elevation band, moderate slope | `xeno_rolling` | Rolling Upland | Transitional ochre → crystal (merged into highland band in shader v1) |
| Mountains / highland | High elevation quantile | `xeno_highland` | Crystal Highland | Pale blue ridge rock |
| Cliffs / steep terrain | Slope above rift threshold | `xeno_rift` | Volcanic Rift | Orange-red fracture rock |
| Rivers / lakes / ocean | Very low elevation + flat + high wetness | `xeno_water` | Deep Violet Sea | Dark violet, smooth |
| Arctic / snow (deferred) | High latitude + elevation (future) | `xeno_frost` | Frost Expanse | Pale cyan-white — **not in shader v1** |
| Urban / developed (deferred) | OSM landuse residential/commercial | `xeno_urban_echo` | Settlement Scar | Subtle grid scar — **Phase 2+** |

---

## Shader v1 procedural mapping (Unity POC)

Macro regions use **Voronoi cells** (~4 km diameter) so forest, plains, wasteland, and wet basins form large continents — not per-fragment noise spots.

| Layer | Source | Effect |
|-------|--------|--------|
| **Macro** | Voronoi cell hash at `_MacroRegionSizeM` | Dominant zone: plains / forest / wasteland / wet basin |
| **Meso** | Cesium elevation + slope | Highland, rift, marsh pockets in wet basins |
| **Micro** | Fine detail noise | Texture grain only — does **not** pick biomes |

Within a single macro cell, one ground biome dominates. Borders blend softly over `_MacroBorderSoftM` meters.

| Priority | Condition | Biome |
|----------|-----------|-------|
| 1 | `slope > _SlopeRiftStart` | `xeno_rift` |
| 2 | wet basin macro ∧ low ∧ flat ∧ wet | `xeno_water` |
| 3 | wet basin macro ∧ low ∧ wet | `xeno_wetland` |
| 4 | `heightBand > highlandCutoff` | `xeno_highland` |
| 5 | macro zone | plains / forest / wasteland |

**Step 2 upgrade:** assign one `TerrainBiome` per 400 m fog tile from OSM + elevation; macro regions become data-driven instead of procedural.

---

## Shared type ids

Canonical enum lives in `packages/shared/src/map/terrain-biome.ts` and is mirrored in Unity `RtgBiomePalette.cs`.

```ts
type TerrainBiome =
  | 'xeno_plains'
  | 'xeno_wasteland'
  | 'xeno_wetland'
  | 'xeno_fungal_forest'
  | 'xeno_highland'
  | 'xeno_rift'
  | 'xeno_water'
  | 'xeno_frost'      // reserved
  | 'xeno_urban_echo' // reserved
```

---

## Phase 2 hooks (not built yet)

| Biome | Example embedded resource |
|-------|---------------------------|
| `xeno_rift` | Magma vents, obsidian veins |
| `xeno_highland` | Crystal deposits |
| `xeno_wetland` | Biogel pools |
| `xeno_fungal_forest` | Spore harvest nodes |
| `xeno_wasteland` | Rare mineral scrap |
| `xeno_plains` | Route crops / solar mats |
| `xeno_water` | Extraction platforms (offshore) |

---

## Pass-over acceptance (Phase 1 exit)

Fly 2+ km at glider altitude over Douglas, WY. Name **≥3 biomes while moving** without a legend. Shader-only pass is sufficient for Phase 1; server tile data (Step 2) must not contradict visible ground.
