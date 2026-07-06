import type { FastifyPluginAsync } from 'fastify';
import {
  flattenConfigPaths,
  GodModePatchSchema,
  GoodieHutConnectRequestSchema,
  AllianceUpdateSchema,
  QueueBuildRequestSchema,
  RushBuildRequestSchema,
  ALIEN_RESOURCES,
  RevealExplorationRequestSchema,
  RESOURCE_MAP_ICONS,
} from '@empire/shared';
import { configStore } from '../services/config-store.js';
import {
  applyRush,
  estimateBuild,
  queueBuildJob,
  quoteRushCost,
  tickBuildJobs,
} from '../services/construction.js';
import { resolveGoodieHutConnect } from '../services/goodie-hut.js';
import {
  buildMapState,
  createExplorationState,
  revealExploration,
} from '../services/exploration.js';
import {
  canPromoteToSuperCity,
  tickNpcAlienEmpire,
  updatePlayerAlliance,
} from '../services/npc-empire.js';
import {
  canAffordCost,
  deductCost,
  seedSettlementDeposits,
} from '../services/resources.js';
import { createWorldInDb, getWorldMap } from '../db/world-repo.js';
import { getExplorationState } from '../db/exploration-repo.js';
import { isDatabaseEnabled } from '../db/client.js';
import { worldStore } from '../services/world-store.js';

function assertGodMode(request: { headers: Record<string, unknown> }): void {
  const config = configStore.get();

  if (!config.godMode.enabled) {
    throw Object.assign(new Error('God mode is disabled'), { statusCode: 403 });
  }

  const secret = request.headers['x-god-mode-secret'];
  if (secret !== config.godMode.secretHeader) {
    throw Object.assign(new Error('Invalid god mode secret'), { statusCode: 401 });
  }
}

