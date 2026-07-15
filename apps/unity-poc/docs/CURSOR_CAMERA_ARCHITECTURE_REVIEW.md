# Cursor Architecture Review: Cockpit Camera System

**Audience:** Cursor AI  
**Source:** External architecture review (July 2026)  
**Related:** [COCKPIT_DRAG_LOOK_SUMMARY.md](COCKPIT_DRAG_LOOK_SUMMARY.md)

## Executive Summary

The current implementation attempts to make a single camera satisfy two
different responsibilities:

1.  Cesium world streaming/navigation.
2.  First-person cockpit free-look.

These responsibilities should be separated architecturally rather than
patched.

## Primary Diagnosis

The cockpit bugs are symptoms of camera ownership.

Multiple systems appear to write to `Camera.main`.

There should be exactly one authority responsible for the final camera
pose.

## Recommended Architecture

    PlayerShip
    ├── ShipRoot
    ├── Graphics
    │   ├── TripoModel
    │   ├── CockpitMesh
    │   ├── EngineSockets
    │   └── Weapons
    ├── CameraRig
    │   ├── ChasePivot
    │   │   └── ChaseCamera
    │   └── CockpitPivot
    │       └── CockpitCamera
    └── Systems

Create a `CameraManager` that activates exactly one camera mode:

-   ChaseCameraMode
-   CockpitCameraMode
-   OrbitCameraMode
-   CinematicCameraMode

Inactive modes should never write transforms.

## Cockpit Free-Look

Rotate a cockpit pivot---not the ship.

    Ship
    └── CockpitPivot
        └── Camera

Touch drag updates only `CockpitPivot.localRotation`.

The camera position should remain locked to the pilot eye.

Do not use `LookAt()`, orbit logic, focus targets, or camera translation
in cockpit mode.

Instead:

``` csharp
camera.position = cockpitEye.position;
camera.rotation = cockpitPivot.rotation;
```

## Camera Ownership

Avoid a feedback loop where Cesium and gameplay both modify
`Camera.main`.

If Cesium must own `Camera.main`, mirror the active gameplay camera into
it rather than allowing multiple writers.

## Reverse Flight

The reverse-flight symptom is likely a model-forward mismatch.

Correct it once in the prefab:

    ShipRoot
    └── ModelOffset (rotate 180° if needed)
        └── TripoModel

Gameplay should always use `ShipRoot.forward`.

## Cockpit Rendering

The current IMGUI overlay is acceptable for the prototype.

For production, replace it with a true 3D cockpit mesh aligned to the
Tripo model.

Benefits:

-   Correct perspective
-   Natural parallax
-   Easier lighting
-   Easier animation
-   Future VR compatibility

## Refactoring Order

1.  Build CameraManager.
2.  Separate chase and cockpit camera modes.
3.  Introduce CockpitPivot.
4.  Remove LookAt from cockpit.
5.  Lock cockpit camera to pilot eye.
6.  Verify forward-axis alignment.
7.  Replace overlay with 3D cockpit mesh when practical.

## Success Criteria

| Criterion | Status |
|-----------|--------|
| Free-look rotates only the cockpit view | **Code OK — visual acceptance deferred** (see below) |
| Camera never slides during free-look | **Code OK (`camΔ=0`, `panned=false`) — user still sees dashboard slide; deferred** |
| Exactly one system owns camera pose | Done — `RtgCameraManager` per mode |
| Ship forward matches navigation | Partial — reverse flight fixed via runtime `cockpitHeadingOffsetDegrees`; prefab `ShipRoot.forward` not adopted |
| Architecture scales to future camera modes | Partial — manager + two modes; no Orbit/Cinematic yet |

### Deferral note (2026-07-14)

Cockpit drag-look is **ON HOLD** for POC. Debug HUD confirms rotation-only look and zero camera/marker translation, but the user still perceives the dashboard/ship view moving with drag. Treated as nice-to-have visual polish, not a go/no-go blocker. Resume checklist: [COCKPIT_DRAG_LOOK_SUMMARY.md](COCKPIT_DRAG_LOOK_SUMMARY.md).

