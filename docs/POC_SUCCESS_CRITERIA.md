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

---

## 2. Game objective — sequential missions

Missions must be completed **in order** (A → B → C). Later missions unlock only after prior ones succeed.

| Mission | Objective |
|---------|-----------|
| **A** | Find and connect **5 Xenite** resources. |
| **B** | Build a **base camp** on a route connected to **all** Xenite resources. |
| **C** | Wait **24 hours** for Xenite resources to feed into the base camp and fill its Xenite reserves. |

---

## 3. Victory

- When missions A, B, and C are all complete, the player achieves **victory**.
- The UI shows a fun **Victory Stats** screen informing the player.

---

## 4. Switch / exit game session mid-play

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

- [ ] User PIN + game session ID both required before Join loads
- [ ] Saved-sessions dropdown lists only that PIN’s worlds (`GET /worlds/saved?pin=`)
- [ ] Selecting a session fills the ID but does not join until Join is pressed
- [ ] Join with empty PIN or empty session ID shows validation error and does not load
- [ ] Wrong PIN for someone else’s session is rejected (403)
- [ ] World / ship play stay gated until Join or New Game
- [ ] Play restart prefills PIN/session from PlayerPrefs; no silent auto-join
- [ ] **New Game** visible and works on device builds (not Editor-only)
- [ ] **New Game** creates a new world tied to the entered PIN and loads it
- [ ] **Exit** returns to overlay, clears markers, keeps PIN, clears game selection
- [ ] Mission A: connect 5 Xenite resources
- [ ] Mission B: build base camp on a route tied to all Xenite resources
- [ ] Mission C: 24h feed fills base camp Xenite reserves
- [ ] Missions enforce sequential order
- [ ] Victory Stats screen on full completion

---

*Update this doc if POC scope or mission rules change materially. Related: `docs/AGENT_HANDOFF.md`.*
