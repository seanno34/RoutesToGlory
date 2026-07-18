import { z } from 'zod';

/** Canonical terrain biome ids — keep in sync with RtgBiomePalette.cs and AlienTerrainBiome.shader. */
export const TERRAIN_BIOMES = [
  'xeno_plains',
  'xeno_wasteland',
  'xeno_wetland',
  'xeno_fungal_forest',
  'xeno_highland',
  'xeno_rift',
  'xeno_water',
  'xeno_frost',
  'xeno_urban_echo',
] as const;

export const TerrainBiomeSchema = z.enum(TERRAIN_BIOMES);
export type TerrainBiome = z.infer<typeof TerrainBiomeSchema>;

export interface TerrainBiomeDefinition {
  id: TerrainBiome;
  displayName: string;
  earthSignals: string[];
  /** sRGB hex for docs, UI, and shader authoring */
  colorHex: string;
  /** Active in Phase 1 procedural shader */
  shaderActive: boolean;
}

/**
 * POC map skin (Jul 2026): unified dark blackish-purple ground + neon pink veins.
 * Hex values stay in the purple family; biome ids still drive gameplay / deposits.
 * See apps/unity-poc/docs/TERRAIN_SKIN_POC.md.
 */
export const TERRAIN_BIOME_DEFINITIONS: Record<TerrainBiome, TerrainBiomeDefinition> = {
  xeno_plains: {
    id: 'xeno_plains',
    displayName: 'Alien Plains',
    earthSignals: ['grassland', 'open plain', 'low slope mid elevation'],
    colorHex: '#170B24',
    shaderActive: true,
  },
  xeno_wasteland: {
    id: 'xeno_wasteland',
    displayName: 'Dust Expanse',
    earthSignals: ['desert', 'barren', 'sparse scrub'],
    colorHex: '#1C0F21',
    shaderActive: true,
  },
  xeno_wetland: {
    id: 'xeno_wetland',
    displayName: 'Fungal Marsh',
    earthSignals: ['marsh', 'wetland', 'water adjacency'],
    colorHex: '#0D0A1C',
    shaderActive: true,
  },
  xeno_fungal_forest: {
    id: 'xeno_fungal_forest',
    displayName: 'Fungal Forest',
    earthSignals: ['forest', 'woodland', 'tree cover'],
    colorHex: '#140E29',
    shaderActive: true,
  },
  xeno_highland: {
    id: 'xeno_highland',
    displayName: 'Crystal Highland',
    earthSignals: ['mountains', 'high elevation', 'rocky ridge'],
    colorHex: '#291A3D',
    shaderActive: true,
  },
  xeno_rift: {
    id: 'xeno_rift',
    displayName: 'Volcanic Rift',
    earthSignals: ['steep slope', 'cliff', 'canyon wall'],
    colorHex: '#330D2E',
    shaderActive: true,
  },
  xeno_water: {
    id: 'xeno_water',
    displayName: 'Deep Violet Sea',
    earthSignals: ['river', 'lake', 'ocean', 'coast'],
    colorHex: '#060512',
    shaderActive: true,
  },
  xeno_frost: {
    id: 'xeno_frost',
    displayName: 'Frost Expanse',
    earthSignals: ['arctic', 'snow', 'tundra'],
    colorHex: '#2A2040',
    shaderActive: false,
  },
  xeno_urban_echo: {
    id: 'xeno_urban_echo',
    displayName: 'Settlement Scar',
    earthSignals: ['urban', 'residential', 'commercial'],
    colorHex: '#1A1428',
    shaderActive: false,
  },
};

export interface TerrainTileRecord {
  tileId: string;
  biome: TerrainBiome;
  elevationM: number;
  waterFraction: number;
}

/** POC heuristic classifier — mirrors AlienTerrainBiome.shader priority order. */
export function classifyTerrainBiomeHeuristic(input: {
  /** Normalized height relative to local reference, same units as shader heightBand */
  heightBand: number;
  /** 0 = flat, 1 = vertical */
  slope: number;
  /** 0–1 wetness channel */
  wetness: number;
  /** 0–1 large-scale region noise */
  regionNoise: number;
  slopeRiftStart?: number;
  wetlandHeightCutoff?: number;
  waterHeightCutoff?: number;
  highlandHeightCutoff?: number;
  forestRegionThreshold?: number;
  wastelandRegionThreshold?: number;
  waterWetnessMin?: number;
  wetlandWetnessMin?: number;
}): TerrainBiome {
  const slopeRiftStart = input.slopeRiftStart ?? 0.38;
  const wetlandHeightCutoff = input.wetlandHeightCutoff ?? -0.12;
  const waterHeightCutoff = input.waterHeightCutoff ?? -0.28;
  const highlandHeightCutoff = input.highlandHeightCutoff ?? 0.22;
  const forestRegionThreshold = input.forestRegionThreshold ?? 0.62;
  const wastelandRegionThreshold = input.wastelandRegionThreshold ?? 0.28;
  const waterWetnessMin = input.waterWetnessMin ?? 0.58;
  const wetlandWetnessMin = input.wetlandWetnessMin ?? 0.52;

  if (input.slope >= slopeRiftStart) return 'xeno_rift';
  if (
    input.heightBand <= waterHeightCutoff &&
    input.slope < 0.12 &&
    input.wetness >= waterWetnessMin
  ) {
    return 'xeno_water';
  }
  if (input.heightBand <= wetlandHeightCutoff && input.wetness >= wetlandWetnessMin) {
    return 'xeno_wetland';
  }
  if (input.heightBand >= highlandHeightCutoff) return 'xeno_highland';
  if (input.regionNoise >= forestRegionThreshold) return 'xeno_fungal_forest';
  if (input.regionNoise <= wastelandRegionThreshold) return 'xeno_wasteland';
  return 'xeno_plains';
}

export const ACTIVE_SHADER_BIOMES = TERRAIN_BIOMES.filter(
  (id) => TERRAIN_BIOME_DEFINITIONS[id].shaderActive,
);
