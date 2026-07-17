# Engine Evaluation — Native Mobile for Routes to Glory

Decision doc for moving **Routes to Glory** (location-based empire builder: map + fog of war + real-world GPS) from the current React/Mapbox PWA toward native mobile for richer graphics and better GPS.

**Status:** research / recommendation. No migration committed yet.
**Primary goal:** high-quality UX with deep, rich, performant graphics.
**Team:** ~15 yrs full-stack, primarily .NET/C#; comfortable rewriting the client in C#.
**Also read:** [ROADMAP.md](ROADMAP.md) · [DEPLOY.md](DEPLOY.md) · [README.md](../README.md)

---

## TL;DR

- The POC works. The question is **client platform**, not architecture — the game rules already live server-side and are engine-agnostic, so any client swap keeps the Node/`@empire/shared` backend.
- **Given the goal (rich, performant graphics) and a C#-native team, the recommended client is Unity (C#).** Unity is where shader-based fog, real-time 3D, particles, and AR are first-class; RN/Flutter map views structurally can't reach that graphics bar.
- **For the map inside Unity, lead with Cesium for Unity** (free OSS plugin, full WGS84 globe, Android/iOS). For the game's **alien-world overlay** vision, use **Cesium World Terrain + a custom stylized raster overlay** (not photoreal) with cartographic polygons for fog/claims — richer, cheaper, and lighter on mobile than Google 3D Tiles.
- **Do NOT lead with Niantic Lightship.** Niantic sold its games business to Scopely (2025); Niantic Spatial deprecated **Lightship.dev**, is migrating to **Scaniverse / NSDK 4.0**, decommissions Lightship.dev on **2027-02-20**, and is pivoting toward enterprise VPS / "physical AI". Keep Niantic **NSDK/VPS** as a *future* option only if camera-AR with centimeter positioning becomes core.
- **React Native + Mapbox** (MapLibre as the OSS/cost hedge) is now the **lighter/faster fallback** — lowest migration risk and best turnkey native GPS, but capped on graphics. Right choice only if the graphics ambition is scaled back.
- The **"fog wonky on new worlds"** bug is a tile-math inconsistency in our own code, **not** a Mapbox limitation. It will follow us to any engine (and must be ported correctly into C#). Fix it independently of the platform decision.

---

## Context: what the POC already is

| Layer | Today | Portable to native? |
|---|---|---|
| Game rules (fog, routes, claims, resources, NPC, diplomacy) | `@empire/shared` + `@empire/api` (Fastify + MySQL) | Yes — server-side, client-agnostic |
| Shared geo/tile math | `packages/shared/src/map/fog-of-war.ts` | Yes (TS) — reusable directly in RN, needs a port elsewhere |
| Map rendering | `mapbox-gl` + `react-map-gl` ([apps/web/package.json](../apps/web/package.json)) | Depends on target (see below) |
| Fog rendering | GeoJSON polygon tiles in Mapbox GL ([apps/web/src/lib/fog-geojson.ts](../apps/web/src/lib/fog-geojson.ts)) | Concept portable; implementation is Mapbox-specific |
| GPS | Browser `navigator.geolocation`, foreground only | Native GPS is the main reason to move |

Key point: **routes, resource claims, resume codes, world seeding, NPC ticks, and diplomacy are all backend concerns.** A client swap re-implements the *view* and *input*, not the game. Keep it that way — it is what makes the platform choice low-stakes and reversible.

Motivation to move (from the query and [ROADMAP.md](ROADMAP.md)):
- **Better GPS** — PWAs get foreground-only, throttled, lower-accuracy geolocation and no reliable background location. Native gives high-accuracy fused location and background/geofence hooks (already parked as a v2 idea: "Capacitor wrapper for improved mobile GPS").
- **Richer graphics** — animated units, particles, 3D, smoother fog transitions.

---

## What "games like Zombie Waves" actually use

- **Zombie Waves** (Fun Formula), **Survivor.io**, **Archero** → **Unity**. These are 3D roguelike/arena shooters. Unity is chosen for real-time 3D combat, particles, and physics — **none of which is map/GPS related.** They are not a precedent for a location game.
- The relevant precedent is the **location-based** class:
  - **Pokémon GO / Ingress / Harry Potter: Wizards Unite / Peridot** → **Unity**. Niantic's Unity C# client APIs wrapped a native plugin for map + geospatial rendering; the hard part they solved is *linking player location to world state at scale* — which is the layer **we already own** in our API.
  - Basemaps come from **OpenStreetMap**-derived tiles (Pokémon GO moved off Google Maps in 2017).

Takeaway: Unity is the proven path **when the game wants AR or heavy 3D**. It does **not** give you GPS, a basemap, anti-spoofing, or multiplayer for free — you build those (we have). So Unity's upside is graphics/AR, and its cost is rebuilding the client + choosing a map layer.

### The Unity map-SDK landscape has churned — this matters

The obvious "Unity + Niantic" answer is now the wrong default:

- **Mapbox Maps SDK for Unity** — unmaintained/effectively dead. (Note: this is the *Unity plugin*, not the alive-and-well **Mapbox mobile SDK for iOS/Android** that React Native uses.)
- **Google Maps for Games** (Unity/Unreal) — discontinued.
- **Niantic Lightship** — Niantic sold its games business to **Scopely** in 2025 and spun off **Niantic Spatial**, which has **deprecated Lightship.dev**, is forcing migration to **Scaniverse + NSDK 4.0**, decommissions Lightship.dev on **2027-02-20**, and is refocusing on **enterprise VPS / "physical AI"** rather than indie game basemaps. World-class AR/VPS tech, but a churny, enterprise-leaning bet for a game map layer today.

**What is actively maintained and graphics-first: [Cesium for Unity](https://cesium.com/platform/cesium-for-unity).** Free, open-source plugin; a full-scale WGS84 globe; streams Cesium World Terrain, imagery, 3D buildings, and (optionally) Google Photorealistic 3D Tiles via **3D Tiles / Cesium ion**; integrates with Unity GameObjects and supports Android/iOS.

### Art direction: stylized "alien world" overlay (this project's vision)

The game's fiction is an **artificial alien world laid over real geography** — players build route-based empires whose growth scales with real-world movement. That means we want a stylized reskin aligned to real coordinates, **not** the real world shown photorealistically. Cesium supports this richly, and dropping photoreal 3D Tiles removes the biggest cost line and the iOS memory issue:

- **Custom raster overlays** — drape our own alien-styled tiles over Cesium World Terrain via `CesiumUrlTemplateRasterOverlay` (any `{z}/{x}/{y}` server), `CesiumTileMapServiceRasterOverlay`, `CesiumWebMapServiceRasterOverlay`, or `CesiumIonRasterOverlay`. Style the tiles in Mapbox Studio / MapTiler / offline. Multiple overlays stack via `Material Key`.
- **Custom tileset material + Shader Graph** — copy `CesiumDefaultTilesetShader` / `CesiumDefaultTilesetMaterial` into the `Opaque Material` slot to recolor, add atmosphere/glow, animate, mask, and drive transparency.
- **Cartographic polygons** — `CesiumCartographicPolygon` clips/highlights geographic regions: a natural primitive for **fog-of-war reveal** and **claimed-territory highlighting** (the default shader already uses an overlay texture as an alpha/clipping mask).
- **Georeferenced Unity geometry** — routes (glowing conduits/particles), empire structures, and units anchored via `CesiumGeoreference` + `CesiumGlobeAnchor`, rendered in real-world position on top of the globe.

Recommended layer stack for the alien look: **Cesium World Terrain** (real elevation) + **custom stylized raster overlay** (alien skin) + optional **Cesium OSM Buildings** restyled with a global alien material + our own **routes/structures/fog** as Unity content.

Caveats: per-building *runtime* restyling is hard (OSM Buildings render batched — a global material is easy, single-building highlight needs feature-ID-in-material work); plain XYZ tiles need `CesiumUrlTemplateRasterOverlay` (TMS requires a `tilemapresource.xml`) and alpha handling so missing tiles don't render white; and the styling is genuine Shader Graph/HLSL work.

---

## Options compared

Scores are relative for *our* use case and are **weighted for this team's priorities**: graphics quality is the top goal, C#/.NET is the team's home turf, and a client rewrite is acceptable. 5 = best. Ramp-up and stack-reuse are therefore scored *for a C# team that is fine rewriting the client*.

| Option | Map + basemap | Native GPS | Fog-of-war effort | 3D / graphics ceiling | AR readiness | Reuse of current stack | Team ramp-up | Cost at scale |
|---|---|---|---|---|---|---|---|---|
| **Unity (C#) + Cesium** | 5 (terrain + custom alien overlay) | 3 (you wire GPS) | 5 (shaders/polygons) | 5 | 4 (add AR Foundation/NSDK) | 1 (rewrite in C#*) | 4 (C# native) | 3 (Unity fee + tile hosting) |
| **React Native + Mapbox** | 5 (Mapbox GL native) | 5 | 3 (GeoJSON fill; no custom shaders) | 2–3 | 2 (add Viro/native AR later) | 5 (React + reuse `@empire/shared` + API) | 5 | 3 (Mapbox per-MAU/loads) |
| **React Native + MapLibre** | 4 (OSS Mapbox fork) | 5 | 3 | 2–3 | 2 | 5 | 5 | 5 (self-host tiles) |
| **Unity (C#) + Niantic NSDK/VPS** | 3 (churn; enterprise pivot) | 5 (VPS cm-accuracy) | 5 | 5 | 5 | 1 (rewrite in C#*) | 3 (C#, but new SDK mid-migration) | 3 (VPS/Maps fees) |
| **Flutter + Mapbox** | 5 | 5 | 3 | 3 | 2 | 1 (new Dart UI; no `@empire/shared` reuse) | 1 (new language) | 3 |
| **Unreal 5** | 2 (no location tooling) | 2 | 5 | 5 | 4 | 1 | 1 (C++/Blueprints, not C#) | 4 (5% after $1M) |
| **Native SwiftUI/Kotlin + MapLibre** | 4 | 5 | 3–4 | 3 | 3 | 2 | 2 (2× platforms) | 5 |

\* Reuse is scored objectively (how much of the current TS/React client carries over). The team has accepted a C# rewrite, so this axis is **low-weight** here — and the Node/`@empire/shared` backend is reused regardless of client.

### Notes per option

- **Unity (C#) + Cesium — recommended.** Best fit for the stated goal: shader-based fog, real-time 3D and particles, and a stylized alien world built on Cesium World Terrain + a custom raster overlay (photoreal 3D Tiles optional). C# is the team's strength. Costs to accept: rewrite the client in C#, port the tile/fog math to C# (and fix the bug below), wire device GPS (Unity `LocationService` or a native plugin), and build/host the styled tiles + shaders. AR can be added later via AR Foundation (and/or Niantic NSDK).
- **React Native + Mapbox — lighter fallback.** Keeps React and reuses the API + TypeScript `@empire/shared` tile math verbatim; best turnkey native GPS, offline tile packs, geofencing. But fog stays a GeoJSON polygon fill and the map is essentially 2.5D vector — it cannot reach the "deep, rich graphics" bar. Choose only if that ambition is scaled back or speed-to-ship dominates.
- **React Native + MapLibre** — Same shape as RN+Mapbox, MIT-licensed, any tile source; removes Mapbox per-load fees at the cost of self-hosting tiles. Same graphics ceiling.
- **Unity (C#) + Niantic NSDK/VPS** — Best-in-class AR and centimeter positioning (VPS 2.0), and the closest to the Pokémon GO stack. But mid-migration (Lightship → Scaniverse/NSDK, Lightship.dev decommissioned 2027-02-20) and pivoting toward enterprise. Reserve for a later AR phase, not the initial map layer.
- **Flutter + Mapbox** — Strong renderer and native GPS, but a full UI rewrite in Dart, no `@empire/shared` reuse, and no C# leverage. Not recommended for this team.
- **Unreal 5** — Best raw graphics, but C++/Blueprints (no C# benefit), heavy for mobile, and no location tooling. Not recommended.
- **Native SwiftUI/Kotlin + MapLibre** — Max control, ~2× platform cost, and would still need a 3D layer for rich graphics. Not recommended.

---

## Recommendation

**Adopt Unity (C#) as the native client, with Cesium for Unity as the real-world map layer.** Given the stated priority (deep, rich, performant graphics) and a C#-native team that accepts a rewrite, this is the option whose ceiling matches the goal:

1. **It matches the top priority.** Only a game engine gives shader-based fog, real-time 3D, particles, and lighting. Cesium supplies a real-world globe (terrain + custom alien overlay) to build the stylized world on. RN/Flutter map views cannot reach this bar.
2. **It plays to the team's strength.** C#/.NET is home turf, so the ramp is Unity's engine model (scenes, prefabs, shaders), not the language.
3. **The expensive parts are already done and reusable.** World state, routes, claims, resources, NPC, and diplomacy stay in the Node/`@empire/shared` backend; the Unity client is a rich view + GPS input over the same API.
4. **AR stays open.** Add AR Foundation later, or Niantic NSDK/VPS if camera-AR with centimeter accuracy becomes a core mechanic.

**Fallback:** if graphics ambition is later scaled back or speed-to-ship dominates, **React Native + Mapbox** (MapLibre as the cost hedge) is the low-risk path that reuses the current TS client and ships fastest — at a hard graphics ceiling.

### Accept these Unity + Cesium trade-offs
- **Client rewrite in C#** (accepted) and a **C# port of the tile/fog math** — fix the bug below during the port.
- **GPS is yours to wire** — Unity `LocationService` or a native plugin; the backend already owns anti-spoof/world logic.
- **Mobile tuning** — raise Maximum Screen Space Error, cap Maximum Cached Bytes. The stylized-overlay approach (World Terrain + custom raster) is far lighter than Google Photoreal 3D Tiles and sidesteps the known iOS memory-growth issue.
- **Shader work is real** — the alien look is Shader Graph/HLSL effort; C# helps with logic, not rendering.
- **Costs** — Unity runtime/rev-share at scale, plus tile/hosting costs for the custom overlay (and Cesium ion / Google 3D Tiles fees only if photoreal is used).

### Protect the migration seam
- Keep **all game rules on the server**; the client is a rich view + GPS input layer.
- Keep the **map layer swappable** (Cesium now; NSDK/VPS or a stylized vector approach later) behind one interface.
- Treat `@empire/shared` as the single source of truth for tile math; the C# port should mirror it exactly (shared test vectors) so web and native never diverge.

### Platform/vendor watch
- **Niantic Lightship → Scaniverse/NSDK 4.0:** Lightship.dev decommissioned **2027-02-20**; do not build the initial map layer on it. Re-evaluate NSDK/VPS only for a dedicated AR phase.
- **Custom tile hosting:** the alien overlay needs styled tiles served somewhere (Mapbox/MapTiler/self-hosted); factor tile-serving cost/ops. Cesium ion / Google 3D Tiles fees apply only if photoreal is used.

---

## Styled-tile tooling (the alien-overlay pipeline)

**Core constraint:** Cesium for Unity overlays consume **raster XYZ tiles** (`CesiumUrlTemplateRasterOverlay`, `{z}/{x}/{y}`, Web Mercator). So the job is always: author an alien style -> produce/serve raster PNG tiles -> point Cesium at the XYZ URL. Author the style as a **portable MapLibre GL style** to avoid vendor lock-in.

### Recommended by tier

1. **Prototype fast (use for the spike): [Stadia Maps](https://docs.stadiamaps.com/raster/) + Stamen.** Ready-made artistic XYZ raster — e.g. `https://tiles.stadiamaps.com/tiles/stamen_toner/{z}/{x}/{y}@2x.png` (high-contrast B/W, ideal to tint in-shader), plus Terrain/Watercolor and custom styles. Zero pipeline; drop straight into a Cesium overlay.
2. **Production custom look (recommended): author + self-serve.**
   - **Author** the alien style in **[Maputnik](https://maplibre.org/maputnik/)** (OSS MapLibre GL style editor) or **MapTiler Customize**.
   - **Data:** **[Planetiler](https://github.com/onthegomap/planetiler)** turns OpenStreetMap into vector tiles (MBTiles/PMTiles) in hours on one machine (OpenMapTiles / Protomaps schema).
   - **Serve as raster:** **[TileServer GL](https://github.com/maptiler/tileserver-gl)** renders vector tiles + GL style into raster XYZ on the fly — exactly Cesium's format — behind a CDN. Owns the look end-to-end, no per-tile fees, no lock-in, and enables offline tile packs later.
3. **Managed alternative (no tile server): MapTiler Cloud** (custom styles + hosted raster; free tier then usage). **MapTiler Server** (~$2,500/yr Standard) for supported self-hosting. Same OpenMapTiles schema, so a Maputnik style ports cleanly.
4. **Premium hosted editor: Mapbox Studio + Static Tiles API.** Best editor; renders GL styles to raster usable in third-party renderers, but billed **per tile request** (~$0.50/1k after free tier), "not actively maintained" for third-party use, no Mapbox Standard style support, and non-portable. Watch cost/lock-in.

### Comparison

| Tool | Role | Hosting | Cost / licensing | Lock-in |
|---|---|---|---|---|
| Maputnik | MapLibre GL style editor | local/web | Free, OSS | None (portable style JSON) |
| Planetiler | OSM -> vector tiles | you run it | Free, OSS | None |
| TileServer GL | vector+style -> raster XYZ | self-host (Docker) | Free, OSS | None |
| PMTiles + S3/R2 | single-file tile archive | static/CDN | Storage only | None (needs rasterizer for Cesium) |
| MapTiler Cloud | styled raster tiles | managed | Free tier -> usage | Low (OpenMapTiles) |
| MapTiler Server | self-host rasterization | self-host | ~$2,500/yr Standard | Low |
| Mapbox Studio + Static Tiles API | editor + raster render | managed | ~$0.50/1k tiles | High (Mapbox style + billing) |
| Stadia Maps (Stamen) | ready artistic raster XYZ | managed | Free tier -> usage | Low |

### Cesium plumbing + gotchas

- Use `CesiumUrlTemplateRasterOverlay`, **Web Mercator**, standard **XYZ** (not TMS — mind the Y-origin flip; `{reverseY}` exists if needed).
- Prefer **512px @2x** tiles; stack multiple overlays via `Material Key`.
- Handle **alpha for missing tiles** in the tileset shader or they render white.
- Show **OSM attribution** (ODbL) in-app; confirm the provider's ToS allows use in a non-Mapbox renderer (self-host and MapTiler/Stadia are clean; Mapbox has per-tile + caching caveats).

**Default recommendation:** prototype on **Stadia/Stamen** now; for production, author a **MapLibre style in Maputnik** over **Planetiler** OSM data, served as raster by **self-hosted TileServer GL**, with **MapTiler Cloud** as the managed escape hatch.

---

## Fog-of-war caveat (engine-independent — fix regardless of platform)

The "fog is still a bit wonky on new worlds" symptom is a **coordinate-math inconsistency in our own code**, not a Mapbox problem, so migrating engines will **not** fix it.

Root causes in the current tile model:

1. **Longitude scale depends on latitude, but inconsistently.** `latLngToTileId` derives the tile X index using `lngM = 111320 * cos(lat)` at the **point's own latitude**:

```9:14:packages/shared/src/map/fog-of-war.ts
  const latM = 111_320;
  const lngM = 111_320 * Math.cos((lat * Math.PI) / 180);
  const x = Math.floor((lng * lngM) / tileSizeM);
  const y = Math.floor((lat * latM) / tileSizeM);
  return `${x}:${y}`;
```

   Meanwhile the fog **grid** is rasterized using a single `refLat` (the focus point) for the whole viewport:

```81:104:apps/web/src/lib/fog-geojson.ts
  const clipped = clipBoundsToRadius(bounds, focus.lat, focus.lng, MAX_FOG_RADIUS_M);
  const refLat = focus.lat;
  ...
  const minX = Math.floor((clipped.west * lngM) / tileSizeM) - 1;
  ...
      const tileId = `${x}:${y}`;
      if (explored.has(tileId)) continue;
```

   Because explored tiles are indexed with each point's own `cos(lat)` but the fog grid is indexed with the focus point's `cos(lat)`, the two grids **drift apart** the farther a world's tiles are from the current focus latitude — so explored tiles don't line up with fog holes on new/distant worlds.

2. **Tile columns are latitude-warped.** Encoding X as `lng * cos(lat)` means the same longitude maps to a different tile column at different latitudes, so the grid is not globally consistent.

Recommended fix (independent of the engine decision):
- Adopt a **single, consistent tile scheme** — either a standard **Web Mercator / slippy-tile** indexing, or a fixed reference latitude **per world** stored on the world seed — and use it identically in `latLngToTileId`, `tileIdToCenter`, and the fog rasterizer.
- Add a unit test asserting `tileIdToCenter(latLngToTileId(p)) ≈ p` and that a point's tile id equals the fog cell drawn over it, across several latitudes.
- Revisit `MAX_FOG_TILES` (500) and `MAX_FOG_RADIUS_M` only after the indexing is consistent; they are mitigations, not the cause.

Doing this first means the native client inherits a correct fog model instead of porting the bug.

---

## Suggested next step

Time-boxed **Unity + Cesium spike (about 1–2 weeks)** to de-risk the graphics-first path on a real device:

1. Unity project with **Cesium for Unity**: **Cesium World Terrain** + a **custom stylized (alien) raster overlay** via `CesiumUrlTemplateRasterOverlay`, georeferenced to a test city. (Photoreal 3D Tiles only as an optional visual comparison.)
2. Device **GPS** (Unity `LocationService`) drives the camera/player over the globe; validate accuracy/stability vs. the PWA.
3. Call the **existing API** and render **fog + claims as cartographic polygons / shader masks** plus a **glowing route line** for an active session, using the C#-ported (and fixed) tile math.
4. Build to a mid-range **Android** and an **iOS** device; measure FPS, memory, thermal, and battery.

Success criteria: the alien-world look clearly exceeds the PWA, GPS is at least as good, fog/claims align with explored tiles, routes render convincingly, memory/thermals are fine on a mid-range phone, and no backend changes were required. If it passes, promote Unity + Cesium to the v-next client track in [ROADMAP.md](ROADMAP.md). If mobile performance is prohibitive, fall back to RN + Mapbox.

---

## Out of scope for this doc

No engine migration or client code changes are made here. A detailed migration/spike implementation plan follows once a direction is confirmed.
