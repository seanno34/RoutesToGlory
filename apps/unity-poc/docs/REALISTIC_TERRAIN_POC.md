# Realistic Terrain & Map Tiles — POC Brief

**Project:** `routestoglory/apps/unity-poc`  
**Date:** July 2026  
**Status:** **Active POC** — Phase 1 shader biomes shipped; **Phase 2 embedded resources in progress**

**Authoritative architecture:** [CESIUM_ALIEN_WORLD_ARCHITECTURE.md](CESIUM_ALIEN_WORLD_ARCHITECTURE.md)  
**Biome taxonomy (locked):** [TERRAIN_BIOME_TAXONOMY.md](TERRAIN_BIOME_TAXONOMY.md)

**Vision:** **Civilization-grade terrain readability** — while the glider passes near or over ground, terrain type is **obvious** without reading UI. Alien resources are **embedded into world tiles** in Phase 2 (not floating pins on flat ground).

**Deferred:** Earth raster overlay spike (`RtgTerrainRasterOverlay`) — wrong path per architecture doc.

**Bar:** A player flying Auto Pilot over Douglas, WY should be able to call out terrain transitions in real time — *"that's alien forest… now open lowland… now water edge… now rocky highland"* — the same way a Civ player reads the strategic map.

**Related:** [ENGINE_EVALUATION.md](../../../docs/ENGINE_EVALUATION.md) · [SETUP.md](../../../docs/SETUP.md) · [ROADMAP.md](../../../docs/ROADMAP.md)

**Deferred:** [HOSTILE_ORDNANCE_POC.md](HOSTILE_ORDNANCE_POC.md) · [COCKPIT_DRAG_LOOK_SUMMARY.md](COCKPIT_DRAG_LOOK_SUMMARY.md)

---

## What "Rival Civilization" Means Here

Civ terrain works because each type has **large, homogeneous, color-coded regions** with **sharp boundaries** — readable at the game's viewing distance. RtG must hit the same bar from **flight altitude**, not just a paused overhead map.

### Viewing context (acceptance camera)

| Mode | Typical height | What must read |
|------|----------------|----------------|
| Chase / low-angle | ~30–120 m above ground | Ground texture dominates peripheral vision during pass-over |
| Cockpit (when used) | ~3.5 m eye height | Horizon band shows biome transitions ahead |
| Brief map pause | Overhead | Confirms same classification as flight (no mismatch) |

**Pass-over test:** Fly a 2+ km leg crossing ≥3 biome boundaries. Player (or reviewer) can name each type **while moving** without pausing or opening a legend.

### Civ design rules to copy

1. **Bold palette separation** — each biome gets a distinct hue/value (not subtle teal variations)
2. **Region scale** — textures tile at sizes that read at 50–200 m altitude (not noise at 2 m)
3. **Sharp edges** — forest/plain/water boundaries visible as lines, not gradients
4. **Elevation reinforcement** — highland reads rocky/bare; lowland reads smooth; water reads flat/dark
5. **Scatter reinforces texture** — props echo biome (tall canopy in forest, sparse rubble in wasteland) but **ground texture leads**. *Current scatter (trees/rocks/brush) is laser-test placeholder art — replacement deferred until after terrain + embedded resources.*

### What we are NOT aiming for (yet)

- Photoreal satellite imagery of Earth
- Subtle PBR micro-detail only visible at ground level
- Floating glow-orbs as the primary resource read (Phase 2 replaces with **tile-embedded deposits**)
- **Realistic environmental props** — current `RtgTerrainScatter` trees/rocks were built to test the Pathfinder beam only; amateur placeholders to be replaced in a later art pass (after Phase 1 terrain + Phase 2 embedded resources)

---

## Two-Phase Plan (Locked)

