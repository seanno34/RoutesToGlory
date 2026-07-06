/**
 * Seed settlements, goodie huts, and resources along drivable roads near spawn.
 */
import type { GameConfig, SettlementTier } from '@empire/shared';
import { ALIEN_RESOURCE_IDS, latLngToTileId } from '@empire/shared';
import { query, newId } from './client.js';
import { insertResourceNode } from './exploration-repo.js';
import {
  haversineM,
  sampleRoadPointsNear,
  type RoadPoint,
} from '../services/road-sampler.js';

const PLAY_RADIUS_M = 12_000;
const ROAD_SPACING_M = 450;

const TIER_GEOFENCE: Record<SettlementTier, number> = {
  goodie_hut: 150,
  settlement: 200,
  town: 250,
  city: 400,
  super_city: 600,
};

const TIER_DEFENSE: Record<SettlementTier, number> = {
  goodie_hut: 0,
  settlement: 10,
  town: 50,
  city: 100,
  super_city: 200,
};

/** Placement plan cycling along the road network away from spawn. */
const ROADSIDE_PLAN: SettlementTier[] = [
  'goodie_hut',
  'goodie_hut',
  'settlement',
  'goodie_hut',
  'goodie_hut',
  'town',
  'goodie_hut',
  'settlement',
  'goodie_hut',
  'goodie_hut',
  'town',
  'goodie_hut',
  'city',
  'goodie_hut',
  'settlement',
  'goodie_hut',
  'goodie_hut',
  'town',
  'goodie_hut',
  'settlement',
];

function rng(seed: number): () => number {
  let s = seed;
  return () => {
    s = (s * 1664525 + 1013904223) % 2 ** 32;
    return s / 2 ** 32;
  };
}

function echoName(tier: SettlementTier, index: number, distanceKm: number): string {
  if (tier === 'goodie_hut') {
    return `Goodie Hut R${index + 1}`;
  }
  const label = tier.replace('_', ' ');
  return `Echo Site ${label} · ${distanceKm.toFixed(1)} km`;
}

async function insertSettlement(
  worldId: string,
  point: RoadPoint,
  tier: SettlementTier,
  index: number,
  random: () => number,
): Promise<void> {
  const isGoodie = tier === 'goodie_hut';
  const slug = `road-${tier}-${index}-${Math.floor(random() * 1_000_000).toString(36)}`;
  const distKm = point.distanceFromOriginM / 1000;
  const name = echoName(tier, index, distKm);

  await query(
    `INSERT INTO settlements (
       id, world_id, slug, name, planet_display_name, terrestrial_label,
       tier, alignment, is_goodie_hut, lat, lng, geofence_radius_m, base_defense
     ) VALUES (?, ?, ?, ?, ?, ?, ?, 'neutral', ?, ?, ?, ?, ?)`,
    [
      newId(),
      worldId,
      slug,
      name,
      name,
      'Roadside route',
      tier,
      isGoodie ? 1 : 0,
      point.lat,
      point.lng,
      TIER_GEOFENCE[tier],
      TIER_DEFENSE[tier],
    ],
  );
}

export async function seedAlongRoads(
  worldId: string,
  spawnLat: number,
  spawnLng: number,
  config: GameConfig,
  seed = Date.now(),
): Promise<{ settlements: number; goodieHuts: number; resources: number; roadPoints: number }> {
  const random = rng(seed);

  let roadPoints = await sampleRoadPointsNear(spawnLat, spawnLng, {
    radiusM: PLAY_RADIUS_M,
    spacingM: ROAD_SPACING_M,
  });

  roadPoints = roadPoints.filter(
    (p) => haversineM(spawnLat, spawnLng, p.lat, p.lng) <= PLAY_RADIUS_M,
  );

  if (roadPoints.length < 8) {
    console.warn(
      'Road sampling returned few points — add MAPBOX_ACCESS_TOKEN to API .env for best results.',
    );
    return { settlements: 0, goodieHuts: 0, resources: 0, roadPoints: roadPoints.length };
  }

  roadPoints.sort((a, b) => a.distanceFromOriginM - b.distanceFromOriginM);

  const minGoodieSep = config.placement.minSeparationM.goodie_hut ?? 2000;
  const minSettlementSep = config.placement.minSeparationM.settlement ?? 3000;

  const usedPoints: RoadPoint[] = [];
  let goodieHuts = 0;
  let settlements = 0;
  let resources = 0;
  let planIndex = 0;
  let minDistanceCursor = 800;

  const pickPoint = (minSep: number): RoadPoint | null => {
    for (const p of roadPoints) {
      if (p.distanceFromOriginM < minDistanceCursor) continue;
      const tooClose = usedPoints.some(
        (u) => haversineM(u.lat, u.lng, p.lat, p.lng) < minSep,
      );
      if (!tooClose) return p;
    }
    return null;
  };

  for (let i = 0; i < ROADSIDE_PLAN.length; i += 1) {
    const tier = ROADSIDE_PLAN[planIndex % ROADSIDE_PLAN.length]!;
    planIndex += 1;
    const minSep = tier === 'goodie_hut' ? minGoodieSep : minSettlementSep;
    const point = pickPoint(minSep);
    if (!point) break;

    await insertSettlement(worldId, point, tier, i, random);
    usedPoints.push(point);
    minDistanceCursor = point.distanceFromOriginM + minSep * 0.5;
    settlements += 1;
    if (tier === 'goodie_hut') goodieHuts += 1;
  }

  const tileSize = config.fogOfWar.tileSizeM;
  const resourceMinSep = 350;

  for (const point of roadPoints) {
    if (resources >= 40) break;

    const nearUsed = usedPoints.some(
      (u) => haversineM(u.lat, u.lng, point.lat, point.lng) < resourceMinSep,
    );
    if (nearUsed) continue;

    const tileId = latLngToTileId(point.lat, point.lng, tileSize);
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

    await insertResourceNode(
      worldId,
      tileId,
      resourceId,
      point.lat,
      point.lng,
      richness,
      yieldPerDay,
    );
    usedPoints.push(point);
    resources += 1;
  }

  return {
    settlements,
    goodieHuts,
    resources,
    roadPoints: roadPoints.length,
  };
}
