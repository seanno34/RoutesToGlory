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

/** Closest point on the polyline to (lat, lng) — used as the route anchor for tap connects. */
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
    const foot = nearestPointOnSegment(lat, lng, a, b);
    const d = haversineM(lat, lng, foot.lat, foot.lng);
    if (d < bestD) {
      bestD = d;
      best = foot;
    }
  }

  return best;
}

function nearestPointOnSegment(
  pLat: number,
  pLng: number,
  a: PathPoint,
  b: PathPoint,
): PathPoint {
  const latM = 111_320;
  const lngM = 111_320 * Math.cos(toRad(pLat));

  const px = pLng * lngM;
  const py = pLat * latM;
  const ax = a.lng * lngM;
  const ay = a.lat * latM;
  const bx = b.lng * lngM;
  const by = b.lat * latM;

  const dx = bx - ax;
  const dy = by - ay;
  const lenSq = dx * dx + dy * dy;

  if (lenSq < 1) return a;

  let t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
  t = Math.max(0, Math.min(1, t));

  return {
    lat: (ay + t * dy) / latM,
    lng: (ax + t * dx) / lngM,
  };
}

export function isWithinRouteCorridor(
  lat: number,
  lng: number,
  path: PathPoint[],
  radiusM: number,
): boolean {
  return distancePointToPathM(lat, lng, path) <= radiusM;
}

export interface LatLngBbox {
  minLat: number;
  maxLat: number;
  minLng: number;
  maxLng: number;
}

/** Uniform decimation — keeps endpoints and caps vertex count for corridor checks. */
export function decimatePath(path: PathPoint[], maxPoints: number): PathPoint[] {
  if (path.length <= maxPoints || maxPoints < 2) return path;

  const result: PathPoint[] = [];
  const step = (path.length - 1) / (maxPoints - 1);
  for (let i = 0; i < maxPoints; i += 1) {
    const idx = Math.min(path.length - 1, Math.round(i * step));
    result.push(path[idx]!);
  }
  return result;
}

export function pathBbox(path: PathPoint[]): LatLngBbox | null {
  if (path.length === 0) return null;

  let minLat = path[0]!.lat;
  let maxLat = path[0]!.lat;
  let minLng = path[0]!.lng;
  let maxLng = path[0]!.lng;

  for (let i = 1; i < path.length; i += 1) {
    const p = path[i]!;
    minLat = Math.min(minLat, p.lat);
    maxLat = Math.max(maxLat, p.lat);
    minLng = Math.min(minLng, p.lng);
    maxLng = Math.max(maxLng, p.lng);
  }

  return { minLat, maxLat, minLng, maxLng };
}

export function expandBboxAroundPoint(
  lat: number,
  lng: number,
  radiusM: number,
): LatLngBbox {
  const latM = 111_320;
  const lngM = latM * Math.cos(toRad(lat));
  const dLat = radiusM / latM;
  const dLng = radiusM / Math.max(1, lngM);
  return {
    minLat: lat - dLat,
    maxLat: lat + dLat,
    minLng: lng - dLng,
    maxLng: lng + dLng,
  };
}

export function bboxesOverlap(a: LatLngBbox, b: LatLngBbox): boolean {
  return !(a.maxLat < b.minLat || a.minLat > b.maxLat || a.maxLng < b.minLng || a.minLng > b.maxLng);
}

export function isWithinAnyRouteCorridor(
  lat: number,
  lng: number,
  paths: PathPoint[][],
  radiusM: number,
): boolean {
  for (const path of paths) {
    if (path.length > 0 && isWithinRouteCorridor(lat, lng, path, radiusM)) return true;
  }
  return false;
}

/** Closest foot on any candidate path — used as the connector anchor. */
export function nearestPointOnAnyPath(
  lat: number,
  lng: number,
  paths: PathPoint[][],
): PathPoint {
  let best: PathPoint = { lat, lng };
  let bestD = Infinity;

  for (const path of paths) {
    if (path.length === 0) continue;
    const foot = nearestPointOnPath(lat, lng, path);
    const d = haversineM(lat, lng, foot.lat, foot.lng);
    if (d < bestD) {
      bestD = d;
      best = foot;
    }
  }

  return best;
}
