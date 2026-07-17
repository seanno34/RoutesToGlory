/**
 * POC sequential missions A → B → C for Routes to Glory.
 *
 * A — connect 5 unique xenite resource nodes (claim / tap-to-connect)
 * B — found a Base Camp on the empire route network that reaches all connected xenite
 * C — fill reserves at 1% of target per hour per connected (owned) xenite site
 */
import { query, newId } from '../db/client.js';
import { parseMysqlJson } from '../db/mysql-json.js';
import { configStore } from './config-store.js';
import { applyMineYields } from './mine-yields.js';
import {
  decimatePath,
  haversineM,
  isWithinAnyRouteCorridor,
  nearestPointOnAnyPath,
  type PathPoint,
} from './route-geometry.js';

export const MISSION_A_XENITE_REQUIRED = 5;
/** Authoritative Mission C fill: 1% of target per hour per connected xenite site. */
export const MISSION_C_FILL_PERCENT_PER_HOUR_PER_SITE = 1.0;
export const MISSION_C_RESERVE_TARGET_PERCENT = 100;
/** Dev / field-test accelerate: finish Mission C in ~60s. */
export const MISSION_C_ACCELERATE_REMAINING_MS = 60_000;

export type MissionId = 'A' | 'B' | 'C' | 'victory';
export type MissionPhaseStatus = 'locked' | 'active' | 'done';

export interface MissionProgress {
  worldId: string;
  empireId: string;
  currentMission: MissionId;
  victory: boolean;
  title: string;
  objective: string;
  progressLabel: string;
  missionA: {
    status: MissionPhaseStatus;
    xeniteConnected: number;
    xeniteRequired: number;
  };
  missionB: {
    status: MissionPhaseStatus;
    baseCampSettlementId: string | null;
  };
  missionC: {
    status: MissionPhaseStatus;
    startedAt: string | null;
    completesAt: string | null;
    remainingSeconds: number; // -1 when N/A
    reserveCurrent: number;
    reserveTarget: number;
    reserveFilled: number;
    /** 0–100; authoritative completion driver while C is active. */
    fillPercent: number;
    /** Live owned xenite count used for the fill rate. */
    connectedXeniteCount: number;
    /** Percent of target filled per hour at the current connected count. */
    fillRatePercentPerHour: number;
  };
}

interface MissionRow {
  empire_id: string;
  world_id: string;
  mission_a_completed_at: Date | string | null;
  mission_b_completed_at: Date | string | null;
  mission_c_started_at: Date | string | null;
  mission_c_completes_at: Date | string | null;
  mission_c_completed_at: Date | string | null;
  base_camp_settlement_id: string | null;
  reserve_target: number;
  reserve_baseline: number;
}

async function ensureMissionRow(worldId: string, empireId: string): Promise<void> {
  await query(
    `INSERT IGNORE INTO empire_missions (empire_id, world_id) VALUES (?, ?)`,
    [empireId, worldId],
  );
}

async function loadMissionRow(
  worldId: string,
  empireId: string,
): Promise<MissionRow | null> {
  await ensureMissionRow(worldId, empireId);
  const result = await query<MissionRow>(
    `SELECT * FROM empire_missions WHERE empire_id = ? AND world_id = ?`,
    [empireId, worldId],
  );
  return result.rows[0] ?? null;
}

async function countConnectedXenite(
  worldId: string,
  empireId: string,
): Promise<{ count: number; yieldPerDaySum: number }> {
  const result = await query<{ cnt: number; yield_sum: number | null }>(
    `SELECT COUNT(*) AS cnt, COALESCE(SUM(yield_per_day), 0) AS yield_sum
     FROM map_resource_nodes
     WHERE world_id = ? AND owner_empire_id = ? AND resource_id = 'xenite'`,
    [worldId, empireId],
  );
  const row = result.rows[0];
  return {
    count: Number(row?.cnt ?? 0),
    yieldPerDaySum: Number(row?.yield_sum ?? 0),
  };
}

async function getStockpileXenite(empireId: string): Promise<number> {
  const result = await query<{ resources: unknown }>(
    `SELECT resources FROM empire_stockpiles WHERE empire_id = ?`,
    [empireId],
  );
  if (!result.rows[0]) return 0;
  const resources = parseMysqlJson<Record<string, number>>(
    result.rows[0].resources,
    {},
  );
  return Number(resources.xenite ?? 0);
}

