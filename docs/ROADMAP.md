# Routes to Glory — Product Roadmap

Source of truth for **what v1 includes**, **what’s left to build**, and **ideas parked for later**.  
Use this file when starting a **new Cursor agent/thread** so you don’t need to re-read the full design chat.

**Also read:** [README.md](../README.md) · [POC_TO_PRODUCTION.md](POC_TO_PRODUCTION.md) · Config: `packages/shared/src/config/defaults.ts` · Schema: `packages/shared/src/config/schema.ts`

---

## How to use this doc

| Situation | Where to work |
|---|---|
| Building or finishing **v1 MVP** | Same thread as active implementation, or new thread + “Read `docs/ROADMAP.md`” |
| **v2+ ideas** only (no v1 code yet) | New thread; add ideas under **v2 parking lot** below |
| **Tuning game balance** | God mode (`/api/god/config`) or edit `defaults.ts` |
| **Changing a locked v1 decision** | Update this file + `defaults.ts` + README in the same PR |

---

## Game premise

You are the **captain** of an advanced starship on a one-way mission to a distant alien world. One-way travel technology gets the crew there; there is no quick ride home until you finish the job.

**Mission:** establish a **base camp**, claim **Echo Sites** across the planet, harvest local **alien resources**, and weave a continent-scale **Light Road** network between your sites. A connected Echo Site network generates more resources and energy as it grows — eventually powering a **super generator** strong enough to energize the **Star Gate**, enabling instant travel to and from Earth. **Mission complete** when the Star Gate is built and powered; then the crew moves on to the next target world.

**Threats & allies:** life was detected. Some **friendly alien life** offers help, technology, and resources. A massive **hostile alien presence** (the Obsidian Concord) is bent on eradicating human explorers. The player must expand and connect Echo Sites fast enough to gather materials and energy for the Star Gate without being overrun.

Real-world **GPS movement** drives exploration and **Light Road** construction in fiction; the map is a Survey World overlay on real geography (see locked decisions).

### Fiction ↔ mechanics

| Fiction | In-game (v1+) |
|---|---|
| Captain / crew | Player (empire) |
| Alien planet | **Survey World** (sci-fi layer on real-world map) |
| Base camp | First **Echo Site** / starting settlement |
| **Light Roads** | **Routes** — built instantly while the player moves |
| **Echo Sites** | Settlements seeded from real-world anchors; connect via routes |
| Alien resources | 10 resource types; fog reveal + stockpiles |
| Friendly aliens | Goodie hut rewards (gold, tech, units); future allied factions (v2) |
| Hostile aliens | **Obsidian Concord** — one NPC empire per world |
| Network yield | More routes + connected Echo Sites → more resources/energy (v1) |
| Super generator | **v2 parking lot** — capstone network build that powers the Star Gate |
| **Star Gate** | **v2 parking lot** — build + power = mission win → next world |

---

## v1 MVP — scope checklist

### Design & config (done)

- [x] GPS routes build instantly from movement (session model under the hood)
- [x] **Always-on route capture** — movement is recorded continuously and auto-connects at settlements; no manual Begin/End (server begin/end driven automatically)
- [x] Goodie hut: found town **or** claim reward (gold / tech / alien unit)
- [x] Progressive build complexity tiers (1–5, fast early → slow late)
- [x] Real-time construction + gold rush (+ xenite discount)
- [x] 10 alien resources with map icons
- [x] Fog of war + resource shimmer on first reveal
- [x] Super city cap (6 per empire in 1v1, 24 per world)
- [x] One hostile NPC alien empire per world (escalating phases)
- [x] Player alliances (neutral / allied / hostile, open anytime)
- [x] God mode for live config tweaking
- [x] Monorepo: `@empire/shared` + `@empire/api`

### Backend services (in memory — done for dev)

- [x] Config store + Zod validation
- [x] Construction queue, rush, estimates
- [x] Resource deposits, stockpiles, costs
- [x] Exploration reveal + fog state
- [x] Goodie hut resolution
- [x] NPC empire ticks
- [x] Diplomacy updates

### v1 still to build

- [x] **MySQL** — schema, migrations (`MYSQL_*` or `DATABASE_URL`; in-memory fallback without DB)
- [x] **World seed generator** — 30 US metros → Echo Sites (`packages/shared/src/seeding/city-catalog.ts`)
- [x] **Route session API** — begin / points / end / geofence auto-connect + GPS validation
- [ ] **Auth** — accounts, empire per user per world (dev user auto-created on world bootstrap)
- [x] **React PWA (`apps/web`)** — Mapbox map, Begin/End route, GPS sampler (fog shimmer UI next)
- [ ] **Combat resolver** — hostile connect contests (async)
- [ ] **Settlement growth** — category modifiers, promotion, suburbs/merge
- [ ] **World event feed** — route established, combat, first connect (DB table exists)
- [ ] **God mode admin UI** — slider panel over config paths
- [ ] **Deploy** — API + static PWA at `8082ventures.com/rtg` ([docs/DEPLOY.md](DEPLOY.md) — SPanel NodeJS Manager)

