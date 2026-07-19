# iOS `level0` / Position out of bounds — root cause (2026-07-18)

**Status:** Fixed by full revert on `main` (`0710d4e` → `508f9f9` → `f026a97`). This doc explains *why* so the Jul 18 features can be re-introduced safely.

**Symptom:** iOS player fail on scene load — `level0` / **Position out of bounds**. Cleared after revert of the Jul 18 Unity visual stack.

---

## Confirmed / most likely cause

**Tripo horizon planets on an 88 km celestial sphere produced world positions (and related mesh scales) that exceed IEEE float16 range (~±65504).** On iOS, Unity’s player/scene path rejects those transforms when loading `level0`.

### Evidence

| Item | Detail |
|------|--------|
| Commit | `72fe52d` — *place Tripo planets on horizon celestial sphere + starry sky* |
| Component | `RtgCelestialBodies` with `distanceMeters: 88000` baked into `SampleScene` |
| Placement | `planet.root.localPosition = direction * distanceMeters` (ENU azimuth/elevation) |
| RinglessPlanet (az 215°, el 1.5°) | **≈ (−50457, 2303, −72060)** — **\|z\| ≈ 72060 > 65504** |
| RingedPlanet (az 40°, el 1°) | **≈ (56557, 1536, 67402)** — **\|z\| ≈ 67402 > 65504** |
| Apparent-size scales | At 88 km, 5° / 2.1° diameters imply target radii ~3.8 km / ~1.6 km → huge uniform mesh scales on Tripo FBX roots |

That matches the earlier investigation finding: **RinglessPlanet z ≈ −72060 at the 88 km sphere**.

`RtgCelestialBodies` used `[ExecuteAlways]` + `OnEnable` / `OnValidate` → `ApplyPlacement()`, and `RtgMapBuilder.EnsureCelestialBodiesInternal` called the same during regenerate. So even when wrapper transforms briefly showed as origin in YAML, editor play, regenerate, and device builds still materialized those out-of-range positions/scales into the loaded hierarchy.

Discarded uncommitted follow-ups that moved the sphere **88 km → 8 km** would have kept max \|pos\| ≈ **6.5k** (safely under float16). That change never landed on `main` before the revert; the smoking gun remained the **88 km baked / ExecuteAlways placement**.

---

## Timeline (reverted commits)

| Commit | What it added | Risk to iOS `level0` |
|--------|----------------|----------------------|
| `df75e4b` | Dark purple terrain skin, night sky shader, xenite-only world spawns | Low alone (no mesh planets at 88 km) |
| `8c6fabf` | Xenite vent vapor VFX; claim bubble/green pad removal | Medium alone (particles); not the float overflow |
| `72fe52d` | Tripo planets + `RtgCelestialBodies` @ **88000 m**, starry sky, scene wiring | **High — primary** |
| `0710d4e` / `508f9f9` / `f026a97` | Reverts of the three above (newest first on `main`) | Restored working iOS load |

**Known discarded uncommitted work** (never the sole fix on device before full revert): celestial distance 88→8 km, runtime planet spawn, ship fill lights, vapor particle tweaks, Manual default, fog marker visibility.

---

## Contributing factors

1. **Baking planets into `SampleScene` + regenerate**  
   Map builder / editor placement wrote celestial hierarchy into the shipping scene instead of spawning at runtime with a hard distance cap.

2. **`[ExecuteAlways]` auto-placement**  
   Opening or validating the scene re-applied 88 km transforms, making it easy to re-bake huge positions/scales into dirty scenes and builds.

3. **Distance chosen for “millions of km” feel via literal meters**  
   Art direction wanted distant planets; implementing that as **88 000 m world units** collided with mobile serialization limits. Angular size at a much closer sphere (or skybox/shader discs) achieves the same look without overflow.