async function setStockpileXenite(empireId: string, xenite: number): Promise<void> {
  const result = await query<{ resources: unknown }>(
    `SELECT resources FROM empire_stockpiles WHERE empire_id = ?`,
    [empireId],
  );
  if (!result.rows[0]) return;
  const resources = {
    ...parseMysqlJson<Record<string, number>>(result.rows[0].resources, {}),
  };
  resources.xenite = Math.max(0, Math.floor(xenite));
  await query(`UPDATE empire_stockpiles SET resources = ? WHERE empire_id = ?`, [
    JSON.stringify(resources),
    empireId,
  ]);
}

function toIso(value: Date | string | null | undefined): string | null {
  if (value == null) return null;
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) return null;
  return d.toISOString();
}

/** Mission C fill from live connected xenite count × hours since C started. */
export function computeMissionCFill(
  startedAt: Date | string | null | undefined,
  connectedXeniteCount: number,
  nowMs: number = Date.now(),
): {
  fillPercent: number;
  hoursElapsed: number;
  fillRatePercentPerHour: number;
  remainingSeconds: number;
} {
  const count = Math.max(0, Math.floor(connectedXeniteCount));
  const fillRatePercentPerHour = count * MISSION_C_FILL_PERCENT_PER_HOUR_PER_SITE;

  if (startedAt == null || count <= 0) {
    return {
      fillPercent: 0,
      hoursElapsed: 0,
      fillRatePercentPerHour,
      remainingSeconds: -1,
    };
  }

  const started = startedAt instanceof Date ? startedAt.getTime() : new Date(startedAt).getTime();
  if (Number.isNaN(started)) {
    return {
      fillPercent: 0,
      hoursElapsed: 0,
      fillRatePercentPerHour,
      remainingSeconds: -1,
    };
  }

  const hoursElapsed = Math.max(0, (nowMs - started) / 3_600_000);
  const fillPercent = Math.min(
    MISSION_C_RESERVE_TARGET_PERCENT,
    fillRatePercentPerHour * hoursElapsed,
  );

  let remainingSeconds = 0;
  if (fillPercent < MISSION_C_RESERVE_TARGET_PERCENT) {
    const remainingPercent = MISSION_C_RESERVE_TARGET_PERCENT - fillPercent;
    remainingSeconds = Math.max(
      0,
      Math.ceil((remainingPercent / fillRatePercentPerHour) * 3600),
    );
  }

  return { fillPercent, hoursElapsed, fillRatePercentPerHour, remainingSeconds };
}

function formatHoursLeft(totalSeconds: number): string {
  if (totalSeconds < 0) return '—';
  if (totalSeconds < 60) return `~${totalSeconds}s left`;
  if (totalSeconds < 3600) {
    const m = Math.max(1, Math.round(totalSeconds / 60));
    return `~${m}m left`;
  }
  const hours = totalSeconds / 3600;
  const rounded = hours >= 10 ? Math.round(hours) : Math.round(hours * 10) / 10;
  return `~${rounded}h left`;
}

