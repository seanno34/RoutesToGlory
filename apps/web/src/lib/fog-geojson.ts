import type { Map as MapboxMap } from 'mapbox-gl';
import { latLngToTileId, tileIdToCenter } from '@empire/shared';

export interface MapBounds {
  north: number;
  south: number;
  east: number;
  west: number;
}

type FogPolygon = {
  type: 'Polygon';
  coordinates: number[][][];
};

export type FogFeatureCollection = {
  type: 'FeatureCollection';
  features: Array<{
    type: 'Feature';
    properties: { tileId: string };
    geometry: FogPolygon;
  }>;
};

/** Hard cap — prevents OOM when the viewport is zoomed out. */
const MAX_FOG_TILES = 500;
/** Fog only renders within this radius of the focus point (meters). */
const MAX_FOG_RADIUS_M = 10_000;
/** Below this zoom, skip fog entirely (continental view). */
export const MIN_FOG_ZOOM = 10;

function clipBoundsToRadius(
  bounds: MapBounds,
  centerLat: number,
  centerLng: number,
  radiusM: number,
): MapBounds {
  const latM = 111_320;
  const lngM = 111_320 * Math.cos((centerLat * Math.PI) / 180);
  const dLat = radiusM / latM;
  const dLng = radiusM / lngM;
  return {
    north: Math.min(bounds.north, centerLat + dLat),
    south: Math.max(bounds.south, centerLat - dLat),
    east: Math.min(bounds.east, centerLng + dLng),
    west: Math.max(bounds.west, centerLng - dLng),
  };
}

function tileIdToPolygon(
  tileId: string,
  tileSizeM: number,
  refLat: number,
): FogPolygon {
  const center = tileIdToCenter(tileId, tileSizeM, refLat);
  const latM = 111_320;
  const lngM = 111_320 * Math.cos((refLat * Math.PI) / 180);
  const halfLat = tileSizeM / 2 / latM;
  const halfLng = tileSizeM / 2 / lngM;
  const ring: number[][] = [
    [center.lng - halfLng, center.lat - halfLat],
    [center.lng + halfLng, center.lat - halfLat],
    [center.lng + halfLng, center.lat + halfLat],
    [center.lng - halfLng, center.lat + halfLat],
    [center.lng - halfLng, center.lat - halfLat],
  ];
  return { type: 'Polygon', coordinates: [ring] };
}

/** GeoJSON features for fog tiles (unexplored) near the focus point. */
export function buildFogGeoJson(
  explored: Set<string>,
  bounds: MapBounds,
  tileSizeM: number,
  focus?: { lat: number; lng: number },
): FogFeatureCollection {
  if (!focus) {
    return { type: 'FeatureCollection', features: [] };
  }

  const clipped = clipBoundsToRadius(bounds, focus.lat, focus.lng, MAX_FOG_RADIUS_M);
  const refLat = focus.lat;
  const latM = 111_320;
  const lngM = 111_320 * Math.cos((refLat * Math.PI) / 180);

  const minY = Math.floor((clipped.south * latM) / tileSizeM) - 1;
  const maxY = Math.ceil((clipped.north * latM) / tileSizeM) + 1;
  const minX = Math.floor((clipped.west * lngM) / tileSizeM) - 1;
  const maxX = Math.ceil((clipped.east * lngM) / tileSizeM) + 1;

  const features: FogFeatureCollection['features'] = [];

  outer: for (let y = minY; y <= maxY; y += 1) {
    for (let x = minX; x <= maxX; x += 1) {
      if (features.length >= MAX_FOG_TILES) break outer;
      const tileId = `${x}:${y}`;
      if (explored.has(tileId)) continue;
      features.push({
        type: 'Feature',
        properties: { tileId },
        geometry: tileIdToPolygon(tileId, tileSizeM, refLat),
      });
    }
  }

  return { type: 'FeatureCollection', features };
}

export function boundsFromMap(map: MapboxMap): MapBounds {
  const b = map.getBounds();
  if (!b) {
    return { north: 0, south: 0, east: 0, west: 0 };
  }
  return {
    north: b.getNorth(),
    south: b.getSouth(),
    east: b.getEast(),
    west: b.getWest(),
  };
}

export { latLngToTileId };
