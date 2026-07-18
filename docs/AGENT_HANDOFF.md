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

- **Biome terrain** — unified dark blackish-purple skin + neon pink veins (`AlienTerrainBiome` + `RtgBiomePalette`); see `TERRAIN_SKIN_POC.md`, `TERRAIN_BIOME_TAXONOMY.md`.
- **Regenerate Playable World** — editor menu in `RtgMapBuilder.cs`: biome terrain, atmosphere, echo sites, ship art sync, Tripo hull bake, presentation refresh.
- **Player ship (Tripo hull)** — textured Tripo fighter in **editor Play** and on **iOS/Android** after Resources bake.
- **Ship orientation** — tuned via `Assets/StreamingAssets/rtg-ship-tuning.json` (do not force zero euler / skip tuning on load).
- **Exhaust VFX** — engine ports, cavities, color stops from same tuning file.
- **Xenite deposits** — Tripo crystal prefab path + tuning sliders; stacking bug fixed (`DestroyImmediate` + clear before rebuild).
- **POC spawn filter** — goodie huts and terrain scatter (trees/rocks/brush) **off by default**; only xenite deposits + Echo Sites/capitals as world markers. See `apps/unity-poc/docs/XENITE_SPAWN_HANDOFF.md` (§ POC world-object filter).
- **Mobile build preprocessor** — `RtgPlayerShipBuildPreprocessor.cs` syncs hull into Resources and fails build if device assets invalid.

### Primary editor workflow

1. Open Unity project: `apps/unity-poc/`
2. **Routes to Glory → Regenerate Playable World** (after Tripo/asset changes)
3. **Play** — join overlay: enter **4-digit user PIN**, then **Join** a session or **New Game** (live API). Editor **Sample (Editor)** is offline-only.
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
6. **Deferred `Destroy()` on deposit refresh** — stacked Xenite crystals (fixed with `DestroyImmediate` + `ClearDepositVisuals`). See §4 for Xenite Tripo material regressions (invisible / yellow wash / Sync Persist).

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

## 4. Xenite deposits — Tripo material / Sync

**Read the spawn handoff + in-code guardrails before changing deposit Sync or materials:**

| Doc / file | What it documents |
|------------|-------------------|
| `apps/unity-poc/docs/XENITE_SPAWN_HANDOFF.md` | **TRIPO MATERIAL / SYNC GUARDRAILS** (pipeline + debug table) |
| `Assets/Scripts/Editor/RtgMapBuilder.cs` | **XENITE TRIPO GUARDRAILS** on `SyncXeniteDeposit` / Persist / albedo |
| `Assets/Scripts/Game/RtgTerrainDeposit.cs` | **XENITE TRIPO GUARDRAILS** + `ConfigureXenitePrefabRenderers` |
| `Assets/Scripts/Game/RtgTerrainDepositGuards.cs` | Resources-local prefab / albedo path constants |

Also: design `XENITE_DEPOSIT_DESIGN_BRIEF.md`, art `XENITE_DEPOSIT_ASSET_BRIEF.md`.

### Required pattern (same class as player ship Tripo hull)

| Step | What |
|------|------|
| Bake source | Resources-local mesh under `Assets/Resources/RTG_Deposits/` — **not** TripoModels-only GUIDs |
| Albedo | `EnsureXeniteAlbedoInResources` → `Xenite_Albedo.jpg` **before** `SaveAsPrefabAsset` |
| Materials | External Resources `.mat` with `_BaseMap`; **`PersistXeniteMaterialsToResources`** on bake instance |
| Validate | `IsRenderableDepositPrefab` after Sync (mesh + material + albedo) |
| Runtime | Do **not** force fuel×2.2 emission / orange base wash (`ConfigureXenitePrefabRenderers`) |

### Past regressions (avoid repeating)