| Phase | Goal | Deliverable |
|-------|------|-------------|
| **Phase 1 (done)** | Civ-rival terrain types obvious at glider pass-over | Macro Voronoi shader + `RtgTerrainMaterialController` |
| **Phase 2 (active)** | Alien resources embedded in world tiles | Biome spawn rules + `RtgTerrainDeposit` + Cesium height anchoring |
| **Phase 3 (later)** | Realistic environmental objects | Replace amateur scatter props (laser-test trees/rocks) + Pathfinder beam targets |

Phase 2 does **not** start until Phase 1 pass-over test passes on device.

---

## Civilization Analogy — What We're Proving

| Civ concept | RtG today | POC target |
|-------------|-----------|------------|
| Terrain tiles with distinct textures (grass, desert, hills…) | Flat teal `RTG_AlienTerrain.mat` on Cesium mesh | **6–8 alien biomes** obvious at glider pass-over |
| Elevation shapes the map | Cesium World Terrain (real) | Keep — highland/lowland reinforced by texture |
| Resources sit *on* terrain (iron on hills, wheat on plains) | Random spawn + floating `RtgGroundMarkerVisual` pins | **Phase 2** — deposits embedded in tile surface |
| Full world visible at mission start | Pre-surveyed world (fog removed) | Keep |
| Strategic readability at a glance | Scatter + glow markers only | **Ground texture leads;** scatter + deposits reinforce |

**Phase 1 exit:** Glider pass-over test passes — ≥3 biome transitions obvious during flight at field-test location.

**Phase 2 exit:** Resources appear **in** the terrain (crystal veins in rift, pools in wetland, ore in highland) — geographically plausible, mineable read.

---

## Current Architecture (Baseline)

```
GPS → backend routes → Cesium World Terrain (elevation mesh only)
                              ├── AlienTerrainBiome.shader (height/slope/noise)  ← active
                              ├── RtgTerrainMaterialController
                              ├── ~~RtgTerrainRasterOverlay~~ (deprecated)
                              └── routes / markers / scatter (gameplay layers)
```

**Why lavender flat ground happened:** Raster overlay never painted (API/curl issues) + `opaqueMaterial = null` → Cesium default + purple scene fog washed everything lavender.

**Correct path:** Assign `RTG_AlienTerrainBiome` material via `RtgTerrainMaterialController`.

**What works:** Real elevation, scatter dressing, ground-anchored markers, pathfinder beam, full-map visibility (fog removed 2026-07-14).

**Fog removed (Unity POC):** Survey fleets pre-scan worlds before human arrival (`RtgWorldScanSettings.PreSurveyedWorld`). This drops the 14 km fog sheet mesh, 256×256 reveal texture rebuilds, exploration API fetch, and per-frame marker visibility passes. Server-side `explored_tiles` still updates for the web PWA — Unity ignores exploration deltas.

**What's missing for Civ-style terrain:**

1. ~~**No raster surface texture**~~ — **shader path active** (`AlienTerrainBiome.shader` + `RtgTerrainMaterialController`)
2. **No terrain/biome data model** — fog tiles are exploration state only, not terrain classification
3. **No tile pipeline in repo** — `infra/tiles` planned in docs, not built (OSM raster path deprecated)
4. **No biome-aware scatter** — current trees/rocks/brush are **Pathfinder beam test props** (amateur); not part of terrain/resource deliverables
5. ~~**Fog tile math inconsistency**~~ — no longer blocks Unity POC (fog disabled); still relevant for web PWA if fog returns

---

## Two Different "Tile" Concepts (Don't Conflate)

| Concept | Size | Purpose | Storage |
|---------|------|---------|---------|
| **Game fog tile** | 400 m grid (`x:y` string) | Exploration on web PWA; scatter chunk indexing in Unity | `explored_tiles` (MySQL) — Unity ignores |
| **Visual map tile** | Standard slippy XYZ (256/512 px) | Raster textures draped on Cesium terrain | TileServer / CDN / Stadia URL |

**POC strategy:** Add **visual raster tiles** for surface appearance. Use 400 m grid only for scatter chunking (via `RtgFogTileMath`), not exploration gating. Optionally add **terrain classification** per cell once visuals prove out.