function buildProgress(
  worldId: string,
  empireId: string,
  row: MissionRow,
  xeniteConnected: number,
): MissionProgress {
  const aDone =
    row.mission_a_completed_at != null || xeniteConnected >= MISSION_A_XENITE_REQUIRED;
  const bDone = row.mission_b_completed_at != null;
  const cDone = row.mission_c_completed_at != null;

  const reserveTarget =
    Number(row.reserve_target ?? 0) > 0
      ? Math.max(0, Number(row.reserve_target))
      : MISSION_C_RESERVE_TARGET_PERCENT;

  const fill = computeMissionCFill(row.mission_c_started_at, xeniteConnected);
  let remainingSeconds = fill.remainingSeconds;
  let fillPercent = fill.fillPercent;

  // Dev accelerate may set an early completes_at override.
  if (!cDone && bDone && row.mission_c_completes_at) {
    const completes = new Date(row.mission_c_completes_at).getTime();
    if (!Number.isNaN(completes)) {
      const accelRemaining = Math.max(0, Math.ceil((completes - Date.now()) / 1000));
      if (remainingSeconds < 0 || accelRemaining < remainingSeconds) {
        remainingSeconds = accelRemaining;
      }
      if (Date.now() >= completes) {
        fillPercent = MISSION_C_RESERVE_TARGET_PERCENT;
        remainingSeconds = 0;
      }
    }
  }

  const reserveCurrent = Math.min(
    reserveTarget,
    Math.round(fillPercent * (reserveTarget / MISSION_C_RESERVE_TARGET_PERCENT)),
  );

  let currentMission: MissionId = 'A';
  if (cDone) currentMission = 'victory';
  else if (bDone) currentMission = 'C';
  else if (aDone) currentMission = 'B';

  const missionAStatus: MissionPhaseStatus = aDone
    ? 'done'
    : currentMission === 'A'
      ? 'active'
      : 'locked';
  const missionBStatus: MissionPhaseStatus = bDone
    ? 'done'
    : aDone
      ? 'active'
      : 'locked';
  const missionCStatus: MissionPhaseStatus = cDone
    ? 'done'
    : bDone
      ? 'active'
      : 'locked';

  let title = 'Mission A — Connect Xenite';
  let objective = `Find and connect ${MISSION_A_XENITE_REQUIRED} Xenite resources.`;
  let progressLabel = `Xenite ${Math.min(xeniteConnected, MISSION_A_XENITE_REQUIRED)}/${MISSION_A_XENITE_REQUIRED}`;

  if (currentMission === 'B') {
    title = 'Mission B — Base Camp';
    objective =
      'Found a Base Camp on a route connected to all of your Xenite resources.';
    progressLabel = 'Place Base Camp on your route';
  } else if (currentMission === 'C') {
    title = 'Mission C — Fill Reserves';
    objective =
      'Connected Xenite extraction sites fill Base Camp reserves (1%/hour each).';
    const pct = Math.min(100, Math.floor(fillPercent));
    const timeLabel =
      remainingSeconds >= 0 ? ` · ${formatHoursLeft(remainingSeconds)}` : '';
    progressLabel = `Reserves ${pct}% · ${xeniteConnected} xenite${timeLabel}`;
  } else if (currentMission === 'victory') {
    title = 'Victory!';
    objective = 'Missions A, B, and C complete. Xenite reserves are full.';
    progressLabel = 'Victory';
  }

  return {
    worldId,
    empireId,
    currentMission,
    victory: cDone,
    title,
    objective,
    progressLabel,
    missionA: {
      status: missionAStatus,
      xeniteConnected,
      xeniteRequired: MISSION_A_XENITE_REQUIRED,
    },
    missionB: {
      status: missionBStatus,
      baseCampSettlementId: row.base_camp_settlement_id,
    },
    missionC: {
      status: missionCStatus,
      startedAt: toIso(row.mission_c_started_at),
      completesAt: toIso(row.mission_c_completes_at),
      // Always a number for Unity JsonUtility (null JSON numbers are unreliable).
      remainingSeconds: remainingSeconds ?? -1,
      reserveCurrent: cDone ? reserveTarget : reserveCurrent,
      reserveTarget,
      reserveFilled: cDone ? reserveTarget : reserveCurrent,
      fillPercent: cDone ? MISSION_C_RESERVE_TARGET_PERCENT : fillPercent,
      connectedXeniteCount: xeniteConnected,
      fillRatePercentPerHour: fill.fillRatePercentPerHour,
    },
  };
}

async function syncMissionAIfReady(
  worldId: string,
  empireId: string,
  xeniteConnected: number,
  row: MissionRow,
): Promise<MissionRow> {
  if (row.mission_a_completed_at != null) return row;
  if (xeniteConnected < MISSION_A_XENITE_REQUIRED) return row;

  await query(
    `UPDATE empire_missions
     SET mission_a_completed_at = COALESCE(mission_a_completed_at, NOW())
     WHERE empire_id = ? AND world_id = ?`,
    [empireId, worldId],
  );
  return (await loadMissionRow(worldId, empireId)) ?? row;
}

