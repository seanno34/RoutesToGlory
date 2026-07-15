# Exhaust / Cavity / Cone VFX — Lessons Learned

**Purpose:** Capture what went wrong and what worked while building the Tripo glider thruster effect in `apps/unity-poc`, so future POC iterations and the production client (`apps/game`) avoid the same traps.

**When to read:** Before touching player ship VFX, engine sockets, exhaust tuning UI, or mobile ship defaults.

**Related:** [POC_TO_PRODUCTION.md](POC_TO_PRODUCTION.md) · `RtgGliderAfterburner.cs` · `RtgGliderExhaustSockets.cs` · `rtg-ship-tuning.json.example`

---

## What we built (current architecture)

```
Ship
└── Hull
    ├── Model              (Tripo mesh)
    └── Attachments        (identity frame; never inherits mesh euler)
        ├── EngineSocket_Main
        ├── EngineSocket_Left
        └── EngineSocket_Right
            └── {Socket}_CavityRoot   (created by RtgGliderAfterburner)
                ├── Outer             (cavity outer sphere)
                ├── Core              (cavity core sphere)
                ├── PlumeOuter        (truncated cone mesh)
                └── PlumeCore         (truncated cone mesh)
```

- **Sockets** hold nozzle position in **Attachments local meters** (`mainEngineLocal`, `leftEngineLocal`, `rightEngineLocal`).
- **VFX** parents to sockets at `localPosition = zero`; cavity offsets are child-local via `RtgEngineCavityTuning`.
- **Cone plumes** replace particle flame streaks (`useConePlume = true`) for a cleaner mobile look.
- **Colors** come from four speed stops × four channels (cavity outer, cavity core, plume body, plume halo).
- **Tuning** persists in `rtg-ship-tuning.json` (see [Persistence](#persistence-editor-vs-mobile) below).

---

## Lessons learned (do / don't)

### 1. Never position exhaust from mesh bounds at runtime

**Don't:** Derive nozzle positions from `Renderer.bounds`, vertex scans, or normalized 0–1 anchors mapped through bounds that change with hull bank/pitch.

**Why it failed:** Bounds shift with hull tilt, extreme euler offsets, and imported mesh orientation. Wingspan/height sliders appeared coupled; save/load seemed broken because the reference frame moved every frame.

**Do:** Fixed **Attachments** frame under `Hull`, three named sockets, positions in **meters** relative to Attachments only. See `RtgGliderExhaustSockets` and the socket architecture guide pattern.

### 2. Lock a single exhaust axis convention

**Convention (Attachments local):**

| Axis | Meaning |
|------|---------|
| +X | Wingspan |
| +Y | Up |
| +Z | Exhaust / aft direction |

**Cone mesh** (`RtgMeshPrimitives.ExhaustCone`): base ring at `z = 0`, tip at `z = +height`. Plume scale extends along **+Z**. Plume attach offset is **positive Z** (aft of cavity center).

**Bug we hit:** Cone was built for **−Z** aft while sockets used **+Z** exhaust — plumes pointed at the nose. Fix mesh + `plumeAttachZ` together; don't fix one without the other.

### 3. Separate color channels — they are not interchangeable

| UI channel | Data field | What it tints |
|------------|------------|---------------|
| Cavity outer | `cavityOuter` | Outer sphere at nozzle |
| Cavity core | `cavityCore` | Inner sphere |
| Plume body | `plumeOuter` / `flame` | Cone plume body |
| Plume halo | `plumeCore` / `glow` | Inner cone |

**Symptom:** “Main cavity outer doesn't respond to RGB sliders” while left/right do.

**Cause:** On main, the **large cone** dominated the visual. Cones use **Plume body**, not Cavity outer. Wing nozzles showed the cavity sphere more clearly.

**Do:** When tuning, know which element you're editing. For main center, use **Plume body** for the cone; use **Cavity outer** for the sphere shell.

### 4. Per-engine cavity intensity can wash out color preview

Main engine often runs `intensity: 5` while wings use `~0.2`. Multiplying HDR palette × intensity saturated additive colors so slider moves looked ineffective on main.

**Do (POC fix):** During color-stop preview (`tuneStopIndex >= 0`), skip per-engine intensity on cavity tints and use a lighter HDR boost so RGB sliders stay WYSIWYG. Flight mode still applies full intensity.

**Production:** Treat **hue** (user color stops) and **gain** (per-engine intensity) as separate shader inputs, or clamp preview gain in tuning UI only.

### 5. Material / property-block rules

**Don't:** Reuse one `MaterialPropertyBlock` across multiple renderers — SRP batching can bleed colors between engines.

**Do:**

- One **material instance** per renderer (`new Material(template)` in `AddEngine`).
- One **MaterialPropertyBlock** per engine per part (outer/core/plume), stored on `EngineVfx`.
- Tint the stored material **and** set the renderer's property block each frame.
- Clear orphan property blocks when rebinding (`SetPropertyBlock` with owned block).

### 6. Clear socket children on reconfigure

**Don't:** Assume `Configure()` only runs once. Legacy or duplicate children under `EngineSocket_Main` (from old anchor system) can survive and **won't** receive color updates.

**Do:** `ClearNozzleVfxChildren(nozzle)` before creating VFX in `AddEngine`. Map cavity tunings by **socket name**, not fragile list index.

### 7. Reconfigure afterburner when sockets move

**Don't:** Only move socket transforms and expect existing VFX to stay valid after hierarchy rebuild.

**Do:** `ApplyEngineMounts` → `ReconfigureAfterburner()`. Single-socket drag → `ApplySingleEngineMount` → `RefreshPresentation()`.

### 8. Persistence: editor vs mobile

| Environment | Save path | Load order |
|-------------|-----------|------------|
| Unity Editor Play | `{repo}/rtg-ship-tuning.json` | Project root JSON, then StreamingAssets |
| Device build | `Application.persistentDataPath/rtg-ship-tuning.json` | Persistent (user save) → **StreamingAssets** bundle |

**Production checklist:**

1. Tune in Editor Play mode → **Save tuning** (copies to `Assets/StreamingAssets/rtg-ship-tuning.json` for builds).
2. Commit **`rtg-ship-tuning.json.example`** (team template) and **StreamingAssets** ship tuning (device defaults).
3. Do **not** commit developer-local `rtg-ship-tuning.json` at repo root (gitignored).

Device saves stay on device until manually pulled — they do not sync back to the repo.

### 9. IMGUI tuning is POC-only; semantics carry forward

Settings gear (`DrawSettingsGearAndPanel`) is intentional for field tuning. Production should replace IMGUI with a proper settings/tuning service, but **keep**:

- Socket-local meter sliders (not bounds-normalized).
- Four-stop color model with preview mph + plume manual toggle.
- Per-engine cavity fill (main/left/right).
- Save/load JSON schema (`RtgShipTuningConfig.ShipTuningFile`).

### 10. Mobile validated without on-device re-tuning

Thruster effect works on mobile build when **StreamingAssets** contains saved tuning. No device-specific scale hack on plumes (`SetPlumeVisibilityScale(1f)` — VFX are world-meter anchored).

**Do:** Always verify `[RTG] Afterburner configured with 3 engine VFX stacks` on device logs. Missing `Resources/RTG_PlayerShip/RTG_*.mat` = silent or partial VFX failure.

---

## Dead ends (don't retry)

| Approach | Why abandoned |
|----------|----------------|
| Normalized exhaust anchors (`span01`, `height01`, `aftInset01`) mapped through bounds | Frame drift with hull motion |
| Screen-space / drag-handle positioning | Couldn't reliably separate X/Y/Z; fought gimbal |
| Shared `MaterialPropertyBlock` for all cavity orbs | Cross-engine color bleed |
| `-Z` cone + `+Z` socket convention | Cones pointed forward |
| Applying cavity tuning by engine list index only | Fragile if socket add order changes |
| `renderer.material` getter every tint without owning instance | Instance drift / batching surprises |

---

## Production carry-forward

| Keep | Rebuild |
|------|---------|
| Socket + Attachments hierarchy | IMGUI tuning panels |
| `RtgEngineCavityTuning` per engine | Bounds-based positioning code paths |
| Four-stop color profile + JSON schema | Particle streak exhaust (optional VFX variant) |
| Cone plume mesh + additive materials | Ad-hoc material sharing |
| `RtgShipTuningConfig` load/save split (editor / persistent / streaming) | Legacy anchor fields (migrate then delete) |
| Exhaust shader resources under `Resources/RTG_PlayerShip/` | Tripo-specific euler hacks in production prefab |

---

## Key files

| File | Role |
|------|------|
| `RtgGliderExhaustSockets.cs` | Attachments + socket CRUD |
| `RtgGliderAfterburner.cs` | Cavity spheres + cone plumes + thrust drive |
| `RtgPlayerShipVisual.cs` | Hull hierarchy, socket mounts, afterburner lifecycle |
| `RtgPlayerLocation.cs` | Settings UI, save/load, preview mode |
| `RtgShipTuningConfig.cs` | JSON persistence |
| `RtgMeshPrimitives.cs` | `ExhaustCone` mesh (+Z aft) |
| `RtgExhaustColorProfile.cs` | Four stops, interpolation, plume length fields |
| `RtgEngineCavityTuning.cs` | Per-engine size/offset/intensity/plume shape |
| `rtg-ship-tuning.json.example` | Committed template |
| `Assets/StreamingAssets/rtg-ship-tuning.json` | Bundled device defaults |

---

## Changelog

| Date | Note |
|------|------|
| 2026-07-14 | Initial doc after socket refactor, cone plumes, color-channel fixes, mobile validation |
