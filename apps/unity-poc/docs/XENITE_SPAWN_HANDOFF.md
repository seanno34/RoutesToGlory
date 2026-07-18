# Xenite Resource Spawning — Handoff

Brief reference for how **Xenite deposits** are placed and rendered in the Unity POC (`apps/unity-poc`).

---

## What it does

Xenite is the **only resource deposit rendered in the POC** today. Nodes come from the world map JSON (API or sample file), are filtered by resource id, anchored to lat/lng on the Cesium globe, and built as **tile-embedded props** (flush with terrain, not floating map pins).

Other resource ids may exist in map data but are **skipped** until added to `ActivePocDepositResourceIds`.

### POC world-object filter (Jul 2026)

Unity starts with **xenite deposits + Echo Sites / capitals** only. Decorative props and goodie huts are off by default (existing saves included — client filter, not API reseed):

| Object | Default | Control |
|--------|---------|---------|
| **Xenite deposits** | Spawn | `RtgTerrainDepositGuards.ActivePocDepositResourceIds` (`xenite` only) |
| **Echo Sites / capitals** | Spawn | `RtgEchoSiteLoader.SpawnSettlement` (non-goodie tiers) |
| **Goodie huts** | **Skipped** | `RtgEchoSiteLoader.spawnGoodieHuts` (default `false`) |
| **Trees / rocks / brush** | **Off** | `spawnTerrainScatterProps` + `RtgTerrainScatter.enabledInPlay` (default `false`) |

Re-enable props via **Routes to Glory → Advanced → Setup Terrain Scatter**, or toggle the Echo Sites inspector flags.

---

## Data → spawn pipeline

```
World map JSON (resources[])
  → RtgEchoSiteLoader.SpawnAll() or RefreshResourceDepositsOnly()
  → filter: RtgTerrainDepositGuards.IsActivePocDeposit("xenite")
  → SpawnResource() per node
  → RtgTerrainDeposit.BuildEmbedded()
  → terrain anchor retry (Cesium height sampling)
  → RtgMapMarker (Kind.Resource) + label
```

### Map sources

| Mode | Source |
|------|--------|
| **Sample file** (default editor) | `Assets/StreamingAssets/sample-world-map.json` |
| **Live API** | `GET /api/worlds/:worldId/map` via `RtgEchoSiteLoader` |

Play area anchor: **Douglas, WY** (~42.76°N, 105.38°W). Canonical test node: `r-xenite-1` (see `XENITE_DEPOSIT_ASSET_BRIEF.md`).

### Full spawn vs deposit-only refresh

| Entry point | When |
|-------------|------|
| `SpawnAll()` | Initial load, **Regenerate Playable World**, world reload |
| `RefreshResourceDepositsOnly()` | Xenite **rotation tuning** sliders in Settings (live refresh without rebuilding echo sites) |
| `ClearMarkers()` / `ResetContainer()` | World reset — destroys marker container |

Console summary example:  
`Spawned N settlements, M deposits (K xenite (T Tripo, P procedural))`

---

## Visual build (Tripo vs procedural)

`RtgTerrainDeposit.BuildEmbedded()`:

1. **`ClearDepositVisuals(root)`** — removes existing children (prevents stacking on refresh).
2. **`TryBuildXeniteFromPrefab()`** — preferred path:
   - Loads prefab from `Resources/RTG_Deposits/` by biome:
     - `xenite_rift` (default / rift)
     - `xenite_highland`, `xenite_wasteland` when biome matches
   - Applies **`RtgXeniteDepositTuningConfig.RuntimeEulerOffset`** (default X=270°).
   - Scales to **richness footprint** (authoring ref: 10 m diameter at scale 1).
3. **Procedural fallback** — orange embedded cube + subtle glow if Tripo prefab missing.

Tripo path logs: `[RTG] Xenite deposit using Tripo prefab (...)`.  
Fallback logs a warning to run **Sync Xenite Deposit (Tripo)**.

---

## Orientation tuning

| Item | Location |
|------|----------|
| Runtime field | `RtgPlayerLocation.xeniteDepositEulerOffset` |
| Settings UI | Play mode → Settings → **Xenite deposit** (pitch/yaw/roll sliders) |
| Saved tuning | `StreamingAssets/rtg-xenite-deposit-tuning.json` (gitignored; `.example` committed) |
| Applied at spawn | `RtgXeniteDepositTuningConfig.RuntimeEulerOffset` on prefab `localRotation` |

