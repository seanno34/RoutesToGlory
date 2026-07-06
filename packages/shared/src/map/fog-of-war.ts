import type { GameConfig } from '../config/schema.js';

/** Convert lat/lng to a stable grid tile id (meters-based). */
export function latLngToTileId(
  lat: number,
  lng: number,
  tileSizeM: number,
): string {
  const latM = 111_320;
  const lngM = 111_320 * Math.cos((lat * Math.PI) / 180);
  const x = Math.floor((lng * lngM) / tileSizeM);
  const y = Math.floor((lat * latM) / tileSizeM);
  return `${x}:${y}`;
}

export function tileIdToCenter(
  tileId: string,
  tileSizeM: number,
  _referenceLat?: number,
): { lat: number; lng: number } {
  const [xStr, yStr] = tileId.split(':');
  const x = Number(xStr);
  const y = Number(yStr);
  const latM = 111_320;
  const lat = ((y + 0.5) * tileSizeM) / latM;
  const lngM = 111_320 * Math.cos((lat * Math.PI) / 180);
  return {
    lat,
    lng: ((x + 0.5) * tileSizeM) / lngM,
  };
}

/** Tiles whose centers fall within radius of a point. */
export function tilesInRadius(
  lat: number,
  lng: number,
  radiusM: number,
  tileSizeM: number,
): string[] {
  const latM = 111_320;
  const lngM = 111_320 * Math.cos((lat * Math.PI) / 180);
  const tileRadius = Math.ceil(radiusM / tileSizeM) + 1;
  const centerX = Math.floor((lng * lngM) / tileSizeM);
  const centerY = Math.floor((lat * latM) / tileSizeM);
  const tiles: string[] = [];

  for (let dy = -tileRadius; dy <= tileRadius; dy += 1) {
    for (let dx = -tileRadius; dx <= tileRadius; dx += 1) {
      const tileId = `${centerX + dx}:${centerY + dy}`;
      const center = tileIdToCenter(tileId, tileSizeM, lat);
      const dLat = (center.lat - lat) * latM;
      const dLng = (center.lng - lng) * lngM;
      if (Math.hypot(dLat, dLng) <= radiusM) {
        tiles.push(tileId);
      }
    }
  }

  return tiles;
}

/** Sample lat/lng points along a segment for corridor fog reveal. */
export function samplePointsAlongSegment(
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

export function fogOpacityForTile(
  explored: Set<string>,
  tileId: string,
  config: GameConfig,
): number {
  if (explored.has(tileId)) {
    return config.fogOfWar.exploredOpacity;
  }

  return config.fogOfWar.unexploredOpacity;
}
