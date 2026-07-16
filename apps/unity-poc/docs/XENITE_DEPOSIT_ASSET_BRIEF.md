# Xenite Deposit Asset — Tripo / Commission Brief

**Purpose:** Source a production-quality **embedded deposit prop** for Xenite (tier-1 fuel), replacing procedural cubes in the Unity POC.  
**Status (2026-07-15):** **Active** — art sourcing; procedural placeholder remains until prefab lands.  
**Target project:** `apps/unity-poc` first → `apps/game` production client.

**Gameplay design:** [XENITE_DEPOSIT_DESIGN_BRIEF.md](XENITE_DEPOSIT_DESIGN_BRIEF.md)  
**Architecture:** [CESIUM_ALIEN_WORLD_ARCHITECTURE.md](CESIUM_ALIEN_WORLD_ARCHITECTURE.md)  
**Glider precedent:** [GLIDER_HERO_ASSET_BRIEF.md](../../../docs/GLIDER_HERO_ASSET_BRIEF.md)

**Map icon reference:** Xenite glow `#f97316`, shimmer `#fdba74` (`packages/shared/src/map/resource-icons.ts`)

---

## 1. What we're building

A **tile-embedded fuel vent prop** — not a floating map pin. Players discover it while **flying over real geography** at 30–120 m chase-cam altitude before tapping to claim.

| View | Camera | What the asset must do |
|------|--------|------------------------|
| **Pass-over** | Chase / low-angle, 30–120 m AGL | Reads as **orange fuel vent + crystals** in ~2 seconds at 15–30 m/s |
| **Near tap** | Same, slowed | Ground contact believable; not a toy block on a glowing square |
| **Map pause** | Brief overhead | Silhouette still recognizable (vent + shard cluster) |

This is a **prop kit**, not a hero vehicle. Budget is **low poly**; many instances may exist in a play area (POC seeds up to 48 nodes; only Xenite renders for now).

**Canonical playtest node:** `r-xenite-1` — lat `42.7638`, lng `-105.3762`, biome `xeno_rift`, richness `rich` (56 m footprint).

---

## 2. Silhouette & design language

### Fiction

Crystalline combustible mined from **xenon vents** — terrestrial echo: fuel / petroleum. Primary spawn: **Volcanic Rift** (`xeno_rift`): orange fracture rock with pressurized vent energy.

### Must read at pass-over

- **Warm fuel palette** — orange-amber core (`#f97316`), lighter shimmer highlights (`#fdba74`)
- **Low embedded mass** — fractured rock shelf mostly **in** the ground; 2–4 crystal spires rising modestly (not a tower)
- **Vent mouth** — dark fracture with emissive orange interior (fuel glow)
- **Ground stain** — optional scorched/discolored ring at base (replaces debug orange unlit quad)

### Must match (rift / primary variant)

```
        /\  /\        ← 2–4 angular crystal spires (emissive tips)
       /  \/  \
      [ vent ]         ← fractured basalt shelf, vent cavity
  ~~~~~~~~~~~~~~~~    ← terrain surface (pivot / origin)
```

- **Footprint diameter at scale 1.0:** **10 m** (authoring reference; runtime scales to richness)
- **Max height above surface:** **3–5 m** at authoring scale (≤25% of rich footprint after scale)
- **Symmetry:** Rough bilateral OK; natural rock breakup preferred over perfect mirror

### Biome material variants (one mesh, three materials)

| Biome | Player name | Material notes |
|-------|-------------|----------------|
| `xeno_rift` | Volcanic Rift | **Primary.** Hot orange emission, dark basalt, strongest vent glow |
| `xeno_highland` | Crystal Highland | Cooler rock; orange core emission, subtle cyan rim on crystals |
| `xeno_wasteland` | Dust Expanse | Weathered tan-violet rock; lowest emission; more buried look |

**v1 deliverable:** Rift mesh + textures required. Highland/wasteland can be **material swaps** on the same UV layout (preferred) or separate prefabs.

### Avoid

- Floating crystal on a flat glowing platform (current debug look)
- Neon green crystals (old placeholder color)
- Hero-scale monument (>8 m spires on rich node)
- Photoreal ore PBR that disappears on orange rift terrain
- Hard-mounted UI billboard geometry in the mesh

---

## 3. Locked technical gates (do not change without design review)

Validated in POC (`RtgTerrainDeposit`, `RtgEchoSiteLoader`):