Slider changes call `ApplyXeniteDepositTuning()` → `RefreshResourceDepositsOnly()`.

**Migration:** old saved X=90° is auto-mapped to default X=270° on load.

---

## Editor / device asset sync

| Menu | Purpose |
|------|---------|
| **Routes to Glory → Sync Xenite Deposit (Tripo)** | Copy Tripo crystal → `Resources/RTG_Deposits/xenite_rift.prefab` |
| **Routes to Glory → Regenerate Playable World** | Full world rebuild including echo sites + deposits |

Device builds need prefab under `Assets/Resources/RTG_Deposits/` (Resources folder ships in player).

Source import candidates: `RtgTerrainDepositGuards.XeniteTripoImportCandidatePaths` (TripoModels folder, etc.).

---

## Other spawn guardrails (do not regress)

1. **POC filter** — only ids in `ActivePocDepositResourceIds` spawn (`xenite` only for now).
2. **Stacking fix** — `ClearDepositVisuals()` before rebuild; `DestroyObject()` uses **`DestroyImmediate`** so refresh doesn’t queue destroys and spawn duplicates.
3. **No orange DepositGlow quad** on Tripo prefab path (removed for Tripo; procedural still uses subtle glow).
4. **Terrain anchoring** — deposits use `depositSurfaceClearanceM` + retry while Cesium tiles stream (`DepositAnchorMaxAttempts`).
5. **Canonical color** — `#f97316` for procedural/map icons only; Tripo path must keep albedo (see below).

---

## TRIPO MATERIAL / SYNC GUARDRAILS

Same class of fix as the player-ship Tripo hull (`PersistHullMaterialsToResources` / `TRIPO HULL GUARDRAILS`). Read in-code summaries before changing Sync or runtime materials:

| File | Guardrail block |
|------|-----------------|
| `RtgMapBuilder.cs` | **XENITE TRIPO GUARDRAILS** on `SyncXeniteDeposit` + helpers |
| `RtgTerrainDeposit.cs` | **XENITE TRIPO GUARDRAILS** (class) + `ConfigureXenitePrefabRenderers` |
| `RtgTerrainDepositGuards.cs` | `XeniteResourcesLocalPrefabGuard` + Resources path constants |

### Past regressions (do not repeat)

1. **Invisible / wrong xenite** — `xenite_rift.prefab` MeshRenderer pointed at **TripoModels** mesh/material GUIDs. Editor looked fine; device (or stripped TripoModels) showed nothing or fell into a broken “Tripo succeeded” path with no procedural fallback.
2. **Sync LogError “not renderable”** — Sync copied Tripo → Resources and normalized `Materials/*.mat` with `_BaseMap`, but **`SaveAsPrefabAsset` left the bake instance on FBX-embedded materials** (no readable `_BaseMap`). Prefab YAML showed material `type: 3` (FBX sub-asset) instead of external `.mat` (`type: 2`).
3. **Solid yellow wash** — runtime (or older Sync) forced **fuel×2.2 emission + orange base tint**, destroying Tripo albedo. Crystal read as a flat yellow slab.

### Required Sync pipeline (do not skip steps)

`Routes to Glory → Sync Xenite Deposit (Tripo)` must:

1. Copy Tripo source → `Assets/Resources/RTG_Deposits/` (Resources-local mesh GUIDs).
2. **`EnsureXeniteAlbedoInResources`** — write flat `Xenite_Albedo.jpg` for `Resources.Load` (mirrors `TripoHull_Albedo`).
3. **`NormalizeXeniteResourcesMaterials`** — external `Materials/*.mat`: `_BaseMap` + white base, **no** fuel HDR emission.
4. Instantiate the **Resources** FBX (not TripoModels-only), then **`PersistXeniteMaterialsToResources`** onto the bake instance **before** `SaveAsPrefabAsset`.
5. Validate with **`IsRenderableDepositPrefab`** (mesh + non-null materials + albedo on `_BaseMap` / `_MainTex`). Fail Sync with LogError if not renderable.

