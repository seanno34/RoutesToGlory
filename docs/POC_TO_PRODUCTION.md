# POC → Production — Living Notes

**Purpose:** Capture prototype learnings while building in `apps/unity-poc`, so `apps/game` starts on evidence—not memory.

**When to read:** Before starting the production Unity project. Update this file as Phase 2 POC work lands.

**Related:** [ROADMAP.md](ROADMAP.md) (exit criteria) · [ENGINE_EVALUATION.md](ENGINE_EVALUATION.md) (client platform) · [SETUP.md](SETUP.md)

---

## Status

| Milestone | State |
|---|---|
| Phase 1 exit criteria | Done (2026-07-12) |
| Phase 2 exit criteria | In progress (1/6 done) |
| Production TDD (`apps/game`) | **Not started** — write after Phase 2 |

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
| **Manual vs Auto Pilot** | Manual = real GPS; Auto Pilot = simulated route | `RtgPlayerLocation` |
| **Field-test workflow** | `pnpm dev:field` (caffeinate + API) for Mac-as-server driving tests | `scripts/field-test-server.sh` |
| **Shader / pipeline artifacts** | Light Road glow concept, alien overlay shader approach, tile pipeline scripts | Copy Shader Graphs + pipeline docs, not POC placeholders |

---

## Rebuild (production-quality, not port)

POC code is disposable. Production should **reimplement** with clean architecture:

| Area | Why rebuild | POC reference |
|---|---|---|
| **UI system** | IMGUI (`OnGUI`) is fine for tuning; production needs uGUI/UI Toolkit + proper touch targets | `RtgPlayerLocation.OnGUI` |
| **Player / camera** | Tightly coupled MonoBehaviour; needs input, camera, and presentation layers | `RtgPlayerLocation` |
| **Route rendering** | Per-route `LineRenderer` + full map load won't scale | `RtgPersistedRouteDrawer`, `RtgLightRoad` |
| **Marker visuals** | Primitive spheres/capsules | `RtgEchoSiteLoader` |
| **Glider presentation** | Flat sprite; needs depth, afterburner, combat presentation | Phase 2 criteria |
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

### Rendering
- **Today:** One `LineRenderer` per route, decimated to 256 display points.
- **Production fix:** GPU instanced ribbons, chunked meshes, or map-tile route layers.

---

## Phase 2 POC — learnings as they land

| Item | Status | Production note |
|---|---|---|
| Route cleanup & snap | Done | Keep RDP + snap semantics; add spatial index + cache |
| Alien terrain dressing | Done (POC) | `RtgTerrainScatter` + proximity-triggered `RtgPathfinderBeam`; cleared tiles persist; forward terrain clearance for hills |
| Ground-anchored resource markers | Pending | Establish scale + glow readability gates before art spend |
| Glider afterburner / depth | Pending | Throttle-linked VFX carries to production |
| Cockpit look-around & rear view | Pending | Tap-drag yaw/pitch in cockpit; side-window sightlines; rear-camera inset — reference `RtgCockpitView` |
| Hostile ordnance | Pending | Client VFX in POC; production needs server combat resolver |

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
