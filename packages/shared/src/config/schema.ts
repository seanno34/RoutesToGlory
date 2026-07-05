import { z } from 'zod';
import {
  AlignmentSchema,
  DiplomacyStatusSchema,
  GoodieHutChoiceSchema,
  GrowthCategorySchema,
  NpcDifficultySchema,
  NpcHostilityPhaseSchema,
  RewardTypeSchema,
  RouteModifierTypeSchema,
  SettlementModifierTypeSchema,
  SettlementTierSchema,
  UnitKindSchema,
  AlienResourceIdSchema,
  BuildTargetTypeSchema,
  BuildJobStatusSchema,
  BuildComplexityTierSchema,
} from '../types/enums.js';

/**
 * All tunable game values live here. Defaults are version-controlled;
 * God mode applies runtime overrides on top via dot-path keys.
 */
export const GameConfigSchema = z.object({
  meta: z.object({
    configVersion: z.number().int().positive(),
  }),

  sampling: z.object({
    pollIntervalMs: z.number().int().positive(),
    minDistanceM: z.number().positive(),
    maxIntervalS: z.number().positive(),
    maxAccuracyM: z.number().positive(),
    nearSettlementHighAccuracyM: z.number().positive(),
    settlementConfirmSamples: z.number().int().positive(),
    settlementConfirmWindowS: z.number().positive(),
    maxRouteGapM: z.number().positive(),
    maxSpeedMps: z.number().positive(),
  }),

  placement: z.object({
    /** Hard minimum center-to-center separation in meters, keyed by tier pair. */
    minSeparationM: z.record(SettlementTierSchema, z.number().positive()),
    metroClusterRadiusM: z.number().positive(),
    influenceRadiusM: z.record(SettlementTierSchema, z.number().positive()),
  }),

  routes: z.object({
    /** Routes are recorded instantly while the player moves — no build queue. */
    buildsInstantly: z.literal(true),
    minConnectDistanceM: z.number().positive(),
    minConnectByTierM: z.record(SettlementTierSchema, z.number().positive()),
    maxUnmodifiedRangeM: z.record(SettlementTierSchema, z.number().positive()),
    idealMinDistanceM: z.number().positive(),
    idealMaxDistanceM: z.number().positive(),
    shortHopPenaltyYieldPct: z.number().min(0).max(100),
    longHaulPenaltyYieldPct: z.number().min(0).max(100),
    modifierSlotsPer15Km: z.number().int().positive(),
    maxModifierSlots: z.number().int().positive(),
    categoryCapPerRoute: z.number().int().positive(),
  }),

  routeModifiers: z.object({
    incompatibilityGroups: z.record(z.string(), z.array(RouteModifierTypeSchema)),
    pairwiseBans: z.array(z.tuple([RouteModifierTypeSchema, RouteModifierTypeSchema])),
    /** Tier-1 base build seconds; scaled by complexity tier profile. */
    tier1BuildDurationSeconds: z.record(RouteModifierTypeSchema, z.number().int().positive()),
    /** Tier-1 base resource costs; scaled by complexity tier profile. */
    tier1ResourceCosts: z.record(
      RouteModifierTypeSchema,
      z.record(AlienResourceIdSchema, z.number().int().nonnegative()),
    ),
    complexityTier: z.record(RouteModifierTypeSchema, BuildComplexityTierSchema),
  }),

  /** All infrastructure except live GPS routes uses real-time construction. */
  construction: z.object({
    /**
     * Complexity tier profiles — tier 1 is jumpstart-fast; tier 5 is endgame.
     * unlockWorldAgeDays gates when new modifier tiers become buildable.
     */
    complexityTierProfiles: z.record(
      BuildComplexityTierSchema,
      z.object({
        label: z.string(),
        timeMultiplier: z.number().positive(),
        costMultiplier: z.number().positive(),
        unlockWorldAgeDays: z.number().int().nonnegative(),
      }),
    ),
    settlementModifierTier1Seconds: z.record(
      SettlementModifierTypeSchema,
      z.number().int().positive(),
    ),
    settlementModifierTier1Costs: z.record(
      SettlementModifierTypeSchema,
      z.record(AlienResourceIdSchema, z.number().int().nonnegative()),
    ),
    settlementModifierComplexityTier: z.record(
      SettlementModifierTypeSchema,
      BuildComplexityTierSchema,
    ),
    promotionProjectTier1Seconds: z.record(
      z.enum(['town', 'city', 'super_city']),
      z.number().int().positive(),
    ),
    promotionComplexityTier: z.record(
      z.enum(['town', 'city', 'super_city']),
      BuildComplexityTierSchema,
    ),
    /** Gold rush: ceil(remainingMinutes * goldPerMinute * targetMultiplier) */
    rush: z.object({
      goldPerMinute: z.number().positive(),
      minRushSeconds: z.number().int().positive(),
      maxInstantCompleteGold: z.number().int().positive(),
      /** Xenite spent grants this fraction off rush gold cost */
      xeniteDiscountPerUnit: z.number().min(0).max(1),
    }),
    /** Goodie-hut found-town modifiers enter queue at this fraction of normal time */
    goodieHutBuildTimeMultiplier: z.number().min(0).max(1),
    /** Ferracite stockpile build-time reduction cap */
    maxBuildTimeReductionPct: z.number().min(0).max(100),
  }),

  resources: z.object({
    /** How many resource types can appear at one settlement (1–3 typical) */
    maxDepositsPerSettlement: z.number().int().min(1).max(5),
    /** Min/max yield per day from an active deposit */
    depositYieldPerDay: z.object({
      min: z.number().int().positive(),
      max: z.number().int().positive(),
    }),
    /** Weighted chance each resource appears in world seed */
    seedWeights: z.record(AlienResourceIdSchema, z.number().nonnegative()),
    /** Stockpile bonus scales per N units */
    stockpileBonusScaleUnits: z.number().int().positive(),
    /** Higher tiers unlock as world age increases (matches exploration) */
    discoveryTierUnlockDays: z.record(z.enum(['1', '2', '3']), z.number().int().nonnegative()),
  }),

  fogOfWar: z.object({
    tileSizeM: z.number().positive(),
    /** GPS reveal radius during route sessions */
    revealRadiusM: z.number().positive(),
    /** Starting vision around empire spawn / first settlement */
    startingVisionRadiusM: z.number().positive(),
    /** Voidglass stockpile adds to reveal radius (m per 100 units) */
    voidglassRevealBonusPer100: z.number().positive(),
    unexploredOpacity: z.number().min(0).max(1),
    exploredOpacity: z.number().min(0).max(1),
    /** How long resource icons shimmer on first tile reveal (ms) */
    resourceShimmerDurationMs: z.number().int().positive(),
    /** Chance an unexplored tile contains a map resource node when first revealed */
    resourceNodeChanceOnReveal: z.number().min(0).max(1),
  }),

  growth: z.object({
    promotionThresholds: z.record(
      SettlementTierSchema,
      z.object({
        growthPoints: z.number().int().nonnegative(),
        minCategoriesAtLevel: z.number().int().nonnegative(),
        minCategoryValue: z.number().int().nonnegative(),
        minActiveRoutes: z.number().int().nonnegative(),
      }).optional(),
    ),
    suburbMaxDistanceM: z.number().positive(),
    mergeMaxDistanceM: z.number().positive(),
    settlementModifierSlots: z.record(SettlementTierSchema, z.number().int().nonnegative()),
    superCityWorldCap: z.number().int().nonnegative(),
    /** Per-empire cap; scales with player count in world. */
    superCityPerEmpireCap: z.object({
      base: z.number().int().nonnegative(),
      perOpponent: z.number().int().nonnegative(),
    }),
  }),

  goodieHut: z.object({
    /** Option A: convert directly to town with starter package. */
    foundTown: z.object({
      targetTier: z.literal('town'),
      bonusPopulation: z.number().int().positive(),
      grantAllSettlementModifiers: z.boolean(),
      settlementModifiers: z.array(SettlementModifierTypeSchema),
    }),
    /** Option B: one-time reward; hut removed or becomes ruins. */
    claimReward: z.object({
      goldMin: z.number().int().nonnegative(),
      goldMax: z.number().int().nonnegative(),
      techUnlockChancePct: z.number().min(0).max(100),
      unitRewardChancePct: z.number().min(0).max(100),
      upgradeExistingUnitChancePct: z.number().min(0).max(100),
    }),
  }),

  npcAlienEmpire: z.object({
    /** Exactly one major hostile NPC per world. */
    perWorldCount: z.literal(1),
    difficultyGrowthMultiplier: z.record(NpcDifficultySchema, z.number().positive()),
    /** Game days between NPC growth ticks at normal difficulty. */
    growthTickIntervalHours: z.number().positive(),
    /** Hostility phase thresholds by world age in days. */
    hostilityPhaseDays: z.record(NpcHostilityPhaseSchema, z.number().int().nonnegative()),
    /** Multiplier applied to player growth rates when comparing NPC pace. */
    baseGrowthRatePerTick: z.number().positive(),
    canBeAllied: z.literal(false),
    /** Players can always break or form alliances with each other. */
    playerAlliancesOpen: z.literal(true),
  }),

  combat: z.object({
    basePower: z.number().int().positive(),
    distancePowerPerKm: z.number().positive(),
    tierDefenseBonus: z.record(SettlementTierSchema, z.number().int().nonnegative()),
    rngMin: z.number().positive(),
    rngMax: z.number().positive(),
    winThreshold: z.number().positive(),
    defeatCooldownHours: z.number().positive(),
    debilitatedYieldPct: z.number().int().min(0).max(100),
  }),

  seeding: z.object({
    typeWeightsByPopulation: z.record(
      z.enum(['small', 'medium', 'large', 'mega']),
      z.object({
        goodie_hut: z.number().nonnegative(),
        settlement: z.number().nonnegative(),
        town: z.number().nonnegative(),
        city: z.number().nonnegative(),
        super_city: z.number().nonnegative(),
      }),
    ),
    alignmentWeights: z.object({
      friendly: z.number().nonnegative(),
      neutral: z.number().nonnegative(),
      hostile: z.number().nonnegative(),
      alien_enclave: z.number().nonnegative(),
    }),
  }),

  godMode: z.object({
    enabled: z.boolean(),
    /** Required header value when enabled (dev/test only). */
    secretHeader: z.string().min(1),
    persistOverridesToDisk: z.boolean(),
  }),
});

