# Routes to Glory

**Routes to Glory** (RtG) — mobile-first GPS empire game. Chart trade routes on Survey Worlds, connect Echo Sites, and compete (or ally) against a growing hostile alien NPC empire.

| | |
|---|---|
| **v1 URL** | [8082ventures.com/rtg](https://8082ventures.com/rtg) |
| **Domain (later)** | `routestoglory.com` — available; hold until post-v1 |
| **Repo folder** | `mobile-gps-app` (internal workspace name) |

When the PWA ships, set the app base path to **`/rtg/`** (e.g. Vite `base: '/rtg/'`).

## Architecture (tunable by design)

```
packages/shared     ← All game rules, types, Zod schemas, defaults
apps/api            ← Server logic, God mode, world/NPC services
apps/web            ← (coming) React PWA + Mapbox
```

**Every tunable value** lives in `packages/shared/src/config/defaults.ts` and is validated by `GameConfigSchema`. Change defaults in code, or patch at runtime via **God mode** without redeploying.

### Config layers

1. **Defaults** — version-controlled (`defaults.ts`)
2. **Runtime overrides** — God mode patches (persisted to `.runtime-config.json` in dev)
3. **Per-world overrides** — (next) stored in Postgres on `worlds.config`

## Locked design decisions

### Goodie hut (two-choice connect)

When a player connects to a Goodie Hut:

| Choice | Outcome |
|---|---|
| **A. Found town** | Instant **Town** tier + population; modifiers enter **build queue** at 50% time |
| **B. Claim reward** | One-time **gold**, **tech**, or **alien unit** |

### Real-time construction (progressive complexity)

**Only routes build instantly.** Infrastructure uses **complexity tiers** — fast early, slow and costly late:

| Tier | Label | Time mult. | Unlocks | Examples |
|---|---|---|---|---|
| 1 | Survey | 1× (2–10 min) | Day 0 | Trade depot, tariff gate |
| 2 | Outpost | 6× | Day 2 | Garrison, guard post |
| 3 | Colony | 24× | Day 5 | Academy, booster pad |
| 4 | Dominion | 96× | Day 10 | Hover lane, alien waystation |
| 5 | Ascendant | 384× | Day 18 | Fusion core, phase stabilizer |

**Gold rush** skips time (tunable). Xenite reduces rush gold cost.

### Ten alien resources + map icons

Each resource has a unique **SVG icon** for embedded map markers (`GET /api/resources/icons`).

| Resource | Domain | Discovery tier |
|---|---|---|
| Xenite, Solari Dust, Ferracite, Lumin Spring | fuel / money / build / civic | 1 (day 0) |
| Quantium Shard, Voidglass, Mycelium Core | tech / exploration / biological | 2 (day 3+) |
| Chrono Moss, Aegis Bark, Nebula Pearl | temporal / defensive / exotic | 3 (day 10+) |

### Fog of war

- **400 m tiles**; unexplored = heavy fog (92% opacity)
- Routes reveal tiles within **150 m** (+ voidglass stockpile bonus)
- **2 km starting vision** at empire spawn
- First reveal of a resource tile → **8 s shimmer** on the map icon

`POST /api/exploration/reveal` · `GET /api/exploration/:worldId/:empireId`

### Super city cap

- **Per empire per world:** `base: 6` in 1v1 (config: `growth.superCityPerEmpireCap`)
- **World cap:** 24 total super cities

### NPC alien empire

- Exactly **one** major hostile NPC per world (`The Obsidian Concord`)
- Growth pace: `slow` (0.65×) / `normal` (1×) / `fast` (1.45×)
- Hostility phases escalate by world age: dormant → observing → probing → raiding → all-out war
- **Cannot be allied** — players ally with each other instead
- Player alliances: open/closed anytime (`neutral` / `allied` / `hostile`)

## Development

### Prerequisites

- Node 20+, pnpm
- MySQL 8+ (e.g. `ventures_rtg_test`) — optional; API falls back to in-memory without `MYSQL_*` / `DATABASE_URL`
- [Mapbox access token](https://account.mapbox.com/) for the web map

### Setup

```bash
cp .env.example .env
cp apps/web/.env.example apps/web/.env
# Add VITE_MAPBOX_TOKEN to apps/web/.env

pnpm install
pnpm db:migrate     # with MYSQL_* or DATABASE_URL set
```

### Run

```bash
# Terminal 1 — API (port 3001)
export MYSQL_DATABASE=ventures_rtg_test
export MYSQL_HOST=localhost
export MYSQL_USER=your_user
export MYSQL_PASSWORD=your_password
pnpm dev

# Terminal 2 — Web PWA (port 5173, base /rtg/)
pnpm dev:web
```

Open http://localhost:5173/rtg/

## Deploy (SPanel)

Full guide: **[docs/DEPLOY.md](docs/DEPLOY.md)**

| Piece | Where on server |
|---|---|
| PWA (`apps/web/dist/`) | `public_html/rtg/` |
| Node API | **Outside** `public_html` — e.g. `/home/USER/rtg-api/` via **SPanel → NodeJS Manager** |
| MySQL | `apps/api/migrations/001_initial.sql` or `pnpm db:migrate` |

SPanel **NodeJS Manager** deploys the API (ports **3000–3500**). Proxy `/rtg/api` to your Node port with `.htaccess`.

## God mode (testing)

Requires header `X-God-Mode-Secret: dev-god-mode` (configurable).

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/god/config` | Full config + dot-path listing |
| PATCH | `/api/god/config` | `{ "path": "growth.superCityPerEmpireCap.base", "value": 8 }` |
| POST | `/api/god/config/reset` | Restore defaults |
| POST | `/api/god/worlds/:id/npc/tick` | Advance NPC empire |
| POST | `/api/god/goodie-hut/resolve` | Test GH choices |
| POST | `/api/god/diplomacy` | Set player alliance status |

### Example: tweak super city cap live

```bash
curl -X PATCH http://localhost:3001/api/god/config \
  -H 'Content-Type: application/json' \
  -H 'X-God-Mode-Secret: dev-god-mode' \
  -d '{"path":"growth.superCityPerEmpireCap.base","value":8}'
```

### Example: create a 1v1 world

```bash
curl -X POST http://localhost:3001/api/worlds \
  -H 'Content-Type: application/json' \
  -d '{"name":"Test World","difficulty":"normal"}'
```

## Next steps

See **[docs/ROADMAP.md](docs/ROADMAP.md)** for the full v1 checklist, locked decisions, and v2 parking lot.

Immediate build order:

- MySQL schema + world seeding from real city catalog
- Route session GPS pipeline
- God mode web UI (admin panel)
- React PWA map client