1. **Invisible xenite** — prefab mesh/mat GUIDs pointed at `TripoModels/`; editor OK, device empty.
2. **Sync “not renderable”** — normalized `Materials/*.mat` but bake left MeshRenderer on **FBX-embedded** mats without readable `_BaseMap` (skipped Persist).
3. **Solid yellow wash** — fuel×2.2 emission + orange tint destroyed Tripo albedo at runtime.
4. **Stacked crystals** on slider refresh — deferred `Destroy()` (fixed with `DestroyImmediate` + `ClearDepositVisuals`).

### Key paths / workflow

- **Resources prefab:** `Assets/Resources/RTG_Deposits/xenite_rift.prefab` (+ `Xenite_Albedo.jpg`, `Materials/*.mat`)
- **Sync:** `Routes to Glory → Sync Xenite Deposit (Tripo)` in `RtgMapBuilder.cs`
- **POC map:** only `xenite` in `ActivePocDepositResourceIds`
- **Tuning:** Settings → `ApplyXeniteDepositTuning()` → `RefreshResourceDepositsOnly()`; default euler X=270°

---

## 5. Dev commands

From repo root (`/Users/seancalderon/Projects/routestoglory`):

```bash
pnpm install

# API only (port 3001) — Unity Editor default
pnpm dev

# API + macOS caffeinate (keeps Mac awake for LAN field testing)
pnpm dev:field

# API + web
pnpm dev:field:all

# Optional: public HTTPS tunnel to local :3001 (only if production API is down / you need local code)
# Requires cloudflared or ngrok. Paste https://…/api into the Unity join panel.
pnpm dev:tunnel

# Web PWA only
pnpm dev:web

pnpm db:migrate   # needs MYSQL_* or DATABASE_URL
```

### API base URLs (Editor vs public mobile)

| Context | URL |
|---------|-----|
| **Unity Editor** | `http://localhost:3001/api` (auto-retries `127.0.0.1`) |
| **Public mobile (preferred)** | **`https://8082ventures.com/rtg_api/api`** |
| Same Wi‑Fi LAN | `http://192.168.x.x:3001/api` (needs Local Network on iOS) |
| Tunnel to local API | `https://<tunnel-host>/api` from `pnpm dev:tunnel` |

Join overlay field + `PlayerPrefs` `rtg.apiBaseUrl` + `rtg-dev-world.json` are editable overrides. If an old LAN URL is stuck in PlayerPrefs, clear it in the join panel (paste the public HTTPS URL) or delete the key.

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
| Echo sites / deposits | `RtgEchoSiteLoader.cs`, `RtgTerrainDeposit.cs` (**XENITE TRIPO GUARDRAILS**) |
| Terrain guards | `RtgTerrainDepositGuards.cs`, `RtgTerrainElevationGuards.cs`, `RtgTerrainClearanceTuningConfig.cs` |
| Persisted / live routes | `RtgPersistedRouteDrawer.cs`, `RtgLightRoad.cs` — see §6b |
| Goodie hut claim | `RtgClaimedGoodieHuts.cs`, `RtgTapToConnect.cs`, `route-claim.ts` — see §6c |
| Biome | `packages/shared` biome types + `RtgBiomePalette.cs`, alien terrain shader |

### 6b. Light Road + persisted routes (elevation + magenta) — production cutover

**Focused doc:** `apps/unity-poc/docs/LIGHT_ROAD_ROUTES_HANDOFF.md`

| Topic | Rule |
|-------|------|
| **Saved route elevation** | Persisted legs must **terrain-sample** like live Light Road / glider — not fixed ellipsoid `groundHeightMeters`. Clearance stack in `RtgTerrainElevationGuards`: travel **+3 m**, connector **+7 m**, glider **+15 m**. |
| **Magenta “routes”** | Unity **missing-material error color** — **not** a route type. |
| Magenta cause (a) | Assigning `line.material` destroys shared templates on reload → use **`sharedMaterial`**; re-ensure after create/re-anchor. |
| Magenta cause (b) | **Scene-baked** unmanaged children under `RTG Persisted Routes` (`Route route-sample-leg` / `Connector route-sample-connector`) with null mats; `SyncRoutes` never tracked them. **Purge** unmanaged `LineRenderer` children on Awake / Clear / Sync. |
| **Production** | Do **not** bake sample LineRenderers into shipped scenes; purge-on-load is a safety net only. |