4. **Stale device builds**  
   Iterating distance/runtime spawn locally while the phone still ran an IPA with 88 km baked planets can look like “fix didn’t work” until a clean uninstall/reinstall.

5. **Stacked day’s changes**  
   Terrain, vapor, and planets shipped close together, so first triage mixed CoreMotion / LLDB / VFX red herrings until planets were isolated as the float overflow source.

---

## Likely NOT primary

These were investigated or present in the same day’s work, but **are not the smoking gun** for Position out of bounds:

| Suspect | Why not primary |
|---------|-----------------|
| CoreMotion / Info.plist | Unrelated to transform serialization on `level0` load |
| Bokken / LLDB `String.h` noise | Tooling/debug artifact, not player scene load |
| Starry skybox / `AlienNightSky` alone | No huge mesh transforms; safe to retry carefully |
| Vent vapor particles alone (`8c6fabf`) | Medium risk for perf/VFX, but working stack after full revert included removing vapor — planets remain the float overflow cause |
| Terrain skin / xenite-only spawn filters (`df75e4b` without mesh planets) | Low risk relative to 88 km meshes |

Vapor and terrain are **re-addable carefully**; planets need the hard rules below.

---

## Safe re-introduction order

1. **Terrain skin + night sky (no mesh planets)** — low risk  
2. **Xenite-only spawn filters** — low risk  
3. **Vent vapor VFX** — medium; **test iOS device load after**  
4. **Ship fill lights** — low/medium  
5. **Planets LAST**  
   - Runtime-only spawn (do **not** bake Tripo planets into `SampleScene`)  
   - Sphere distance **≪ 50 km** so **\|pos\| < 50000** (prefer ≤ ~8–20 km + angular diameter sizing)  
   - Build/editor preprocessor **rejects** transforms with any component \|v\| ≥ 65504 (or a tighter project cap, e.g. 50000)  
   - Clean device install after any celestial change  

---

## Hard rules going forward (iOS scene serialization)

1. **Never serialize world/local positions or scales with any component whose absolute value approaches float16 max (~65504).** Prefer a project soft cap of **50000**.  
2. **Do not bake distant celestial meshes into `SampleScene`.** Spawn at runtime (or use skybox/shader impostors).  
3. **Celestial sphere distance must stay well under 50 km** if using real mesh transforms; sell “vast distance” with **angular diameter**, haze, and lighting — not literal 88 km offsets.  
4. **Map regenerate must not write huge planet transforms into the scene asset.** If an editor preview applies placement, reset or strip before save/build.  
5. **Avoid `[ExecuteAlways]` placement that dirties shipping scenes** with out-of-range transforms; gate editor preview vs player build paths.  
6. **Add a build-time check** (editor script / CI) that scans open scenes / prefabs for Transform positions/scales exceeding the cap and **fails the build**.  
7. After celestial or large-transform changes: **clean uninstall + fresh iOS install** before declaring success.  
8. Re-introduce Jul 18 features **in the order above**, with an iOS smoke load after each step — especially after vapor and before/after planets.

---

## Quick reference math

```
float16 max ≈ 65504

distanceMeters = 88000
Ringless (215°, 1.5°) → z ≈ -72061  ❌
Ringed   (40°, 1°)    → z ≈  67402  ❌

distanceMeters = 8000
Ringless (215°, 1.5°) → \|pos\|max ≈ 6551  ✅
```

---

## Related commits / files (historical)

- `apps/unity-poc/Assets/Scripts/Game/RtgCelestialBodies.cs` (removed by `0710d4e`)
- `apps/unity-poc/Assets/Scenes/SampleScene.unity` (`CelestialBodies`, `distanceMeters: 88000`)
- `apps/unity-poc/Assets/Scripts/Editor/RtgMapBuilder.cs` (`EnsureCelestialBodiesInternal`)
- `docs/PLANET_PLACEMENT_INSTRUCTIONS.md` (removed by revert; art intent still valid — implement with safe distances)