export type GameConfig = z.infer<typeof GameConfigSchema>;

/** Partial deep override tree — God mode sends dot-path patches against this shape. */
export type GameConfigPatch = Partial<{
  [K in keyof GameConfig]: Partial<GameConfig[K]>;
}>;

export const GoodieHutConnectRequestSchema = z.object({
  sessionId: z.string().uuid(),
  worldId: z.string().uuid(),
  settlementId: z.string().uuid(),
  empireId: z.string().uuid(),
  choice: GoodieHutChoiceSchema,
  rewardType: RewardTypeSchema.optional(),
});

export type GoodieHutConnectRequest = z.infer<typeof GoodieHutConnectRequestSchema>;

export const AllianceUpdateSchema = z.object({
  worldId: z.string().uuid(),
  empireAId: z.string().uuid(),
  empireBId: z.string().uuid(),
  status: DiplomacyStatusSchema,
});

export type AllianceUpdate = z.infer<typeof AllianceUpdateSchema>;

export const GodModePatchSchema = z.object({
  path: z.string().min(1),
  value: z.unknown(),
});

export type GodModePatch = z.infer<typeof GodModePatchSchema>;

export const UnitDefinitionSchema = z.object({
  id: z.string(),
  name: z.string(),
  kind: UnitKindSchema,
  basePower: z.number().int().positive(),
  alienOrigin: z.boolean(),
  upgradeOf: z.string().optional(),
});

