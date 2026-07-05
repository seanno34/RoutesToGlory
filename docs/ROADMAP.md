# Routes to Glory — Product Roadmap

Source of truth for **what v1 includes**, **what’s left to build**, and **ideas parked for later**.  
Use this file when starting a **new Cursor agent/thread** so you don’t need to re-read the full design chat.

**Also read:** [README.md](../README.md) · Config: `packages/shared/src/config/defaults.ts` · Schema: `packages/shared/src/config/schema.ts`

---

## How to use this doc

| Situation | Where to work |
|---|---|
| Building or finishing **v1 MVP** | Same thread as active implementation, or new thread + “Read `docs/ROADMAP.md`” |
| **v2+ ideas** only (no v1 code yet) | New thread; add ideas under **v2 parking lot** below |
| **Tuning game balance** | God mode (`/api/god/config`) or edit `defaults.ts` |
| **Changing a locked v1 decision** | Update this file + `defaults.ts` + README in the same PR |

---

## v1 MVP — scope checklist

### Design & config (done)

- [x] Session-based GPS routes (one-shot sampling design; routes build instantly)
- [x] Begin route / end route / auto-connect at settlements
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

---

## Locked v1 decisions (quick reference)

| Topic | Decision |
|---|---|
| **Product name** | Routes to Glory (RtG) |
| **v1 public path** | `https://8082ventures.com/rtg` |
| **Dedicated domain** | `routestoglory.com` — deferred until after v1 |
| Map alignment | Real-world GPS; sci-fi “Survey World” fiction layer |
| Routes | Only thing that builds instantly while player moves |
| Infrastructure | Real-time timers; complexity tiers 1–5 |
| Goodie hut A | Instant town + population; modifiers queued at 50% time |
| Goodie hut B | One-time gold, tech, or alien unit (new or upgrade) |
| Super cities | Max 6 per empire (1v1), 24 per world |
| NPC | One per world; cannot ally; difficulty affects growth |
| PvP diplomacy | Alliances flip anytime |
| Resources | 10 types; 3 discovery tiers by world age |
| Fog | 400 m tiles; shimmer 8 s on new resource reveal |

---

## v2 parking lot

_Ideas only — not designed or scheduled. Add new bullets here; promote to v1 only if scope changes._

### Gameplay

- Player-founded trade posts and settlements
- Live caravan visibility on map during active sessions
- Season / league worlds with resets
- Co-op empires (multiple users per empire)
- Additional NPC factions and alien enclaves
- PvE raid events from Obsidian Concord
- Legendary world landmarks with server-wide first-claim history
- Route tolls and trade agreements between empires
- Weather / terrain modifiers affecting route yield

### Economy & monetization

- Optional premium currency for rush (keep fair “earn-only” path)
- Cosmetic empire colors, map skins, unit skins
- Battle pass / season rewards (if seasons added)

### Tech & platform

- Capacitor wrapper for improved mobile GPS + optional background hints
- Push notifications (build complete, under attack, ally request)
- Offline GPS point queue with sync (client-side IndexedDB)
- Mapbox map-matching / road snapping for route validation
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
