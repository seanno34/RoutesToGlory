export interface RoadPoint {
  lat: number;
  lng: number;
  /** Distance from spawn along the road network (meters). */
  distanceFromOriginM: number;
}

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

function offsetFromOrigin(
  originLat: number,
  originLng: number,
  distanceM: number,
  bearingRad: number,
): { lat: number; lng: number } {
  const latM = 111_320;
  const lngM = 111_320 * Math.cos(toRad(originLat));
  return {
    lat: originLat + (Math.cos(bearingRad) * distanceM) / latM,
    lng: originLng + (Math.sin(bearingRad) * distanceM) / lngM,
  };
}

/** Walk a polyline and emit points every `spacingM` meters. */
export function sampleAlongPolyline(
  coords: Array<{ lat: number; lng: number }>,
  spacingM: number,
  originLat: number,
  originLng: number,
): RoadPoint[] {
  if (coords.length < 2) return [];

  const points: RoadPoint[] = [];
  let chainDist = 0;
  let nextSampleAt = spacingM;

  for (let i = 1; i < coords.length; i += 1) {
    const a = coords[i - 1]!;
    const b = coords[i]!;
    const segLen = haversineM(a.lat, a.lng, b.lat, b.lng);
    if (segLen < 1) continue;

    const segStart = chainDist;
    const segEnd = chainDist + segLen;

    while (nextSampleAt <= segEnd) {
      const intoSeg = nextSampleAt - segStart;
      const t = intoSeg / segLen;
      const lat = a.lat + (b.lat - a.lat) * t;
      const lng = a.lng + (b.lng - a.lng) * t;
      points.push({
        lat,
        lng,
        distanceFromOriginM: haversineM(originLat, originLng, lat, lng),
      });
      nextSampleAt += spacingM;
    }

    chainDist = segEnd;
  }

  return points;
}

export function dedupeRoadPoints(
  points: RoadPoint[],
  minSeparationM: number,
): RoadPoint[] {
  const sorted = [...points].sort(
    (a, b) => a.distanceFromOriginM - b.distanceFromOriginM,
  );
  const kept: RoadPoint[] = [];

  for (const p of sorted) {
    const tooClose = kept.some(
      (k) => haversineM(k.lat, k.lng, p.lat, p.lng) < minSeparationM,
    );
    if (!tooClose) kept.push(p);
  }

  return kept;
}

async function mapboxRouteCoords(
  originLat: number,
  originLng: number,
  destLat: number,
  destLng: number,
  token: string,
): Promise<Array<{ lat: number; lng: number }>> {
  const url =
    `https://api.mapbox.com/directions/v5/mapbox/driving/` +
    `${originLng},${originLat};${destLng},${destLat}` +
    `?geometries=geojson&overview=full&access_token=${encodeURIComponent(token)}`;

  const res = await fetch(url);
  if (!res.ok) return [];

  const data = (await res.json()) as {
    routes?: Array<{ geometry?: { coordinates?: [number, number][] } }>;
  };

  const coords = data.routes?.[0]?.geometry?.coordinates;
  if (!coords?.length) return [];

  return coords.map(([lng, lat]) => ({ lat, lng }));
}

async function sampleWithMapbox(
  originLat: number,
  originLng: number,
  radiusM: number,
  spacingM: number,
  token: string,
): Promise<RoadPoint[]> {
  const spokes = 10;
  const allSamples: RoadPoint[] = [];

  for (let i = 0; i < spokes; i += 1) {
    const bearing = (i / spokes) * Math.PI * 2;
    const dest = offsetFromOrigin(originLat, originLng, radiusM * 0.85, bearing);
    const route = await mapboxRouteCoords(
      originLat,
      originLng,
      dest.lat,
      dest.lng,
      token,
    );
    if (route.length < 2) continue;
    allSamples.push(...sampleAlongPolyline(route, spacingM, originLat, originLng));
  }

  return dedupeRoadPoints(allSamples, spacingM * 0.75);
}

type OverpassWay = {
  geometry?: Array<{ lat: number; lon: number }>;
};

async function sampleWithOverpass(
  originLat: number,
  originLng: number,
  radiusM: number,
  spacingM: number,
): Promise<RoadPoint[]> {
  const query = `
    [out:json][timeout:25];
    way(around:${Math.floor(radiusM)},${originLat},${originLng})
      ["highway"~"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential)$"]
      ["access"!~"private"];
    out geom;
  `;

  const res = await fetch('https://overpass-api.de/api/interpreter', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: `data=${encodeURIComponent(query)}`,
  });

  if (!res.ok) return [];

  const data = (await res.json()) as { elements?: OverpassWay[] };
  const allSamples: RoadPoint[] = [];

  for (const way of data.elements ?? []) {
    if (!way.geometry?.length) continue;
    const coords = way.geometry.map((n) => ({ lat: n.lat, lng: n.lon }));
    allSamples.push(...sampleAlongPolyline(coords, spacingM, originLat, originLng));
  }

  return dedupeRoadPoints(allSamples, spacingM * 0.75);
}

/**
 * Sample drivable road points near spawn using Mapbox Directions (preferred)
 * or OpenStreetMap Overpass as fallback.
 */
export async function sampleRoadPointsNear(
  originLat: number,
  originLng: number,
  options?: {
    radiusM?: number;
    spacingM?: number;
  },
): Promise<RoadPoint[]> {
  const radiusM = options?.radiusM ?? 12_000;
  const spacingM = options?.spacingM ?? 450;

  const token =
    process.env.MAPBOX_ACCESS_TOKEN ??
    process.env.MAPBOX_TOKEN ??
    process.env.VITE_MAPBOX_TOKEN;

  if (token) {
    try {
      const mapboxPoints = await sampleWithMapbox(
        originLat,
        originLng,
        radiusM,
        spacingM,
        token,
      );
      if (mapboxPoints.length >= 20) return mapboxPoints;
    } catch {
      /* fall through to Overpass */
    }
  }

  try {
    const overpassPoints = await sampleWithOverpass(
      originLat,
      originLng,
      radiusM,
      spacingM,
    );
    if (overpassPoints.length >= 10) return overpassPoints;
  } catch {
    /* fall through */
  }

  return [];
}
