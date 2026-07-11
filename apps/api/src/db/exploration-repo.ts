import type { GameConfig, MapResourceNode } from '@empire/shared';
import {
  ALIEN_RESOURCE_IDS,
  RESOURCE_MAP_ICONS,
  tileIdToCenter,
  tilesInRadius,
} from '@empire/shared';
import { configStore } from '../services/config-store.js';
import { applyMineYields } from '../services/mine-yields.js';
import { query, newId } from './client.js';

export async function getExploredTileIds(
  worldId: string,
  empireId: string,
): Promise<string[]> {
  const result = await query<{ tile_id: string }>(
    `SELECT tile_id FROM explored_tiles WHERE world_id = ? AND empire_id = ?`,
    [worldId, empireId],
  );
  return result.rows.map((r) => r.tile_id);
}

export async function getMapResourceNodes(worldId: string): Promise<MapResourceNode[]> {
  const result = await query<{
    id: string;
    world_id: string;
    tile_id: string;
    resource_id: string;
    lat: number;
    lng: number;
    richness: string;
    yield_per_day: number;
    owner_empire_id: string | null;
    route_id: string | null;
  }>(`SELECT * FROM map_resource_nodes WHERE world_id = ?`, [worldId]);

  return result.rows.map((row) => {
    const icon = RESOURCE_MAP_ICONS[row.resource_id as keyof typeof RESOURCE_MAP_ICONS];
    return {
      id: row.id,
      worldId: row.world_id,
      tileId: row.tile_id,
      resourceId: row.resource_id as MapResourceNode['resourceId'],
      lat: Number(row.lat),
      lng: Number(row.lng),
      richness: row.richness as MapResourceNode['richness'],
      yieldPerDay: row.yield_per_day,
      ownerEmpireId: row.owner_empire_id ?? undefined,
      routeId: row.route_id ?? undefined,
      iconSpriteId: icon?.spriteId ?? 'res-xenite',
      glowColor: icon?.glowColor ?? '#f97316',
    };
  });
}

export async function grantStartingVision(
  worldId: string,
  empireId: string,
  lat: number,
  lng: number,
  config: GameConfig,
): Promise<void> {
  const tiles = tilesInRadius(
    lat,
    lng,
    config.fogOfWar.startingVisionRadiusM,
    config.fogOfWar.tileSizeM,
  );

  for (const tileId of tiles) {
    await query(
      `INSERT IGNORE INTO explored_tiles (world_id, empire_id, tile_id) VALUES (?, ?, ?)`,
      [worldId, empireId, tileId],
    );
  }
}

export type RevealTilesOptions = {
  /** When false, only stamp explored tiles — no random resource spawns (route backfill). */
  spawnResources?: boolean;
};

export async function revealTilesAtPoint(
  worldId: string,
  empireId: string,
  lat: number,
  lng: number,
  config: GameConfig = configStore.get(),
  options: RevealTilesOptions = {},
): Promise<{ newlyRevealedTileIds: string[]; newResourceNodeIds: string[] }> {
  const spawnResources = options.spawnResources !== false;
  const tiles = tilesInRadius(
    lat,
    lng,
    config.fogOfWar.revealRadiusM,
    config.fogOfWar.tileSizeM,
  );

  const existing = new Set(await getExploredTileIds(worldId, empireId));
  const newlyRevealedTileIds: string[] = [];

  for (const tileId of tiles) {
    if (existing.has(tileId)) continue;
    await query(
      `INSERT IGNORE INTO explored_tiles (world_id, empire_id, tile_id) VALUES (?, ?, ?)`,
      [worldId, empireId, tileId],
    );
    existing.add(tileId);
    newlyRevealedTileIds.push(tileId);
  }

  const newResourceNodeIds: string[] = [];
  if (spawnResources) {
    const nodesOnTile = await query<{ tile_id: string }>(
      `SELECT tile_id FROM map_resource_nodes WHERE world_id = ?`,
      [worldId],
    );
    const occupiedTiles = new Set(nodesOnTile.rows.map((r) => r.tile_id));

    for (const tileId of newlyRevealedTileIds) {
      if (occupiedTiles.has(tileId)) continue;
      if (Math.random() >= config.fogOfWar.resourceNodeChanceOnReveal) continue;

      const nodeId = await insertRandomResourceNode(worldId, tileId, config);
      if (nodeId) {
        newResourceNodeIds.push(nodeId);
        occupiedTiles.add(tileId);
      }
    }
  }

  return { newlyRevealedTileIds, newResourceNodeIds };
}

function samplePointsAlongSegment(
  from: { lat: number; lng: number },
  to: { lat: number; lng: number },
  stepM: number,
): Array<{ lat: number; lng: number }> {
  const latM = 111_320;
  const avgLat = (from.lat + to.lat) / 2;
  const lngM = 111_320 * Math.cos((avgLat * Math.PI) / 180);
  const dLat = (to.lat - from.lat) * latM;
  const dLng = (to.lng - from.lng) * lngM;
  const dist = Math.hypot(dLat, dLng);
  if (dist < 1) {
    return [to];
  }

  const steps = Math.max(1, Math.ceil(dist / stepM));
  const points: Array<{ lat: number; lng: number }> = [];
  for (let i = 0; i <= steps; i += 1) {
    const t = i / steps;
    points.push({
      lat: from.lat + (to.lat - from.lat) * t,
      lng: from.lng + (to.lng - from.lng) * t,
    });
  }
  return points;
}

