# Cockpit Swivel Camera — Summary for External Review

**Project:** `routestoglory/apps/unity-poc`  
**Date:** July 2026  
**Status:** POC in progress — blocking `apps/game` kickoff (with hostile ordnance)

---

## Goal

First-person **cockpit mode** where:

- A **fixed screen-space overlay** (dashboard + frame) stays put on the HUD
- The **3D world** is visible through an open glass canopy (270° FOV, like the Tripo glider — no solid roof/sides)
- **Drag in the glass area** = head swivel only (yaw ±135°, pitch up/down)
- **Rear blocked** by yaw limits; rear view only via dashboard inset camera
- The **ship keeps flying its route**; drag must **not** pan the map or slide the whole scene

---

## Architecture (Current)

| Layer | Implementation |
|--------|----------------|
| **World** | Cesium globe + `CesiumGlobeAnchor` on player marker |
| **Camera** | `Camera.main` — shared with Cesium tile streaming |
| **Normal mode** | Chase cam: `DesiredCameraPosition(_focus)` + `LookAt(_focus)`; drag pans `_focusTarget` in world space (Google Maps style) |
| **Cockpit overlay** | IMGUI full-screen art from `RtgCockpitView` (`glider_cockpit_01.png`) |
| **Cockpit camera** | `ApplyCockpitCamera()` — eye at `marker.position + up * 3.5m`, rotation from travel heading + drag offsets |
| **Rear inset** | `RtgCockpitRearCamera` — render texture drawn on dashboard |

**Key files:**

- `Assets/Scripts/Game/RtgPlayerLocation.cs` — main logic
- `Assets/Scripts/Game/RtgCockpitView.cs` — overlay
- `Assets/Scripts/Game/RtgCockpitRearCamera.cs` — rear inset
- `Assets/Scripts/Game/RtgCockpitCameraLock.cs` — end-of-frame pose lock

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

8. **`RtgCockpitCameraLock`** — Re-applies `ApplyCockpitCamera()` in `LateUpdate` + `OnPreCull` on the camera object to override Cesium/fly-camera writes after `RtgPlayerLocation.LateUpdate`.

9. **Disable Cesium controllers in cockpit** — `CesiumCameraController.enabled = false`, `CesiumOriginShift.enabled = false`; `_focus` locked to marker.

10. **Tuning** — Cockpit FOV 90°, yaw max ±135°, pitch up to 42°, ship hidden via `SetShipVisible(false)`.

---

## Current Bugs

| # | Symptom | Likely causes we haven't ruled out |
|---|---------|-------------------------------------|
| **1** | **Ship moves in reverse** in cockpit | Camera heading 180° off from travel direction (`shipHeadingOffsetDegrees`, `Atan2(forward.x, forward.z)` vs glider nose); view faces aft while marker still advances along route |
| **2** | **Drag still pans entire ship/scene** | Map pan still leaking; Cesium overwriting camera after our lock; drag-look not applying (hit-test / `_gameUiRects` timing); user perceiving route motion + wrong rotation as “pan”; single shared camera fighting Cesium's globe-relative updates |

---

## Suspected Root Issues (For Reviewer)

1. **One camera for everything** — `Camera.main` drives both Cesium streaming and cockpit FPV. Cesium may assume it controls pose for globe precision / origin shift.

2. **Screen-fixed overlay vs world-rotating camera** — Overlay doesn't move; only the world behind the glass should rotate. Any **translation** of the camera or focus reads as “the whole ship sliding.”

3. **Heading / forward vector** — `_travelHeadingRad` from GPS delta; cockpit applies `Mathf.Atan2(forward.x, forward.z) + shipHeadingOffsetDegrees + lookYaw`. A 180° mismatch would explain **reverse** motion feel.

4. **Input / hit-test timing** — `_gameUiRects` built in `OnGUI` (after `LateUpdate`); drag uses **previous frame's** UI rects. May block or mis-route drags.

5. **Art vs behavior** — `glider_cockpit_01.png` is a closed-canopy interior; open glass is approximated by selective UV draws. Production likely needs a **glass-masked overlay** or **3D cockpit mesh** aligned to the Tripo model.

---

## What “Success” Looks Like

- **Manual mode, stationary:** drag left/right/up/down → horizon/world rotates inside fixed frame; **zero** lateral translation; no `Center` button / `_panned` flag
- **Auto Pilot:** ship advances along route; view stays forward-relative; drag adds look offset only
- **Visual:** open top/sides matching Tripo 270° glass; dashboard opaque; no stray bars/lines

---

## Questions for External Opinion

1. Should cockpit use a **dedicated child camera** (or Cinemachine VC) instead of fighting over `Camera.main`?
2. Is **parenting an eye-height pivot** (rotate at eye, not marker origin) the right fix, or stay world-space?
3. How should this interact with **CesiumGlobeAnchor / origin shift** at georeferenced scale?
4. **2D IMGUI overlay + rotating main camera** vs **3D cockpit interior mesh** — which path for POC → production?
5. Best practice for **touch drag-look** with IMGUI dead zones (joystick, buttons, rear inset)?

---

## Related Context

- **Completed:** Socket exhaust VFX, Tripo hero glider (Phase B)
- **POC remaining:** Cockpit drag-look (this doc), hostile ordnance
- **Cockpit art assets:** `Assets/Resources/RTG_PlayerShip/glider_cockpit_01.png`, `glider_cockpit_portrait_01.png`
- **Glider reference:** Tripo model with 270° reinforced glass canopy (open top/sides, pilot view except rear)

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
| `shipHeadingOffsetDegrees` | 0 (tunable) | Nose alignment — suspect for reverse view |
