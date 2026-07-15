# Glider Hero Asset — Commission Brief & Asset Store Search Spec

**Purpose:** Source a production-quality 3D player ship for Routes to Glory, matching the approved concept art `glider_01`.  
**Priority:** Moved ahead of remaining POC items (cockpit look-around, hostile ordnance).  
**Target project:** `apps/game` (production Unity client). POC Phase A blockout in `apps/unity-poc` proves the pipeline.

**Reference art:** `apps/images/glider_01.png` (attach to commission; include in Asset Store evaluation)

**Related:** [ROADMAP.md](ROADMAP.md) · [POC_TO_PRODUCTION.md](POC_TO_PRODUCTION.md)

---

## 1. What we're building

A **hero player glider** for a GPS-driven sci-fi map game (Survey World overlay on real geography). The ship is visible in three camera modes:

| View | Camera | What the asset must do |
|---|---|---|
| **Map** | Overhead chase (~60–80° down, 45–300 m altitude) | Strong top-down silhouette; reads at ~24 m wingspan on screen |
| **Route** | Low angle behind ship (~20° elevation) | Visible fuselage depth, wing thickness, engine pods |
| **Cockpit** | First-person from pilot seat | Nose/canopy area plausible in frame (cockpit frame is a separate 2D overlay) |

The ship is the **only** hero vehicle in v1. Performance budget is generous for one mesh; **Cesium terrain tiles** dominate GPU cost, not the glider.

---

## 2. Silhouette & design language (from `glider_01`)

### Must match

- **Layout:** Central fuselage + **delta / swept wings**; sharp pointed nose; wide stern
- **Three engines:** one large **center aft** nozzle + **two smaller cylindrical pods** at wing trailing edges
- **Cockpit:** Elongated hexagonal canopy, **cyan/teal glass** with interior glow (emissive)
- **Color blocking:**
  - Primary hull: off-white / light grey paneling (~`#E8ECF0`)
  - Accents: bold **red** wing edges, nose cap, engine bands (~`#C41E2A` / `#8B1520`)
  - Mechanical: dark grey vents, nozzles, recessed panels (~`#3A3F48`)
- **Panel lines:** Visible but not noisy — industrial, slightly weathered military-sci-fi (not pristine TRON, not grimdark)
- **Symmetry:** Bilateral mirror (single mesh, no asymmetric damage for v1)

### Nice to have

- Small red chevron / marking on dorsal spine (per concept art)
- Slight panel wear in albedo (not a separate damage pass)

### Avoid

- Round blob fighters, Star Wars X-Wing clones, chunky low-poly “mobile game” silhouettes
- Dark stealth palette (must read on dark alien map tiles)
- Excessive greeble that disappears at map zoom
- Asymmetric weapons hard-mounted on one wing only

---

## 3. Locked technical gates (do not change without design review)

These values are validated in POC field testing:

| Parameter | Value | Notes |
|---|---|---|
| **Wingspan** | **24 m** | 1 Unity unit = 1 meter |
| **Forward axis** | **+Z = nose** | Ship travels +Z when heading north |
| **Up axis** | **+Y** | Cesium globe anchor |
| **Ground clearance** | ~1.2 m above terrain sample | Avoid z-fight on Cesium mesh |
| **Map follow height** | 300 m × zoom (0.15–12×) | Ship occupies ~2–8% of screen width at typical play |
| **Bank range** | ±22° roll into turns | Mesh must tolerate roll without clipping blob shadow |
| **Thrust pitch** | ~5° nose-up at full throttle | Subtle |

---

## 4. 3D asset deliverables

### Required files

| Deliverable | Spec |
|---|---|
| **Mesh** | FBX or GLTF, Y-up, +Z forward, centered near origin (center of mass ~cockpit) |
| **Triangle budget** | **5 000–15 000 tris** (LOD0); LOD1 optional ≤3 000 tris |
| **Textures** | 1024×1024 minimum; **2048×2048 preferred** for hero asset |
| **Maps** | Albedo (sRGB), Metallic + Smoothness (or ORM packed), Normal (OpenGL +Y), Emission (cockpit + nozzle rings) |
| **Engine empties / bones** | `Engine_Main`, `Engine_Left`, `Engine_Right` at nozzle exits (see §5) |
| **Prefab-ready** | Clean pivots; no embedded scale tricks; no animation required for v1 |

### Optional (Phase B+)

- LOD1 simplified mesh + 256 px impostor atlas for far zoom
- `.blend` source file
- Substance `.sbsar` or layered PSD

### Not required for v1

- Rigged pilot character
- Landing gear animation
- Damage states / variant skins
- Interior cockpit mesh (2D overlay handles FP frame)

---

## 5. Engine attachment points (integration contract)

POC Phase A wires VFX to these transforms. Production mesh **must** expose equivalent points (empties in FBX or named child objects):

```
GliderRoot (+Z nose, +Y up)
├── Engine_Main     — center aft nozzle; exhaust along −Z
├── Engine_Left     — left wing pod; exhaust along −Z
└── Engine_Right    — right wing pod; exhaust along −Z
```

**Placement tolerance:** Nozzle exit within **5 cm** of concept art positions relative to wingspan.

**Downstream systems parented here:**

- VFX Graph / particle afterburner (throttle-driven emission)
- Pathfinder beam origin (center engine or separate `BeamOrigin` forward of nose — TBD in integration)

---

## 6. Materials & rendering (Unity URP mobile)

