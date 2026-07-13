import type { GameConfig, GpsPointInput } from '@empire/shared';
import { query, newId } from '../db/client.js';
import { revealTilesAlongRoutePath } from '../db/exploration-repo.js';
import { configStore } from '../services/config-store.js';
import { cleanupPathForPersist } from './route-geometry.js';

export interface ValidatedPoint {
  accepted: boolean;
  rejectReason?: string;
  lat: number;
  lng: number;
  accuracyM?: number;
  speedMps?: number;
  recordedAt: string;
  distanceFromLastM?: number;
  impliedSpeedMps?: number;
}

export interface SettlementRow {
  id: string;
  tier: string;
  alignment: string;
  geofence_radius_m: number;
  lat: number;
  lng: number;
  name: string;
}

function haversineM(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const R = 6_371_000;
  const toRad = (d: number) => (d * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLng = toRad(lng2 - lng1);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(a));
}

export function validateGpsPoint(
  config: GameConfig,
  point: GpsPointInput,
  lastAccepted: { lat: number; lng: number; recordedAt: string } | null,
): ValidatedPoint {
  const accuracyM = point.accuracyM ?? 50;
  const maxAccuracy = config.sampling.maxAccuracyM;

  if (accuracyM > maxAccuracy) {
    return {
      accepted: false,
      rejectReason: 'accuracy',
      lat: point.lat,
      lng: point.lng,
      accuracyM,
      speedMps: point.speedMps,
      recordedAt: point.recordedAt,
    };
  }

  if (!lastAccepted) {
    return {
      accepted: true,
      lat: point.lat,
      lng: point.lng,
      accuracyM,
      speedMps: point.speedMps,
      recordedAt: point.recordedAt,
      distanceFromLastM: 0,
    };
  }

  const distanceM = haversineM(
    lastAccepted.lat,
    lastAccepted.lng,
    point.lat,
    point.lng,
  );
  const elapsedS = Math.max(
    1,
    (new Date(point.recordedAt).getTime() -
      new Date(lastAccepted.recordedAt).getTime()) /
      1000,
  );
  const impliedSpeed = distanceM / elapsedS;

  if (impliedSpeed > config.sampling.maxSpeedMps) {
    return {
      accepted: false,
      rejectReason: 'speed',
      lat: point.lat,
      lng: point.lng,
      accuracyM,
      impliedSpeedMps: impliedSpeed,
      recordedAt: point.recordedAt,
      distanceFromLastM: distanceM,
    };
  }

  if (distanceM > 2000 && elapsedS < 60) {
    return {
      accepted: false,
      rejectReason: 'gap',
      lat: point.lat,
      lng: point.lng,
      accuracyM,
      recordedAt: point.recordedAt,
      distanceFromLastM: distanceM,
    };
  }

  if (distanceM < 15) {
    return {
      accepted: false,
      rejectReason: 'duplicate',
      lat: point.lat,
      lng: point.lng,
      accuracyM,
      recordedAt: point.recordedAt,
      distanceFromLastM: distanceM,
    };
  }

  return {
    accepted: true,
    lat: point.lat,
    lng: point.lng,
    accuracyM,
    speedMps: point.speedMps,
    recordedAt: point.recordedAt,
    distanceFromLastM: distanceM,
    impliedSpeedMps: impliedSpeed,
  };
}

export async function loadSettlements(worldId: string): Promise<SettlementRow[]> {
  const result = await query<SettlementRow>(
    `SELECT id, tier, alignment, geofence_radius_m, name, lat, lng
     FROM settlements WHERE world_id = ?`,
    [worldId],
  );
  return result.rows;
}

export function findSettlementAtPoint(
  settlements: SettlementRow[],
  lat: number,
  lng: number,
  config: GameConfig,
): SettlementRow | null {
  for (const s of settlements) {
    const dist = haversineM(lat, lng, s.lat, s.lng);
    if (dist <= s.geofence_radius_m) {
      return s;
    }
  }
  return null;
}