### v1 explicitly out of scope

- In-app purchases / real-money gold (gold rush is in-game only for v1)
- Background GPS when app is closed (foreground sessions only)
- Native iOS/Android apps (PWA first)
- Multiple NPC factions (one Obsidian Concord per world)
- Player-founded settlements (fixed + seeded nodes only for v1)
- Star Gate endgame and mission-complete flow (fiction exists; mechanics v2)
- Random cinematic Events (video clips + game-state effects — v2)

---

## Locked v1 decisions (quick reference)

| Topic | Decision |
|---|---|
| **Product name** | Routes to Glory (RtG) |
| **v1 public path** | `https://8082ventures.com/rtg` |
| **Dedicated domain** | `routestoglory.com` — deferred until after v1 |
| Map alignment | Real-world GPS; sci-fi “Survey World” fiction layer |
| Narrative frame | Starship captain; one-way colonization; Star Gate = long-term mission win (v2) |
| Routes / Light Roads | Only thing that builds instantly while player moves |
| Route capture | **Always-on** — any movement while the app is open is captured; routes auto-connect at settlements and the next leg auto-starts. No manual Begin/End. (Adopted from v2 "always-on movement sync".) |
| Tap-to-connect | Tap a nearby Echo Site or resource when it is within **minConnectDistanceM** (~1 km) of your active route **path** (not your GPS pin). Server anchors the connector at the **nearest route point**. |
| Echo Sites | Colony nodes; real-world seeded anchors; connect into resource network |
| Infrastructure | Real-time timers; complexity tiers 1–5 |
| Goodie hut A | Instant town + population; modifiers queued at 50% time |
| Goodie hut B | One-time gold, tech, or alien unit (new or upgrade) |
| Super cities | Max 6 per empire (1v1), 24 per world |
| NPC | One per world; cannot ally; difficulty affects growth |
| PvP diplomacy | Alliances flip anytime |
| Resources | 10 types; 3 discovery tiers by world age |
| Fog | 400 m tiles; shimmer 8 s on new resource reveal |

---

## POC → Production strategy

The Unity build is a **throwaway proof of concept**, not the production codebase. Standard game-dev progression: **Prototype (throwaway) → Vertical slice (production-quality core loop) → Production → Live-ops.** We plan to rewrite prototype *code*, but keep the *repo and infrastructure* continuous.

### Locked decisions

| Topic | Decision |
|---|---|
| Repo strategy | **Single repo** (`routestoglory` monorepo). No new repo for production. |
| Prototype location | Unity POC lives under `apps/` (e.g. `apps/unity-poc`); treated as disposable |
| Production start | New clean Unity project as a **fresh folder in the same repo** (e.g. `apps/game`), reusing backend, `@empire/shared`, tile pipeline, art, CI, and docs |
| Backend | `@empire/api` + `@empire/shared` are **authoritative and client-agnostic** — carried forward unchanged; never forked or duplicated |
| Retiring the POC | Archive/delete the POC folder when production starts; git history preserves it |

**Why not a new repo:** the backend is already production-grade and shared, and the tile pipeline, Cesium know-how, shaders, art, CI, and decision docs all carry over. A new repo would fragment git history and force awkward backend duplication for no benefit. A separate repo is only justified if the POC repo gets polluted with large experimental LFS binaries, the tech direction changes fundamentally, or team/ownership boundaries require it.

**What de-risks the transition:** all game rules live server-side and the client is replaceable, so the correctness-critical layer (world state, routes, claims, economy) stays continuous regardless of client rewrites.

### POC exit criteria (go / no-go)

Prototype is "done" when we can make a confident go/no-go decision against **both** phases below.

#### Phase 1 — Core technical validation (done)

- [x] GPS accuracy/stability acceptable on a real mid-range device — smoothing + live in-game tuners; validated in field driving
- [x] Alien-overlay map + shader-based fog + Echo Site claims render correctly and read well — fog shader + claims proven; high-speed polish deferred to production
- [x] Core loop (real-world movement → Light Road → Echo Site growth) feels good — movement → Light Road → tap-to-connect validated on device
- [x] Performance acceptable on a mid-range phone (FPS, memory, thermal)
- [x] Cesium 3D-tile cost tolerable at expected usage
- [x] Backend integration proven with **zero server changes**