---

## Alien Terrain Type Taxonomy (Locked)

See **[TERRAIN_BIOME_TAXONOMY.md](TERRAIN_BIOME_TAXONOMY.md)** for Earth signal → alien type mapping, shader priority stack, and Phase 2 resource hooks.

Shared types: `packages/shared/src/map/terrain-biome.ts`  
Unity palette: `RtgBiomePalette.cs`

---

## Implementation Plan

### Step 0 — Fix fog tile math (web PWA only — optional for Unity)

**Unity POC:** Fog disabled; skip this for terrain work unless re-enabling fog.

**Web PWA:** Known `cos(lat)` drift between indexing and rasterization — fix before fog ↔ terrain alignment matters on web.
- Adopt single indexing scheme (fixed `refLat` per world **or** Web Mercator slippy tiles)
- Shared test vectors in `@empire/shared`; C# `RtgFogTileMath` must match
- Unit test: `tileIdToCenter(latLngToTileId(p)) ≈ p` at multiple latitudes

**Files:** `packages/shared/src/map/fog-of-war.ts`, `apps/web/src/lib/fog-geojson.ts`, `apps/unity-poc/Assets/Scripts/Game/RtgFogTileMath.cs`

---

### Step 1 — Alien terrain shader (active)

**Status:** `AlienTerrainBiome.shader` v2 + `RtgTerrainMaterialController` + `RtgBiomePalette` on `RTG Terrain`.

**v2 enhancements (2026-07-15):**
- **7 active biomes:** plains (default), wasteland, wetland, fungal forest, highland, rift, water
- **Macro Voronoi regions** (~4 km cells) — large continents of one biome type; fixes leopard-spot noise
- **Wet basins** — marsh/water only inside designated macro wet zones + low elevation
- **Locked palette** from `TERRAIN_BIOME_TAXONOMY.md` applied at runtime via `RtgBiomePalette`

Cesium provides elevation mesh only. Macro zones from Voronoi cells; meso from height/slope; micro detail is texture only.

**Play mode console:**
```
[RTG] Alien terrain biome material applied (shader-based, no Earth raster).
```

**Look for:** Ochre plains, tan wasteland patches, blue-green marsh (localized), bright green forest blobs, crystal blue ridges, orange rifts, dark violet water pockets.

**Tuning:** `RTG_AlienTerrainBiome` material → `_MacroRegionSizeM` (patch size), `_MacroBorderSoftM` (edge blend), `_WetBasinFraction`.

**Architecture:** [CESIUM_ALIEN_WORLD_ARCHITECTURE.md](CESIUM_ALIEN_WORLD_ARCHITECTURE.md) · [TERRAIN_BIOME_TAXONOMY.md](TERRAIN_BIOME_TAXONOMY.md)

---

### ~~Step 1 — Raster overlay~~ (deprecated)

Earth XYZ tiles (`CesiumUrlTemplateRasterOverlay` / `RtgTerrainRasterOverlay`) abandoned — wrong art path. API proxy remains in repo for experiments only.

---

### ~~Step 1b — Alien biome style via Maputnik~~ (deferred)

MapLibre/Planetiler OSM styling deferred. Shader biomes prove pass-over readability first.

---

### Step 2 — Terrain classification data (1–2 weeks)

**Goal:** Server knows terrain type per fog tile; client can query for scatter/biome logic.

**Minimal schema:**

```ts
// packages/shared/src/map/terrain-biome.ts
type TerrainBiome =
  | 'xeno_plains' | 'xeno_wasteland' | 'xeno_wetland'
  | 'xeno_fungal_forest' | 'xeno_highland' | 'xeno_rift'
  | 'xeno_water' | 'xeno_frost' | 'xeno_urban_echo';

interface TerrainTile {
  tileId: string;      // same 400 m fog tile id
  biome: TerrainBiome;
  elevationM: number;  // sampled at tile center
  waterFraction: number; // 0–1 heuristic
}
```