async function syncMissionCIfReady(
  worldId: string,
  empireId: string,
  row: MissionRow,
  xeniteConnected: number,
  stockpileXenite: number,
): Promise<{ row: MissionRow; stockpileXenite: number }> {
  if (row.mission_c_completed_at != null || row.mission_b_completed_at == null) {
    return { row, stockpileXenite };
  }

  const fill = computeMissionCFill(row.mission_c_started_at, xeniteConnected);
  const completesAt = row.mission_c_completes_at
    ? new Date(row.mission_c_completes_at).getTime()
    : null;
  const acceleratedDone = completesAt != null && Date.now() >= completesAt;
  const reservesFull = fill.fillPercent >= MISSION_C_RESERVE_TARGET_PERCENT;

  if (!acceleratedDone && !reservesFull) {
    return { row, stockpileXenite };
  }

  const reserveTarget =
    Number(row.reserve_target ?? 0) > 0
      ? Math.max(0, Number(row.reserve_target))
      : MISSION_C_RESERVE_TARGET_PERCENT;
  const reserveBaseline = Math.max(0, Number(row.reserve_baseline ?? 0));

  // Flavor only: bump stockpile to baseline + target when C completes.
  let nextStockpile = stockpileXenite;
  if (reserveTarget > 0 && stockpileXenite < reserveBaseline + reserveTarget) {
    nextStockpile = reserveBaseline + reserveTarget;
    await setStockpileXenite(empireId, nextStockpile);
  }

  await query(
    `UPDATE empire_missions
     SET mission_c_completed_at = COALESCE(mission_c_completed_at, NOW())
     WHERE empire_id = ? AND world_id = ?`,
    [empireId, worldId],
  );

  await query(
    `INSERT INTO world_events (id, world_id, type, payload)
     VALUES (?, ?, 'mission_c_complete', ?)`,
    [
      newId(),
      worldId,
      JSON.stringify({
        empireId,
        reserveTarget,
        fillPercent: MISSION_C_RESERVE_TARGET_PERCENT,
        connectedXeniteCount: xeniteConnected,
        stockpileXenite: nextStockpile,
        accelerated: acceleratedDone && !reservesFull,
      }),
    ],
  );

  return {
    row: (await loadMissionRow(worldId, empireId)) ?? row,
    stockpileXenite: nextStockpile,
  };
}

/** Refresh mission progress (accrues mine yields, auto-advances A/C). */
export async function getMissionProgress(
  worldId: string,
  empireId: string,
): Promise<MissionProgress> {
  const empire = await query<{ id: string }>(
    `SELECT id FROM empires WHERE id = ? AND world_id = ?`,
    [empireId, worldId],
  );
  if (!empire.rows[0]) {
    throw Object.assign(new Error('Empire not found in this world'), { statusCode: 404 });
  }

  // Flavor stockpile accrual — not used as Mission C completion driver.
  await applyMineYields(worldId, empireId);

  const xenite = await countConnectedXenite(worldId, empireId);
  let row = await loadMissionRow(worldId, empireId);
  if (!row) {
    throw Object.assign(new Error('Mission row missing'), { statusCode: 500 });
  }

  row = await syncMissionAIfReady(worldId, empireId, xenite.count, row);
  const stockpileXenite = await getStockpileXenite(empireId);
  const synced = await syncMissionCIfReady(
    worldId,
    empireId,
    row,
    xenite.count,
    stockpileXenite,
  );
  row = synced.row;

  return buildProgress(worldId, empireId, row, xenite.count);
}

async function loadEmpireRoutePaths(
  worldId: string,
  empireId: string,
): Promise<PathPoint[][]> {
  const result = await query<{ path_json: unknown }>(
    `SELECT path_json FROM routes
     WHERE world_id = ? AND empire_id = ? AND status = 'active'`,
    [worldId, empireId],
  );

  const paths: PathPoint[][] = [];
  for (const route of result.rows) {
    const path = parseMysqlJson<PathPoint[]>(route.path_json, []);
    if (Array.isArray(path) && path.length > 0) {
      paths.push(decimatePath(path, 512));
    }
  }
  return paths;
}

async function createBaseCampSettlement(
  worldId: string,
  empireId: string,
  lat: number,
  lng: number,
): Promise<string> {
  const id = newId();
  const slug = `base-camp-${empireId.slice(0, 8)}`;
  const name = 'Base Camp';

  // Replace prior base camp for this empire if reset left a stale row.
  await query(
    `DELETE FROM settlements
     WHERE world_id = ? AND owner_empire_id = ? AND slug LIKE 'base-camp-%'`,
    [worldId, empireId],
  );

  await query(
    `INSERT INTO settlements (
       id, world_id, slug, name, planet_display_name, terrestrial_label,
       tier, alignment, is_goodie_hut, owner_empire_id, lat, lng,
       geofence_radius_m, base_defense
     ) VALUES (?, ?, ?, ?, ?, ?, 'town', 'friendly', 0, ?, ?, ?, 200, 25)`,
    [id, worldId, slug, name, name, name, empireId, lat, lng],
  );

  return id;
}

