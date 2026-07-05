import { z } from 'zod';

/** Difficulty affects NPC alien empire growth pace relative to players. */
export const NpcDifficultySchema = z.enum(['slow', 'normal', 'fast']);
export type NpcDifficulty = z.infer<typeof NpcDifficultySchema>;

export const SettlementTierSchema = z.enum([
  'goodie_hut',
  'settlement',
  'town',
  'city',
  'super_city',
]);
export type SettlementTier = z.infer<typeof SettlementTierSchema>;

export const AlignmentSchema = z.enum([
  'friendly',
  'neutral',
  'hostile',
  'alien_enclave',
]);
export type Alignment = z.infer<typeof AlignmentSchema>;

export const GoodieHutChoiceSchema = z.enum(['found_town', 'claim_reward']);
export type GoodieHutChoice = z.infer<typeof GoodieHutChoiceSchema>;

export const RewardTypeSchema = z.enum(['gold', 'tech', 'unit']);
export type RewardType = z.infer<typeof RewardTypeSchema>;

export const DiplomacyStatusSchema = z.enum(['neutral', 'allied', 'hostile']);
export type DiplomacyStatus = z.infer<typeof DiplomacyStatusSchema>;

export const RouteModifierTypeSchema = z.enum([
  'refill_station',
  'guard_post',
  'relay_beacon',
  'booster_pad',
  'hover_lane',
  'scanner_array',
  'harmonic_relay',
  'tariff_gate',
  'free_passage',
  'alien_waystation',
  'phase_stabilizer',
]);
export type RouteModifierType = z.infer<typeof RouteModifierTypeSchema>;

export const SettlementModifierTypeSchema = z.enum([
  'trade_depot',
  'academy_node',
  'garrison',
  'consensus_hall',
  'fusion_core',
]);
export type SettlementModifierType = z.infer<typeof SettlementModifierTypeSchema>;

export const GrowthCategorySchema = z.enum([
  'economic',
  'educational',
  'military',
  'philosophical',
]);
export type GrowthCategory = z.infer<typeof GrowthCategorySchema>;

export const UnitKindSchema = z.enum(['military', 'civic']);
export type UnitKind = z.infer<typeof UnitKindSchema>;

export const NpcHostilityPhaseSchema = z.enum([
  'dormant',
  'observing',
  'probing',
  'raiding',
  'all_out_war',
]);
export type NpcHostilityPhase = z.infer<typeof NpcHostilityPhaseSchema>;

/** Ten alien world resources — each maps to a distinct bonus domain and map icon. */
export const AlienResourceIdSchema = z.enum([
  'xenite',
  'solari_dust',
  'ferracite',
  'lumin_spring',
  'quantium_shard',
  'voidglass',
  'mycelium_core',
  'chrono_moss',
  'aegis_bark',
  'nebula_pearl',
]);
export type AlienResourceId = z.infer<typeof AlienResourceIdSchema>;

export const ResourceBonusDomainSchema = z.enum([
  'fuel',
  'monetary',
  'building',
  'civic',
  'tech',
  'exploration',
  'biological',
  'temporal',
  'defensive',
  'exotic',
]);
export type ResourceBonusDomain = z.infer<typeof ResourceBonusDomainSchema>;

/** Build complexity 1 = fast/jumpstart; 5 = endgame megastructures. */
export const BuildComplexityTierSchema = z.union([
  z.literal(1),
  z.literal(2),
  z.literal(3),
  z.literal(4),
  z.literal(5),
]);
export type BuildComplexityTier = z.infer<typeof BuildComplexityTierSchema>;

export const BuildTargetTypeSchema = z.enum([
  'settlement_modifier',
  'route_modifier',
  'settlement_upgrade',
  'special_project',
]);
export type BuildTargetType = z.infer<typeof BuildTargetTypeSchema>;

export const BuildJobStatusSchema = z.enum([
  'queued',
  'in_progress',
  'completed',
  'cancelled',
]);
export type BuildJobStatus = z.infer<typeof BuildJobStatusSchema>;