export const godModeRoutes: FastifyPluginAsync = async (app) => {
  app.addHook('preHandler', async (request) => {
    assertGodMode(request);
  });

  app.get('/config', async () => ({
    config: configStore.get(),
    overrides: configStore.getOverrides(),
    paths: flattenConfigPaths(configStore.get()),
  }));

  app.patch('/config', async (request) => {
    const body = GodModePatchSchema.parse(request.body);
    const config = await configStore.patch(body.path, body.value);

    return { ok: true, path: body.path, value: body.value, config };
  });

  app.post('/config/reset', async () => {
    const config = await configStore.reset();
    return { ok: true, config };
  });

  app.post('/worlds/:worldId/npc/tick', async (request) => {
    const { worldId } = request.params as { worldId: string };
    const world = worldStore.get(worldId);

    if (!world) {
      throw Object.assign(new Error('World not found'), { statusCode: 404 });
    }

    const result = tickNpcAlienEmpire(
      configStore.get(),
      world.npcEmpire,
      new Date(world.startedAt),
    );

    worldStore.updateNpc(worldId, result.state);

    return result;
  });

  app.post('/goodie-hut/resolve', async (request) => {
    const body = GoodieHutConnectRequestSchema.parse(request.body);
    const config = configStore.get();
    const resolution = resolveGoodieHutConnect(config, body, {
      settlementId: body.settlementId,
      empireId: body.empireId,
      worldId: body.worldId,
      ownedUnitIds: worldStore.getEmpireUnits(body.empireId),
    });

    if (resolution.choice === 'found_town') {
      worldStore.addBuildJobs(body.worldId, resolution.queuedBuildJobs);
    }

    return resolution;
  });

  app.post('/build/queue', async (request) => {
    const body = QueueBuildRequestSchema.parse(request.body);
    const config = configStore.get();
    const worldAgeDays = worldStore.getWorldAgeDays(body.worldId);
    const stockpile = worldStore.getStockpile(body.worldId, body.empireId);
    const estimate = estimateBuild(
      config,
      body.targetType,
      body.targetKey,
      worldAgeDays,
    );

    if (!canAffordCost(stockpile, estimate.resourceCost)) {
      throw Object.assign(new Error('Insufficient resources'), { statusCode: 400 });
    }

    const job = queueBuildJob(config, body, { worldAgeDays });
    worldStore.setStockpile(
      body.worldId,
      body.empireId,
      deductCost(stockpile, estimate.resourceCost),
    );
    worldStore.addBuildJobs(body.worldId, [job]);

    return { job, estimate };
  });

  app.get('/build/estimate', async (request) => {
    const query = request.query as {
      targetType?: string;
      targetKey?: string;
      worldId?: string;
    };
    const config = configStore.get();
    const worldAgeDays = query.worldId
      ? worldStore.getWorldAgeDays(query.worldId)
      : 0;

    if (!query.targetType || !query.targetKey) {
      throw Object.assign(new Error('targetType and targetKey required'), {
        statusCode: 400,
      });
    }

    return estimateBuild(
      config,
      query.targetType as never,
      query.targetKey,
      worldAgeDays,
    );
  });

  app.post('/build/rush', async (request) => {
    const body = RushBuildRequestSchema.parse(request.body);
    const config = configStore.get();
    const found = worldStore.findBuildJob(body.jobId);

    if (!found || found.job.empireId !== body.empireId) {
      throw Object.assign(new Error('Build job not found'), { statusCode: 404 });
    }

    const quote = quoteRushCost(config, found.job, {
      rushSeconds: body.rushSeconds,
      xeniteToSpend: body.xeniteToSpend,
    });

    const updated = applyRush(config, found.job, body);
    worldStore.updateBuildJob(found.worldId, updated);

    if (body.xeniteToSpend && body.xeniteToSpend > 0) {
      const stockpile = worldStore.getStockpile(found.worldId, body.empireId);
      stockpile.xenite = Math.max(0, stockpile.xenite - body.xeniteToSpend);
      worldStore.setStockpile(found.worldId, body.empireId, stockpile);
    }

    return { job: updated, quote };
  });

  app.post('/build/tick', async (request) => {
    const { worldId } = (request.body ?? {}) as { worldId?: string };
    if (!worldId) {
      throw Object.assign(new Error('worldId required'), { statusCode: 400 });
    }

    const jobs = tickBuildJobs(worldStore.getBuildJobs(worldId));
    for (const job of jobs) {
      worldStore.updateBuildJob(worldId, job);
    }

    return { jobs };
  });

  app.post('/settlements/:settlementId/deposits/seed', async (request) => {
    const { settlementId } = request.params as { settlementId: string };
    const { worldId } = (request.body ?? {}) as { worldId?: string };

    if (!worldId) {
      throw Object.assign(new Error('worldId required'), { statusCode: 400 });
    }

    const deposits = seedSettlementDeposits(
      configStore.get(),
      worldId,
      settlementId,
    );
    worldStore.addSettlementDeposits(worldId, deposits);

    return deposits;
  });

  app.post('/exploration/reveal', async (request) => {
    const body = RevealExplorationRequestSchema.parse(request.body);
    const config = configStore.get();
    const world = worldStore.get(body.worldId);

    if (!world) {
      throw Object.assign(new Error('World not found'), { statusCode: 404 });
    }

    let state = worldStore.getExplorationState(body.worldId, body.empireId);
    if (!state) {
      state = createExplorationState();
      world.explorationByEmpire[body.empireId] = state;
    }

    const stockpile = worldStore.getStockpile(body.worldId, body.empireId);
    const worldAgeDays = worldStore.getWorldAgeDays(body.worldId);
    const nodes = world.mapResourceNodes;

    const result = revealExploration(
      config,
      state,
      nodes,
      body,
      worldAgeDays,
      { voidglassStockpile: stockpile.voidglass },
    );

    return result;
  });

  app.get('/exploration/:worldId/:empireId', async (request) => {
    const { worldId, empireId } = request.params as {
      worldId: string;
      empireId: string;
    };
    const config = configStore.get();
    const state = worldStore.getExplorationState(worldId, empireId);

    if (!state) {
      throw Object.assign(new Error('Exploration state not found'), { statusCode: 404 });
    }

    return buildMapState(
      config,
      worldId,
      empireId,
      state,
      worldStore.getMapResourceNodes(worldId),
    );
  });

  app.post('/exploration/spawn', async (request) => {
    const body = request.body as {
      worldId: string;
      empireId: string;
      lat: number;
      lng: number;
    };
    worldStore.setEmpireSpawn(
      body.worldId,
      body.empireId,
      body.lat,
      body.lng,
      configStore.get(),
    );

    return { ok: true };
  });

  app.post('/diplomacy', async (request) => {
    const body = AllianceUpdateSchema.parse(request.body);
    const world = worldStore.get(body.worldId);

    if (!world) {
      throw Object.assign(new Error('World not found'), { statusCode: 404 });
    }

    const diplomacy = updatePlayerAlliance(world.diplomacy, body);
    worldStore.updateDiplomacy(body.worldId, diplomacy);

    return diplomacy;
  });

  app.get('/worlds/:worldId/super-city-cap/:empireId', async (request) => {
    const { worldId, empireId } = request.params as {
      worldId: string;
      empireId: string;
    };
    const world = worldStore.get(worldId);

    if (!world) {
      throw Object.assign(new Error('World not found'), { statusCode: 404 });
    }

    const config = configStore.get();
    const count = worldStore.countSuperCities(empireId);
    const cap =
      config.growth.superCityPerEmpireCap.base +
      config.growth.superCityPerEmpireCap.perOpponent *
        Math.max(0, world.playerEmpireIds.length - 1);

    return {
      empireId,
      current: count,
      cap,
      canPromote: canPromoteToSuperCity(
        config,
        count,
        world.playerEmpireIds.length,
      ),
    };
  });
};

