import type {
  AllianceUpdate,
  DiplomacyStatus,
  GameConfig,
  NpcDifficulty,
  NpcEmpireState,
  NpcHostilityPhase,
  WorldDiplomacy,
} from '@empire/shared';

export interface NpcTickResult {
  state: NpcEmpireState;
  phaseChanged: boolean;
  previousPhase: NpcHostilityPhase;
}

export function createNpcAlienEmpire(
  worldId: string,
  difficulty: NpcDifficulty,
  worldStartAt: Date,
): NpcEmpireState {
  return {
    worldId,
    empireId: crypto.randomUUID(),
    name: 'The Obsidian Concord',
    difficulty,
    growthPoints: 0,
    territoryCount: 1,
    hostilityPhase: 'dormant',
    worldAgeDays: 0,
    lastTickAt: worldStartAt.toISOString(),
  };
}

export function tickNpcAlienEmpire(
  config: GameConfig,
  state: NpcEmpireState,
  worldStartAt: Date,
  now: Date = new Date(),
): NpcTickResult {
  const worldAgeDays =
    (now.getTime() - worldStartAt.getTime()) / (1000 * 60 * 60 * 24);
  const multiplier =
    config.npcAlienEmpire.difficultyGrowthMultiplier[state.difficulty] ?? 1;
  const growthGain = Math.floor(
    config.npcAlienEmpire.baseGrowthRatePerTick * multiplier,
  );

  const previousPhase = state.hostilityPhase;
  const hostilityPhase = resolveHostilityPhase(
    config,
    worldAgeDays,
  );

  const territoryGain =
    hostilityPhase === 'all_out_war'
      ? 2
      : hostilityPhase === 'raiding'
        ? 1
        : 0;

  const nextState: NpcEmpireState = {
    ...state,
    growthPoints: state.growthPoints + growthGain,
    territoryCount: state.territoryCount + territoryGain,
    hostilityPhase,
    worldAgeDays,
    lastTickAt: now.toISOString(),
  };

  return {
    state: nextState,
    phaseChanged: previousPhase !== hostilityPhase,
    previousPhase,
  };
}

function resolveHostilityPhase(
  config: GameConfig,
  worldAgeDays: number,
): NpcHostilityPhase {
  const thresholds = config.npcAlienEmpire.hostilityPhaseDays;
  const ordered: NpcHostilityPhase[] = [
    'all_out_war',
    'raiding',
    'probing',
    'observing',
    'dormant',
  ];

  for (const phase of ordered) {
    const threshold = thresholds[phase] ?? 0;
    if (worldAgeDays >= threshold) {
      return phase;
    }
  }

  return 'dormant';
}

export function updatePlayerAlliance(
  diplomacy: WorldDiplomacy,
  update: AllianceUpdate,
): WorldDiplomacy {
  if (!configAllowsAlliance(update.status)) {
    throw new Error(`Alliance status ${update.status} is not supported`);
  }

  const pairKey = sortedPair(update.empireAId, update.empireBId);
  const withoutPair = diplomacy.relations.filter(
    (relation) => sortedPair(relation.empireAId, relation.empireBId) !== pairKey,
  );

  return {
    worldId: diplomacy.worldId,
    relations: [
      ...withoutPair,
      {
        empireAId: update.empireAId,
        empireBId: update.empireBId,
        status: update.status,
        updatedAt: new Date().toISOString(),
      },
    ],
  };
}

function configAllowsAlliance(status: DiplomacyStatus): boolean {
  return status === 'allied' || status === 'neutral' || status === 'hostile';
}

function sortedPair(a: string, b: string): string {
  return [a, b].sort().join(':');
}

export function canPromoteToSuperCity(
  config: GameConfig,
  currentSuperCityCount: number,
  playerCountInWorld: number,
): boolean {
  const empireCap =
    config.growth.superCityPerEmpireCap.base +
    config.growth.superCityPerEmpireCap.perOpponent *
      Math.max(0, playerCountInWorld - 1);

  return currentSuperCityCount < empireCap;
}

export function npcAggressionModifier(phase: NpcHostilityPhase): number {
  switch (phase) {
    case 'dormant':
      return 0;
    case 'observing':
      return 0.1;
    case 'probing':
      return 0.25;
    case 'raiding':
      return 0.5;
    case 'all_out_war':
      return 1;
  }
}
