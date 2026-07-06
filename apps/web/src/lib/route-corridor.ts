const EARTH_R = 6_371_000;

function toRad(d: number): number {
  return (d * Math.PI) / 180;
}

function haversineM(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const dLat = toRad(lat2 - lat1);
  const dLng = toRad(lng2 - lng1);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) ** 2;
  return 2 * EARTH_R * Math.asin(Math.sqrt(a));
}

export function distancePointToPathM(
  lat: number,
  lng: number,
  path: Array<{ lat: number; lng: number }>,
): number {
  if (path.length === 0) return Infinity;
  if (path.length === 1) {
    return haversineM(lat, lng, path[0]!.lat, path[0]!.lng);
  }

  let min = Infinity;
  for (let i = 1; i < path.length; i += 1) {
    const a = path[i - 1]!;
    const b = path[i]!;
    const latM = 111_320;
    const lngM = 111_320 * Math.cos(toRad(lat));
    const px = lng * lngM;
    const py = lat * latM;
    const ax = a.lng * lngM;
    const ay = a.lat * latM;
    const bx = b.lng * lngM;
    const by = b.lat * latM;
    const dx = bx - ax;
    const dy = by - ay;
    const lenSq = dx * dx + dy * dy;
    let t = lenSq < 1 ? 0 : ((px - ax) * dx + (py - ay) * dy) / lenSq;
    t = Math.max(0, Math.min(1, t));
    const d = Math.hypot(px - (ax + t * dx), py - (ay + t * dy));
    if (d < min) min = d;
  }
  return min;
}

export function isNearRoute(
  lat: number,
  lng: number,
  path: Array<{ lat: number; lng: number }>,
  radiusM: number,
): boolean {
  return distancePointToPathM(lat, lng, path) <= radiusM;
}