#### Phase 2 — World feel, routes & combat (in progress)

- [x] **Route cleanup & snap-to-corridor** — Snap glider onto nearby owned routes while moving; Douglas-Peucker simplify on save; one-time server cleanup for existing routes (`POST /worlds/:id/routes/cleanup`)
- [x] **Alien terrain dressing** — Procedural scatter (`RtgTerrainScatter`) dresses revealed fog tiles; **Pathfinder beam** (`RtgPathfinderBeam`) activates on proximity and vaporizes props; terrain clearance glides over hills
- [x] **Ground-anchored resource markers** — `RtgGroundMarkerVisual` glow pads + per-resource deposits on terrain; legacy floating orbs removed
- [x] **Glider combat vehicle presentation (Phase A)** — Low-poly 3D blockout (`RtgGliderBlockoutMesh`), blob shadow, particle afterburner; proves Unity 3D pipeline on device
- [x] **Glider hero asset (Phase B)** — Tripo `futuristic_fighter_3d_model` integrated with socket exhaust VFX; accepted as production-ready for POC/v1 (visual polish optional later). See [GLIDER_HERO_ASSET_BRIEF.md](GLIDER_HERO_ASSET_BRIEF.md) if revisiting art.
- [ ] **Cockpit look-around & rear view** — Tap-and-drag yaw/pitch in cockpit; rear backup camera inset (`RtgCockpitRearCamera` scaffolded, needs wiring)
- [ ] **Hostile ordnance** — Client VFX + targeting when hostiles in range; server combat resolver stays lightweight

**Status (2026-07-14):** Phase 1 complete. Phase 2 **almost done** — cockpit drag-look and ordnance are the remaining active POC items before go/no-go.

On **go** (both phases): finalize [POC_TO_PRODUCTION.md](POC_TO_PRODUCTION.md) and start `apps/game` on production-quality foundations.

### Art & assets staging

The Unity POC uses **graybox placeholders** (spheres, cubes, capsules, lines) on purpose. Real art is authored in the production project (`apps/game`), not the throwaway POC, because any placeholder art in `apps/unity-poc` is discarded at the production cutover. Standard progression: **placeholders → art-style test → vertical-slice hero assets → full production library.**

| Phase | Art work | Where |
|---|---|---|
| **Prototype (now)** | Graybox placeholders + **Phase 2 art tests**: terrain scatter props, ground-anchored resource tiles with alien glow, glider afterburner VFX, ordnance VFX. Prove each reads on-device before `apps/game`. | `apps/unity-poc` |
| **Vertical slice** (post go/no-go) | First **production-quality** assets for a small representative set: 1 player ship, 1–2 Echo Site tiers, 2–3 resources, 1 goodie hut, Light Road as real VFX/Shader Graph. Lock the **asset pipeline** (import settings, LODs, atlases, Addressables, naming). Prove perf on-device. | `apps/game` |
| **Production** | Full library: all settlement tiers, all 10 resources, alignment variants, animations, VFX, environment/skybox polish, UI. | `apps/game` |

**Gates before commissioning any art** (avoid re-cutting assets later):
- **Scale + camera framing locked** — assets are authored for their on-screen size and the overhead/tilted follow angle.
- **Device performance budget** — poly/texture/draw-call limits from mid-range-phone testing, so artists build to budget.

**Carries across the POC→production boundary (not throwaway):** the Light Road **shader/VFX**, **materials**, the **tile pipeline**, and an **art-style guide** (palette, silhouette language, material language). Because the Light Road is the signature mechanic and drives the "feels/looks good" judgment, prototyping its *shader* earlier than other art is worthwhile (Shader Graphs copy over cleanly).

**Build vs. buy (solo dev):** for the vertical slice, favor **asset-store/marketplace kits + light customization** and a defined style guide over bespoke modeling; commission or build bespoke hero assets only where the marketplace can't deliver the look. Revisit bespoke production art once the core loop is validated.

---

## v2 parking lot

_Ideas only — not designed or scheduled. Add new bullets here; promote to v1 only if scope changes._

### Gameplay

