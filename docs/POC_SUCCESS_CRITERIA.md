# Routes to Glory — POC Success Criteria

Definition of done for the Unity POC. The POC is achieved when all criteria below are true.

**Client focus:** Unity POC (`apps/unity-poc/`).  
**Security note:** User PIN is 4-digit numeric (`0000`–`9999`) and game session IDs reuse `worlds.access_code` (6–8 alphanumeric) — intentionally low security for POC field testing. No JWT or auth middleware.

---

## 1. Login and game session selection

- Player enters a **user PIN** (4 digits) that identifies their tester account.
- Player then selects or enters a **game session ID** (`worlds.access_code`) from a dropdown of **that PIN’s** sessions only.
- Both PIN and session ID are required before Join loads the world.
- After Join (or **New Game**), the chosen world loads via the live API (`GET /worlds/saved?pin=…`, `GET /worlds/by-code/:code?pin=…`).
- Dropdown labels: `{accessCode} — {name} ({date})`.
- Selecting a saved session **fills** the game session ID field only; **Join** is required.
- **1a.** Until Join / New Game confirms a session: no markers, no sample fallback, no ship/tap interaction.
- PlayerPrefs may **prefill** remembered PIN (`rtg.userPin`) and session (`rtg.accessCode`); Play does **not** silent-auto-resume.
- **Exit** (in-game, near Gear) returns to the join overlay: clears world/session prefs and markers, **keeps PIN**, clears game selection.
- Overlay **Clear session** clears remembered session prefs but keeps PIN.
- Editor-only **Sample (Editor)** remains a secondary offline escape hatch — not the primary create path.

**How testers get/set a PIN:** pick any 4-digit number (e.g. `1234`) and use it consistently. First **New Game** or join that creates/looks up the user stores `users.pin`. Share PINs only within your tester group; sessions are isolated per PIN.

**API base URL:** public mobile uses **`https://8082ventures.com/rtg_api/api`**; Unity Editor uses `http://localhost:3001/api`. Override in the join panel for local/LAN/tunnel.

---

## 2. Game objective — sequential missions

Missions must be completed **in order** (A → B → C). Later missions unlock only after prior ones succeed.

| Mission | Objective |
|---------|-----------|
| **A** | Find and connect **5 Xenite** resources. |
| **B** | Build a **base camp** on a route connected to **all** Xenite resources. |
| **C** | Fill Base Camp Xenite reserves at **1% of target per hour per connected xenite** extraction site. |

### Rules (implemented)

- **Connect** = existing claim / tap-to-connect of `resource_id == xenite` (unique nodes owned by the player empire in this world).
- **Mission UI** = IMGUI HUD after login/session: title, short objective, progress (e.g. `Xenite 2/5`). Hidden while the join overlay is open.
- **Base Camp (B)** = `POST /worlds/:worldId/missions/base-camp` founds a “Base Camp” settlement on/near the empire route network (same corridor radius as claims). Requires Mission A complete and all connected xenite on the route network.
- **Mission C** = starts when B completes (`mission_c_started_at`). Authoritative fill: `fillPercent = min(100, connectedXeniteCount × 1.0 × hoursElapsed)` where `hoursElapsed = (now − cStartedAt) / 3600`. Connected xenite count is recomputed live each poll (more sites → faster fill). Completes when `fillPercent >= 100`. Mine-yield stockpile accrual may still run for flavor but does **not** gate completion. HUD example: `Reserves 37% · 8 xenite · ~7.9h left`.
- **Dev accelerate (field testing):** Settings → **Missions (dev)** → `C → ~60s` (force-complete override in ~60 seconds) or `Skip C` (complete fill immediately). Also `POST /worlds/:worldId/missions/accelerate` with `{ "empireId", "mode": "near"|"finish" }`.
- Progress is persisted server-side in `empire_missions` (per world + empire). Client also caches the last snapshot in PlayerPrefs.

