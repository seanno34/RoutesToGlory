# Routes to Glory — Agent Handoff (Jul 2026)

Handoff for continuing work on **Routes to Glory** (`routestoglory` / GitHub `RoutesToGlory`).  
Last major push: **`0f3362d`** on `main` — Tripo hull editor + device rendering, Xenite deposit integration, terrain/biome work, guardrail comments.

---

## 1. What this project is

**Routes to Glory (RtG)** is a mobile-first GPS empire game: fly a glider over real geography (Cesium), connect Echo Sites, harvest alien resources, and compete against an NPC empire.

| Layer | Path | Role |
|-------|------|------|
| Shared rules & types | `packages/shared/` | Zod schemas, game config defaults, biome/resource rules |
| API | `apps/api/` | Fastify server, world DB, spawn seeds |
| Web PWA | `apps/web/` | React + Mapbox (base `/rtg/`) |
| Unity POC | `apps/unity-poc/` | **Primary active client** — Cesium alien world, ship, deposits, field testing |

**Map anchor (POC):** Douglas, WY (~42.76°N, 105.38°W).

---

## 2. Unity POC — current state

### What works (verified Jul 2026)

- **Biome terrain** — alien shader + classifier (not flat teal); see `TERRAIN_BIOME_TAXONOMY.md`, `RtgBiomePalette`, enhanced `AlienTerrainBiome` shader.
- **Regenerate Playable World** — editor menu in `RtgMapBuilder.cs`: biome terrain, atmosphere, echo sites, ship art sync, Tripo hull bake, presentation refresh.
- **Player ship (Tripo hull)** — textured Tripo fighter in **editor Play** and on **iOS/Android** after Resources bake.
- **Ship orientation** — tuned via `Assets/StreamingAssets/rtg-ship-tuning.json` (do not force zero euler / skip tuning on load).
- **Exhaust VFX** — engine ports, cavities, color stops from same tuning file.
- **Xenite deposits** — Tripo crystal prefab path + tuning sliders; stacking bug fixed (`DestroyImmediate` + clear before rebuild).
- **Mobile build preprocessor** — `RtgPlayerShipBuildPreprocessor.cs` syncs hull into Resources and fails build if device assets invalid.

### Primary editor workflow

1. Open Unity project: `apps/unity-poc/`
2. **Routes to Glory → Regenerate Playable World** (after Tripo/asset changes)
3. **Play** — sample world map from `Assets/StreamingAssets/sample-world-map.json`
4. Save scene after structural edits (Cmd+S)

For live API field tests: run API with sleep prevention (see §5).

---

## 3. Tripo player ship — critical architecture

**Read the in-code guardrails before changing ship code:**

| File | What it documents |
|------|-------------------|
| `Assets/Scripts/Game/RtgPlayerShipVisual.cs` | **TRIPO HULL GUARDRAILS** (class summary) |
| `Assets/Scripts/Editor/RtgPlayerShipAssetSync.cs` | **TRIPO DEVICE PIPELINE** (sync → bake → validate) |
| `Assets/Scripts/Editor/RtgPlayerShipBuildPreprocessor.cs` | Mobile build gate |
| `Assets/Scripts/Game/RtgPlayerLocation.cs` | `EnsureDefaultShipHullPrefab` — editor vs device paths |

### Two paths (do not conflate)

| | Editor Play | Device build |
|---|-------------|--------------|
| **Hull source** | `Assets/TripoModels/.../futuristic_fighter_3d_model.fbx` | `Resources/RTG_PlayerShip/TripoGlider/TripoGlider.prefab` (fallback: same folder FBX) |
| **Textures** | Embedded in TripoModels FBX | `TripoHull.mat` + `TripoHull_Albedo.png` in Resources |
| **Scale** | `FitImportedHullScale()` at runtime | Same — always run after instantiate (localScale reset to 1 first) |
| **Orientation** | `rtg-ship-tuning.json` via `ApplyTo()` | Same |

### Past regressions (avoid repeating)

1. **Log says “using Tripo imported hull” but nothing visible** — renderer existed; materials/mesh refs broken. Use `IsDeviceReadyHullPrefab`, not renderer-only checks.
2. **Baking prefab from `TripoModels/`** — mesh GUIDs outside Resources → invisible on device.
3. **`TripoHull.mat` with URP/Lit but empty `_BaseMap`** — gray/invisible hull on device; editor looked fine.
4. **Forcing `autoOrient=true` / `hullEuler=zero`** — broke tuned orientation from `rtg-ship-tuning.json`.
5. **Skipping `FitImportedHullScale` for “baked” prefab** — microscopic hull on device.
6. **Deferred `Destroy()` on deposit refresh** — stacked Xenite crystals (fixed with `DestroyImmediate` + `ClearDepositVisuals`).

### Key Resources paths (must ship in player)

```
Assets/Resources/RTG_PlayerShip/TripoGlider/
  TripoGlider.prefab          # baked device hull
  futuristic_fighter_3d_model.fbx
  TripoHull.mat
  TripoHull_Albedo.png        # copied on sync
Assets/StreamingAssets/
  rtg-ship-tuning.json        # committed tuned exhaust/orientation
  rtg-ship-tuning.json.example
  rtg-xenite-deposit-tuning.json.example
  sample-world-map.json
```

