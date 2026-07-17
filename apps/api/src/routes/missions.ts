import type { FastifyPluginAsync } from 'fastify';
import { z } from 'zod';
import { isDatabaseEnabled } from '../db/client.js';
import {
  accelerateMissionC,
  foundBaseCamp,
  getMissionProgress,
} from '../services/missions.js';

const EmpireIdSchema = z.object({
  empireId: z.string().uuid(),
});

const FoundBaseCampSchema = z.object({
  empireId: z.string().uuid(),
  lat: z.number(),
  lng: z.number(),
  routePath: z
    .array(z.object({ lat: z.number(), lng: z.number() }))
    .optional(),
});

const AccelerateSchema = z.object({
  empireId: z.string().uuid(),
  /** finish = complete C now; near = force-complete override in ~60s */
  mode: z.enum(['finish', 'near']).optional().default('near'),
});

export const missionRoutes: FastifyPluginAsync = async (app) => {
  app.get('/worlds/:worldId/missions', async (request, reply) => {
    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required' });
    }

    const { worldId } = request.params as { worldId: string };
    const queryParams = EmpireIdSchema.parse(request.query);

    return getMissionProgress(worldId, queryParams.empireId);
  });

  app.post('/worlds/:worldId/missions/base-camp', async (request, reply) => {
    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required' });
    }

    const { worldId } = request.params as { worldId: string };
    const body = FoundBaseCampSchema.parse(request.body);

    return foundBaseCamp({
      worldId,
      empireId: body.empireId,
      lat: body.lat,
      lng: body.lng,
      routePath: body.routePath,
    });
  });

  app.post('/worlds/:worldId/missions/accelerate', async (request, reply) => {
    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required' });
    }

    const { worldId } = request.params as { worldId: string };
    const body = AccelerateSchema.parse(request.body);

    return accelerateMissionC(worldId, body.empireId, body.mode);
  });
};
