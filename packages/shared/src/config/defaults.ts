import type { GameConfig } from './schema.js';

/** Version-controlled baseline — tweak here or via God mode at runtime. */
export const DEFAULT_GAME_CONFIG: GameConfig = {
  meta: {
    configVersion: 3,
  },

  sampling: {
    pollIntervalMs: 3_000,
    minDistanceM: 100,
    maxIntervalS: 180,
    maxAccuracyM: 100,
    nearSettlementHighAccuracyM: 500,
    settlementConfirmSamples: 2,
    settlementConfirmWindowS: 120,
    maxRouteGapM: 500,
    maxSpeedMps: 55,
  },

  placement: {
    minSeparationM: {
      goodie_hut: 2_000,
      settlement: 3_000,
      town: 8_000,
      city: 25_000,
      super_city: 80_000,
    },
    metroClusterRadiusM: 50_000,
    influenceRadiusM: {
      goodie_hut: 3_000,
      settlement: 6_000,
      town: 12_000,
      city: 25_000,
      super_city: 50_000,
    },
  },

  routes: {
    buildsInstantly: true,
    minConnectDistanceM: 1_000,
    minConnectByTierM: {
      goodie_hut: 500,
      settlement: 1_000,
      town: 1_000,
      city: 3_000,
      super_city: 8_000,
    },
    maxUnmodifiedRangeM: {
      goodie_hut: 15_000,
      settlement: 15_000,
      town: 30_000,
      city: 60_000,
      super_city: 120_000,
    },
    idealMinDistanceM: 2_000,
    idealMaxDistanceM: 40_000,
    shortHopPenaltyYieldPct: 50,
    longHaulPenaltyYieldPct: 25,
    modifierSlotsPer15Km: 1,
    maxModifierSlots: 6,
    categoryCapPerRoute: 2,
  },

  routeModifiers: {
    incompatibilityGroups: {
      speed: ['refill_station', 'booster_pad', 'hover_lane'],
      civic_yield: ['harmonic_relay', 'tariff_gate', 'free_passage'],
      intel: ['scanner_array'],
      alien: ['alien_waystation', 'phase_stabilizer'],
    },
    pairwiseBans: [
      ['hover_lane', 'guard_post'],
      ['tariff_gate', 'free_passage'],
      ['booster_pad', 'refill_station'],
    ],
    tier1BuildDurationSeconds: {
      refill_station: 90,
      guard_post: 180,
      relay_beacon: 240,
      booster_pad: 300,
      hover_lane: 420,
      scanner_array: 300,
      harmonic_relay: 240,
      tariff_gate: 120,
      free_passage: 120,
      alien_waystation: 600,
      phase_stabilizer: 900,
    },
    tier1ResourceCosts: {
      refill_station: { ferracite: 8, xenite: 10 },
      guard_post: { ferracite: 15, aegis_bark: 5 },
      relay_beacon: { ferracite: 10, quantium_shard: 5 },
      booster_pad: { ferracite: 8, xenite: 15 },
      hover_lane: { ferracite: 12, xenite: 20, quantium_shard: 8 },
      scanner_array: { ferracite: 6, quantium_shard: 12, voidglass: 5 },
      harmonic_relay: { ferracite: 8, lumin_spring: 15 },
      tariff_gate: { ferracite: 10, solari_dust: 10 },
      free_passage: { ferracite: 6, lumin_spring: 8 },
      alien_waystation: { ferracite: 10, quantium_shard: 15, mycelium_core: 8 },
      phase_stabilizer: { ferracite: 12, quantium_shard: 20, xenite: 15, chrono_moss: 10 },
    },
    complexityTier: {
      refill_station: 1,
      guard_post: 2,
      relay_beacon: 2,
      booster_pad: 3,
      hover_lane: 4,
      scanner_array: 3,
      harmonic_relay: 2,
      tariff_gate: 1,
      free_passage: 1,
      alien_waystation: 4,
      phase_stabilizer: 5,
    },
  },

  construction: {
    complexityTierProfiles: {
      1: { label: 'Survey', timeMultiplier: 1, costMultiplier: 0.5, unlockWorldAgeDays: 0 },
      2: { label: 'Outpost', timeMultiplier: 6, costMultiplier: 0.75, unlockWorldAgeDays: 2 },
      3: { label: 'Colony', timeMultiplier: 24, costMultiplier: 1, unlockWorldAgeDays: 5 },
      4: { label: 'Dominion', timeMultiplier: 96, costMultiplier: 1.5, unlockWorldAgeDays: 10 },
      5: { label: 'Ascendant', timeMultiplier: 384, costMultiplier: 2.5, unlockWorldAgeDays: 18 },
    },
    settlementModifierTier1Seconds: {
      trade_depot: 120,
      academy_node: 300,
      garrison: 180,
      consensus_hall: 240,
      fusion_core: 600,
    },
    settlementModifierTier1Costs: {
      trade_depot: { ferracite: 12, solari_dust: 8 },
      academy_node: { ferracite: 10, quantium_shard: 12 },
      garrison: { ferracite: 18, aegis_bark: 6 },
      consensus_hall: { ferracite: 10, lumin_spring: 12, mycelium_core: 4 },
      fusion_core: { ferracite: 20, xenite: 15, quantium_shard: 10, chrono_moss: 8 },
    },
    settlementModifierComplexityTier: {
      trade_depot: 1,
      garrison: 1,
      consensus_hall: 2,
      academy_node: 3,
      fusion_core: 5,
    },
    promotionProjectTier1Seconds: {
      town: 600,
      city: 3_600,
      super_city: 10_800,
    },
    promotionComplexityTier: {
      town: 1,
      city: 3,
      super_city: 5,
    },
    rush: {
      goldPerMinute: 3,
      minRushSeconds: 60,
      maxInstantCompleteGold: 5_000,
      xeniteDiscountPerUnit: 0.02,
    },
    goodieHutBuildTimeMultiplier: 0.5,
    maxBuildTimeReductionPct: 25,
  },

  resources: {
    maxDepositsPerSettlement: 2,
    depositYieldPerDay: { min: 5, max: 40 },
    seedWeights: {
      xenite: 18,
      solari_dust: 20,
      ferracite: 22,
      lumin_spring: 14,
      quantium_shard: 8,
      voidglass: 6,
      mycelium_core: 5,
      chrono_moss: 3,
      aegis_bark: 3,
      nebula_pearl: 1,
    },
    stockpileBonusScaleUnits: 100,
    discoveryTierUnlockDays: {
      '1': 0,
      '2': 3,
      '3': 10,
    },
  },

  fogOfWar: {
    tileSizeM: 400,
    revealRadiusM: 250,
    startingVisionRadiusM: 2_000,
    voidglassRevealBonusPer100: 25,
    unexploredOpacity: 0.92,
    exploredOpacity: 0,
    resourceShimmerDurationMs: 8_000,
    resourceNodeChanceOnReveal: 0.12,
  },

  growth: {
    promotionThresholds: {
      town: {
        growthPoints: 150,
        minCategoriesAtLevel: 2,
        minCategoryValue: 25,
        minActiveRoutes: 1,
      },
      city: {
        growthPoints: 400,
        minCategoriesAtLevel: 3,
        minCategoryValue: 40,
        minActiveRoutes: 2,
      },
      super_city: {
        growthPoints: 850,
        minCategoriesAtLevel: 4,
        minCategoryValue: 50,
        minActiveRoutes: 1,
      },
    },
    suburbMaxDistanceM: 8_000,
    mergeMaxDistanceM: 10_000,
    settlementModifierSlots: {
      goodie_hut: 0,
      settlement: 4,
      town: 6,
      city: 8,
      super_city: 10,
    },
    superCityWorldCap: 24,
    superCityPerEmpireCap: {
      /** 1v1 → 6 super cities each (base 6 + 0 opponents). */
      base: 6,
      perOpponent: 0,
    },
  },

  goodieHut: {
    foundTown: {
      targetTier: 'town',
      bonusPopulation: 500,
      grantAllSettlementModifiers: true,
      settlementModifiers: [
        'trade_depot',
        'academy_node',
        'garrison',
        'consensus_hall',
      ],
    },
    claimReward: {
      goldMin: 800,
      goldMax: 2_500,
      techUnlockChancePct: 35,
      unitRewardChancePct: 45,
      upgradeExistingUnitChancePct: 40,
    },
  },

  npcAlienEmpire: {
    perWorldCount: 1,
    difficultyGrowthMultiplier: {
      slow: 0.65,
      normal: 1.0,
      fast: 1.45,
    },
    growthTickIntervalHours: 6,
    hostilityPhaseDays: {
      dormant: 0,
      observing: 3,
      probing: 7,
      raiding: 14,
      all_out_war: 21,
    },
    baseGrowthRatePerTick: 12,
    canBeAllied: false,
    playerAlliancesOpen: true,
  },

  combat: {
    basePower: 100,
    distancePowerPerKm: 5,
    tierDefenseBonus: {
      goodie_hut: 0,
      settlement: 10,
      town: 50,
      city: 100,
      super_city: 200,
    },
    rngMin: 0.85,
    rngMax: 1.15,
    winThreshold: 1.05,
    defeatCooldownHours: 24,
    debilitatedYieldPct: 50,
  },

  seeding: {
    typeWeightsByPopulation: {
      small: {
        goodie_hut: 25,
        settlement: 45,
        town: 25,
        city: 4,
        super_city: 1,
      },
      medium: {
        goodie_hut: 15,
        settlement: 30,
        town: 35,
        city: 15,
        super_city: 5,
      },
      large: {
        goodie_hut: 8,
        settlement: 15,
        town: 30,
        city: 35,
        super_city: 12,
      },
      mega: {
        goodie_hut: 5,
        settlement: 5,
        town: 20,
        city: 40,
        super_city: 30,
      },
    },
    alignmentWeights: {
      friendly: 35,
      neutral: 40,
      hostile: 20,
      alien_enclave: 5,
    },
  },

  godMode: {
    enabled: true,
    secretHeader: 'dev-god-mode',
    persistOverridesToDisk: true,
  },
};