Gitignored local tuning (user machine): `rtg-xenite-deposit-tuning.json` under StreamingAssets.

---

## 4. Xenite deposits

- **Design:** `apps/unity-poc/docs/XENITE_DEPOSIT_DESIGN_BRIEF.md`
- **Asset brief:** `apps/unity-poc/docs/XENITE_DEPOSIT_ASSET_BRIEF.md`
- **Runtime:** `RtgTerrainDeposit.cs`, `RtgEchoSiteLoader.cs`, `RtgXeniteDepositTuningConfig.cs`
- **Resources prefab:** `Assets/Resources/RTG_Deposits/` (Tripo crystal)
- **POC map:** only `xenite` in active deposit list (`RtgTerrainDepositGuards`)
- **Tuning:** Settings sliders → `ApplyXeniteDepositTuning()` → `RefreshResourceDepositsOnly()`; orientation defaults X=270° (migrated from old 90°)

Stacking fix: `RtgEchoSiteLoader.DestroyObject()` uses `DestroyImmediate`; `BuildEmbedded()` calls `ClearDepositVisuals()` first.

---

## 5. Dev commands

From repo root (`/Users/seancalderon/Projects/routestoglory`):

```bash
pnpm install

# API only (port 3001)
pnpm dev

# API + macOS caffeinate (field testing — keeps Mac awake)
pnpm dev:field

# API + web
pnpm dev:field:all

# Web PWA only
pnpm dev:web

pnpm db:migrate   # needs MYSQL_* or DATABASE_URL
```

Unity mobile build: preprocessor runs automatically; or run **Regenerate Playable World** before building.

---

## 6. Important scripts (Unity)

| Area | Files |
|------|--------|
| World / menu | `Assets/Scripts/Editor/RtgMapBuilder.cs` |
| Ship sync / bake | `Assets/Scripts/Editor/RtgPlayerShipAssetSync.cs` |
| Ship visual | `Assets/Scripts/Game/RtgPlayerShipVisual.cs` |
| Player / marker | `Assets/Scripts/Game/RtgPlayerLocation.cs` |
| Ship tuning | `Assets/Scripts/Game/RtgShipTuningConfig.cs` |
| Echo sites / deposits | `RtgEchoSiteLoader.cs`, `RtgTerrainDeposit.cs` |
| Terrain guards | `RtgTerrainDepositGuards.cs`, `RtgTerrainElevationGuards.cs`, `RtgTerrainClearanceTuningConfig.cs` |
| Biome | `packages/shared` biome types + `RtgBiomePalette.cs`, alien terrain shader |

---

## 7. Shared package (biome taxonomy)

Recent addition: Earth→alien biome mapping documented in `TERRAIN_BIOME_TAXONOMY.md`; classifier in `packages/shared`; 7 biome types in shader (Plains, Wasteland, Wetland, Fungal Forest, Highland, Rift, Water).

---

## 8. Open / follow-up (not blocking)

- **Web PWA** — scaffold exists; not the current focus.
- **ENGINE_EVALUATION docs** — unstaged locally at handoff time.
- **Unity `_Recovery` scenes** — unstaged; do not commit.
- **Xenite** — art brief still marks some procedural fallback paths; Tripo prefab path is wired but may need polish per biome variants.
- **God mode / live world** — Unity can use sample file mode or connect Echo Sites to live API (`RtgEchoSiteLoader`).

---

## 9. Git & deploy notes

- **Branch:** `main` (pushed through `0f3362d`)
- **Do not commit:** `.env`, `tilesource.local.json`, local tuning JSON (gitignored), build artifacts, `deploy/rtg_api_bundle/` unless intentional.
- **Deploy:** see `docs/DEPLOY.md` — PWA to `public_html/rtg/`, API outside public_html.

---

## 10. Debugging checklist (ship)

1. Console: `[RTG] Player ship using Tripo imported hull.` **and** `albedo=<name>` (not `none`).
2. Editor: Hierarchy `Player Marker → Ship → Hull → Model` has enabled `MeshRenderer`.
3. Device: confirm build log from preprocessor; rebuild after **Regenerate Playable World**.
4. If orientation wrong: check `rtg-ship-tuning.json` loaded (`ApplyTo`, not exhaust-only).
5. If invisible on device only: inspect `TripoGlider.prefab` mesh GUID (must be Resources FBX) and `TripoHull.mat` `_BaseMap`.

---

## 11. Conversation context

Recent agent work focused on:

1. Xenite Tripo deposit integration + world reset menu cleanup  
2. Regenerate Playable World (single command for full restore)  
3. Xenite orientation sliders + `rtg-xenite-deposit-tuning.json`  
4. Tripo hull **editor vs device** rendering (multi-day regression cycle — now fixed + documented)  
5. Xenite stacking on slider refresh  
6. Guardrail comments in ship pipeline code  

Prior transcript (Tripo hull debugging arc): agent session `8e33b70d-51fa-4ef9-a06b-4a5c938af2d1` in Cursor agent transcripts.

---

*Update this doc when ship/deposit pipeline or primary workflows change materially.*