| Parameter | Value | Notes |
|-----------|-------|-------|
| **Scale** | 1 Unity unit = **1 meter** | Same as glider |
| **Up axis** | **+Y** | Cesium globe anchor on deposit root |
| **Forward** | **+Z** optional | Deposit may be rotationally symmetric; align vent opening along +Z if asymmetric |
| **Root pivot** | **Base center on terrain surface** | Bottom of vent shelf at Y=0 on prefab root |
| **Authoring footprint** | **10 m diameter** | Runtime scales to richness footprint |
| **Richness footprints** | sparse 28 m · moderate 40 m · rich **56 m** | `uniform scale = footprint / 10` |
| **Surface clearance** | **+0.45 m** on anchor | `depositSurfaceClearanceM` — prevents Cesium bury |
| **Label height** | Auto from mesh bounds | `BuildResult.LabelHeightM` after scale |
| **Ground ring** | Optional child `GroundDecal` | Replaces procedural glow quad; alpha ≤0.22 |

---

## 4. 3D asset deliverables

### Required files (rift variant)

| Deliverable | Spec |
|-------------|------|
| **Mesh** | FBX or GLTF, Y-up, pivot at ground center |
| **Triangle budget** | **800–2 500 tris** LOD0 (deposit prop, not hero ship) |
| **Textures** | **1024×1024** albedo + emission; 512 normal optional |
| **Maps** | Albedo (sRGB), Emission (vent + crystal tips), Normal (OpenGL +Y) if used |
| **Prefab-ready** | Clean scale; no embedded 100× import tricks |
| **Optional child** | `GroundDecal` — horizontal disc/stain mesh, separate material |

### Optional (Phase B)

- LOD1 ≤400 tris for far instances
- Highland + wasteland material presets (URP Material variants)
- `.blend` source

### Not required for v1

- Mining animation / extractor rig
- Colliders
- Particle systems (engine adds later)
- Separate mesh per richness tier (scale handles density)

---

## 5. Prefab hierarchy (integration contract)

POC loads from `Resources/RTG_Deposits/`. Expose this hierarchy (names exact for tooling):

```
XeniteDeposit_Rift (+Y up, pivot at ground center)
├── GroundDecal          (optional) — horizontal stain ring, Y=0.02
├── RockShelf            — fractured basalt / vent collar
├── VentMouth            — emissive interior (can be part of RockShelf)
├── Crystal_01 … _04     — angled shards (merge to one mesh OK)
└── (no Collider)
```

**Highland / wasteland:** Either:

- `Resources/RTG_Deposits/xenite_highland` / `xenite_wasteland` prefabs, **or**
- Same prefab + material variant selected by `RtgTerrainDeposit` from `biome` string

**Downstream systems:**

- `RtgEchoSiteLoader.SpawnResource` → `RtgTerrainDeposit.BuildEmbedded`
- `AnchorResourcesToTerrain` — Cesium height + clearance (do not animate height per frame)

---

## 6. Materials & rendering (Unity URP mobile)

| Requirement | Detail |
|-------------|--------|
| **Pipeline** | Universal Render Pipeline (URP) forward |
| **Shader** | URP Lit + emission map on vent/crystals |
| **Emission color** | Orange `#f97316` core; `#fdba74` highlights |
| **Shadows** | Cast/receive **off** (matches current deposit meshes) |
| **Mobile** | ≤2 draw calls per deposit (mesh + optional decal) |
| **Instances** | Static batching friendly; no skinned mesh |

---

## 7. Tripo / Asset Store search spec

### Tripo prompt (copy/paste)

```
Low-poly game asset: alien xenon fuel vent embedded in volcanic rock.
Fractured dark basalt shelf flush with ground, glowing orange vent mouth,
3 angular orange-amber crystal spires rising 3 meters. Sci-fi mobile game
prop, top-down readable, 10 meter footprint, pivot at ground center.
URP PBR, emissive crystals, not cartoon, not realistic oil rig.
```

### Search keywords

```
low poly crystal vent
sci-fi resource node game
volcanic crystal deposit
stylized ore vent 3D
alien mineral prop mobile
geothermal vent stylized
```

### Hard filters (reject if missing)

- [ ] Game-ready FBX/GLTF or Unity package
- [ ] **≤3k tris** for LOD0
- [ ] Pivot at base / ground contact obvious
- [ ] Emission-friendly UV islands (vent + crystals)
- [ ] License: commercial mobile game

