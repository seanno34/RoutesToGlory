export * from './types/enums.js';
export type {
  GameConfig,
  GameConfigPatch,
  GoodieHutConnectRequest,
  GoodieHutResolution,
  AllianceUpdate,
  GodModePatch,
  AlienReward,
  NpcEmpireState,
  WorldDiplomacy,
  UnitDefinition,
  BuildJobRecord,
  QueueBuildRequest,
  RushBuildRequest,
  SettlementDeposits,
  MapResourceNode,
  RevealExplorationRequest,
  ExplorationMapState,
} from './config/schema.js';

export {
  GameConfigSchema,
  GoodieHutConnectRequestSchema,
  GoodieHutResolutionSchema,
  AllianceUpdateSchema,
  GodModePatchSchema,
  QueueBuildRequestSchema,
  RushBuildRequestSchema,
  RevealExplorationRequestSchema,
  MapResourceNodeSchema,
  ExplorationMapStateSchema,
} from './config/schema.js';
export {
  BeginRouteSessionSchema,
  AppendRoutePointsSchema,
  EndRouteSessionSchema,
  GpsPointInputSchema,
  RouteSessionStatusSchema,
} from './routes/session-schema.js';
export type {
  BeginRouteSession,
  AppendRoutePoints,
  EndRouteSession,
  GpsPointInput,
  RouteSessionStatus,
} from './routes/session-schema.js';
export * from './config/defaults.js';
export * from './config/merge.js';
export * from './units/alien-units.js';
export * from './resources/alien-resources.js';
export * from './resources/resource-biome-rules.js';
export * from './map/resource-icons.js';
export * from './map/fog-of-war.js';
export * from './map/terrain-biome.js';
export * from './map/tile-biome-classifier.js';
export * from './seeding/city-catalog.js';