export type UnitDefinition = z.infer<typeof UnitDefinitionSchema>;

export const AlienRewardSchema = z.discriminatedUnion('type', [
  z.object({
    type: z.literal('gold'),
    amount: z.number().int().positive(),
  }),
  z.object({
    type: z.literal('tech'),
    techId: z.string(),
    name: z.string(),
  }),
  z.object({
    type: z.literal('unit'),
    unit: UnitDefinitionSchema,
    upgraded: z.boolean(),
  }),
]);

export type AlienReward = z.infer<typeof AlienRewardSchema>;

export const BuildJobRecordSchema = z.object({
  id: z.string().uuid(),
  worldId: z.string().uuid(),
  empireId: z.string().uuid(),
  targetType: BuildTargetTypeSchema,
  targetKey: z.string(),
  settlementId: z.string().uuid().optional(),
  routeId: z.string().uuid().optional(),
  status: BuildJobStatusSchema,
  durationSeconds: z.number().int().positive(),
  startedAt: z.string().datetime().optional(),
  completesAt: z.string().datetime().optional(),
  resourceCost: z.record(AlienResourceIdSchema, z.number().int().nonnegative()),
  goldRushed: z.number().int().nonnegative(),
  createdAt: z.string().datetime(),
});

export type BuildJobRecord = z.infer<typeof BuildJobRecordSchema>;

