# Xenite Deposit — Design Brief (v1)

**Project:** `routestoglory/apps/unity-poc`  
**Date:** July 2026  
**Status:** **Active** — first resource to receive a full embedded-deposit art pass  
**Scope:** Visual + readability spec for Xenite only; spawn/economy rules already locked in shared

**Related:** [REALISTIC_TERRAIN_POC.md](REALISTIC_TERRAIN_POC.md) · [TERRAIN_BIOME_TAXONOMY.md](TERRAIN_BIOME_TAXONOMY.md) · [CESIUM_ALIEN_WORLD_ARCHITECTURE.md](CESIUM_ALIEN_WORLD_ARCHITECTURE.md) · [XENITE_DEPOSIT_ASSET_BRIEF.md](XENITE_DEPOSIT_ASSET_BRIEF.md)

---

## Why Xenite first

| Reason | Detail |
|--------|--------|
| **Tier 1 fuel** | Unlocks day 0; highest tutorial priority after terrain readability |
| **Economy hook** | Rush discount (`xeniteDiscountPerUnit`), booster_pad, hover_lane, phase_stabilizer |
| **Biome fiction** | Strongest tie to `xeno_rift` — validates Phase 2 “resource in tile” bar |
| **Sample world** | `r-xenite-1` in `sample-world-map.json` is the canonical playtest node |
| **Placeholder gap** | Current art is two rotated cubes (vein + crystal); readable at tap distance, not pass-over |

Other resources keep generic `RtgTerrainDeposit` placeholders until Xenite v1 passes the pass-over test.

---

## Game definition (locked — do not change in v1 art pass)

From `packages/shared/src/resources/alien-resources.ts`:

| Field | Value |
|-------|-------|
| **Name** | Xenite |
| **Terrestrial echo** | Fuel / petroleum |
| **Domain** | `fuel` |
| **Description** | Crystalline combustible mined from xenon vents. Powers boosters, hover lanes, and production rush furnaces. |
| **Discovery tier** | 1 (day 0) |
| **Primary uses** | Booster pad, Hover lane, Rush production (partial gold discount) |
| **Stockpile bonus** | +2% route speed modifier effectiveness per 100 units |

**Spawn biomes** (`resource-biome-rules.ts`):

| Priority | Biome | Player name | Fiction |
|----------|-------|-------------|---------|
| 1 | `xeno_rift` | Volcanic Rift | Xenon vents at fracture rock — **signature look** |
| 2 | `xeno_highland` | Crystal Highland | Exposed crystal veins in pale ridge rock |
| 3 | `xeno_wasteland` | Dust Expanse | Buried scrap / weathered surface outcrops |

**Seed weight:** 18 (mid-high among tier-1 resources in `defaults.ts`).

---

## Pass-over acceptance (v1 exit criteria)

Same bar as Phase 2 in [REALISTIC_TERRAIN_POC.md](REALISTIC_TERRAIN_POC.md):

> Flying over a Xenite deposit, the player identifies **fuel / orange vent energy** in the ground **before tap** — at chase-cam altitude (~30–120 m), without reading the floating label.

### Must read at pass-over

1. **Color** — warm fuel tone (orange-amber core, not generic green crystal)
2. **Silhouette** — low embedded mass + one vertical “vent” or shard cluster (not a floating pin)
3. **Ground contact** — deposit sits **in** rift/highland rock; slight embed depth; subtle glow ring at terrain surface
4. **Biome fit** — rift variant reads against orange fracture shader; highland variant reads against pale blue rock

### Must NOT regress

- Glider / Light Road elevation (see `RtgTerrainElevationGuards.cs`)
- Settlement markers (still `RtgGroundMarkerVisual`)
- Tap-to-connect geofence behavior
- Cesium height anchoring (`anchorDepositsToTerrain` on `RtgEchoSiteLoader`)

---

## Visual language (v1 target)

### Brand colors (align Unity ↔ shared)

| Source | Xenite color | Notes |
|--------|--------------|-------|
| `RESOURCE_MAP_ICONS` | `#f97316` glow, `#fdba74` shimmer | **Canonical** for map + deposit |
| `RtgTerrainDepositGuards.XeniteCanonicalColor` | `#f97316` | Unity emissive + prefab materials |

v1 implementation must use shared orange-amber for emissive body + deposit glow ring.

### Silhouette by biome

#### `xeno_rift` (primary — sample node `r-xenite-1`)

- **Read:** “Pressurized vent bleeding luminous fuel crystals”
- **Forms:** fractured basalt shelf (low, wide) + 2–4 angled crystal spires + faint radial glow at vent mouth
- **Scale:** footprint from richness; spires ≤ 25% of footprint height (embedded, not tower)
- **Shader interaction:** emissive peaks pick up rift orange ground; deposit glow **subtle** (α ≈ 0.18, current `GetDepositGlowMaterial`)

