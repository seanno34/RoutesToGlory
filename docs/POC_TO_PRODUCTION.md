# POC → Production — Living Notes

**Purpose:** Capture prototype learnings while building in `apps/unity-poc`, so `apps/game` starts on evidence—not memory.

**When to read:** Before starting the production Unity project. Update this file as Phase 2 POC work lands.

**Related:** [ROADMAP.md](ROADMAP.md) (exit criteria) · [ENGINE_EVALUATION.md](ENGINE_EVALUATION.md) (client platform) · [EXHAUST_VFX_LESSONS.md](EXHAUST_VFX_LESSONS.md) (ship thruster VFX) · [SETUP.md](SETUP.md)

---

## Status

| Milestone | State |
|---|---|
| Phase 1 exit criteria | Done (2026-07-12) |
| Phase 2 exit criteria | In progress — **realistic terrain/map tiles** active (textures → resources); cockpit drag-look + hostile ordnance deferred |
| Production TDD (`apps/game`) | **Not started** — after Phase 2 complete |

---

## Keep (carry forward unchanged)

These proved out in POC and should **not** be re-invented in production:

| Area | What to keep | Where |
|---|---|---|
| **Backend** | `@empire/api` + `@empire/shared` — authoritative game rules, route sessions, claims, fog, config | `apps/api`, `packages/shared` |
| **Repo layout** | Single monorepo; production client as new folder `apps/game` | [ROADMAP.md](ROADMAP.md) |
| **Route capture model** | Always-on movement → session points → persist on end/connect | `RtgRouteSession`, `route-session.ts` |
| **Tap-to-connect** | Corridor distance to **path**, not GPS pin; `useNetworkRoutes` on server | `RtgTapToConnect`, `route-claim.ts` |
| **GPS smoothing** | Velocity prediction + exponential smooth + glitch snap; tunable in-game | `RtgDeviceLocationProvider` |
| **Cockpit UX pattern** | Overlay draw order (world → cockpit art → controls); anchor UI to cockpit art UV | `RtgCockpitView`, `RtgPlayerLocation` |
| **Pre-surveyed world** | Full map visible at mission start; no client fog rendering | `RtgWorldScanSettings`, `RtgEchoSiteLoader.preSurveyedWorld` |
| **Manual vs Auto Pilot** | Manual = real GPS; Auto Pilot = simulated route | `RtgPlayerLocation` |
| **Field-test workflow** | `pnpm dev:field` (caffeinate + API) for Mac-as-server driving tests | `scripts/field-test-server.sh` |
| **Shader / pipeline artifacts** | Light Road glow concept, alien overlay shader approach, tile pipeline scripts | Copy Shader Graphs + pipeline docs, not POC placeholders |
| **Route elevation + materials** | Terrain-sample persisted routes (not fixed `groundHeightMeters`); clearance stack travel +3 / connector +7 / glider +15; `sharedMaterial` + purge unmanaged LineRenderers | [LIGHT_ROAD_ROUTES_HANDOFF.md](../apps/unity-poc/docs/LIGHT_ROAD_ROUTES_HANDOFF.md), `RtgPersistedRouteDrawer`, `RtgLightRoad`, `RtgTerrainElevationGuards` |
| **Goodie hut one-time claim** | Server atomic claim (`is_goodie_hut=1 AND owner_empire_id IS NULL` → 409); client session claimed-ID set + modal lock; skip owned/converted; SampleFile local claim (no fake live ids). Corridor scatter pin is **single-use** (no post-claim swap) — POC tap-test only | [GOODIE_HUT_CLAIM_HANDOFF.md](../apps/unity-poc/docs/GOODIE_HUT_CLAIM_HANDOFF.md), `RtgClaimedGoodieHuts`, `route-claim.ts` |

**Production scene hygiene:** do not ship sample LineRenderers under `RTG Persisted Routes`; purge-on-load is only a safety net.

---

