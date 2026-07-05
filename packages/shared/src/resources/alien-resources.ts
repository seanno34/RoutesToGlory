import type { AlienResourceId, ResourceBonusDomain } from '../types/enums.js';

export interface AlienResourceDefinition {
  id: AlienResourceId;
  name: string;
  terrestrialEcho: string;
  domain: ResourceBonusDomain;
  description: string;
  stockpileBonusPer100: string;
  primaryUses: string[];
  /** Rarer resources appear later in exploration weights */
  discoveryTier: 1 | 2 | 3;
}

export const ALIEN_RESOURCES: Record<AlienResourceId, AlienResourceDefinition> = {
  xenite: {
    id: 'xenite',
    name: 'Xenite',
    terrestrialEcho: 'fuel / petroleum',
    domain: 'fuel',
    description:
      'Crystalline combustible mined from xenon vents. Powers boosters, hover lanes, and production rush furnaces.',
    stockpileBonusPer100: '+2% route speed modifier effectiveness',
    primaryUses: ['Booster pad', 'Hover lane', 'Rush production (partial discount)'],
    discoveryTier: 1,
  },
  solari_dust: {
    id: 'solari_dust',
    name: 'Solari Dust',
    terrestrialEcho: 'gold / currency ore',
    domain: 'monetary',
    description:
      'Photovoltaic granules valued across Survey Worlds. Trade standard and tariff currency.',
    stockpileBonusPer100: '+3% gold from connected trade routes',
    primaryUses: ['Trade depot', 'Tariff gate', 'Gold rush payments'],
    discoveryTier: 1,
  },
  ferracite: {
    id: 'ferracite',
    name: 'Ferracite',
    terrestrialEcho: 'rock / iron ore',
    domain: 'building',
    description:
      'Dense structural ore. Required for nearly all infrastructure and fortifications.',
    stockpileBonusPer100: '-1% build time for new infrastructure (max stack capped in config)',
    primaryUses: ['All settlement modifiers', 'Guard posts', 'Garrisons'],
    discoveryTier: 1,
  },
  lumin_spring: {
    id: 'lumin_spring',
    name: 'Lumin Spring',
    terrestrialEcho: 'water / fertile spring',
    domain: 'civic',
    description:
      'Bio-luminescent aquifer essence. Sustains population and harmonic civic projects.',
    stockpileBonusPer100: '+2% population growth and civic modifier yield',
    primaryUses: ['Consensus hall', 'Harmonic relay', 'Town promotion projects'],
    discoveryTier: 1,
  },
  quantium_shard: {
    id: 'quantium_shard',
    name: 'Quantium Shard',
    terrestrialEcho: 'rare earth / research crystals',
    domain: 'tech',
    description:
      'Unstable lattice fragments used in academies, scanners, and alien tech integration.',
    stockpileBonusPer100: '+2% tech unlock speed and educational growth',
    primaryUses: ['Academy node', 'Scanner array', 'Phase stabilizer'],
    discoveryTier: 2,
  },
  voidglass: {
    id: 'voidglass',
    name: 'Voidglass',
    terrestrialEcho: 'obsidian / radar crystal',
    domain: 'exploration',
    description:
      'Dark translucent crystal that bends sensor light. Extends fog-of-war reveal and intel range.',
    stockpileBonusPer100: '+5% exploration reveal radius',
    primaryUses: ['Scanner array', 'Fog reveal boost', 'Alien waystation'],
    discoveryTier: 2,
  },
  mycelium_core: {
    id: 'mycelium_core',
    name: 'Mycelium Core',
    terrestrialEcho: 'fungi / biomass',
    domain: 'biological',
    description:
      'Living fungal network node. Enables alien diplomacy and biological trade modifiers.',
    stockpileBonusPer100: '+3% alliance favor and alien unit recovery',
    primaryUses: ['Consensus hall', 'Alien waystation', 'Diplomatic projects'],
    discoveryTier: 2,
  },
  chrono_moss: {
    id: 'chrono_moss',
    name: 'Chrono Moss',
    terrestrialEcho: 'rare moss / temporal algae',
    domain: 'temporal',
    description:
      'Time-warping lichen that accelerates construction when processed correctly.',
    stockpileBonusPer100: '-2% remaining build time on active jobs (cap in config)',
    primaryUses: ['Build rush discount', 'Relay beacon', 'Promotion projects'],
    discoveryTier: 3,
  },
  aegis_bark: {
    id: 'aegis_bark',
    name: 'Aegis Bark',
    terrestrialEcho: ' hardwood / armor plating',
    domain: 'defensive',
    description:
      'Petrified bark harder than alloy. Core material for fortifications and route defense.',
    stockpileBonusPer100: '+4% route combat defense',
    primaryUses: ['Garrison', 'Guard post', 'Hover lane fortification'],
    discoveryTier: 3,
  },
  nebula_pearl: {
    id: 'nebula_pearl',
    name: 'Nebula Pearl',
    terrestrialEcho: 'gem / luxury pearl',
    domain: 'exotic',
    description:
      'Iridescent orb formed in low-gravity caves. Extremely valuable; discovery triggers map shimmer.',
    stockpileBonusPer100: '+5% all trade yields; first discovery bonus gold',
    primaryUses: ['Tariff gate', 'Super city projects', 'Legendary discoveries'],
    discoveryTier: 3,
  },
};

export const ALIEN_RESOURCE_IDS = Object.keys(ALIEN_RESOURCES) as AlienResourceId[];

export type ResourceCost = Partial<Record<AlienResourceId, number>>;

export function totalResourceCost(cost: ResourceCost): number {
  return Object.values(cost).reduce((sum, amount) => sum + (amount ?? 0), 0);
}

export function emptyResourceStockpile(): Record<AlienResourceId, number> {
  return {
    xenite: 0,
    solari_dust: 0,
    ferracite: 0,
    lumin_spring: 0,
    quantium_shard: 0,
    voidglass: 0,
    mycelium_core: 0,
    chrono_moss: 0,
    aegis_bark: 0,
    nebula_pearl: 0,
  };
}

export function resourcesByDiscoveryTier(tier: 1 | 2 | 3): AlienResourceId[] {
  return ALIEN_RESOURCE_IDS.filter(
    (id) => ALIEN_RESOURCES[id].discoveryTier === tier,
  );
}
