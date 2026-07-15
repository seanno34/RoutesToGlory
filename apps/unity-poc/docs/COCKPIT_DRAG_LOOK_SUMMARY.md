# Cockpit Swivel Camera — Summary for External Review

**Project:** `routestoglory/apps/unity-poc`  
**Date:** July 2026  
**Status:** **ON HOLD (deferred)** — nice-to-have for POC; not blocking go/no-go or `apps/game` kickoff

**Next active POC item:** [HOSTILE_ORDNANCE_POC.md](HOSTILE_ORDNANCE_POC.md)

---

## Goal

First-person **cockpit mode** where:

- A **fixed screen-space overlay** (dashboard + frame) stays put on the HUD
- The **3D world** is visible through an open glass canopy (270° FOV, like the Tripo glider — no solid roof/sides)
- **Drag in the glass area** = head swivel only (yaw ±135°, pitch up/down)
- **Rear blocked** by yaw limits; rear view only via dashboard inset camera
- The **ship keeps flying its route**; drag must **not** pan the map or slide the whole scene

---

## Where We Left Off (2026-07-14)

### What works (code + debug HUD confirmed)

| Behavior | Status |
|----------|--------|
| Cockpit mode entry/exit | Works |
| `RtgCameraManager` — Chase vs Cockpit ownership | Works |
| `CockpitCamera` renders; fly camera GO deactivated in cockpit | Works |
| Map pan blocked in cockpit (`panned=false`) | Works |
| Look yaw/pitch update on drag (`lookYaw`, `lookPitch` change) | Works |
| Camera translation locked (`markerΔ=0`, `camΔ=0`) | Works |
| Reverse flight heading | Reportedly fixed via `cockpitHeadingOffsetDegrees` |
| Rear inset camera scaffold | Wired (`RtgCockpitRearCamera`) |
| Open-glass IMGUI overlay | Implemented (`RtgCockpitView`) |
| Duplicate AudioListener warnings | Fixed (`RtgAudioSession.SetActiveListener`) |

### What does **not** meet user acceptance (reason for deferral)

**Symptom (user-confirmed):** On drag, the **ship dashboard / whole view still appears to move** with the finger — as if the scene is sliding, not just the world rotating behind a fixed HUD.

**Debug HUD at time of deferral:**

- `cam=CockpitCamera` ✓
- `lookYaw` / `lookPitch` change on drag ✓
- `markerΔ = 0` ✓
- `camΔ = 0` ✓
- `panned = false` ✓

**Interpretation:** The code path is doing **rotation-only head look**, not map pan. The remaining issue is likely **visual/UX**:

1. IMGUI dashboard may not feel truly screen-locked while the 3D camera rotates (world rotation reads as “dashboard sliding”)
2. Ship/dashboard art may still be visible or moving (`SetShipVisible(false)` may be incomplete)
3. Cesium rendering artifact or parallax mismatch between 2D overlay and 3D world
4. User perception of world rotation as “dragging the ship” rather than head swivel

**Decision:** Not critical for user play/function in POC. Park until production (`apps/game`) or a dedicated polish pass.

---

## Architecture (Current)

| Layer | Implementation |
|--------|----------------|
| **World** | Cesium globe + `CesiumGlobeAnchor` on player marker |
| **Camera** | `RtgCameraManager` — `ChaseCamera` / `CockpitCamera` on pivots; `CockpitCamera` renders in cockpit |
| **Normal mode** | Chase cam: `DesiredCameraPosition(_focus)` + `LookAt(_focus)`; drag pans `_focusTarget` in world space |
| **Cockpit overlay** | IMGUI full-screen art from `RtgCockpitView` (`glider_cockpit_01.png`) |
| **Cockpit camera** | `CockpitPivot` at eye height; rotation from travel heading + drag offsets; no `LookAt`, no pan |
| **Rear inset** | `RtgCockpitRearCamera` — render texture drawn on dashboard |

**Key files:**

- `Assets/Scripts/Game/RtgCameraManager.cs` — mode ownership, mirror pattern
- `Assets/Scripts/Game/RtgPlayerLocation.cs` — cockpit entry, look input, pan blocking, debug HUD
- `Assets/Scripts/Game/RtgCockpitView.cs` — overlay
- `Assets/Scripts/Game/RtgCockpitRearCamera.cs` — rear inset
- `Assets/Scripts/Game/RtgAudioSession.cs` — single AudioListener
- `Assets/Scripts/Game/RtgPathfinderBeam.cs` — world-fixed beam in cockpit (not camera-locked)

**Removed:** `RtgCockpitCameraLock.cs` — superseded by `RtgCameraManager`

---

## What We Tried (Chronological)

1. **Scaffold drag-look** — `_cockpitLookYawDeg` / pitch targets, `HandleCockpitLookInput()`, `SmoothCockpitLook()`, `ApplyCockpitCamera()` wired in `LateUpdate`.

2. **Glass canopy overlay (v1)** — Programmatic side pillars, cyan rim lines, console-hood band, dashboard UV strips.  
   **Result:** Horizontal bar + dark vertical bars; looked wrong on top of cockpit art.