- ~~**Always-on movement sync** — no Begin/End Route button; any player movement while the app is open translates to in-game movement~~ **(adopted into current design — see "Route capture" locked decision)**
- **Route reinforcement / extension** — recognize when the player re-walks or extends an existing route and reinforce/merge it (route matching + dedup) instead of creating a duplicate; server-side feature, not yet built
- **Static plot objects** — place mines, extractors, and similar buildings on surrounding city plots to gather resources without moving (complements route-based play)
- **Super generator** — late-game capstone build; connected Echo Site network must generate enough energy to power the Star Gate
- **Star Gate** — endgame objective: build, power, mission complete → unlock next Survey World
- Player-founded trade posts and settlements
- Live caravan visibility on map during active sessions
- Season / league worlds with resets
- Co-op empires (multiple users per empire)
- Additional NPC factions and alien enclaves
- PvE raid events from Obsidian Concord
- Legendary world landmarks with server-wide first-claim history
- Route tolls and trade agreements between empires
- Weather / terrain modifiers affecting route yield

### Events

Random **positive or negative** world events that interrupt normal play with a short **video clip** showing what happened and how it affects the player’s empire.

**Presentation:** event triggers → brief cinematic/video → applied game-state change (map + stats reflect outcome).

**Effect categories (TBD per event type):**

| Type | Examples |
|---|---|
| **Obstacles** | Block or slow progress — impassable terrain, route delays, construction halts, movement penalties |
| **Destruction** | Remove or damage existing assets — Light Roads, buildings, extractor sites, stockpiles |
| **Bonuses** | Extra resources, technology unlocks, temporary buffs, discovery of new resource deposits or types |

- Event pool, frequency, and targeting rules (global vs. local vs. empire-specific) — TBD
- May overlap with Obsidian Concord PvE raids (one event source among many)

### Economy & monetization

- Optional premium currency for rush (keep fair “earn-only” path)
- Cosmetic empire colors, map skins, unit skins
- Battle pass / season rewards (if seasons added)

### Tech & platform

- Capacitor wrapper for improved mobile GPS + optional background hints
- Push notifications (build complete, under attack, ally request)
- Offline GPS point queue with sync (client-side IndexedDB)
- Mapbox map-matching / road snapping for route validation
- Event video playback — short clips on trigger; cache for offline/PWA (format, CDN — TBD)
- Spectator mode / shared world replays

### Social

- In-game chat per world
- Alliance shared vision (partial fog reveal)
- Empire profiles and public stats

---

## Key paths for agents

```
packages/shared/src/config/defaults.ts   # All tunable numbers
packages/shared/src/config/schema.ts     # Zod types + validation
packages/shared/src/resources/           # Resource definitions
packages/shared/src/map/                 # Fog helpers + map icons
apps/api/src/services/                   # Game logic services
apps/api/src/routes/game.ts              # HTTP routes
```

**Config version:** see `meta.configVersion` in defaults (currently **3**).

---

## Changelog (roadmap only)

| Date | Change |
|---|---|
| 2026-07-04 | v1 implementation started: MySQL schema, world seed, route sessions, web PWA |
| 2026-07-04 | Branding: Routes to Glory; v1 at `8082ventures.com/rtg`; domain deferred |
| 2026-07-04 | Initial roadmap: v1 checklist, locked decisions, v2 parking lot |
| 2026-07-06 | v2: always-on movement sync, static plot objects (mines/extractors) |
| 2026-07-06 | Game premise: captain, Light Roads, Echo Sites, Star Gate win condition |
| 2026-07-06 | Star Gate + super generator moved to v2 parking lot (out of v1 scope) |
| 2026-07-06 | v2: Events system — random positive/negative, video clips, obstacles/destruction/bonuses |
| 2026-07-07 | Added POC → Production strategy: Unity POC is throwaway; keep single repo; production as `apps/game`; backend continuous; go/no-go exit criteria |
| 2026-07-09 | Added Art & assets staging (placeholders → vertical-slice → production; art gates; build-vs-buy) |
| 2026-07-09 | Route capture is now **always-on** (no manual Begin/End); adopted from v2. Route reinforcement/extension parked in v2. |
| 2026-07-12 | POC Phase 1 exit criteria met (GPS, fog/claims, core loop, perf, Cesium cost, backend integration) |
| 2026-07-12 | POC Phase 2 criteria added: route cleanup/snap, terrain dressing, resource tiles, glider VFX, cockpit look-around/rear view, hostile ordnance |
| 2026-07-12 | POC Phase 2 route cleanup/snap: corridor snap, RDP simplify on save, server cleanup endpoint |
| 2026-07-13 | Glider Phase A (3D blockout + blob shadow + particles); Phase B hero asset brief; cockpit/ordnance deferred |
| 2026-07-14 | Phase B hero glider accepted (Tripo mesh + socket exhaust); Phase 2 active scope complete |
| 2026-07-14 | Cockpit drag-look + ordnance undeferred — remaining Phase 2 POC work |