export const worldRoutes: FastifyPluginAsync = async (app) => {
  app.post('/worlds', async (request) => {
    const body = (request.body ?? {}) as {
      name?: string;
      difficulty?: 'slow' | 'normal' | 'fast';
      playerName?: string;
      spawnLat?: number;
      spawnLng?: number;
    };

    if (isDatabaseEnabled()) {
      const bootstrap = await createWorldInDb({
        name: body.name ?? 'Survey World Alpha',
        difficulty: body.difficulty ?? 'normal',
        playerName: body.playerName ?? 'Explorer',
        spawnLat: body.spawnLat,
        spawnLng: body.spawnLng,
      });

      return {
        id: bootstrap.worldId,
        slug: bootstrap.slug,
        empireId: bootstrap.empireId,
        userId: bootstrap.userId,
        settlementCount: bootstrap.settlementCount,
        storage: 'mysql',
      };
    }

    const world = worldStore.create({
      name: body.name ?? 'Survey World Alpha',
      difficulty: body.difficulty ?? 'normal',
      playerEmpireIds: [crypto.randomUUID()],
    });

    return { ...world, storage: 'memory' };
  });

  app.get('/worlds/:worldId/map', async (request, reply) => {
    const { worldId } = request.params as { worldId: string };

    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required for world map' });
    }

    return getWorldMap(worldId);
  });

  app.get('/worlds/:worldId/exploration/:empireId', async (request, reply) => {
    const { worldId, empireId } = request.params as {
      worldId: string;
      empireId: string;
    };

    if (!isDatabaseEnabled()) {
      return reply.status(503).send({ error: 'Database required' });
    }

    const state = await getExplorationState(worldId, empireId);

    return {
      worldId,
      empireId,
      ...state,
      fogOfWar: configStore.get().fogOfWar,
    };
  });

  app.get('/worlds/:worldId', async (request, reply) => {
    const { worldId } = request.params as { worldId: string };
    const world = worldStore.get(worldId);

    if (!world) {
      throw Object.assign(new Error('World not found'), { statusCode: 404 });
    }

    return world;
  });

  app.get('/config/public', async () => ({
    configVersion: configStore.get().meta.configVersion,
    sampling: configStore.get().sampling,
    routes: configStore.get().routes,
    construction: {
      complexityTierProfiles: configStore.get().construction.complexityTierProfiles,
      rush: configStore.get().construction.rush,
      goodieHutBuildTimeMultiplier:
        configStore.get().construction.goodieHutBuildTimeMultiplier,
    },
    resources: configStore.get().resources,
    fogOfWar: configStore.get().fogOfWar,
    growth: {
      superCityPerEmpireCap: configStore.get().growth.superCityPerEmpireCap,
      superCityWorldCap: configStore.get().growth.superCityWorldCap,
    },
    playerAlliancesOpen: configStore.get().npcAlienEmpire.playerAlliancesOpen,
  }));

  app.get('/resources/catalog', async () => ALIEN_RESOURCES);

  app.get('/resources/icons', async () => RESOURCE_MAP_ICONS);
};