**Generation (POC heuristic, no ML):**
- At world seed: sample Cesium height at tile centers (or use SRTM/terrain raster from Planetiler)
- Classify biome from elevation band + OSM landcover polygon intersection
- Store in `terrain_tiles` table or embed in world seed JSON for POC
- Expose: `GET /worlds/:id/terrain?bbox=…` or extend exploration response

**Files:** `packages/shared`, `apps/api/src/db/`, `apps/api/src/services/terrain-classifier.ts` (new)

---

### Step 3 — Unity biome consumption (optional / minimal for POC)

**Goal:** Ground texture carries Phase 1; scatter is **not** the readability driver.

**POC scope:** Keep existing `RtgTerrainScatter` for Pathfinder beam testing only. Do **not** invest in biome-aware prop palettes until Phase 3 environmental art.

**Deferred (Phase 3):**
- Replace amateur procedural trees/rocks/brush with realistic alien flora/geology prefabs
- Biome-matched prop density and silhouettes
- Updated Pathfinder beam obstacle targets on new prop meshes

**Files (Phase 3):** `RtgTerrainScatter.cs`, `RtgScatterObstacle.cs`, `RtgPathfinderBeam.cs`

---

### Step 4 — Tile pipeline in repo (parallel / week 2–4)

**Goal:** Repeatable alien map style, not one-off Stadia URL.

**Structure:**

```
infra/tiles/
  ├── styles/rtg-alien.json      # Maputnik export
  ├── data/wyoming.osm.pbf       # Geofabrik extract (gitignored, large)
  ├── docker-compose.yml         # TileServer GL
  ├── scripts/build-tiles.sh     # Planetiler → MBTiles
  └── README.md                  # URLs, attribution, Cesium wiring
```

**Serve:** `http://localhost:8080/styles/rtg-alien/{z}/{x}/{y}.png` → Cesium overlay

**Carry to production:** Style JSON, pipeline scripts, attribution — not POC placeholder materials.

---

## Phase 2 — Embedded Alien Resources (Active)

**Xenite v1 design brief:** [XENITE_DEPOSIT_DESIGN_BRIEF.md](XENITE_DEPOSIT_DESIGN_BRIEF.md) — first full deposit art pass; other resources stay on placeholders until Xenite passes pass-over.

Resources should read like Civ strategic resources — **part of the tile**, not a glowing pin hovering above it.

| Work | Status | Description |
|------|--------|-------------|
| Terrain-classified spawn | **Done** | `RESOURCE_BIOME_PREFERENCES` (10/10 resources) + `pickResourceForBiome()` in `packages/shared`; wired in `spawn-seed.ts` + `exploration-repo.ts` |
| Procedural tile classifier | **Done** | `classifyTileBiomeProcedural()` mirrors macro Voronoi shader; `biome` on `MapResourceNode` API response |
| Tile-embedded visuals | **Done** | `RtgTerrainDeposit.cs` — flush deposits, subtle glow ring, per-resource geometry |
| Terrain height anchoring | **Done** | `RtgTerrainHeight.cs` batches `Cesium3DTileset.SampleHeightMostDetailed` after spawn |
| Replace floating markers | **Done** | `RtgEchoSiteLoader` uses deposits for resources; settlements still use `RtgGroundMarkerVisual` |
| Scatter integration | Deferred | Biome-aware exclusion zones around deposits — Phase 2b |
| DB `biome` column | Deferred | Classified at read/spawn time; optional MySQL column later |

**Phase 2 pass-over test:** Flying over a ferracite deposit in highland, player sees ore **in** the rocky ground before tapping — same readability bar as terrain types.

**Files:** `resource-biome-rules.ts`, `tile-biome-classifier.ts`, `spawn-seed.ts`, `exploration-repo.ts`, `RtgTerrainDeposit.cs`, `RtgTerrainHeight.cs`, `RtgEchoSiteLoader.cs`

