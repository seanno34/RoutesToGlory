# Hostile Ordnance — POC Brief

**Project:** `routestoglory/apps/unity-poc`  
**Date:** July 2026  
**Status:** **Deferred** — bumped below realistic terrain/map tiles (cockpit drag-look also deferred)

**Related:** [ROADMAP.md](../../../docs/ROADMAP.md) Phase 2 · [POC_TO_PRODUCTION.md](../../../docs/POC_TO_PRODUCTION.md)

---

## Goal

Prove **client-side ordnance VFX + targeting** when hostiles are in range, with a **lightweight server combat resolver** for authoritative outcomes. Client is presentation only.

POC passes when: approaching a hostile contact triggers targeting VFX, a fire/engage action calls the server, and the result is visible on device — without building full v1 async connect-contest logic.

---

## Scope (POC)

| In scope | Out of scope (v1 / production) |
|----------|-------------------------------|
| Client targeting in forward corridor (reuse beam math) | Full PvE raid event cinematics |
| Graybox ordnance VFX (tracer/beam/impact) | Async connect-contest combat loop |
| Hostile contacts from map data (`alignment: "hostile"`) | Full military unit simulation |
| Minimal `POST /combat/engage` resolver | PvP combat |
| NPC `hostilityPhase` gate (e.g. `probing+`) | Massive route-network combat AI |

---

## What Exists Today

### Backend / shared

- **Obsidian Concord** NPC per world — `apps/api/src/services/npc-empire.ts`
- **Combat tuning config** — `packages/shared/src/config/defaults.ts` (`combat` block)
- **Military unit definitions** — `packages/shared/src/units/alien-units.ts` (reward layer, not in-flight combat)
- **No combat resolver service** — no ordnance HTTP routes yet
- **Map API gap** — `GET /worlds/:id/map` has no NPC phase or hostile unit positions

### Unity POC (patterns to clone)

| System | File | Reuse for ordnance |
|--------|------|-------------------|
| Forward corridor targeting | `RtgForwardCorridor.cs` | In-wedge hostile acquisition |
| Pathfinder beam VFX | `RtgPathfinderBeam.cs` | Tracer/beam materials, proximity scan |
| Hostile markers | `RtgEchoSiteLoader.cs` | `alignment == "hostile"` → red contacts |
| Player loop hook | `RtgPlayerLocation.cs` | `TickPathfinderBeam` sibling pattern |
| VFX pipeline | `RtgGliderAfterburner.cs` | Additive cone materials |

**No ordnance scripts yet** — greenfield `RtgHostileOrdnance` (or similar).

---

## Suggested Implementation Order

### 1. Define POC hostile contact model

- Source: hostile settlements from map JSON (`alignment: "hostile"`, e.g. Hollow Spire in sample data)
- Range: reuse `pathfinderDetectionRangeM` (~115 m) or new `ordnanceRangeM`
- Gate: world `hostilityPhase >= probing` (expose on map or status endpoint)

### 2. Server — minimal resolver

- Add `apps/api/src/services/combat-resolver.ts` using `config.combat` + `npcAggressionModifier(phase)`
- Expose `hostilityPhase` on map or `GET /worlds/:id/status`
- Add `POST /worlds/:worldId/combat/engage` → `{ outcome, playerPower, defenderPower, eventId }`
- Insert `world_events` row type `combat`

### 3. Unity — `RtgHostileOrdnance`

- Scan hostile markers from `RtgEchoSiteLoader` / `RtgMapMarkerRegistry`
- `RtgForwardCorridor.TryWorldCorridorFrame` for in-wedge targeting
- On acquire: additive tracer/beam VFX (beam colors from `RtgPathfinderBeam`)
- On server ack: impact flash + optional haptic (`RtgDeviceHaptics`)

### 4. Wire into player

- Component on `RTG Player` in `SampleScene.unity`
- Tick from `RtgPlayerLocation.LateUpdate` (alongside pathfinder beam) or sibling `Update`

### 5. Field-test gate

- God-mode NPC tick to `probing` or `raiding`
- Drive/fly toward hostile marker
- Targeting VFX acquires in corridor
- Engage → server response → visible hit/miss on device

---

## Key Files (starting points)

**Docs**
- `docs/ROADMAP.md` — Phase 2 exit criteria
- `docs/POC_TO_PRODUCTION.md` — carry-forward notes
- `docs/EXHAUST_VFX_LESSONS.md` — VFX material patterns

**Shared / API**
- `packages/shared/src/config/defaults.ts`
- `apps/api/src/services/npc-empire.ts`
- `apps/api/src/db/world-repo.ts` — extend map payload

**Unity**
- `Assets/Scripts/Game/RtgPathfinderBeam.cs`
- `Assets/Scripts/Game/RtgForwardCorridor.cs`
- `Assets/Scripts/Game/RtgEchoSiteLoader.cs`
- `Assets/Scripts/Game/RtgPlayerLocation.cs`

---

## POC Exit Criteria

- [ ] Hostile contact acquired in forward corridor when in range + phase gate met
- [ ] Ordnance VFX plays on device (readable at chase + cockpit camera)
- [ ] Server engage endpoint returns authoritative outcome
- [ ] Client shows hit/miss feedback without desyncing world state
- [ ] No regression to route capture, fog, or pathfinder beam

---

## Changelog

| Date | Note |
|------|------|
| 2026-07-14 | Brief created; cockpit drag-look deferred; ordnance is active POC |
