import type { GameConfig, GpsPointInput } from '@empire/shared';
import { query, newId } from '../db/client.js';

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
  let distanceM = 0;
  for (let i = 1; i < points.length; i++) {
    distanceM += haversineM(
      points[i - 1]!.lat,
      points[i - 1]!.lng,
      points[i]!.lat,
      points[i]!.lng,
    );
  }

  const routeId = newId();
  await query(
    `INSERT INTO routes (
       id, world_id, empire_id, session_id, from_settlement_id, to_settlement_id,
       path_json, distance_m, status
     ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'active')`,
    [
      routeId,
      worldId,
      empireId,
      sessionId,
      fromSettlementId ?? toSettlementId,
      toSettlementId,
      JSON.stringify(points),
      distanceM,
    ],
  );

  await query(
    `UPDATE route_sessions
     SET status = 'completed', end_reason = 'connected', ended_at = NOW()
     WHERE id = ?`,
    [sessionId],
  );

  await query(
    `INSERT INTO world_events (id, world_id, type, payload)
     VALUES (?, ?, 'route_established', ?)`,
    [
      newId(),
      worldId,
      JSON.stringify({
        routeId,
        sessionId,
        empireId,
        toSettlementId,
      }),
    ],
  );

  return { routeId };
}