**Do not:**

- Leave MeshRenderer on FBX-embedded materials after bake.
- Bake from TripoModels paths so prefab mesh GUIDs sit outside Resources.
- Force fuel×2.2 emission or orange base wash at runtime (`ConfigureXenitePrefabRenderers` may only apply **subtle** textured emission ~0.22).

### Runtime load rules

- Prefer `Resources/RTG_Deposits/xenite_rift` (biome variants when present).
- `TryBuildXeniteFromPrefab` → `ConfigureXenitePrefabRenderers` → `IsRenderableDepositInstance`; unrenderable → destroy instance → procedural fallback.
- Albedo fallback: `Resources.Load` `RTG_Deposits/Xenite_Albedo` if the material lost `_BaseMap`.

### Debug checklist (Tripo texture / Sync)

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| **Solid yellow skin** | Fuel emission / base tint wash, or mat without Tripo JPEG on `_BaseMap` | Re-Sync; confirm Resources mat `_BaseMap` = `Xenite_Albedo.jpg`, `_EmissionColor` black on disk; runtime must not restore fuel×2.2 |
| **Sync LogError “not renderable”** | Persist skipped — prefab still on FBX-embedded mats | Re-run Sync; confirm Persist runs before `SaveAsPrefabAsset`; prefab materials GUID = external `.mat` (`type: 2`), not FBX (`type: 3`) |
| **Invisible on device / “using Tripo” but nothing** | Mesh/mat GUIDs outside Resources (`TripoModels/`) | Re-Sync so bake uses Resources-local FBX + external mat; confirm `IsRenderableDepositPrefab` |
| Console spawn: procedural only | Prefab missing or fail renderability gate | Run **Sync Xenite Deposit (Tripo)**; check LogWarning for missing mesh/material/albedo |
| Stacked crystals after sliders | Deferred `Destroy` | Keep `ClearDepositVisuals` + `DestroyImmediate` |
| Wrong orientation | Tuning JSON / sliders | `rtg-xenite-deposit-tuning.json` + `RefreshResourceDepositsOnly` |

---

## Key files

| File | Role |
|------|------|
| `RtgEchoSiteLoader.cs` | Map load, `SpawnAll`, `SpawnResource`, `RefreshResourceDepositsOnly` |
| `RtgTerrainDeposit.cs` | `BuildEmbedded`, `TryBuildXeniteFromPrefab`, `ConfigureXenitePrefabRenderers`, `ClearDepositVisuals` |
| `RtgTerrainDepositGuards.cs` | POC filter, prefab paths, color/footprint, Resources-local GUID guard |
| `RtgXeniteDepositTuningConfig.cs` | JSON load/save, runtime euler |
| `RtgPlayerLocation.cs` | Settings UI, `ApplyXeniteDepositTuning()` |
| `RtgMapBuilder.cs` | **Sync Xenite Deposit (Tripo)** — albedo persist + bake + validate |
| `Resources/RTG_Deposits/xenite_rift.prefab` | Device-safe baked prefab |
| `Resources/RTG_Deposits/Xenite_Albedo.jpg` | Flat Tripo albedo for Resources.Load |
| `sample-world-map.json` | Offline xenite node positions |
| `docs/XENITE_DEPOSIT_ASSET_BRIEF.md` | Art/spec for Tripo crystal prop |

---

## Debugging checklist (spawn / placement)

1. Console: spawn summary shows xenite count; Tripo vs procedural split.
2. Prefer the **TRIPO MATERIAL / SYNC GUARDRAILS** table above for yellow skin, Sync LogError, or invisible Tripo.
3. Procedural fallback: orange embedded cubes + log `Xenite using procedural placeholder`.
4. **Nothing near player**: Live API worlds need **New Game** after the Orin spawn fix (old sessions may still be Denver). Expect a near ring of xenite within ~2 km of play center; sample map has `r-xenite-1`…`r-xenite-5` around Douglas.

---

## Related docs

- `XENITE_DEPOSIT_ASSET_BRIEF.md` — mesh, materials, biome variants, footprint
- `XENITE_DEPOSIT_DESIGN_BRIEF.md` — gameplay / fiction
- `docs/AGENT_HANDOFF.md` — broader Unity POC + Tripo ship context
