import type { FastifyPluginAsync } from 'fastify';
import { z } from 'zod';
import { GoodieHutChoiceSchema } from '@empire/shared';
import { isDatabaseEnabled } from '../db/client.js';
import { claimNearRoute } from '../services/route-claim.js';

const ClaimNearRouteSchema = z.object({
  empireId: z.string().uuid(),
  sessionId: z.string().uuid().optional(),
  routePath: z.array(z.object({ lat: z.number(), lng: z.number() })).min(1),
  playerLat: z.number().optional(),
  playerLng: z.number().optional(),
  /** POC scatter pin — corridor check uses these for goodie huts when near the DB site. */
  approachLat: z.number().optional(),
  approachLng: z.number().optional(),
  targetKind: z.enum(['settlement', 'resource']),
  targetId: z.string().uuid(),
  goodieChoice: GoodieHutChoiceSchema.optional(),
});

export const claimRoutes: FastifyPluginAsync = async (app) => {
  app.post('/worlds/:worldId/claim', async (request, reply) => {
    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required' });
    }

    const { worldId } = request.params as { worldId: string };
    const body = ClaimNearRouteSchema.parse(request.body);

    return claimNearRoute({
      worldId,
      empireId: body.empireId,
      sessionId: body.sessionId,
      routePath: body.routePath,
      playerLat: body.playerLat,
      playerLng: body.playerLng,
      approachLat: body.approachLat,
      approachLng: body.approachLng,
      targetKind: body.targetKind,
      targetId: body.targetId,
      goodieChoice: body.goodieChoice,
    });
  });
};
