import type { FastifyPluginAsync } from 'fastify';
import {
  AppendRoutePointsSchema,
  BeginRouteSessionSchema,
  EndRouteSessionSchema,
} from '@empire/shared';
import { configStore } from '../services/config-store.js';
import { isDatabaseEnabled, query, newId } from '../db/client.js';
import {
  completeRouteSession,
  detectGeofenceConnect,
  loadSettlements,
  validateGpsPoint,
} from '../services/route-session.js';
import { revealTilesAtPoint } from '../db/exploration-repo.js';

export const sessionRoutes: FastifyPluginAsync = async (app) => {
  app.post('/sessions', async (request, reply) => {
    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required for route sessions' });
    }

    const body = BeginRouteSessionSchema.parse(request.body);

    const sessionId = newId();
    await query(
      `INSERT INTO route_sessions (
         id, world_id, empire_id, origin_settlement_id, target_settlement_id,
         origin_lat, origin_lng, status
       ) VALUES (?, ?, ?, ?, ?, ?, ?, 'active')`,
      [
        sessionId,
        body.worldId,
        body.empireId,
        body.originSettlementId ?? null,
        body.targetSettlementId ?? null,
        body.lat,
        body.lng,
      ],
    );

    await query(
      `INSERT INTO route_session_points (
         session_id, seq, lat, lng, accuracy_m, recorded_at, accepted
       ) VALUES (?, 0, ?, ?, 10, NOW(), 1)`,
      [sessionId, body.lat, body.lng],
    );

    await query(`UPDATE route_sessions SET point_count = 1 WHERE id = ?`, [sessionId]);

    return { sessionId, status: 'active' };
  });

  app.post('/sessions/:sessionId/points', async (request, reply) => {
    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required' });
    }

    const { sessionId } = request.params as { sessionId: string };
    const body = AppendRoutePointsSchema.parse(request.body);
    const config = configStore.get();

    const sessionResult = await query<{
      id: string;
      world_id: string;
      empire_id: string;
      status: string;
      target_settlement_id: string | null;
      origin_settlement_id: string | null;
      point_count: number;
      distance_m: number;
    }>(`SELECT * FROM route_sessions WHERE id = ?`, [sessionId]);

    const session = sessionResult.rows[0];
    if (!session || session.status !== 'active') {
      return reply.status(404).send({ error: 'Active session not found' });
    }

    const lastPointResult = await query<{
      lat: number;
      lng: number;
      recorded_at: string;
    }>(
      `SELECT lat, lng, recorded_at
       FROM route_session_points
       WHERE session_id = ? AND accepted = 1
       ORDER BY seq DESC LIMIT 1`,
      [sessionId],
    );

    let lastAccepted = lastPointResult.rows[0]
      ? {
          lat: lastPointResult.rows[0].lat,
          lng: lastPointResult.rows[0].lng,
          recordedAt: lastPointResult.rows[0].recorded_at,
        }
      : null;

    let seq = session.point_count;
    let addedDistance = 0;
    const validated: ReturnType<typeof validateGpsPoint>[] = [];

    for (const point of body.points) {
      const v = validateGpsPoint(config, point, lastAccepted);
      validated.push(v);

      await query(
        `INSERT INTO route_session_points (
           session_id, seq, lat, lng, accuracy_m, speed_mps,
           accepted, reject_reason, recorded_at
         ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
        [
          sessionId,
          seq,
          point.lat,
          point.lng,
          point.accuracyM ?? null,
          point.speedMps ?? null,
          v.accepted ? 1 : 0,
          v.rejectReason ?? null,
          point.recordedAt,
        ],
      );

      if (v.accepted) {
        addedDistance += v.distanceFromLastM ?? 0;
        lastAccepted = {
          lat: point.lat,
          lng: point.lng,
          recordedAt: point.recordedAt,
        };
      }
      seq += 1;
    }

    await query(
      `UPDATE route_sessions
       SET point_count = ?, distance_m = distance_m + ?
       WHERE id = ?`,
      [seq, addedDistance, sessionId],
    );

    const exploration = {
      newlyRevealedTileIds: [] as string[],
      newResourceNodeIds: [] as string[],
    };

    for (const point of body.points) {
      const reveal = await revealTilesAtPoint(
        session.world_id,
        session.empire_id,
        point.lat,
        point.lng,
      );
      exploration.newlyRevealedTileIds.push(...reveal.newlyRevealedTileIds);
      exploration.newResourceNodeIds.push(...reveal.newResourceNodeIds);
    }

    const recentAccepted = await query<{ lat: number; lng: number; recorded_at: string }>(
      `SELECT lat, lng, recorded_at
       FROM route_session_points
       WHERE session_id = ? AND accepted = 1
       ORDER BY seq DESC LIMIT 5`,
      [sessionId],
    );

    const settlements = await loadSettlements(session.world_id);
    const connected = detectGeofenceConnect(
      settlements,
      recentAccepted.rows.reverse().map((p) => ({
        lat: p.lat,
        lng: p.lng,
        recordedAt: p.recorded_at,
      })),
      config,
      session.target_settlement_id ?? undefined,
    );

    if (connected) {
      const route = await completeRouteSession(
        sessionId,
        connected.id,
        session.origin_settlement_id,
        session.empire_id,
        session.world_id,
      );

      return {
        validated,
        connected: true,
        settlement: connected,
        routeId: route.routeId,
        exploration,
      };
    }

    return { validated, connected: false, exploration };
  });

  app.post('/sessions/:sessionId/end', async (request, reply) => {
    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required' });
    }

    const { sessionId } = request.params as { sessionId: string };
    EndRouteSessionSchema.parse(request.body ?? {});

    await query(
      `UPDATE route_sessions
       SET status = 'abandoned', end_reason = 'manual', ended_at = NOW()
       WHERE id = ? AND status = 'active'`,
      [sessionId],
    );

    return { ok: true, sessionId, status: 'abandoned' };
  });

  app.get('/sessions/:sessionId', async (request, reply) => {
    const { sessionId } = request.params as { sessionId: string };

    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required' });
    }

    const session = await query(`SELECT * FROM route_sessions WHERE id = ?`, [
      sessionId,
    ]);

    if (!session.rows[0]) {
      return reply.status(404).send({ error: 'Session not found' });
    }

    const points = await query(
      `SELECT seq, lat, lng, accepted, reject_reason, recorded_at
       FROM route_session_points WHERE session_id = ? ORDER BY seq`,
      [sessionId],
    );

    return { session: session.rows[0], points: points.rows };
  });
};