### Soft scoring (need ≥4 on silhouette & color)

| Criterion | Weight |
|-----------|--------|
| Pass-over silhouette (vent + shards) | High |
| Orange-amber fuel read (not green/blue gem) | High |
| Embedded / grounded (not floating) | High |
| Mobile poly count | Medium |
| Clean UVs for biome material variants | Medium |

**Budget guidance:** $10–40 store kit; $150–500 custom/Tripo gen + cleanup; **not** glider-tier ($300–1500 hero).

---

## 8. Commission brief (for 3D artist / Tripo cleanup)

**Project:** Routes to Glory — mobile GPS sci-fi strategy (Unity URP, Cesium real world)  
**Asset:** **Xenite fuel vent** — one embedded deposit prop (+ optional ground decal)  
**Reference:** Map icon color `#f97316`; spawn on orange volcanic rift terrain.

**Views that matter:** **Top-down pass-over** at 50–100 m altitude and **low chase** at 30 m. Player must identify “fuel vent” while moving.

**Dimensions:** Author at **10 m footprint**, **3–5 m** max height. Runtime scales to 28–56 m footprint by mine richness.

**Style:** Stylized industrial-alien — crisp shapes, emissive fuel crystals, dark rock. Closer to **Civ resource on hex** than photoreal Subnautica.

**Deliverables:**

1. Game mesh 800–2500 tris + optional ground decal
2. PBR textures 1024²: albedo, emission (normal optional)
3. FBX +Y up, pivot ground center
4. Unity import screenshot (top + 3/4 view on orange background)

**Technical:** No rig, collider, or animation. Emissive: vent interior + crystal tips.

**Usage rights:** Full commercial license for mobile game + marketing.

---

## 9. Integration checklist (engineering, after asset arrives)

Use in `apps/unity-poc` first.

### Import paths

| Asset | Resources path | Fallback |
|-------|----------------|----------|
| Rift (primary) | `RTG_Deposits/xenite_rift` | Procedural cubes |
| Highland | `RTG_Deposits/xenite_highland` | Rift prefab + material |
| Wasteland | `RTG_Deposits/xenite_wasteland` | Rift prefab + material |

Place prefabs under `Assets/Resources/RTG_Deposits/`.

### Engineering steps

- [ ] Import FBX; confirm **10 m footprint** at scale 1, pivot at ground
- [ ] Run **Routes to Glory → Sync Xenite Deposit (Tripo)** (creates `xenite_rift.prefab` at Resources path above)
- [ ] Assign URP Lit materials; wire emission (`RtgTerrainDepositGuards.XeniteCanonicalColor`)
- [ ] Verify `RtgTerrainDeposit.TryBuildXeniteFromPrefab` loads prefab (auto when present)
- [ ] Hide/remove procedural `DepositGlow` orange quad when prefab includes `GroundDecal`
- [ ] Field-test pass-over at `r-xenite-1` — identify before tap
- [ ] Field-test tap-to-connect + terrain anchor after tile stream
- [ ] Richness scale: sparse 2.8× · moderate 4× · rich 5.6× uniform
- [ ] Copy pattern to `apps/game` when promoting POC

### Tripo → Unity workflow (same as glider)

1. Generate / download from Tripo Smart Mesh (`glowing_lava_crystal_3d_model`)
2. Tripo plugin imports at **`Assets/glowing_lava_crystal_3d_model/`** (project root) — **no move required for editor playtests**
3. **Routes to Glory → Sync Xenite Deposit (Tripo)** — copies textures/mesh to `Assets/Resources/RTG_Deposits/` and saves `xenite_rift.prefab`
4. `RtgTerrainDeposit` auto-loads that prefab in the editor; normalizes to **10 m** footprint + ground pivot
5. Enter Play mode or run **Reset & Reload World** to respawn deposits at `r-xenite-1`
6. Optional cleanup in Blender if silhouette needs tuning; re-run Sync Xenite Deposit (Tripo)

---

## 10. Priority vs other deposit art

| Order | Item |
|-------|------|
| **Now** | Xenite Tripo prop (this brief) + prefab integration |
| **After pass-over sign-off** | Ferracite / Lumin Spring briefs (same prop-kit pattern) |
| **Deferred** | Particle vent haze, LOD billboards, scatter exclusion zones |

---

## Changelog

| Date | Note |
|------|------|
| 2026-07-15 | Initial brief; prefab wiring in `RtgTerrainDeposit` |
