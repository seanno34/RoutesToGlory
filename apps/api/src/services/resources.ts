import type {
  AlienResourceId,
  GameConfig,
  SettlementDeposits,
} from '@empire/shared';
import { ALIEN_RESOURCE_IDS } from '@empire/shared';

export type Richness = 'sparse' | 'moderate' | 'rich';

const RICHNESS_MULTIPLIER: Record<Richness, number> = {
  sparse: 0.6,
  moderate: 1,
  rich: 1.6,
};

/** Deterministic deposit seed from world + settlement ids. */
export function seedSettlementDeposits(
  config: GameConfig,
  _worldId: string,
  settlementId: string,
  rng: () => number = Math.random,
): SettlementDeposits {
  const maxDeposits = config.resources.maxDepositsPerSettlement;
  const depositCount = 1 + Math.floor(rng() * maxDeposits);
  const weights = config.resources.seedWeights;
  const picked = new Set<AlienResourceId>();
  const deposits: SettlementDeposits['deposits'] = [];

  while (deposits.length < depositCount && picked.size < ALIEN_RESOURCE_IDS.length) {
    const resourceId = weightedPick(weights, rng);
    if (picked.has(resourceId)) {
      continue;
    }

    picked.add(resourceId);
    const richness = pickRichness(rng);
    const base =
      config.resources.depositYieldPerDay.min +
      rng() *
        (config.resources.depositYieldPerDay.max -
          config.resources.depositYieldPerDay.min);
    const yieldPerDay = Math.max(
      1,
      Math.floor(base * RICHNESS_MULTIPLIER[richness]),
    );

    deposits.push({ resourceId, richness, yieldPerDay });
  }

  return { settlementId, deposits };
}

function weightedPick(
  weights: Partial<Record<AlienResourceId, number>>,
  rng: () => number,
): AlienResourceId {
  const entries = ALIEN_RESOURCE_IDS.map((id) => [id, weights[id] ?? 0] as const);
  const total = entries.reduce((sum, [, w]) => sum + w, 0);
  let roll = rng() * total;

  for (const [id, weight] of entries) {
    roll -= weight;
    if (roll <= 0) {
      return id;
    }
  }

  return entries[0]![0];
}

function pickRichness(rng: () => number): Richness {
  const roll = rng();
  if (roll < 0.25) return 'sparse';
  if (roll < 0.75) return 'moderate';
  return 'rich';
}

export function canAffordCost(
  stockpile: Record<AlienResourceId, number>,
  cost: Partial<Record<AlienResourceId, number>>,
): boolean {
  for (const [resourceId, amount] of Object.entries(cost)) {
    const available = stockpile[resourceId as AlienResourceId] ?? 0;
    if (available < (amount ?? 0)) {
      return false;
    }
  }

  return true;
}

export function deductCost(
  stockpile: Record<AlienResourceId, number>,
  cost: Partial<Record<AlienResourceId, number>>,
): Record<AlienResourceId, number> {
  const next = { ...stockpile };

  for (const [resourceId, amount] of Object.entries(cost)) {
    const id = resourceId as AlienResourceId;
    next[id] = Math.max(0, (next[id] ?? 0) - (amount ?? 0));
  }

  return next;
}

export function dailyHarvest(
  deposits: SettlementDeposits['deposits'],
): Partial<Record<AlienResourceId, number>> {
  const harvest: Partial<Record<AlienResourceId, number>> = {};

  for (const deposit of deposits) {
    harvest[deposit.resourceId] =
      (harvest[deposit.resourceId] ?? 0) + deposit.yieldPerDay;
  }

  return harvest;
}