Key files: `RtgPersistedRouteDrawer`, `RtgLightRoad`, `RtgTerrainElevationGuards`.

### 6c. Goodie hut one-time claim — production cutover

**Focused doc:** `apps/unity-poc/docs/GOODIE_HUT_CLAIM_HANDOFF.md`

| Topic | Rule |
|-------|------|
| **Root cause** | After claim + map reload, corridor tap pin **swapped** a new unclaimed hut onto the same spot (new settlement id) → HashSet-by-id never blocked. SampleFile claims against live API with fake ids also left markers claimable. |
| **Session gate** | `RtgClaimedGoodieHuts` HashSet survives respawn; Clear on login / New Game / world reset. |
| **Corridor pin** | **Single-use** — `BindCorridorPin` / `RetireCorridorPin`; never re-select nearest unclaimed for that slot after claim. |
| **Modal lock** | `BlockGoodieInteraction` on modal open; Remember + Retire on choice. |
| **SampleFile** | Local claim path in `RtgRouteSession` — do not POST fake ids to live API. |
| **Server** | Atomic `UPDATE … WHERE is_goodie_hut = 1 AND owner_empire_id IS NULL`; 409 if `affectedRows = 0`; coerce TINYINT/Buffer flags. |
| **Spawn / web** | Skip owned / converted / session-claimed huts as claimable goodies. |

### Past regressions (goodie claim — avoid repeating)

1. **Corridor pin rebind after claim** — same screen spot, different id; HashSet misses.
2. **SampleFile → live claim** — fake ids; marker state never sticks.
3. **Non-atomic goodie UPDATE** — double-tap / race can re-roll rewards.
4. **Raw MySQL `is_goodie_hut` as bool** — Buffer/`0`/`1` fools “still claimable” checks.

Key files: `RtgClaimedGoodieHuts`, `RtgTapToConnect`, `RtgEchoSiteLoader`, `RtgMapMarker`, `RtgRouteSession`, `apps/api/src/services/route-claim.ts`.

---

## 7. Shared package (biome taxonomy)

Earth→alien biome mapping in `TERRAIN_BIOME_TAXONOMY.md`; classifier in `packages/shared`; 7 biome types in shader (Plains, Wasteland, Wetland, Fungal Forest, Highland, Rift, Water).

**POC visual skin:** dark blackish-purple family + procedural neon pink veins — `apps/unity-poc/docs/TERRAIN_SKIN_POC.md`. Regenerate Playable World / Apply Biome Terrain picks it up via `RtgBiomePalette.ApplyToMaterial`.

---

## 8. POC success criteria

**Definition of done:** `docs/POC_SUCCESS_CRITERIA.md` — user PIN + game session select, sequential missions A–C (connect 5 Xenite → base camp → reserves at 1%/hr per connected xenite), Victory Stats, in-game Exit near Gear, New Game world gen on mobile + Editor.

**Unity login (criterion 1):** Testers use a **4-digit user PIN** (`users.pin`) plus a **game session ID** (`worlds.access_code`). `RtgGameSessionLogin` calls `GET /worlds/saved?pin=…`, `GET /worlds/by-code/:code?pin=…`, and `POST /worlds` with `{ pin }` for **New Game**. Exit clears markers and returns to the overlay (PIN kept). Editor **Sample (Editor)** remains a secondary offline hatch only. **API URL:** join overlay + `PlayerPrefs` `rtg.apiBaseUrl` / `rtg-dev-world.json` — **Editor = `http://localhost:3001/api`** (retries `127.0.0.1`); **public mobile = `https://8082ventures.com/rtg_api/api`**. Editable override for local `pnpm dev` / LAN / `pnpm dev:tunnel`. See §5 and `docs/DEPLOY.md`.