export const GoodieHutResolutionSchema = z.discriminatedUnion('choice', [
  z.object({
    choice: z.literal('found_town'),
    settlementId: z.string().uuid(),
    tier: z.literal('town'),
    bonusPopulation: z.number().int(),
    /** Modifier types queued — not active until build jobs complete */
    queuedModifierTypes: z.array(SettlementModifierTypeSchema),
    queuedBuildJobs: z.array(BuildJobRecordSchema),
  }),
  z.object({
    choice: z.literal('claim_reward'),
    reward: AlienRewardSchema,
    hutStatus: z.enum(['claimed_ruins', 'removed']),
  }),
]);

export type GoodieHutResolution = z.infer<typeof GoodieHutResolutionSchema>;

export const NpcEmpireStateSchema = z.object({
  worldId: z.string().uuid(),
  empireId: z.string().uuid(),
  name: z.string(),
  difficulty: NpcDifficultySchema,
  growthPoints: z.number().int().nonnegative(),
  territoryCount: z.number().int().nonnegative(),
  hostilityPhase: NpcHostilityPhaseSchema,
  worldAgeDays: z.number().nonnegative(),
  lastTickAt: z.string().datetime(),
});

export type NpcEmpireState = z.infer<typeof NpcEmpireStateSchema>;

export const WorldDiplomacySchema = z.object({
  worldId: z.string().uuid(),
  /** empire pair key: sorted uuid uuid */
  relations: z.array(
    z.object({
      empireAId: z.string().uuid(),
      empireBId: z.string().uuid(),
      status: DiplomacyStatusSchema,
      updatedAt: z.string().datetime(),
    }),
  ),
});

export type WorldDiplomacy = z.infer<typeof WorldDiplomacySchema>;

export const QueueBuildRequestSchema = z.object({
  worldId: z.string().uuid(),
  empireId: z.string().uuid(),
  targetType: BuildTargetTypeSchema,
  targetKey: z.string(),
  settlementId: z.string().uuid().optional(),
  routeId: z.string().uuid().optional(),
});

export type QueueBuildRequest = z.infer<typeof QueueBuildRequestSchema>;

export const RushBuildRequestSchema = z.object({
  jobId: z.string().uuid(),
  empireId: z.string().uuid(),
  /** Seconds to skip (or omit for instant complete) */
  rushSeconds: z.number().int().positive().optional(),
  /** Optional xenite to reduce gold cost */
  xeniteToSpend: z.number().int().nonnegative().optional(),
});

export type RushBuildRequest = z.infer<typeof RushBuildRequestSchema>;

export const SettlementDepositsSchema = z.object({
  settlementId: z.string().uuid(),
  deposits: z.array(
    z.object({
      resourceId: AlienResourceIdSchema,
      richness: z.enum(['sparse', 'moderate', 'rich']),
      yieldPerDay: z.number().int().positive(),
    }),
  ),
});

export type SettlementDeposits = z.infer<typeof SettlementDepositsSchema>;

export const MapResourceNodeSchema = z.object({
  id: z.string().uuid(),
  worldId: z.string().uuid(),
  tileId: z.string(),
  resourceId: AlienResourceIdSchema,
  lat: z.number(),
  lng: z.number(),
  richness: z.enum(['sparse', 'moderate', 'rich']),
  yieldPerDay: z.number().int().positive(),
  /** Sprite id for embedded map icon */
  iconSpriteId: z.string(),
  glowColor: z.string(),
});

export type MapResourceNode = z.infer<typeof MapResourceNodeSchema>;

export const RevealExplorationRequestSchema = z.object({
  worldId: z.string().uuid(),
  empireId: z.string().uuid(),
  lat: z.number(),
  lng: z.number(),
  radiusM: z.number().positive().optional(),
});

export type RevealExplorationRequest = z.infer<typeof RevealExplorationRequestSchema>;

export const ExplorationMapStateSchema = z.object({
  worldId: z.string().uuid(),
  empireId: z.string().uuid(),
  exploredTileIds: z.array(z.string()),
  resourceNodes: z.array(
    MapResourceNodeSchema.extend({
      shimmer: z.boolean(),
      shimmerUntil: z.string().datetime().optional(),
    }),
  ),
  newlyRevealedTileIds: z.array(z.string()),
  newlyDiscoveredResourceIds: z.array(z.string().uuid()),
});

export type ExplorationMapState = z.infer<typeof ExplorationMapStateSchema>;