### API

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/worlds/:worldId/missions?empireId=` | Current mission progress + HUD strings |
| POST | `/worlds/:worldId/missions/base-camp` | Found Base Camp at `{ empireId, lat, lng, routePath? }` |
| POST | `/worlds/:worldId/missions/accelerate` | Dev: `{ empireId, mode: "near"\|"finish" }` |

### Editor test plan (A → B → C)

1. Run API (`pnpm dev` or `pnpm dev:field`) and open Unity POC Play.
2. Enter PIN → **New Game** (or Join a session) so live map + empire load.
3. Lay a Light Road near xenite deposits; tap-to-connect **5** xenite. HUD should show `Xenite n/5`, then unlock Mission B.
4. Stand on/near your route; press **Found Base Camp** on the mission HUD. Confirm Mission C starts (HUD shows `Reserves n% · N xenite · ~Xh left`).
5. Open **Settings → Missions (dev)** → **C → ~60s** (or **Skip C**). Wait/poll until Victory banner appears.
6. Optional curl (replace IDs):

```bash
curl -s "http://127.0.0.1:3001/api/worlds/$WORLD_ID/missions?empireId=$EMPIRE_ID" | jq .
curl -s -X POST "http://127.0.0.1:3001/api/worlds/$WORLD_ID/missions/base-camp" \
  -H 'content-type: application/json' \
  -d "{\"empireId\":\"$EMPIRE_ID\",\"lat\":42.76,\"lng\":-105.38}"
curl -s -X POST "http://127.0.0.1:3001/api/worlds/$WORLD_ID/missions/accelerate" \
  -H 'content-type: application/json' \
  -d "{\"empireId\":\"$EMPIRE_ID\",\"mode\":\"near\"}"
```

---

## 3. Victory

- When missions A, B, and C are all complete, the player achieves **victory**.
- **POC stub:** Unity shows a simple **Victory!** banner (full Victory Stats screen is criterion 3 polish — not required for mission gating).

---

## 4. Switch / exit game session mid-play — **Done**

- Player can **Exit** from an active session (button near Gear) to return to the login/session UI and pick another session or New Game.
- (Optional later) Session switcher popup near Gear may still be added; Exit is the required path for POC field testing.

---

## 5. Start a new game

- **New Game** is available on **mobile and Editor** (not Editor-only).
- Requires user PIN first → `POST /worlds` with `{ pin }` → creates a seeded world, associates it with that PIN’s user, returns bootstrap, and loads into play.
- Replaces the old primary “Sample game / Use sample world (dev)” production action.

---

## Acceptance checklist

Use this when verifying the POC end-to-end:

- [x] User PIN + game session ID both required before Join loads
- [x] Saved-sessions dropdown lists only that PIN’s worlds (`GET /worlds/saved?pin=`)
- [x] Selecting a session fills the ID but does not join until Join is pressed
- [x] Join with empty PIN or empty session ID shows validation error and does not load
- [x] Wrong PIN for someone else’s session is rejected (403)
- [x] World / ship play stay gated until Join or New Game
- [x] Play restart prefills PIN/session from PlayerPrefs; no silent auto-join
- [x] **New Game** visible and works on device builds (not Editor-only)
- [x] **New Game** creates a new world tied to the entered PIN and loads it
- [x] **Exit** returns to overlay, clears markers, keeps PIN, clears game selection
- [ ] Mission A: connect 5 Xenite resources
- [ ] Mission B: build base camp on a route tied to all Xenite resources
- [ ] Mission C: reserves fill at 1%/hr per connected xenite (live count); HUD shows % · sites · ETA
- [ ] Missions enforce sequential order
- [ ] Dev accelerate Mission C (~60s / Skip C) works for field testing
- [ ] Victory banner (stub) on full completion — full Victory Stats = criterion 3

---

*Update this doc if POC scope or mission rules change materially. Related: `docs/AGENT_HANDOFF.md`.*