async function createConnectorRoute(
  worldId: string,
  empireId: string,
  fromSettlementId: string,
  toSettlementId: string,
  path: PathPoint[],
): Promise<string> {
  let distanceM = 0;
  for (let i = 1; i < path.length; i += 1) {
    distanceM += haversineM(
      path[i - 1]!.lat,
      path[i - 1]!.lng,
      path[i]!.lat,
      path[i]!.lng,
    );
  }

  const routeId = newId();
  await query(
    `INSERT INTO routes (
       id, world_id, empire_id, session_id, from_settlement_id, to_settlement_id,
       path_json, distance_m, status
     ) VALUES (?, ?, ?, NULL, ?, ?, ?, ?, 'active')`,
    [
      routeId,
      worldId,
      empireId,
      fromSettlementId,
      toSettlementId,
      JSON.stringify(path),
      distanceM,
    ],
  );
  return routeId;
}

export interface FoundBaseCampInput {
  worldId: string;
  empireId: string;
  lat: number;
  lng: number;
  /** Optional active-leg hint for corridor check. */
  routePath?: PathPoint[];
}

export async function foundBaseCamp(
  input: FoundBaseCampInput,
): Promise<{ ok: true; settlementId: string; progress: MissionProgress }> {
  const { worldId, empireId, lat, lng } = input;

  const progressBefore = await getMissionProgress(worldId, empireId);
  if (progressBefore.missionA.status !== 'done') {
    throw Object.assign(
      new Error(
        `Complete Mission A first (connect ${MISSION_A_XENITE_REQUIRED} Xenite).`,
      ),
      { statusCode: 409 },
    );
  }
  if (progressBefore.missionB.status === 'done') {
    throw Object.assign(new Error('Base Camp already founded'), { statusCode: 409 });
  }

  const xenite = await countConnectedXenite(worldId, empireId);
  if (xenite.count < MISSION_A_XENITE_REQUIRED) {
    throw Object.assign(
      new Error(`Need ${MISSION_A_XENITE_REQUIRED} connected Xenite`),
      { statusCode: 409 },
    );
  }

  // All connected xenite must already be tied to a route (claim sets route_id).
  const unlinked = await query<{ id: string }>(
    `SELECT id FROM map_resource_nodes
     WHERE world_id = ? AND owner_empire_id = ? AND resource_id = 'xenite'
       AND (route_id IS NULL OR route_id = '')
     LIMIT 1`,
    [worldId, empireId],
  );
  if (unlinked.rows.length > 0) {
    throw Object.assign(
      new Error('All connected Xenite must be on your route network'),
      { statusCode: 400 },
    );
  }

  const radiusM = configStore.get().routes.minConnectDistanceM ?? 800;
  const paths = await loadEmpireRoutePaths(worldId, empireId);
  if (input.routePath && input.routePath.length > 0) {
    paths.unshift(decimatePath(input.routePath, 256));
  }
  if (paths.length === 0) {
    throw Object.assign(new Error('Lay a route first — Base Camp needs an active route'), {
      statusCode: 400,
    });
  }

  if (!isWithinAnyRouteCorridor(lat, lng, paths, radiusM)) {
    throw Object.assign(
      new Error(`Base Camp must be within ${radiusM}m of a route connected to your Xenite`),
      { statusCode: 400 },
    );
  }

  const owned = await query<{ id: string; lat: number; lng: number; name: string }>(
    `SELECT id, lat, lng, name FROM settlements
     WHERE world_id = ? AND owner_empire_id = ?`,
    [worldId, empireId],
  );

  const settlementId = await createBaseCampSettlement(worldId, empireId, lat, lng);
  const anchor = nearestPointOnAnyPath(lat, lng, paths);
  const fromSite =
    owned.rows.sort(
      (a, b) =>
        haversineM(lat, lng, Number(a.lat), Number(a.lng)) -
        haversineM(lat, lng, Number(b.lat), Number(b.lng)),
    )[0] ?? null;

  if (fromSite) {
    await createConnectorRoute(worldId, empireId, fromSite.id, settlementId, [
      anchor,
      { lat, lng },
    ]);
  } else {
    await createConnectorRoute(worldId, empireId, settlementId, settlementId, [
      anchor,
      { lat, lng },
    ]);
  }

  const stockpileXenite = await getStockpileXenite(empireId);
  const reserveTarget = MISSION_C_RESERVE_TARGET_PERCENT;

  await query(
    `UPDATE empire_missions SET
       mission_a_completed_at = COALESCE(mission_a_completed_at, NOW()),
       mission_b_completed_at = NOW(),
       mission_c_started_at = NOW(),
       mission_c_completes_at = NULL,
       mission_c_completed_at = NULL,
       base_camp_settlement_id = ?,
       reserve_target = ?,
       reserve_baseline = ?
     WHERE empire_id = ? AND world_id = ?`,
    [settlementId, reserveTarget, stockpileXenite, empireId, worldId],
  );

  await query(
    `INSERT INTO world_events (id, world_id, type, payload)
     VALUES (?, ?, 'mission_b_base_camp', ?)`,
    [
      newId(),
      worldId,
      JSON.stringify({
        empireId,
        settlementId,
        reserveTarget,
        xeniteConnected: xenite.count,
        fillRatePercentPerHour:
          xenite.count * MISSION_C_FILL_PERCENT_PER_HOUR_PER_SITE,
      }),
    ],
  );

  const progress = await getMissionProgress(worldId, empireId);
  return { ok: true, settlementId, progress };
}