**Sequential missions (criterion 2):** `RtgMissionProgress` + API `empire_missions` / `/worlds/:id/missions*`. A = connect 5 xenite; B = Found Base Camp on route; C = fill reserves at **1%/hour per connected xenite** (`fillPercent = min(100, count × hoursElapsed)`; live count). Settings **Missions (dev)** accelerate (`near` ~60s / `finish` Skip C) still works. Criterion **4 (Exit)** is Done.

## 9. Open / follow-up (not blocking)

- **Web PWA** — scaffold exists; not the current focus.
- **ENGINE_EVALUATION docs** — unstaged locally at handoff time.
- **Unity `_Recovery` scenes** — unstaged; do not commit.
- **Xenite** — art brief still marks some procedural fallback paths; Tripo prefab path is wired but may need polish per biome variants.
- **God mode / live world** — Unity can use sample file mode or connect Echo Sites to live API (`RtgEchoSiteLoader`).

---

## 10. Git & deploy notes

- **Branch:** `main` (pushed through `0f3362d`)
- **Do not commit:** `.env`, `tilesource.local.json`, local tuning JSON (gitignored), build artifacts, `deploy/rtg_api_bundle/`, Unity `Assets/_Recovery/`, `*.tsbuildinfo` unless intentional.
- **Deploy:** see `docs/DEPLOY.md` — PWA to `public_html/rtg/`, API outside public_html; **public Unity API base = `https://8082ventures.com/rtg_api/api`** (not `/rtg/api`).

---

## 11. Debugging checklist (ship)

1. Console: `[RTG] Player ship using Tripo imported hull.` **and** `albedo=<name>` (not `none`).
2. Editor: Hierarchy `Player Marker → Ship → Hull → Model` has enabled `MeshRenderer`.
3. Device: confirm build log from preprocessor; rebuild after **Regenerate Playable World**.
4. If orientation wrong: check `rtg-ship-tuning.json` loaded (`ApplyTo`, not exhaust-only).
5. If invisible on device only: inspect `TripoGlider.prefab` mesh GUID (must be Resources FBX) and `TripoHull.mat` `_BaseMap`.

### Xenite (Tripo skin / Sync)

See `apps/unity-poc/docs/XENITE_SPAWN_HANDOFF.md` → **TRIPO MATERIAL / SYNC GUARDRAILS**. Quick hits:

1. **Yellow skin** — re-Sync; no fuel×2.2 emission; `_BaseMap` = `Xenite_Albedo.jpg`.
2. **Sync “not renderable”** — Persist external mat before bake (not FBX-embedded mats).
3. **Invisible on device** — Resources-local mesh+mat GUIDs in `xenite_rift.prefab`.

---

## 12. Conversation context

Recent agent work focused on:

1. **User PIN + game session login** — `RtgGameSessionLogin`, `users.pin` migration/heal, New Game + Exit near Gear  
2. Xenite Tripo deposit integration + world reset menu cleanup  
3. Regenerate Playable World (single command for full restore)  
4. Xenite orientation sliders + `rtg-xenite-deposit-tuning.json`  
5. Tripo hull **editor vs device** rendering (multi-day regression cycle — now fixed + documented)  
6. Xenite stacking on slider refresh  
7. Guardrail comments in ship pipeline code  
8. Xenite Tripo albedo Persist / yellow-wash fix + **XENITE TRIPO GUARDRAILS** docs/comments  
9. **Persisted Light Road elevation** (terrain-sample + clearance stack) + **magenta** material/`sharedMaterial` + orphan purge under `RTG Persisted Routes` — see §6b / `LIGHT_ROAD_ROUTES_HANDOFF.md`
10. **Goodie hut one-time claim** — session claimed set + single-use corridor pin + SampleFile local claim + server atomic UPDATE — see §6c / `GOODIE_HUT_CLAIM_HANDOFF.md`

Prior transcript (Tripo hull debugging arc): agent session `8e33b70d-51fa-4ef9-a06b-4a5c938af2d1` in Cursor agent transcripts.

---

*Update this doc when ship/deposit/route/claim pipeline or primary workflows change materially.*
