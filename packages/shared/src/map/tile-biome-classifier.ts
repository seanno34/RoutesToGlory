import type { TerrainBiome } from './terrain-biome.js';

const LAT_M = 111_320;

export interface TileBiomeInput {
  lat: number;
  lng: number;
  /** Optional real elevation (m) relative to local reference */
  elevationM?: number;
  referenceHeightM?: number;
  macroRegionSizeM?: number;
  wetBasinFraction?: number;
}

function hash21(x: number, y: number): number {
  const v = Math.sin(x * 127.1 + y * 311.7) * 43758.5453;
  return v - Math.floor(v);
}

function hash22(x: number, y: number): { x: number; y: number } {
  return { x: hash21(x, y), y: hash21(x + 17.7, y + 9.2) };
}

function latLngToWorldXZ(lat: number, lng: number): { x: number; z: number } {
  const lngM = LAT_M * Math.cos((lat * Math.PI) / 180);
  return { x: lng * lngM, z: lat * LAT_M };
}

type MacroZone = 'plains' | 'forest' | 'wasteland' | 'wet_basin';

function macroZoneFromHash(h: number, wetBasinFraction: number): MacroZone {
  const wetCut = wetBasinFraction;
  const forestCut = wetCut + 0.28;
  const wasteCut = forestCut + 0.22;
  if (h < wetCut) return 'wet_basin';
  if (h < forestCut) return 'forest';
  if (h < wasteCut) return 'wasteland';
  return 'plains';
}

function sampleMacroZone(worldX: number, worldZ: number, cellSize: number, wetBasinFraction: number): MacroZone {
  const invCell = 1 / Math.max(cellSize, 400);
  const warpStrength = 0.42;
  const warpX =
    (hash21(worldX * invCell * 0.31 + 12.7, worldZ * invCell * 0.31) - 0.5) *
    warpStrength *
    cellSize;
  const warpZ =
    (hash21(worldX * invCell * 0.31 + 51.2, worldZ * invCell * 0.31 + 3.1) - 0.5) *
    warpStrength *
    cellSize;

  const warpedX = worldX + warpX;
  const warpedZ = worldZ + warpZ;
  const cellX = Math.floor(warpedX * invCell);
  const cellZ = Math.floor(warpedZ * invCell);
  const cellUvX = warpedX * invCell - cellX;
  const cellUvZ = warpedZ * invCell - cellZ;

  let bestDist = 1e9;
  let bestCellX = cellX;
  let bestCellZ = cellZ;

  for (let oz = -1; oz <= 1; oz += 1) {
    for (let ox = -1; ox <= 1; ox += 1) {
      const nX = cellX + ox;
      const nZ = cellZ + oz;
      const feature = hash22(nX * 2.17 + 9.4, nZ * 2.17 + 9.4);
      const fx = feature.x * 0.72 + 0.14;
      const fz = feature.y * 0.72 + 0.14;
      const dx = ox + fx - cellUvX;
      const dz = oz + fz - cellUvZ;
      const dist = dx * dx + dz * dz;
      if (dist < bestDist) {
        bestDist = dist;
        bestCellX = nX;
        bestCellZ = nZ;
      }
    }
  }

  const cellHash = hash21(bestCellX * 2.17 + 9.4, bestCellZ * 2.17 + 9.4);
  return macroZoneFromHash(cellHash, wetBasinFraction);
}

/**
 * Deterministic procedural biome for a lat/lng — mirrors Unity macro Voronoi shader.
 * Used for Phase 2 spawn rules until real OSM/elevation classification lands.
 */
export function classifyTileBiomeProcedural(input: TileBiomeInput): TerrainBiome {
  const cellSize = input.macroRegionSizeM ?? 4200;
  const wetBasinFraction = input.wetBasinFraction ?? 0.18;
  const { x, z } = latLngToWorldXZ(input.lat, input.lng);
  const macro = sampleMacroZone(x, z, cellSize, wetBasinFraction);

  const refH = input.referenceHeightM ?? 1476;
  const elev = input.elevationM ?? refH + (hash21(x * 0.0013, z * 0.0011) - 0.5) * 120;
  const heightBand = (elev - refH) / 55;

  const slopeHash = hash21(x * 0.0071, z * 0.0063);
  const slope = slopeHash > 0.88 ? 0.42 + (slopeHash - 0.88) * 2 : slopeHash * 0.08;

  if (slope >= 0.36) return 'xeno_rift';
  if (heightBand >= 0.22) return 'xeno_highland';

  if (macro === 'wet_basin') {
    const wetness = hash21(x * 0.00035 + 23.1, z * 0.00035 + 9.7);
    if (heightBand <= -0.28 && slope < 0.12 && wetness >= 0.55) return 'xeno_water';
    if (heightBand <= -0.12 && wetness >= 0.45) return 'xeno_wetland';
    return 'xeno_plains';
  }
  if (macro === 'forest') return 'xeno_fungal_forest';
  if (macro === 'wasteland') return 'xeno_wasteland';
  return 'xeno_plains';
}
