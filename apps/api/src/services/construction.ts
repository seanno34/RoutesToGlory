import type {
  AlienResourceId,
  BuildComplexityTier,
  BuildJobRecord,
  BuildTargetType,
  GameConfig,
  QueueBuildRequest,
  RushBuildRequest,
  RouteModifierType,
  SettlementModifierType,
} from '@empire/shared';

export interface RushQuote {
  remainingSeconds: number;
  goldCost: number;
  xeniteDiscountApplied: number;
  canInstantComplete: boolean;
}

export interface BuildEstimate {
  durationSeconds: number;
  resourceCost: Partial<Record<AlienResourceId, number>>;
  complexityTier: BuildComplexityTier;
  tierLabel: string;
  unlocked: boolean;
  unlockWorldAgeDays: number;
}

function getComplexityTier(
  config: GameConfig,
  targetType: BuildTargetType,
  targetKey: string,
): BuildComplexityTier {
  switch (targetType) {
    case 'settlement_modifier':
      return (
        config.construction.settlementModifierComplexityTier[
          targetKey as SettlementModifierType
        ] ?? 1
      );
    case 'route_modifier':
      return config.routeModifiers.complexityTier[targetKey as RouteModifierType] ?? 1;
    case 'settlement_upgrade':
      return (
        config.construction.promotionComplexityTier[
          targetKey as 'town' | 'city' | 'super_city'
        ] ?? 1
      );
    default:
      return 3;
  }
}

function getTier1BaseSeconds(
  config: GameConfig,
  targetType: BuildTargetType,
  targetKey: string,
): number {
  switch (targetType) {
    case 'settlement_modifier':
      return (
        config.construction.settlementModifierTier1Seconds[
          targetKey as SettlementModifierType
        ] ?? 300
      );
    case 'route_modifier':
      return (
        config.routeModifiers.tier1BuildDurationSeconds[targetKey as RouteModifierType] ??
        180
      );
    case 'settlement_upgrade':
      return (
        config.construction.promotionProjectTier1Seconds[
          targetKey as 'town' | 'city' | 'super_city'
        ] ?? 600
      );
    default:
      return 600;
  }
}

function getTier1BaseCost(
  config: GameConfig,
  targetType: BuildTargetType,
  targetKey: string,
): Partial<Record<AlienResourceId, number>> {
  switch (targetType) {
    case 'settlement_modifier':
      return (
        config.construction.settlementModifierTier1Costs[
          targetKey as SettlementModifierType
        ] ?? { ferracite: 10 }
      );
    case 'route_modifier':
      return (
        config.routeModifiers.tier1ResourceCosts[targetKey as RouteModifierType] ?? {
          ferracite: 10,
        }
      );
    default:
      return { ferracite: 25, solari_dust: 10 };
  }
}

function scaleCost(
  cost: Partial<Record<AlienResourceId, number>>,
  multiplier: number,
): Partial<Record<AlienResourceId, number>> {
  const scaled: Partial<Record<AlienResourceId, number>> = {};
  for (const [key, value] of Object.entries(cost)) {
    scaled[key as AlienResourceId] = Math.max(1, Math.ceil((value ?? 0) * multiplier));
  }
  return scaled;
}

export function isBuildTierUnlocked(
  config: GameConfig,
  complexityTier: BuildComplexityTier,
  worldAgeDays: number,
): boolean {
  const profile = config.construction.complexityTierProfiles[complexityTier];
  return worldAgeDays >= (profile?.unlockWorldAgeDays ?? 0);
}

export function estimateBuild(
  config: GameConfig,
  targetType: BuildTargetType,
  targetKey: string,
  worldAgeDays: number,
  options?: { goodieHutBonus?: boolean },
): BuildEstimate {
  const complexityTier = getComplexityTier(config, targetType, targetKey);
  const profile = config.construction.complexityTierProfiles[complexityTier] ?? {
    label: 'Survey',
    timeMultiplier: 1,
    costMultiplier: 1,
    unlockWorldAgeDays: 0,
  };
  let durationSeconds = Math.ceil(
    getTier1BaseSeconds(config, targetType, targetKey) * profile.timeMultiplier,
  );

  if (options?.goodieHutBonus) {
    durationSeconds = Math.ceil(
      durationSeconds * config.construction.goodieHutBuildTimeMultiplier,
    );
  }

  return {
    durationSeconds,
    resourceCost: scaleCost(
      getTier1BaseCost(config, targetType, targetKey),
      profile.costMultiplier,
    ),
    complexityTier,
    tierLabel: profile.label,
    unlocked: isBuildTierUnlocked(config, complexityTier, worldAgeDays),
    unlockWorldAgeDays: profile.unlockWorldAgeDays,
  };
}

