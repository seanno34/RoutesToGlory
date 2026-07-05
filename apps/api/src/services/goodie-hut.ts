import type {
  AlienReward,
  BuildJobRecord,
  GameConfig,
  GoodieHutConnectRequest,
  GoodieHutResolution,
  QueueBuildRequest,
  SettlementModifierType,
} from '@empire/shared';
import {
  ALIEN_CIVIC_UNITS,
  ALIEN_MILITARY_UNITS,
  ALIEN_TECH_UNLOCKS,
  findUpgradeCandidate,
  pickRandomUnit,
} from '@empire/shared';
import { queueBuildJob } from './construction.js';

export interface GoodieHutContext {
  settlementId: string;
  empireId: string;
  worldId: string;
  ownedUnitIds: string[];
  rng?: () => number;
}

export function resolveGoodieHutConnect(
  config: GameConfig,
  request: GoodieHutConnectRequest,
  context: GoodieHutContext,
): GoodieHutResolution {
  if (request.choice === 'found_town') {
    return resolveFoundTown(config, request, context);
  }

  return resolveClaimReward(config, request, context);
}

function resolveFoundTown(
  config: GameConfig,
  request: GoodieHutConnectRequest,
  context: GoodieHutContext,
): GoodieHutResolution {
  const { foundTown } = config.goodieHut;
  const modifierTypes: SettlementModifierType[] = foundTown.grantAllSettlementModifiers
    ? [...foundTown.settlementModifiers]
    : [];

  const queuedBuildJobs: BuildJobRecord[] = modifierTypes.map((targetKey) =>
    queueBuildJob(
      config,
      {
        worldId: context.worldId,
        empireId: request.empireId,
        targetType: 'settlement_modifier',
        targetKey,
        settlementId: context.settlementId,
      } satisfies QueueBuildRequest,
      { goodieHutBonus: true },
    ),
  );

  return {
    choice: 'found_town',
    settlementId: context.settlementId,
    tier: foundTown.targetTier,
    bonusPopulation: foundTown.bonusPopulation,
    queuedModifierTypes: modifierTypes,
    queuedBuildJobs,
  };
}

function resolveClaimReward(
  config: GameConfig,
  request: GoodieHutConnectRequest,
  context: GoodieHutContext,
): GoodieHutResolution {
  const rng = context.rng ?? Math.random;
  const reward = request.rewardType
    ? buildRewardByType(config, request.rewardType, context, rng)
    : rollRandomReward(config, context, rng);

  return {
    choice: 'claim_reward',
    reward,
    hutStatus: 'claimed_ruins',
  };
}

function rollRandomReward(
  config: GameConfig,
  context: GoodieHutContext,
  rng: () => number,
): AlienReward {
  const roll = rng() * 100;
  const { techUnlockChancePct, unitRewardChancePct } = config.goodieHut.claimReward;

  if (roll < unitRewardChancePct) {
    return buildUnitReward(config, context, rng);
  }

  if (roll < unitRewardChancePct + techUnlockChancePct) {
    return buildTechReward(rng);
  }

  return buildGoldReward(config, rng);
}

function buildRewardByType(
  config: GameConfig,
  type: 'gold' | 'tech' | 'unit',
  context: GoodieHutContext,
  rng: () => number,
) {
  switch (type) {
    case 'gold':
      return buildGoldReward(config, rng);
    case 'tech':
      return buildTechReward(rng);
    case 'unit':
      return buildUnitReward(config, context, rng);
  }
}

function buildGoldReward(config: GameConfig, rng: () => number) {
  const { goldMin, goldMax } = config.goodieHut.claimReward;
  const amount = Math.floor(goldMin + rng() * (goldMax - goldMin + 1));
  return { type: 'gold' as const, amount };
}

function buildTechReward(rng: () => number) {
  const index = Math.floor(rng() * ALIEN_TECH_UNLOCKS.length);
  const tech = ALIEN_TECH_UNLOCKS[index] ?? ALIEN_TECH_UNLOCKS[0]!;
  return { type: 'tech' as const, techId: tech.id, name: tech.name };
}

function buildUnitReward(
  config: GameConfig,
  context: GoodieHutContext,
  rng: () => number,
) {
  const { upgradeExistingUnitChancePct } = config.goodieHut.claimReward;
  const preferMilitary = rng() > 0.45;
  const pool = preferMilitary ? ALIEN_MILITARY_UNITS : ALIEN_CIVIC_UNITS;

  const upgrade =
    rng() * 100 < upgradeExistingUnitChancePct
      ? findUpgradeCandidate(context.ownedUnitIds, pool)
      : undefined;

  const eligible = pool.filter(
    (candidate) =>
      !candidate.upgradeOf || context.ownedUnitIds.includes(candidate.upgradeOf),
  );
  const unit = upgrade ?? pickRandomUnit(eligible.length > 0 ? eligible : pool, rng);

  return {
    type: 'unit' as const,
    unit,
    upgraded: upgrade !== undefined,
  };
}
