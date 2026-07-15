import type { AlienResourceId } from '../types/enums.js';
import type { TerrainBiome } from '../map/terrain-biome.js';
import { ALIEN_RESOURCE_IDS, ALIEN_RESOURCES } from './alien-resources.js';

/** Preferred biomes per resource — Phase 2 spawn + deposit placement. */
export const RESOURCE_BIOME_PREFERENCES: Record<AlienResourceId, TerrainBiome[]> = {
  xenite: ['xeno_rift', 'xeno_highland', 'xeno_wasteland'],
  solari_dust: ['xeno_plains', 'xeno_wasteland', 'xeno_highland'],
  ferracite: ['xeno_highland', 'xeno_rift', 'xeno_wasteland'],
  lumin_spring: ['xeno_wetland', 'xeno_water', 'xeno_wetland'],
  quantium_shard: ['xeno_highland', 'xeno_rift', 'xeno_fungal_forest'],
  voidglass: ['xeno_highland', 'xeno_rift', 'xeno_wetland'],
  mycelium_core: ['xeno_fungal_forest', 'xeno_wetland', 'xeno_plains'],
  chrono_moss: ['xeno_wetland', 'xeno_fungal_forest', 'xeno_plains'],
  aegis_bark: ['xeno_fungal_forest', 'xeno_plains', 'xeno_highland'],
  nebula_pearl: ['xeno_water', 'xeno_wetland', 'xeno_highland'],
};

const FALLBACK_BIOMES: TerrainBiome[] = [
  'xeno_plains',
  'xeno_fungal_forest',
  'xeno_wasteland',
  'xeno_highland',
];

export function resourcesPreferringBiome(biome: TerrainBiome): AlienResourceId[] {
  const matches = ALIEN_RESOURCE_IDS.filter((id) =>
    RESOURCE_BIOME_PREFERENCES[id].includes(biome),
  );
  return matches.length > 0 ? matches : ALIEN_RESOURCE_IDS;
}

export interface PickResourceOptions {
  /** 0–1 RNG */
  roll: number;
  /** World age in days — gates discovery tiers */
  worldAgeDays?: number;
}

function isTierUnlocked(resourceId: AlienResourceId, worldAgeDays: number): boolean {
  const tier = ALIEN_RESOURCES[resourceId].discoveryTier;
  if (tier === 1) return true;
  if (tier === 2) return worldAgeDays >= 3;
  return worldAgeDays >= 10;
}

/** Pick a resource id suited to the tile biome (with tier gating). */
export function pickResourceForBiome(
  biome: TerrainBiome,
  options: PickResourceOptions,
): AlienResourceId {
  const worldAgeDays = options.worldAgeDays ?? 0;
  let pool = resourcesPreferringBiome(biome).filter((id) =>
    isTierUnlocked(id, worldAgeDays),
  );
  if (pool.length === 0) {
    pool = ALIEN_RESOURCE_IDS.filter((id) => isTierUnlocked(id, worldAgeDays));
  }

  const idx = Math.min(pool.length - 1, Math.floor(options.roll * pool.length));
  return pool[idx] ?? 'xenite';
}

export function biomeMatchesResource(biome: TerrainBiome, resourceId: AlienResourceId): boolean {
  return RESOURCE_BIOME_PREFERENCES[resourceId].includes(biome);
}

export function fallbackBiomeForResource(resourceId: AlienResourceId): TerrainBiome {
  return RESOURCE_BIOME_PREFERENCES[resourceId][0] ?? FALLBACK_BIOMES[0];
}
