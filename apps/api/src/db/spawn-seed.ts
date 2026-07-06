/**
 * Dense test seed around the player's spawn — goodie huts + resource nodes.
 * Tuned for Orin Junction, WY play-testing but works at any spawn GPS.
 */
import type { GameConfig } from '@empire/shared';
import { ALIEN_RESOURCE_IDS, latLngToTileId } from '@empire/shared';
import { query, newId } from './client.js';
import { insertResourceNode } from './exploration-repo.js';

const PLAY_AREA_RADIUS_M = 12_000;
const GOODIE_HUT_COUNT = 16;
const RESOURCE_NODE_COUNT = 48;

function haversineM(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const R = 6_371_000;
  const toRad = (d: number) => (d * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLng = toRad(lng2 - lng1);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(a));
}

function offsetMeters(
  lat: number,
  lng: number,
  northM: number,
  eastM: number,
): { lat: number; lng: number } {
  const latM = 111_320;
  const lngM = 111_320 * Math.cos((lat * Math.PI) / 180);
  return {
    lat: lat + northM / latM,
    lng: lng + eastM / lngM,
  };
}

function rng(seed: number): () => number {
  let s = seed;
  return () => {
    s = (s * 1664525 + 1013904223) % 2 ** 32;
    return s / 2 ** 32;
  };
}

export async function seedPlayArea(
  worldId: string,
  centerLat: number,
  centerLng: number,
  config: GameConfig,
  seed = Date.now(),
): Promise<{ goodieHuts: number; resources: number }> {
  const random = rng(seed);
  let goodieHuts = 0;
  let resources = 0;

  const placed: Array<{ lat: number; lng: number }> = [];

  for (let i = 0; i < GOODIE_HUT_COUNT; i += 1) {
    const angle = random() * Math.PI * 2;
    const dist = 400 + random() * PLAY_AREA_RADIUS_M;
    const northM = Math.cos(angle) * dist;
    const eastM = Math.sin(angle) * dist;
    const { lat, lng } = offsetMeters(centerLat, centerLng, northM, eastM);

    const minSep = config.placement.minSeparationM.goodie_hut ?? 2000;
    const tooClose = placed.some(
      (p) => haversineM(lat, lng, p.lat, p.lng) < minSep,
    );
    if (tooClose) continue;

    const slug = `gh-${Math.floor(random() * 1_000_000).toString(36)}`;
    const name = `Goodie Hut ${String.fromCharCode(65 + (i % 26))}${i + 1}`;

    await query(
      `INSERT INTO settlements (
         id, world_id, slug, name, planet_display_name, terrestrial_label,
         tier, alignment, is_goodie_hut, lat, lng, geofence_radius_m, base_defense
       ) VALUES (?, ?, ?, ?, ?, ?, 'goodie_hut', 'neutral', 1, ?, ?, 150, 0)`,
      [
        newId(),
        worldId,
        slug,
        name,
        name,
        'Orin Junction sector',
        lat,
        lng,
      ],
    );

    placed.push({ lat, lng });
    goodieHuts += 1;
  }

  const tileSize = config.fogOfWar.tileSizeM;
  const gridSteps = Math.ceil(Math.sqrt(RESOURCE_NODE_COUNT));

  for (let gy = 0; gy < gridSteps && resources < RESOURCE_NODE_COUNT; gy += 1) {
    for (let gx = 0; gx < gridSteps && resources < RESOURCE_NODE_COUNT; gx += 1) {
      const northM = -PLAY_AREA_RADIUS_M + (gy / Math.max(1, gridSteps - 1)) * PLAY_AREA_RADIUS_M * 2;
      const eastM = -PLAY_AREA_RADIUS_M + (gx / Math.max(1, gridSteps - 1)) * PLAY_AREA_RADIUS_M * 2;
      const jitterN = (random() - 0.5) * 300;
      const jitterE = (random() - 0.5) * 300;
      const { lat, lng } = offsetMeters(
        centerLat,
        centerLng,
        northM + jitterN,
        eastM + jitterE,
      );

      if (haversineM(lat, lng, centerLat, centerLng) > PLAY_AREA_RADIUS_M) continue;

      const tileId = latLngToTileId(lat, lng, tileSize);
      const resourceId = ALIEN_RESOURCE_IDS[Math.floor(random() * ALIEN_RESOURCE_IDS.length)]!;
      const richnessRoll = random();
      const richness =
        richnessRoll < 0.15 ? 'sparse' : richnessRoll < 0.55 ? 'moderate' : 'rich';
      const mult = richness === 'sparse' ? 0.6 : richness === 'moderate' ? 1 : 1.6;
      const base =
        config.resources.depositYieldPerDay.min +
        random() *
          (config.resources.depositYieldPerDay.max - config.resources.depositYieldPerDay.min);
      const yieldPerDay = Math.max(2, Math.floor(base * mult));

      await insertResourceNode(worldId, tileId, resourceId, lat, lng, richness, yieldPerDay);
      resources += 1;
    }
  }

  return { goodieHuts, resources };
}
