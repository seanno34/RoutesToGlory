import type {
  AlienResourceId,
  ExplorationMapState,
  GameConfig,
  MapResourceNode,
  RevealExplorationRequest,
} from '@empire/shared';
import {
  ALIEN_RESOURCE_IDS,
  ALIEN_RESOURCES,
  RESOURCE_MAP_ICONS,
  tileIdToCenter,
  tilesInRadius,
} from '@empire/shared';

export interface EmpireExplorationState {
  exploredTileIds: Set<string>;
  tileResourceNodes: Map<string, string>;
  shimmerUntil: Map<string, string>;
}

export function createExplorationState(): EmpireExplorationState {
  return {
    exploredTileIds: new Set(),
    tileResourceNodes: new Map(),
    shimmerUntil: new Map(),
  };
}

export function revealExploration(
  config: GameConfig,
  state: EmpireExplorationState,
  worldResourceNodes: MapResourceNode[],
  request: RevealExplorationRequest,
  worldAgeDays: number,
  options?: {
    voidglassStockpile?: number;
    rng?: () => number;
    now?: Date;
  },
): ExplorationMapState {
  const rng = options?.rng ?? Math.random;
  const now = options?.now ?? new Date();
  const voidglassBonus =
    ((options?.voidglassStockpile ?? 0) / 100) *
    config.fogOfWar.voidglassRevealBonusPer100;
  const radiusM =
    (request.radiusM ?? config.fogOfWar.revealRadiusM) + voidglassBonus;

  const tiles = tilesInRadius(
    request.lat,
    request.lng,
    radiusM,
    config.fogOfWar.tileSizeM,
  );

  const newlyRevealedTileIds: string[] = [];
  const newlyDiscoveredResourceIds: string[] = [];

  for (const tileId of tiles) {
    if (state.exploredTileIds.has(tileId)) {
      continue;
    }

    state.exploredTileIds.add(tileId);
    newlyRevealedTileIds.push(tileId);

    const existingNode = worldResourceNodes.find((node) => node.tileId === tileId);
    if (existingNode) {
      state.tileResourceNodes.set(tileId, existingNode.id);
      const shimmerUntil = new Date(
        now.getTime() + config.fogOfWar.resourceShimmerDurationMs,
      ).toISOString();
      state.shimmerUntil.set(existingNode.id, shimmerUntil);
      newlyDiscoveredResourceIds.push(existingNode.id);
      continue;
    }

    if (rng() < config.fogOfWar.resourceNodeChanceOnReveal) {
      const node = spawnResourceNodeOnTile(
        config,
        request.worldId,
        tileId,
        worldAgeDays,
        rng,
      );
      worldResourceNodes.push(node);
      state.tileResourceNodes.set(tileId, node.id);
      const shimmerUntil = new Date(
        now.getTime() + config.fogOfWar.resourceShimmerDurationMs,
      ).toISOString();
      state.shimmerUntil.set(node.id, shimmerUntil);
      newlyDiscoveredResourceIds.push(node.id);
    }
  }

  return buildMapState(
    config,
    request.worldId,
    request.empireId,
    state,
    worldResourceNodes,
    newlyRevealedTileIds,
    newlyDiscoveredResourceIds,
    now,
  );
}

function spawnResourceNodeOnTile(
  config: GameConfig,
  worldId: string,
  tileId: string,
  worldAgeDays: number,
  rng: () => number,
): MapResourceNode {
  const resourceId = pickDiscoveryResource(config, worldAgeDays, rng);
  const center = tileIdToCenter(tileId, config.fogOfWar.tileSizeM, 0);
  const icon = RESOURCE_MAP_ICONS[resourceId];
  const richnessRoll = rng();
  const richness =
    richnessRoll < 0.2 ? 'sparse' : richnessRoll < 0.7 ? 'moderate' : 'rich';
  const mult = richness === 'sparse' ? 0.6 : richness === 'moderate' ? 1 : 1.6;
  const base =
    config.resources.depositYieldPerDay.min +
    rng() *
      (config.resources.depositYieldPerDay.max -
        config.resources.depositYieldPerDay.min);

  return {
    id: crypto.randomUUID(),
    worldId,
    tileId,
    resourceId,
    lat: center.lat,
    lng: center.lng,
    richness,
    yieldPerDay: Math.max(1, Math.floor(base * mult)),
    iconSpriteId: icon.spriteId,
    glowColor: icon.glowColor,
  };
}

function pickDiscoveryResource(
  config: GameConfig,
  worldAgeDays: number,
  rng: () => number,
): AlienResourceId {
  const unlockedTiers: Array<1 | 2 | 3> = [1];
  const unlockDays = config.resources.discoveryTierUnlockDays['2'] ?? 3;
  if (worldAgeDays >= unlockDays) {
    unlockedTiers.push(2);
  }
  const tier3Days = config.resources.discoveryTierUnlockDays['3'] ?? 10;
  if (worldAgeDays >= tier3Days) {
    unlockedTiers.push(3);
  }

  const eligible = ALIEN_RESOURCE_IDS.filter((id) =>
    unlockedTiers.includes(ALIEN_RESOURCES[id].discoveryTier),
  );
  const weights = config.resources.seedWeights;
  const entries = eligible.map((id) => [id, weights[id] ?? 1] as const);
  const total = entries.reduce((sum, [, w]) => sum + w, 0);
  let roll = rng() * total;

  for (const [id, weight] of entries) {
    roll -= weight;
    if (roll <= 0) {
      return id;
    }
  }

  return eligible[0] ?? 'ferracite';
}

export function buildMapState(
  _config: GameConfig,
  worldId: string,
  empireId: string,
  state: EmpireExplorationState,
  worldResourceNodes: MapResourceNode[],
  newlyRevealedTileIds: string[] = [],
  newlyDiscoveredResourceIds: string[] = [],
  now: Date = new Date(),
): ExplorationMapState {
  const visibleNodes = worldResourceNodes
    .filter((node) => state.exploredTileIds.has(node.tileId))
    .map((node) => {
      const shimmerUntil = state.shimmerUntil.get(node.id);
      const shimmer =
        shimmerUntil !== undefined && new Date(shimmerUntil) > now;

      return {
        ...node,
        shimmer,
        shimmerUntil: shimmer ? shimmerUntil : undefined,
      };
    });

  return {
    worldId,
    empireId,
    exploredTileIds: [...state.exploredTileIds],
    resourceNodes: visibleNodes,
    newlyRevealedTileIds,
    newlyDiscoveredResourceIds,
  };
}

export function grantStartingVision(
  config: GameConfig,
  state: EmpireExplorationState,
  lat: number,
  lng: number,
): void {
  const tiles = tilesInRadius(
    lat,
    lng,
    config.fogOfWar.startingVisionRadiusM,
    config.fogOfWar.tileSizeM,
  );

  for (const tileId of tiles) {
    state.exploredTileIds.add(tileId);
  }
}