/** Reveal fog along the driven segment, not just at the endpoint. */
export async function revealTilesAlongSegment(
  worldId: string,
  empireId: string,
  from: { lat: number; lng: number } | null,
  to: { lat: number; lng: number },
  config: GameConfig = configStore.get(),
  options: RevealTilesOptions = {},
): Promise<{ newlyRevealedTileIds: string[]; newResourceNodeIds: string[] }> {
  const stepM = Math.max(
    config.fogOfWar.revealRadiusM,
    Math.floor(config.fogOfWar.tileSizeM / 2),
  );
  const samples = from
    ? samplePointsAlongSegment(from, to, stepM)
    : [{ lat: to.lat, lng: to.lng }];

  const allNewTiles: string[] = [];
  const allNewResources: string[] = [];

  for (const sample of samples) {
    const result = await revealTilesAtPoint(
      worldId,
      empireId,
      sample.lat,
      sample.lng,
      config,
      options,
    );
    allNewTiles.push(...result.newlyRevealedTileIds);
    allNewResources.push(...result.newResourceNodeIds);
  }

  return {
    newlyRevealedTileIds: [...new Set(allNewTiles)],
    newResourceNodeIds: [...new Set(allNewResources)],
  };
}

async function insertRandomResourceNode(
  worldId: string,
  tileId: string,
  config: GameConfig,
): Promise<string | null> {
  const resourceId = ALIEN_RESOURCE_IDS[Math.floor(Math.random() * ALIEN_RESOURCE_IDS.length)]!;
  const center = tileIdToCenter(tileId, config.fogOfWar.tileSizeM, 42.63);
  const richnessRoll = Math.random();
  const richness =
    richnessRoll < 0.2 ? 'sparse' : richnessRoll < 0.7 ? 'moderate' : 'rich';
  const mult = richness === 'sparse' ? 0.6 : richness === 'moderate' ? 1 : 1.6;
  const base =
    config.resources.depositYieldPerDay.min +
    Math.random() *
      (config.resources.depositYieldPerDay.max - config.resources.depositYieldPerDay.min);
  const yieldPerDay = Math.max(1, Math.floor(base * mult));
  const id = newId();

  try {
    await query(
      `INSERT INTO map_resource_nodes
         (id, world_id, tile_id, resource_id, lat, lng, richness, yield_per_day)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
      [id, worldId, tileId, resourceId, center.lat, center.lng, richness, yieldPerDay],
    );
    return id;
  } catch {
    return null;
  }
}

export async function insertResourceNode(
  worldId: string,
  tileId: string,
  resourceId: MapResourceNode['resourceId'],
  lat: number,
  lng: number,
  richness: 'sparse' | 'moderate' | 'rich',
  yieldPerDay: number,
): Promise<string> {
  const id = newId();
  await query(
    `INSERT IGNORE INTO map_resource_nodes
       (id, world_id, tile_id, resource_id, lat, lng, richness, yield_per_day)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
    [id, worldId, tileId, resourceId, lat, lng, richness, yieldPerDay],
  );
  return id;
}

/** Every saved route corridor must stay explored — routes and fog are separate writes. */
export async function revealTilesAlongRoutePath(
  worldId: string,
  empireId: string,
  points: Array<{ lat: number; lng: number }>,
  config: GameConfig = configStore.get(),
  options: RevealTilesOptions = { spawnResources: false },
): Promise<void> {
  if (points.length === 0) return;
  if (points.length === 1) {
    await revealTilesAtPoint(
      worldId,
      empireId,
      points[0]!.lat,
      points[0]!.lng,
      config,
      options,
    );
    return;
  }

  for (let i = 1; i < points.length; i += 1) {
    await revealTilesAlongSegment(
      worldId,
      empireId,
      points[i - 1]!,
      points[i]!,
      config,
      options,
    );
  }
}

async function ensureExplorationAlongPersistedRoutes(
  worldId: string,
  empireId: string,
  config: GameConfig,
): Promise<void> {
  const routes = await query<{ path_json: string }>(
    `SELECT path_json FROM routes
     WHERE world_id = ? AND empire_id = ? AND status = 'active'`,
    [worldId, empireId],
  );

  for (const row of routes.rows) {
    let points: Array<{ lat: number; lng: number }>;
    try {
      points = JSON.parse(row.path_json) as Array<{ lat: number; lng: number }>;
    } catch {
      continue;
    }
    await revealTilesAlongRoutePath(worldId, empireId, points, config, {
      spawnResources: false,
    });
  }
}

export async function getExplorationState(
  worldId: string,
  empireId: string,
): Promise<{
  exploredTileIds: string[];
  resourceNodes: MapResourceNode[];
  mineYieldAccrued?: Record<string, number>;
  activeMineCount?: number;
}> {
  const config = configStore.get();
  await ensureExplorationAlongPersistedRoutes(worldId, empireId, config);

  const yieldResult = await applyMineYields(worldId, empireId);
  const exploredTileIds = await getExploredTileIds(worldId, empireId);
  const explored = new Set(exploredTileIds);
  const allNodes = await getMapResourceNodes(worldId);

  return {
    exploredTileIds,
    resourceNodes: allNodes.filter(
      (node) =>
        explored.has(node.tileId) &&
        (!node.ownerEmpireId || node.ownerEmpireId === empireId),
    ),
    mineYieldAccrued: yieldResult.accrued,
    activeMineCount: yieldResult.mineCount,
  };
}