3. **Block map pan in cockpit** — `HandlePanInput()` skipped when `_cockpitView.IsActive`; cockpit branch returns before chase-cam `LookAt`.

4. **Camera parented to marker** — Camera child of marker at `(0, eyeHeight, 0)` with local yaw.  
   **Result:** Yaw orbited around marker **base**, not eye → lateral sliding when dragging. **Reverted.**

5. **Legacy full PNG overlay** — Alpha-blended full `glider_cockpit_01.png`.  
   **Result:** Visual artifacts gone, but **opaque baked roof/sides** — inconsistent with Tripo glider glass canopy.

6. **Open glass overlay (v2)** — Only dashboard + left/right A-pillar UV strips from art; no roof, no tint bars.  
   **Result:** Better match to open canopy intent; frame art still sliced from closed-canopy PNG.

7. **World-space euler at eye** — `Quaternion.Euler(pitch, heading + lookYaw, 0)` with position snapped to eye each frame (no parenting).

8. **`RtgCockpitCameraLock`** — Re-applies pose in `LateUpdate` + `OnPreCull`. **Superseded** by `RtgCameraManager`.

9. **Disable Cesium controllers in cockpit** — `CesiumCameraController`, `CesiumOriginShift`, `CesiumGlobeAnchor` on `Camera.main` disabled during gameplay ownership.

10. **`RtgCameraManager` mirror pattern** — Separate `ChaseCamera` / `CockpitCamera`; `CockpitCamera` renders; fly camera GO deactivated in cockpit.

11. **Early suppression in `FixedUpdate`** — Order -32000 before Cesium `FixedUpdate`.

12. **Yaw-before-pitch rotation order** — Standard FPS order on `CockpitPivot`.

13. **Pathfinder beam world-fixed** — No longer camera-locked in cockpit.

14. **Debug HUD** — `cockpitLookDebugHud` showing yaw/pitch, markerΔ, camΔ, panned.

---

## Resume Checklist (when re-prioritized)

1. **Reproduce with debug HUD on device** — Confirm `markerΔ=0`, `camΔ=0`, `panned=false` while user still sees slide.
2. **IMGUI vs 3D split** — Decide if overlay must be screen-fixed while only world rotates (current) vs true split rendering / render layers.
3. **Ship visibility** — Verify `SetShipVisible(false)` hides all dashboard/ship mesh that could move with camera.
4. **Disable pathfinder beam in cockpit** — Rule out beam VFX as slide perception.
5. **URP `UniversalAdditionalCameraData`** — Verify runtime-created `CockpitCamera` stack settings match `Camera.main`.
6. **3D cockpit mesh** — Architecture review step 7; likely production path for correct parallax.
7. **Prefab forward axis** — `ShipRoot` / `ModelOffset` (180°) instead of runtime `cockpitHeadingOffsetDegrees`.
8. **Manual mode stationary test** — Drag with ship not moving; isolates route motion from look motion.

See also: [CURSOR_CAMERA_ARCHITECTURE_REVIEW.md](CURSOR_CAMERA_ARCHITECTURE_REVIEW.md)

---

## What “Success” Looks Like (acceptance criteria — not yet met)

- **Manual mode, stationary:** drag left/right/up/down → horizon/world rotates inside fixed frame; **zero** lateral translation; no `Center` button / `_panned` flag
- **Auto Pilot:** ship advances along route; view stays forward-relative; drag adds look offset only
- **Visual:** dashboard/HUD feels **fixed on screen**; open top/sides matching Tripo 270° glass; no stray bars/lines

---

## Tuning Fields (Inspector — `RtgPlayerLocation`)

| Field | Current default | Purpose |
|-------|-----------------|---------|
| `cockpitEyeHeightMeters` | 3.5 | Pilot eye above marker |
| `cockpitFieldOfView` | 90 | Horizontal FOV in cockpit |
| `cockpitLookYawMaxDegrees` | 135 | ±yaw from travel heading |
| `cockpitLookPitchMinDegrees` | -12 | Extra pitch down |
| `cockpitLookPitchMaxDegrees` | 42 | Extra pitch up (sky through glass) |
| `cockpitLookYawSensitivity` | 0.16 | Degrees per pixel (horizontal drag) |
| `cockpitLookPitchSensitivity` | 0.12 | Degrees per pixel (vertical drag) |
| `cockpitLookSmoothing` | 16 | Look lag smoothing |
| `cockpitHeadingOffsetDegrees` | 0 (often 180) | Nose alignment |
| `cockpitLookDebugHud` | true | Debug overlay for resume work |

---

## Related Context

- **Completed:** Socket exhaust VFX, Tripo hero glider (Phase B), camera manager refactor
- **Deferred:** Cockpit drag-look visual acceptance (this doc)
- **Next POC:** Hostile ordnance — [HOSTILE_ORDNANCE_POC.md](HOSTILE_ORDNANCE_POC.md)
- **Cockpit art assets:** `Assets/Resources/RTG_PlayerShip/glider_cockpit_01.png`, `glider_cockpit_portrait_01.png`
- **Glider reference:** Tripo model with 270° reinforced glass canopy (open top/sides, pilot view except rear)