export function detectGeofenceConnect(
  settlements: SettlementRow[],
  recentPoints: Array<{ lat: number; lng: number; recordedAt: string }>,
  config: GameConfig,
  targetSettlementId?: string,
): SettlementRow | null {
  const windowMs = config.sampling.settlementConfirmWindowS * 1000;
  const need = config.sampling.settlementConfirmSamples;
  const now = recentPoints.at(-1)?.recordedAt ?? new Date().toISOString();
  const windowStart = new Date(now).getTime() - windowMs;

  const inWindow = recentPoints.filter(
    (p) => new Date(p.recordedAt).getTime() >= windowStart,
  );

  const candidates = settlements.filter((s) => {
    const hits = inWindow.filter(
      (p) => haversineM(p.lat, p.lng, s.lat, s.lng) <= s.geofence_radius_m,
    );
    return hits.length >= need;
  });

  if (candidates.length === 0) return null;

  if (targetSettlementId) {
    const target = candidates.find((c) => c.id === targetSettlementId);
    if (target) return target;
  }

  const tierRank: Record<string, number> = {
    super_city: 5,
    city: 4,
    town: 3,
    settlement: 2,
    goodie_hut: 1,
  };

  return candidates.sort(
    (a, b) => (tierRank[b.tier] ?? 0) - (tierRank[a.tier] ?? 0),
  )[0]!;
}

/** Only anchor a route end to a node when the path actually enters its geofence. */
function nodeAnchorAtPoint(
  settlements: SettlementRow[],
  lat: number,
  lng: number,
): string | null {
  for (const s of settlements) {
    const dist = haversineM(lat, lng, Number(s.lat), Number(s.lng));
    if (dist <= s.geofence_radius_m) return s.id;
  }
  return null;
}

function pathDistanceM(points: Array<{ lat: number; lng: number }>): number {
  let distanceM = 0;
  for (let i = 1; i < points.length; i += 1) {
    distanceM += haversineM(
      Number(points[i - 1]!.lat),
      Number(points[i - 1]!.lng),
      Number(points[i]!.lat),
      Number(points[i]!.lng),
    );
  }
  return distanceM;
}

async function insertPersistedRoute(params: {
  worldId: string;
  empireId: string;
  sessionId: string;
  points: Array<{ lat: number; lng: number }>;
  fromSettlementId: string | null;
  toSettlementId: string | null;
  sessionEndReason: 'recorded' | 'connected';
  eventPayload: Record<string, unknown>;
}): Promise<string> {
  const routeId = newId();
  const cleanedPoints = cleanupPathForPersist(params.points, 12);
  const distanceM = pathDistanceM(cleanedPoints);

  await query(
    `INSERT INTO routes (
       id, world_id, empire_id, session_id, from_settlement_id, to_settlement_id,
       path_json, distance_m, status
     ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'active')`,
    [
      routeId,
      params.worldId,
      params.empireId,
      params.sessionId,
      params.fromSettlementId,
      params.toSettlementId,
      JSON.stringify(cleanedPoints),
      distanceM,
    ],
  );

  await query(
    `UPDATE route_sessions
     SET status = 'completed', end_reason = ?, ended_at = NOW()
     WHERE id = ?`,
    [params.sessionEndReason, params.sessionId],
  );

  await query(
    `INSERT INTO world_events (id, world_id, type, payload)
     VALUES (?, ?, 'route_established', ?)`,
    [
      newId(),
      params.worldId,
      JSON.stringify({ ...params.eventPayload, routeId }),
    ],
  );

  const config = configStore.get();
  await revealTilesAlongRoutePath(
    params.worldId,
    params.empireId,
    cleanedPoints,
    config,
    { spawnResources: false },
  );

  return routeId;
}