## Implementation Status (2026-07-14, updated)

### POC complete (refactoring steps 1–5)

- [x] **`RtgCameraManager`** — `Chase` and `Cockpit` modes; inactive mode never writes transforms
- [x] **Separate gameplay cameras** — `ChaseCamera` on `ChasePivot`, `CockpitCamera` on `CockpitPivot` (disabled renderers; transform authority only)
- [x] **Mirror pattern** — `Camera.main` stays **unparented**; receives mirrored pose/FOV from active gameplay camera for Cesium tile streaming
- [x] **`CameraRig` sibling of marker** (under `RTG Player`, not child of `Player Marker`) — decouples cockpit from `CesiumGlobeAnchor` globe-orientation rotation on the marker
- [x] **Cockpit free-look via pivot only** — drag updates look yaw/pitch; `CockpitPivot.localRotation` only. No `LookAt`, no focus pan, no drag translation
- [x] **Eye lock** — `CockpitCamera` at local `(0,0,0)` on `CockpitPivot` at eye height offset
- [x] **Cesium isolation** — `SetGameplayCameraOwnership(true)` disables `CesiumCameraController`, `CesiumOriginShift`, and `CesiumGlobeAnchor` on `Camera.main`
- [x] **Pose re-apply** — `LateUpdate`, `Camera.onPreCull`, and `Application.onBeforeRender` mirror gameplay → main
- [x] **Map pan blocked in cockpit** — `BlocksMapPan()` + redundant `IsCockpitCameraActive()` guard in `HandlePanInput`
- [x] **Rear inset decoupled from look** — `RtgCockpitRearCamera.Render` uses `CameraRig` travel heading only
- [x] **Open-glass IMGUI overlay** — `RtgCockpitView.useGlassCanopyOverlay`
- [x] **Removed `RtgCockpitCameraLock`** — superseded by `RtgCameraManager`

**Key files:** `RtgCameraManager.cs`, `RtgPlayerLocation.cs`, `RtgCockpitView.cs`, `RtgCockpitRearCamera.cs`

### Partial (step 6 — forward axis)

- [~] **Reverse flight** — reportedly fixed in editor with `cockpitHeadingOffsetDegrees` (often 180°). Not yet corrected at the prefab level via `ShipRoot` / `ModelOffset`

### Chase mode

- [x] Chase poses `ChaseCamera`, mirrors into `Camera.main` via `ApplyChaseLookAt`
- [x] `ChasePivot` holds `ChaseCamera` (offset placeholder for future chase rig tuning)

### Camera ownership approach

Gameplay cameras on ship pivots own pose. `Camera.main` is a Cesium streaming mirror only — never parented to the ship, never receives look/pan logic directly.

### Not done (production / future)

- [ ] Full **`ShipRoot` → Graphics → CameraRig`** hierarchy on the player prefab
- [ ] Prefab-level **`ModelOffset` (180°)** instead of runtime heading offsets
- [ ] **3D cockpit mesh** aligned to Tripo glider
- [ ] **Orbit / Cinematic** camera modes
- [ ] **Device playtest** confirming drag-look visual acceptance (dashboard feels screen-fixed) — **deferred**

### Refactoring order checklist

| # | Step | Status |
|---|------|--------|
| 1 | Build CameraManager | Done |
| 2 | Separate chase and cockpit modes | Done — separate gameplay cameras + mirror |
| 3 | Introduce CockpitPivot | Done |
| 4 | Remove LookAt from cockpit | Done |
| 5 | Lock cockpit camera to pilot eye | Done — `CockpitCamera` on pivot |
| 6 | Verify forward-axis alignment | Partial — runtime offset, not prefab |
| 7 | Replace overlay with 3D cockpit mesh | Deferred (production) |