## Rebuild (production-quality, not port)

POC code is disposable. Production should **reimplement** with clean architecture:

| Area | Why rebuild | POC reference |
|---|---|---|
| **UI system** | IMGUI (`OnGUI`) is fine for tuning; production needs uGUI/UI Toolkit + proper touch targets | `RtgPlayerLocation.OnGUI` |
| **Player / camera** | Tightly coupled MonoBehaviour; needs input, camera, and presentation layers | `RtgPlayerLocation` |
| **Route rendering** | Per-route `LineRenderer` + full map load won't scale | `RtgPersistedRouteDrawer`, `RtgLightRoad` |
| **Marker visuals** | Primitive spheres/capsules | `RtgEchoSiteLoader` |
| **Glider / exhaust VFX** | Socket-based Attachments frame, cone plumes, four-stop colors, JSON tuning — **not** bounds-anchored | [EXHAUST_VFX_LESSONS.md](EXHAUST_VFX_LESSONS.md), `RtgGliderExhaustSockets` |
| **Glider presentation (remaining)** | Combat tilt, ordnance, hero mesh polish beyond Tripo POC | Phase 2 criteria |
| **Settings / tuners** | Gear panel is IMGUI; persist tuning via proper settings service | `DrawSettingsGearAndPanel` |

---

## Validated tuning defaults (from field testing)

| Setting | POC default | Notes |
|---|---|---|
| GPS smoothing | 10 | In-game slider 2–24 |
| GPS update distance | 1 m | Restart location service when changed |
| GPS max snap | 120 m | Glitch re-acquire threshold |
| Route snap proximity | **25 m** | 80 m was too far—pulls parallel roads |
| Route simplify tolerance | 12 m | Douglas-Peucker on save + cleanup |
| Route sample spacing | 30 m | Must exceed server ~15 m duplicate reject |
| Tap connect radius | 1000 m | From `/config/public` `minConnectDistanceM` |
| Cockpit pitch | Portrait/landscape separate | In-game tuner when in cockpit |

---

## Known scalability debt (fix in production)

Documented during POC; acceptable for field testing, **not** for massive route networks:

### Route snap (client)
- **Today:** Every GPS tick scans **all** empire routes, decimates each (≤256 pts), allocates new lists.
- **Cost:** ~O(routes × 256) per frame.
- **Production fix:** Spatial index (tile/bbox bucket), cached decimated network, throttle snap checks (e.g. 2–5 Hz or on 5 m movement).

### Route cleanup (server)
- **Today:** `POST /worlds/:id/routes/cleanup` loads all routes, RDP each, once per app session.
- **Production fix:** Background job, incremental cleanup, route merge/dedup for parallel corridors.

### Map fetch
- **Today:** Full world map JSON including all route `path_json`.
- **Production fix:** Viewport/tile-paged routes; server spatial query.

### Claims (server) — partially scaled
- **Today:** Bbox filter around probe point before corridor math (`route-claim.ts`). Good pattern—reuse client-side.
- **Goodie huts:** one-time claim is **server-authoritative** (atomic UPDATE + 409). Client session set / corridor-pin retire are UX nets; do not rely on HashSet-by-id alone if any shared interactive slot can rebind to a new settlement id. See [GOODIE_HUT_CLAIM_HANDOFF.md](../apps/unity-poc/docs/GOODIE_HUT_CLAIM_HANDOFF.md).

### Rendering
- **Today:** One `LineRenderer` per route, decimated to 256 display points.
- **Production fix:** GPU instanced ribbons, chunked meshes, or map-tile route layers.

---

## Phase 2 POC — learnings as they land