export function getBuildDurationSeconds(
  config: GameConfig,
  targetType: BuildTargetType,
  targetKey: string,
  options?: { goodieHutBonus?: boolean; worldAgeDays?: number },
): number {
  return estimateBuild(
    config,
    targetType,
    targetKey,
    options?.worldAgeDays ?? 999,
    options,
  ).durationSeconds;
}

export function getResourceCost(
  config: GameConfig,
  targetType: BuildTargetType,
  targetKey: string,
  worldAgeDays = 999,
): Partial<Record<AlienResourceId, number>> {
  return estimateBuild(config, targetType, targetKey, worldAgeDays).resourceCost;
}

export function queueBuildJob(
  config: GameConfig,
  request: QueueBuildRequest,
  options?: { goodieHutBonus?: boolean; now?: Date; worldAgeDays?: number },
): BuildJobRecord {
  const now = options?.now ?? new Date();
  const worldAgeDays = options?.worldAgeDays ?? 999;
  const estimate = estimateBuild(
    config,
    request.targetType,
    request.targetKey,
    worldAgeDays,
    options,
  );

  if (!estimate.unlocked) {
    throw new Error(
      `Build tier ${estimate.tierLabel} unlocks at world day ${estimate.unlockWorldAgeDays}`,
    );
  }

  const startedAt = now.toISOString();
  const completesAt = new Date(
    now.getTime() + estimate.durationSeconds * 1000,
  ).toISOString();

  return {
    id: crypto.randomUUID(),
    worldId: request.worldId,
    empireId: request.empireId,
    targetType: request.targetType,
    targetKey: request.targetKey,
    settlementId: request.settlementId,
    routeId: request.routeId,
    status: 'in_progress',
    durationSeconds: estimate.durationSeconds,
    startedAt,
    completesAt,
    resourceCost: estimate.resourceCost as Record<AlienResourceId, number>,
    goldRushed: 0,
    createdAt: startedAt,
  };
}

export function getRemainingSeconds(job: BuildJobRecord, now: Date = new Date()): number {
  if (job.status === 'completed' || job.status === 'cancelled') {
    return 0;
  }

  if (!job.completesAt) {
    return job.durationSeconds;
  }

  const completesMs = new Date(job.completesAt).getTime();
  return Math.max(0, Math.ceil((completesMs - now.getTime()) / 1000));
}

export function quoteRushCost(
  config: GameConfig,
  job: BuildJobRecord,
  options?: {
    rushSeconds?: number;
    xeniteToSpend?: number;
    now?: Date;
  },
): RushQuote {
  const now = options?.now ?? new Date();
  const remaining = getRemainingSeconds(job, now);
  const rushSeconds = Math.min(options?.rushSeconds ?? remaining, remaining);
  const minutes = rushSeconds / 60;
  const { goldPerMinute, maxInstantCompleteGold, xeniteDiscountPerUnit } =
    config.construction.rush;

  let goldCost = Math.ceil(minutes * goldPerMinute);
  const xeniteSpent = options?.xeniteToSpend ?? 0;
  const xeniteDiscountApplied = Math.floor(
    goldCost * Math.min(0.5, xeniteSpent * xeniteDiscountPerUnit),
  );
  goldCost = Math.max(0, goldCost - xeniteDiscountApplied);

  if (options?.rushSeconds === undefined && rushSeconds === remaining) {
    goldCost = Math.min(goldCost, maxInstantCompleteGold);
  }

  return {
    remainingSeconds: remaining,
    goldCost,
    xeniteDiscountApplied,
    canInstantComplete: goldCost <= maxInstantCompleteGold,
  };
}

export function applyRush(
  config: GameConfig,
  job: BuildJobRecord,
  request: RushBuildRequest,
  now: Date = new Date(),
): BuildJobRecord {
  const quote = quoteRushCost(config, job, {
    rushSeconds: request.rushSeconds,
    xeniteToSpend: request.xeniteToSpend,
    now,
  });

  const skipSeconds = request.rushSeconds ?? quote.remainingSeconds;

  if (skipSeconds <= 0) {
    return { ...job, status: 'completed', completesAt: now.toISOString() };
  }

  const remaining = getRemainingSeconds(job, now);
  const newRemaining = Math.max(0, remaining - skipSeconds);
  const completesAt = new Date(now.getTime() + newRemaining * 1000).toISOString();

  return {
    ...job,
    completesAt,
    goldRushed: job.goldRushed + quote.goldCost,
    status: newRemaining <= 0 ? 'completed' : 'in_progress',
  };
}

export function tickBuildJobs(
  jobs: BuildJobRecord[],
  now: Date = new Date(),
): BuildJobRecord[] {
  return jobs.map((job) => {
    if (job.status !== 'in_progress') {
      return job;
    }

    if (getRemainingSeconds(job, now) <= 0) {
      return { ...job, status: 'completed' };
    }

    return job;
  });
}

export function routesBuildInstantly(config: GameConfig): boolean {
  return config.routes.buildsInstantly;
}