| Requirement | Detail |
|---|---|
| **Pipeline** | Universal Render Pipeline (URP) forward |
| **Shader** | URP Lit (or custom Lit with emission); no Built-in pipeline |
| **Emission** | Cockpit glass + engine nozzle rings (cyan `#5AD4F0` – `#7EE8FF`) |
| **Shadows** | Ship does **not** cast Cesium terrain shadows; uses **blob shadow** decal under ship (POC shader `RTG/GliderBlobShadow`) |
| **Collider** | None |
| **Mobile** | One skinned or static mesh, ≤2 draw calls for hull |

---

## 7. Asset Store search spec

Use when evaluating Unity Asset Store, Sketchfab, or similar marketplaces.

### Search keywords

```
sci-fi fighter low poly
delta wing spaceship
stylized spaceship URP
top down spaceship 3D
fighter craft game ready
sci-fi vehicle PBR mobile
```

### Hard filters (reject if missing)

- [ ] Game-ready FBX/Unity package
- [ ] Documented or measurable scale (or easy to rescale to 24 m wingspan)
- [ ] **≤20k tris** for hero LOD
- [ ] PBR textures included
- [ ] Clear forward direction (+Z or easy 90° fix)
- [ ] License allows use in commercial mobile game

### Soft scoring (rate 1–5; need ≥4 average on silhouette & palette)

| Criterion | Weight | Notes |
|---|---|---|
| Delta-wing silhouette | High | Must read like concept from above |
| White/red/cyan palette fit | High | Retexture acceptable if UV layout is clean |
| Triple-engine layout | Medium | Can add third center engine if only twin pods |
| Mobile-friendly poly count | Medium | |
| URP compatibility | Medium | Built-in-only assets need material rework |
| Emissive cockpit | Low | Can be painted into emission map |

### Known Asset Store categories to browse

- *3D → Vehicles → Space* (filter: Low Poly, URP)
- *3D → Sci-Fi → Ships*
- Publisher kits with modular fighters (evaluate single-ship export)

### Retexture path (if silhouette is 80% match)

Acceptable workflow for solo dev per ROADMAP build-vs-buy guidance:

1. Buy mesh with good topology + UVs
2. Repaint albedo/emission in Photoshop or Substance to match §2 palette
3. Rename/markings to match RtG fiction (no third-party logos on hull)

**Budget guidance:** $15–80 for store asset; $300–1 500 for custom commission if store search fails.

---

## 8. Commission brief (for 3D artist)

Copy/paste or attach this section plus `glider_01.png`.

---

**Project:** Routes to Glory — mobile GPS sci-fi exploration game (Unity URP)  
**Asset:** Player hero glider — **one** game-ready spaceship  
**Reference:** Top-down concept illustration (attached). Match silhouette and color blocking; artist may add underside/belly detail not visible in concept.

**Views that matter:** Primarily **top-down** (map mode) and **rear three-quarter** (chase camera). Belly and side thickness should look credible at low camera angles.

**Dimensions:** **24 m wingspan**, **~18–20 m nose-to-tail**. Human-scale game world (real-world map).

**Style:** Clean military-sci-fi illustration translate to 3D — crisp panel lines, slight wear, bold red accents, glowing cyan cockpit and engine rings. Not photoreal, not cartoon.

**Deliverables:**

1. Low-poly game mesh (5–15k tris) + optional LOD1
2. PBR texture set (2048² preferred): albedo, metallic/smoothness, normal, emission
3. FBX with +Y up, +Z forward, three engine empties (`Engine_Main`, `Engine_Left`, `Engine_Right`)
4. Unity-import sanity check screenshot (orthographic top + side)

**Technical:**

- No rigging/animation required
- No interior cockpit geometry
- No colliders
- Emissive areas: canopy glass, engine nozzle lips
- File formats: FBX + PNG textures; `.blend` source optional

**Timeline:** [Fill in — typical 1–3 weeks for indie commission]  
**Budget:** [Fill in]

**Usage rights:** Full commercial license for mobile game + marketing screenshots.

---

## 9. Integration checklist (engineering, after asset arrives)

Use in `apps/unity-poc` first (swap blockout), then `apps/game`.

- [ ] Import FBX; confirm scale → 24 m wingspan along X
- [ ] Assign URP Lit materials; wire emission map
- [ ] Map `Engine_*` transforms to `RtgGliderAfterburner` emitters
- [ ] Replace `RtgGliderBlockoutMesh` with prefab reference in `RtgPlayerShipVisual`
- [ ] Keep `RtgGliderBlobShadow` (ellipse under ship)
- [ ] Upgrade afterburner to VFX Graph (optional Phase B polish)
- [ ] Field-test: map zoom, route view, cockpit — FPS vs blockout baseline
- [ ] Document import settings in art pipeline doc

---

## 10. Priority vs remaining POC work

| Order | Item | Where |
|---|---|---|
| **Now** | Hero glider asset (this brief) + integrate into POC | `apps/unity-poc` → `apps/game` |
| **Next** | VFX Graph exhaust polish | `apps/game` |
| **Deferred** | Cockpit look-around & rear camera | `apps/unity-poc` |
| **Deferred** | Hostile ordnance client VFX | `apps/unity-poc` |

Remaining POC items are **not** go/no-go blockers for sourcing the glider; they resume after hero asset integration.

---

## Changelog

| Date | Note |
|---|---|
| 2026-07-13 | Initial brief; glider Phase B moved ahead of cockpit/ordnance POC items |