| Item | Status | Production note |
|---|---|---|
| Route cleanup & snap | Done | Keep RDP + snap semantics; add spatial index + cache |
| Alien terrain dressing | Done (POC) | `RtgTerrainScatter` + proximity-triggered `RtgPathfinderBeam`; cleared tiles persist; forward terrain clearance for hills |
| Glider Phase A (3D blockout) | Done (POC) | Blob shadow + particle exhaust; see `RtgGliderBlockoutMesh` |
| Glider exhaust (Tripo + sockets + cones) | Done (POC) | See [EXHAUST_VFX_LESSONS.md](EXHAUST_VFX_LESSONS.md); mobile-validated via StreamingAssets tuning |
| Glider hero asset (Phase B) | Done (POC) | Tripo mesh accepted production-ready; optional visual polish later — carry prefab + sockets to `apps/game` |
| Ground-anchored resource markers | Done (POC) | `RtgGroundMarkerVisual` |
| Cockpit look-around & rear view | **Deferred (POC)** | `RtgCameraManager` + drag-look code path done; visual acceptance not met (dashboard slides). See `apps/unity-poc/docs/COCKPIT_DRAG_LOOK_SUMMARY.md` |
| Realistic terrain & map tiles | **Active (POC)** | Civ-rival biome readability at glider pass-over; raster pipeline. Phase 2: tile-embedded resources. Phase 3: replace laser-test scatter props. See `apps/unity-poc/docs/REALISTIC_TERRAIN_POC.md` |
| Hostile ordnance | **Deferred (POC)** | Client VFX + targeting; lightweight server resolver. See `apps/unity-poc/docs/HOSTILE_ORDNANCE_POC.md` |

---

## Architecture decisions confirmed by POC

1. **Server-authoritative routes** — client streams points; server validates speed/accuracy/gaps.
2. **Client replaceable** — zero server changes required for Phase 1 validation.
3. **Cesium + alien overlay** — viable on mid-range phone at tested zoom/pan (monitor tile cost in production).
4. **Synthetic timestamps** — simulated movement passes server speed checks via `gpsSpeedMps` fabricator.
5. **Scatter pins for tap testing** — `approachLat/Lng` separate from DB coords; keep for dev, hide in production UX.

---

## Production kickoff checklist

When Phase 2 is done:

- [ ] Read this doc + [ENGINE_EVALUATION.md](ENGINE_EVALUATION.md)
- [ ] Freeze scale/camera framing gates (see ROADMAP art gates)
- [ ] Create `apps/game` clean Unity project
- [ ] Copy forward: shaders, materials, tile pipeline, art-style guide
- [ ] Do **not** copy: graybox scripts, IMGUI tuners, placeholder meshes
- [ ] Implement spatial route index before scaling route snap
- [ ] Archive `apps/unity-poc` (git history preserves it)

---

## Changelog

| Date | Note |
|---|---|
| 2026-07-12 | Initial doc: Phase 1 done, route snap/cleanup learnings, scalability debt, tuning defaults |
| 2026-07-12 | Phase 2: added cockpit look-around (tap-drag) + rear backup camera criterion |
| 2026-07-14 | Tripo glider exhaust: socket attachments, cone plumes, tuning JSON; lessons in EXHAUST_VFX_LESSONS.md |
| 2026-07-14 | Hero glider (Tripo) accepted as production-ready; Phase 2 active scope complete |
| 2026-07-14 | Cockpit drag-look + ordnance re-added to active POC scope (undeferred) |
| 2026-07-14 | Cockpit drag-look **deferred** (visual UX); hostile ordnance is sole remaining go/no-go POC item |
| 2026-07-14 | Priority shift: **realistic terrain/map tiles** replaces hostile ordnance as active POC |
| 2026-07-14 | Unity fog of war **disabled** — pre-surveyed world fiction; GPU budget for terrain tiles |
| 2026-07-17 | Persisted Light Road terrain-sample + magenta material/orphan purge; see LIGHT_ROAD_ROUTES_HANDOFF.md |
| 2026-07-17 | Goodie hut one-time claim: session set + single-use corridor pin + SampleFile local path + atomic server claim; see GOODIE_HUT_CLAIM_HANDOFF.md |
