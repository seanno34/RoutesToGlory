# Light Road / Persisted Routes — Handoff (Jul 2026)

Production-cutover notes for **live Light Road**, **saved travel/connector lines**, and the **magenta** Unity error-color false alarm.

**Also:** short bullets in `docs/AGENT_HANDOFF.md` §6b.

---

## Key files

| File | Role |
|------|------|
| `Assets/Scripts/Game/RtgPersistedRouteDrawer.cs` | Draws saved routes from map API; terrain-anchor; material ensure + orphan purge |
| `Assets/Scripts/Game/RtgLightRoad.cs` | Live GPS trail ribbon (same travel clearance as persisted travel) |
| `Assets/Scripts/Game/RtgTerrainElevationGuards.cs` | Clearance constants + elevation pipeline guardrails |

Related: `RtgTerrainHeight`, `RtgPlayerLocation` (glider clearance), `RtgConnectorLineDrawer` (live tap connectors).

---

## 1. Saved route elevation

`path_json` is **lat/lng only**. Persisted routes must **terrain-sample** per vertex (same Cesium sampler idea as deposits / live Light Road), then add clearance.

**Do not** place saved lines at fixed ellipsoid `groundHeightMeters + offset` alone. That floats routes above the terrain-following glider whenever real ground is below the Douglas fallback (~1476 m).

### Clearance stack (`RtgTerrainElevationGuards`)

| Layer | Clearance above sampled terrain |
|-------|----------------------------------|
| Travel / live Light Road | **+3 m** (`TravelRoadClearanceM`) |
| Tap-claim connectors | **+7 m** (`ConnectorClearanceM`) |
| Glider | **+15 m** (`GliderClearanceM`) |

Keep the glider above route clearances. Prefer fixing route elevation near terrain over raising the ship.

`groundHeightMeters` on the drawer remains a **fallback** while Cesium samples are pending — not the final heighting strategy.

---

## 2. Magenta routes are not a route type

Pink/magenta lines are Unity’s **missing-material error color**, not a designed “magenta route” mode.

### Cause (a) — destroying shared materials

Assigning `line.material` can destroy shared template materials when route GameObjects are destroyed/reloaded. Use **`sharedMaterial`** on live Light Road and persisted LineRenderers. Re-ensure materials after create and after terrain re-anchor (`EnsureMaterials` / `EnsureRouteLineMaterial`).

### Cause (b) — scene-baked unmanaged orphans

Historically, SampleScene saved editor play-mode draws under `RTG Persisted Routes`:

- `Route route-sample-leg`
- `Connector route-sample-connector`

Those children often have **null materials** and were **never** in `_lines`, so `SyncRoutes` never updated them → persistent magenta orphans.

**Fix:** purge unmanaged `LineRenderer` children on **Awake**, **Clear**, and **Sync** (`DestroyUnmanagedChildren`).

### Production cutover

- Do **not** bake sample / editor LineRenderers into the shipped scene.
- Purge-on-load is a **safety net**, not a substitute for a clean scene hierarchy.

---

## 3. Quick verification

1. Reload a saved world with travel + connector routes — lines sit just above terrain, under the glider.
2. No magenta LineRenderers under `RTG Persisted Routes` after Play (console may log purge of unmanaged children once).
3. Live Light Road color matches travel persisted color family (cyan-ish Unlit), not error magenta.
4. After map Sync / rejoin, materials still assigned (`sharedMaterial`).

---

*Update when clearance constants, heighting, or route LineRenderer lifecycle changes.*