#### `xeno_highland` (secondary)

- **Read:** “Crystal vein exposed in ridge rock”
- **Forms:** flat ore shelf + single taller shard; cooler rim light on emissive (slight cyan in emission, orange core)
- **Less glow** than rift — vein catches sun, not vent pressure

#### `xeno_wasteland` (tertiary)

- **Read:** “Weathered surface outcrop / half-buried scrap”
- **Forms:** low mound + broken shard fragments flush with sand; lowest emissive; widest footprint for `rich`

### Richness tiers

| Richness | Footprint (m) | v1 density |
|----------|---------------|------------|
| `sparse` | 28 | 1 small vein, 1 shard |
| `moderate` | 40 | 1 shelf + 2 shards |
| `rich` | 56 | 1 shelf + 3–4 shards + wider glow ring |

Yield is server-side (`yield_per_day`); art only scales footprint via `RichnessFootprint()`.

---

## Fiction hooks (UI copy — optional v1)

| Context | Suggested line |
|---------|----------------|
| Label (first line) | Xenite |
| Label (rift) | rich · Volcanic Rift |
| Tap connect | “Extractor mine — fuel domain” |
| First discovery | “Xenon vent crystals. Rush furnaces accept partial burn.” |

---

## Technical implementation (current → v1)

### Pipeline (unchanged)

```
GET /worlds/:id/map  →  RtgEchoSiteLoader.SpawnResource
                              →  RtgTerrainDeposit.BuildEmbedded (local Y≥0 from surface root)
                              →  AnchorResourcesToTerrain (retry + clearance + ResolveDepositGroundHeight)
```

**Anchoring rules:** Deposit root = `max(Cesium sample, cache, one-shot raycast) + depositSurfaceClearanceM`.
Geometry builds **upward** from the root — never anchor then bury with negative local Y. Retries run while tiles stream in.

### Files to touch for v1

| File | v1 work |
|------|---------|
| `RtgTerrainDeposit.cs` | Prefab path `Resources/RTG_Deposits/xenite_*` with procedural fallback |
| `RtgEchoSiteLoader.cs` | Fix `ResourceColor("xenite")` → orange-amber; optionally read from shared constants later |
| `sample-world-map.json` | Keep `r-xenite-1` at Douglas rift coords for regression playtest |
| `resource-icons.ts` | Already canonical — reference only |

### Files to NOT touch for v1

| File | Reason |
|------|--------|
| `RtgTerrainHeight.cs` / `RtgTerrainElevationGuards.cs` | Glider + trail stable |
| `resource-biome-rules.ts` | Spawn rules locked |
| `alien-resources.ts` | Game definition locked |
| Other `RtgTerrainDeposit` cases | Out of scope until Xenite passes pass-over |

### Code guardrails (in repo)

- `RtgTerrainDepositGuards.ActivePocDepositResourceIds` — only these spawn in Unity (`RtgEchoSiteLoader`)
- `RtgTerrainDeposit` — `xenite` case documents v1 brief + biome branches

---

## Implementation phases (v1)

| Step | Deliverable | Owner |
|------|-------------|-------|
| **v1.0** | Color fix (orange emissive) + improved procedural mesh (vein/vent/shards) for `xeno_rift` | Unity POC |
| **v1.1** | Biome variants (`highland`, `wasteland`) in `BuildEmbedded` switch | Unity POC |
| **v1.2** | Device pass-over sign-off on Douglas sample node + one live-spawned rift node | Playtest |
| **v1.3** | Tripo/static mesh prefab — [XENITE_DEPOSIT_ASSET_BRIEF.md](XENITE_DEPOSIT_ASSET_BRIEF.md) | Art + `Resources/RTG_Deposits/xenite_rift` |

**v1 non-goals:** scatter exclusion zones, DB `biome` column, particle VFX, mining animation, shader graph materials.

---

## Playtest script (v1 sign-off)

1. Load `sample-world-map.json` (or live API with xenite in `xeno_rift`).
2. Auto Pilot tour or HomeToCasper until within ~200 m of `r-xenite-1` (42.7638, -105.3762).
3. Chase cam at ~15–30 m/s; **do not tap** until after overflight.
4. Pass if reviewer calls “fuel / orange vent / xenite” while moving.
5. Tap connect → confirm mine claim still works; deposit stays terrain-anchored after Cesium samples complete.
6. Repeat on physical device at slow cruise.

---

## Open questions (post-v1)

- Particle haze at rift vents (GPU cost on mobile)?
- Shared `ResourceColor` in C# generated from `RESOURCE_MAP_ICONS`?
- Audio cue on first pass-over discovery?

---

## Changelog

| Date | Note |
|------|------|
| 2026-07-15 | v1 brief resumed after terrain/glider/trail elevation fixes |
