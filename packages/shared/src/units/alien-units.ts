import type { UnitDefinition } from '../config/schema.js';

/** Unique alien units grantable from goodie hut Option B. */
export const ALIEN_MILITARY_UNITS: UnitDefinition[] = [
  {
    id: 'xenon_sentinel',
    name: 'Xenon Sentinel',
    kind: 'military',
    basePower: 140,
    alienOrigin: true,
  },
  {
    id: 'phase_lancer',
    name: 'Phase Lancer',
    kind: 'military',
    basePower: 175,
    alienOrigin: true,
    upgradeOf: 'xenon_sentinel',
  },
  {
    id: 'void_harrier',
    name: 'Void Harrier',
    kind: 'military',
    basePower: 210,
    alienOrigin: true,
  },
];

export const ALIEN_CIVIC_UNITS: UnitDefinition[] = [
  {
    id: 'harmonic_envoy',
    name: 'Harmonic Envoy',
    kind: 'civic',
    basePower: 80,
    alienOrigin: true,
  },
  {
    id: 'archive_probe',
    name: 'Archive Probe',
    kind: 'civic',
    basePower: 95,
    alienOrigin: true,
    upgradeOf: 'harmonic_envoy',
  },
];

export const ALIEN_TECH_UNLOCKS = [
  { id: 'hover_lane_mk1', name: 'Hover Lane Protocol' },
  { id: 'phase_stabilizer_mk1', name: 'Phase Stabilization' },
  { id: 'scanner_array_mk1', name: 'Deep Scanner Array' },
  { id: 'relay_beacon_mk1', name: 'Quantum Relay Beacon' },
] as const;

export function findUpgradeCandidate(
  ownedUnitIds: string[],
  pool: UnitDefinition[],
): UnitDefinition | undefined {
  for (const unit of pool) {
    if (typeof unit.upgradeOf === 'string' && ownedUnitIds.includes(unit.upgradeOf)) {
      return unit;
    }
  }

  return undefined;
}

export function pickRandomUnit(
  pool: UnitDefinition[],
  rng: () => number = Math.random,
): UnitDefinition {
  const index = Math.floor(rng() * pool.length);
  return pool[index] ?? pool[0]!;
}
