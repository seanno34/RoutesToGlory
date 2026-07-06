/** Distance helpers for route corridor claims. */

const EARTH_R = 6_371_000;

function toRad(d: number): number {
  return (d * Math.PI) / 180;
}

export function haversineM(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const dLat = toRad(lat2 - lat1);
  const dLng = toRad(lng2 - lng1);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) ** 2;
  return 2 * EARTH_R * Math.asin(Math.sqrt(a));
}

export interface PathPoint {
  lat: number;
  lng: number;
}

/** Shortest distance from a point to a polyline (meters). */
export function distancePointToPathM(
  lat: number,
  lng: number,
  path: PathPoint[],
): number {
  if (path.length === 0) return Infinity;
  if (path.length === 1) {
    return haversineM(lat, lng, path[0]!.lat, path[0]!.lng);
  }

  let min = Infinity;
  for (let i = 1; i < path.length; i += 1) {
    const a = path[i - 1]!;
    const b = path[i]!;
    const d = distancePointToSegmentM(lat, lng, a.lat, a.lng, b.lat, b.lng);
    if (d < min) min = d;
  }
  return min;
}

function distancePointToSegmentM(
  pLat: number,
  pLng: number,
  aLat: number,
  aLng: number,
  bLat: number,
  bLng: number,
): number {
  const latM = 111_320;
  const lngM = 111_320 * Math.cos(toRad(pLat));

  const px = pLng * lngM;
  const py = pLat * latM;
  const ax = aLng * lngM;
  const ay = aLat * latM;
  const bx = bLng * lngM;
  const by = bLat * latM;

  const dx = bx - ax;
  const dy = by - ay;
  const lenSq = dx * dx + dy * dy;

  if (lenSq < 1) {
    return haversineM(pLat, pLng, aLat, aLng);
  }

  let t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
  t = Math.max(0, Math.min(1, t));

  const cx = ax + t * dx;
  const cy = ay + t * dy;
  return Math.hypot(px - cx, py - cy);
}

export function nearestPointOnPath(
  lat: number,
  lng: number,
  path: PathPoint[],
): PathPoint {
  if (path.length === 0) return { lat, lng };
  if (path.length === 1) return path[0]!;

  let best = path[0]!;
  let bestD = Infinity;

  for (let i = 1; i < path.length; i += 1) {
    const a = path[i - 1]!;
    const b = path[i]!;
    const mid = {
      lat: (a.lat + b.lat) / 2,
      lng: (a.lng + b.lng) / 2,
    };
    const d = haversineM(lat, lng, mid.lat, mid.lng);
    if (d < bestD) {
      bestD = d;
      best = mid;
    }
  }

  return best;
}

export function isWithinRouteCorridor(
  lat: number,
  lng: number,
  path: PathPoint[],
  radiusM: number,
): boolean {
  return distancePointToPathM(lat, lng, path) <= radiusM;
}