---

## POC Exit Criteria (Go/No-Go for Terrain)

### Phase 1 — Terrain textures (Civ-rival pass-over)

- [ ] Raster overlay wired in Unity; visible on device at field-test location
- [ ] **Pass-over test:** ≥3 biome transitions obvious **during glider flight** (not only paused overhead)
- [ ] Biome palette has ≥6 distinguishable alien hues (not teal monochrome)
- [ ] FPS/memory improved or unchanged vs pre-fog-removal baseline
- [ ] Documented tile pipeline path (Stadia spike → custom Maputnik style)

### Phase 2 — Embedded resources (active)

- [x] 10/10 resources have terrain preference rules in `RESOURCE_BIOME_PREFERENCES`
- [x] Deposits render **in** terrain surface via `RtgTerrainDeposit` (not floating glow pins)
- [ ] Pass-over test: player spots resource type from flight before tap
- [x] Spawn distribution uses `classifyTileBiomeProcedural` + `pickResourceForBiome`

---

## Recommended Starting Sequence

1. **Now:** Wire Stadia raster overlay in Unity (Step 1A) — prove drape + flight readability same day
2. **This week:** Alien Maputnik style with Civ-grade hue separation (Step 1B) + device pass-over test
3. **Next:** `infra/tiles` pipeline + terrain classification schema (Steps 2–4)
4. **Phase 2:** Embedded resource deposits on classified tiles
5. **Phase 3 (later):** Realistic environmental props — replace laser-test scatter

---

## Key Files Reference

| Area | Path |
|------|------|
| Architecture | `docs/ENGINE_EVALUATION.md` (layer stack, tile tooling) |
| Setup steps | `docs/SETUP.md` (Cesium overlay, tile pipeline) |
| Fog tile math | `packages/shared/src/map/fog-of-war.ts` |
| Unity fog (disabled) | `apps/unity-poc/Assets/Scripts/Game/RtgFogOfWar.cs` |
| Pre-surveyed world flag | `apps/unity-poc/Assets/Scripts/Game/RtgWorldScanSettings.cs` |
| Unity scatter | `apps/unity-poc/Assets/Scripts/Game/RtgTerrainScatter.cs` |
| Terrain raster overlay | `apps/unity-poc/Assets/Scripts/Game/RtgTerrainRasterOverlay.cs` |
| Terrain material | `apps/unity-poc/Assets/Materials/RTG_AlienTerrain.mat` |
| Scene | `apps/unity-poc/Assets/Scenes/SampleScene.unity` |
| Resources (Phase 2) | `packages/shared/src/resources/resource-biome-rules.ts` |
| Tile classifier | `packages/shared/src/map/tile-biome-classifier.ts` |
| Unity deposits | `apps/unity-poc/Assets/Scripts/Game/RtgTerrainDeposit.cs` |
| Terrain height | `apps/unity-poc/Assets/Scripts/Game/RtgTerrainHeight.cs` |

---

## Changelog

| Date | Note |
|------|------|
| 2026-07-14 | Priority shift: **realistic terrain/map tiles** replaces hostile ordnance as active POC |
| 2026-07-14 | **Fog removed from Unity POC** — pre-surveyed world (`RtgWorldScanSettings`); budget freed for terrain tiles |
| 2026-07-14 | **Raster overlay spike** — `RtgTerrainRasterOverlay` + API tile proxy (`terrain-tiles.ts`) |
| 2026-07-14 | Goal sharpened: **Civ-rival pass-over readability** at glider altitude; Phase 2 = **tile-embedded** resources |
| 2026-07-14 | Scatter props noted as **laser-test placeholders** — realistic environmental art deferred to Phase 3 |
| 2026-07-15 | **Phase 2 started** — biome spawn rules, `RtgTerrainDeposit`, Cesium height anchoring, API `biome` on map nodes |
