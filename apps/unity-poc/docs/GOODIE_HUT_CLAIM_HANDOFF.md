# Goodie Hut One-Time Claim — Handoff (Jul 2026)

Root cause + fix for goodie huts that could be claimed again after map reload (Unity POC + API + web).

**Also:** short bullets in `docs/AGENT_HANDOFF.md` §6c · cutover note in `docs/POC_TO_PRODUCTION.md`.

---

## Symptom

After Found Town / Claim Reward + map reload, the player could open the goodie choice modal again on what looked like the same corridor tap-test pin.

---

## Root cause

Two failures stacked:

1. **Corridor pin swap** — Tap-test scatter pins the nearest Douglas goodie hut on the simulated tour corridor. After claim + reload, that settlement was owned/converted, so `SelectCorridorGoodieTarget` picked a **different** unclaimed hut and pinned it on the **same interactive screen spot**. Client `HashSet`-by-id never blocked the new id.
2. **SampleFile vs live API** — Offline / sample-map claims sent fake settlement ids to the live claim endpoint (or failed silently). Marker state never stuck; the hut stayed “claimable” after reload.

HashSet-by-id alone is insufficient when the interactive slot can be rebound to a new settlement id.

---

## Solution

| Layer | Behavior |
|-------|----------|
| **Session claimed-ID set** | `RtgClaimedGoodieHuts` remembers ids for this Play session; survives marker respawn / map reload; cleared on login / New Game / world reset |
| **SampleFile local claim** | `RtgRouteSession.ClaimNearRoute` completes locally when `DataSource.SampleFile` (or sample fallback) — no live API with fake ids |
| **Single-use corridor pin** | `BindCorridorPin` / `RetireCorridorPin` — once the sticky hut is claimed (or vanishes), **never swap** another unclaimed hut onto that slot until Clear |
| **Modal open lock** | `BlockGoodieInteraction` as soon as the choice modal opens; `Remember` + `RetireCorridorPin` on choice submit |
| **Server atomic claim** | `UPDATE … WHERE is_goodie_hut = 1 AND owner_empire_id IS NULL`; `affectedRows === 0` → 409 already claimed; heal leftover goodie flags on already-owned sites |
| **Skip owned / converted** | Spawn + tap + web treat owned or session-claimed huts as settlements, not claimable goodies |

---

## Key files

| File | Role |
|------|------|
| `Assets/Scripts/Game/RtgClaimedGoodieHuts.cs` | Session HashSet + corridor pin sticky / retire |
| `Assets/Scripts/Game/RtgTapToConnect.cs` | Modal lock, Remember/Retire, SampleFile-aware unlock |
| `Assets/Scripts/Game/RtgEchoSiteLoader.cs` | Spawn skips owned/session-claimed; single-use corridor select |
| `Assets/Scripts/Game/RtgMapMarker.cs` | `IsUnclaimedGoodieHut`, `MarkGoodieClaimed`, interaction block |
| `Assets/Scripts/Game/RtgRouteSession.cs` | SampleFile local claim path |
| `apps/api/src/services/route-claim.ts` | Atomic goodie UPDATE + 409; TINYINT/Buffer flag coerce |
| `apps/api/src/db/world-repo.ts` | Normalize `is_goodie_hut` for map payloads |
| `apps/web/src/App.tsx`, `MapView.tsx` | Only unclaimed goodies open the choice UI |

---

## Past regressions (avoid repeating)

1. **Re-selecting “nearest unclaimed” for the corridor slot after a claim** — looks like the same pin, different id → HashSet misses. Use sticky id + `RetireCorridorPin`; return null when retired.
2. **Calling live claim from SampleFile** — fake ids never persist; marker stays claimable. Local `ClaimResult.Ok` for sample maps.
3. **Trusting MySQL `is_goodie_hut` as boolean** — TINYINT/BIT may arrive as Buffer/`0`/`1`; coerce before “still a goodie?” checks (API + web).
4. **Non-atomic `UPDATE` without `owner_empire_id IS NULL`** — double-tap / race can re-roll rewards. Require `affectedRows > 0`.

---

## Production cutover

- Keep **server-authoritative** one-time claim (atomic UPDATE + 409).
- Client session set + interaction lock are UX / offline safety nets — not a substitute for the server gate.
- Corridor scatter pin is **POC tap-test only**; production UX should not rebind a shared interactive slot to a different settlement after claim.
- Clear session claimed state on New Game / world reset (server restores goodie huts).

---

*Update when claim / corridor scatter / goodie conversion rules change materially.*