export type AccelerateMode = 'finish' | 'near';

/** Dev helper: complete Mission C now, or set remaining wall-clock to ~60s. */
export async function accelerateMissionC(
  worldId: string,
  empireId: string,
  mode: AccelerateMode = 'near',
): Promise<MissionProgress> {
  const progress = await getMissionProgress(worldId, empireId);
  if (progress.missionB.status !== 'done') {
    throw Object.assign(new Error('Found Base Camp (Mission B) before accelerating C'), {
      statusCode: 409,
    });
  }
  if (progress.missionC.status === 'done') {
    return progress;
  }

  const row = await loadMissionRow(worldId, empireId);
  if (!row) {
    throw Object.assign(new Error('Mission row missing'), { statusCode: 500 });
  }

  if (mode === 'finish') {
    const target =
      Number(row.reserve_target ?? 0) > 0
        ? Math.max(0, Number(row.reserve_target))
        : MISSION_C_RESERVE_TARGET_PERCENT;
    const baseline = Math.max(0, Number(row.reserve_baseline ?? 0));
    if (target > 0) {
      await setStockpileXenite(empireId, baseline + target);
    }
    await query(
      `UPDATE empire_missions SET
         mission_c_completes_at = NOW(),
         mission_c_completed_at = NOW()
       WHERE empire_id = ? AND world_id = ?`,
      [empireId, worldId],
    );
    await query(
      `INSERT INTO world_events (id, world_id, type, payload)
       VALUES (?, ?, 'mission_c_accelerated', ?)`,
      [newId(), worldId, JSON.stringify({ empireId, mode: 'finish' })],
    );
  } else {
    // Force-complete override in ~60s (does not change the live fill formula).
    const completesAt = new Date(Date.now() + MISSION_C_ACCELERATE_REMAINING_MS);
    await query(
      `UPDATE empire_missions SET mission_c_completes_at = ?
       WHERE empire_id = ? AND world_id = ?`,
      [completesAt, empireId, worldId],
    );
    await query(
      `INSERT INTO world_events (id, world_id, type, payload)
       VALUES (?, ?, 'mission_c_accelerated', ?)`,
      [newId(), worldId, JSON.stringify({ empireId, mode: 'near', completesAt })],
    );
  }

  return getMissionProgress(worldId, empireId);
}

/** Wipe mission row(s) when resetting world progress. */
export async function resetEmpireMissions(
  worldId: string,
  empireId?: string,
): Promise<void> {
  if (empireId) {
    await query(`DELETE FROM empire_missions WHERE world_id = ? AND empire_id = ?`, [
      worldId,
      empireId,
    ]);
    return;
  }
  await query(`DELETE FROM empire_missions WHERE world_id = ?`, [worldId]);
}
