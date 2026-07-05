/**
 * Seed Echo Sites from real-world city anchors.
 * Population-weighted tier + alignment rolls; metro dedupe within cluster radius.
 */
import type { GameConfig, SettlementTier, Alignment } from '@empire/shared';
import { query, newId } from './client.js';
import { CITY_CATALOG } from '@empire/shared';

type PopBand = 'small' | 'medium' | 'large' | 'mega';

function popBand(population: number): PopBand {
  if (population >= 5_000_000) return 'mega';
  if (population >= 1_000_000) return 'large';
  if (population >= 100_000) return 'medium';
  return 'small';
}

function weightedRoll<T extends string>(
  weights: Record<T, number>,
  rng: () => number,
): T {
  const entries = Object.entries(weights) as [T, number][];
  const total = entries.reduce((s, [, w]) => s + w, 0);
  let roll = rng() * total;
  for (const [key, weight] of entries) {
    roll -= weight;
    if (roll <= 0) return key;
  }
  return entries[0]![0];
}

function rollTier(population: number, config: GameConfig, rng: () => number): SettlementTier {
  const band = popBand(population);
  const weights = config.seeding.typeWeightsByPopulation[band];
  if (!weights) return 'settlement';
  return weightedRoll(weights, rng) as SettlementTier;
}

function rollAlignment(config: GameConfig, rng: () => number): Alignment {
  return weightedRoll(config.seeding.alignmentWeights, rng) as Alignment;
}

function echoName(cityName: string, slug: string): string {
  const sector = slug.slice(0, 3).toUpperCase();
  return `Echo Site ${sector}-${cityName.split(' ')[0]?.toUpperCase() ?? 'X'}`;
}

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

export async function seedWorldSettlements(
  worldId: string,
  config: GameConfig,
  seed = Date.now(),
): Promise<number> {
  let rngState = seed;
  const rng = () => {
    rngState = (rngState * 1664525 + 1013904223) % 2 ** 32;
    return rngState / 2 ** 32;
  };

  const clusterRadius = config.placement.metroClusterRadiusM;
  const sorted = [...CITY_CATALOG].sort((a, b) => b.population - a.population);
  const placed: Array<{ lat: number; lng: number }> = [];
  let count = 0;

  for (const city of sorted) {
    const tooClose = placed.some(
      (p) => haversineM(city.lat, city.lng, p.lat, p.lng) < clusterRadius,
    );
    if (tooClose) continue;

    const tier = rollTier(city.population, config, rng);
    const alignment = rollAlignment(config, rng);
    const slug = city.slug;
    const name = echoName(city.name, slug);

    await query(
      `INSERT IGNORE INTO settlements (
         id, world_id, slug, name, planet_display_name, terrestrial_label,
         tier, alignment, is_goodie_hut, lat, lng, geofence_radius_m, base_defense
       ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      [
        newId(),
        worldId,
        slug,
        name,
        name,
        city.name,
        tier,
        alignment,
        tier === 'goodie_hut' ? 1 : 0,
        city.lat,
        city.lng,
        TIER_GEOFENCE[tier],
        TIER_DEFENSE[tier],
      ],
    );

    placed.push({ lat: city.lat, lng: city.lng });
    count += 1;
  }

  return count;
}