/** Persist any driven path (≥2 points). Node anchors are optional — bonuses come later. */
export async function saveRouteSessionOnEnd(
  sessionId: string,
  clientPath?: Array<{ lat: number; lng: number }>,
): Promise<{ saved: boolean; routeId?: string; reason?: string }> {
  const sessionResult = await query<{
    id: string;
    world_id: string;
    empire_id: string;
    status: string;
    origin_settlement_id: string | null;
    target_settlement_id: string | null;
  }>(`SELECT * FROM route_sessions WHERE id = ?`, [sessionId]);

  const session = sessionResult.rows[0];
  if (!session || session.status !== 'active') {
    return { saved: false, reason: 'session_not_active' };
  }

  const existingRoute = await query<{ id: string }>(
    `SELECT id FROM routes WHERE session_id = ?`,
    [sessionId],
  );
  if (existingRoute.rows[0]) {
    await query(
      `UPDATE route_sessions
       SET status = 'completed', end_reason = 'manual', ended_at = NOW()
       WHERE id = ?`,
      [sessionId],
    );
    return { saved: true, routeId: existingRoute.rows[0].id };
  }

  const pointsResult = await query<{ lat: number; lng: number }>(
    `SELECT lat, lng FROM route_session_points
     WHERE session_id = ?
     ORDER BY seq`,
    [sessionId],
  );

  let points = pointsResult.rows.map((p) => ({
    lat: Number(p.lat),
    lng: Number(p.lng),
  }));

  if (points.length < 2 && clientPath && clientPath.length >= 2) {
    points = clientPath;
  }

  if (points.length < 2) {
    await query(
      `UPDATE route_sessions
       SET status = 'abandoned', end_reason = 'manual', ended_at = NOW()
       WHERE id = ?`,
      [sessionId],
    );
    return { saved: false, reason: 'too_few_points' };
  }

  const settlements = await loadSettlements(session.world_id);
  const start = points[0]!;
  const end = points[points.length - 1]!;
  const fromSettlementId =
    session.origin_settlement_id ??
    nodeAnchorAtPoint(settlements, Number(start.lat), Number(start.lng));
  const toSettlementId =
    session.target_settlement_id ??
    nodeAnchorAtPoint(settlements, Number(end.lat), Number(end.lng));

  const routeId = await insertPersistedRoute({
    worldId: session.world_id,
    empireId: session.empire_id,
    sessionId,
    points,
    fromSettlementId,
    toSettlementId,
    sessionEndReason: 'recorded',
    eventPayload: {
      sessionId,
      empireId: session.empire_id,
      fromSettlementId,
      toSettlementId,
      travelLeg: true,
    },
  });

  return { saved: true, routeId };
}

export async function completeRouteSession(
  sessionId: string,
  toSettlementId: string,
  fromSettlementId: string | null,
  empireId: string,
  worldId: string,
): Promise<{ routeId: string }> {
  const pointsResult = await query<{ lat: number; lng: number }>(
    `SELECT lat, lng FROM route_session_points
     WHERE session_id = ? AND accepted = 1
     ORDER BY seq`,
    [sessionId],
  );

  const points = pointsResult.rows;
  const routeId = await insertPersistedRoute({
    worldId,
    empireId,
    sessionId,
    points,
    fromSettlementId: fromSettlementId ?? toSettlementId,
    toSettlementId,
    sessionEndReason: 'connected',
    eventPayload: {
      sessionId,
      empireId,
      toSettlementId,
      nodeConnected: true,
    },
  });

  return { routeId };
}

/** Simplify existing persisted travel routes for an empire (POC route cleanup). */
export async function cleanupEmpireRoutes(
  worldId: string,
  empireId: string,
  toleranceM = 12,
): Promise<{ updated: number; total: number }> {
  const routesResult = await query<{ id: string; path_json: string }>(
    `SELECT id, path_json FROM routes
     WHERE world_id = ? AND empire_id = ? AND status = 'active'`,
    [worldId, empireId],
  );

  let updated = 0;
  for (const route of routesResult.rows) {
    let path: Array<{ lat: number; lng: number }>;
    try {
      path = JSON.parse(route.path_json) as Array<{ lat: number; lng: number }>;
    } catch {
      continue;
    }

    if (path.length < 3) continue;

    const cleaned = cleanupPathForPersist(path, toleranceM);
    if (cleaned.length >= 2 && cleaned.length < path.length) {
      await query(
        `UPDATE routes SET path_json = ?, distance_m = ? WHERE id = ?`,
        [JSON.stringify(cleaned), pathDistanceM(cleaned), route.id],
      );
      updated += 1;
    }
  }

  return { updated, total: routesResult.rows.length };
}
