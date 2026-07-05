import type {
  AlienResourceId,
  BuildJobRecord,
  MapResourceNode,
  NpcDifficulty,
  NpcEmpireState,
  SettlementDeposits,
  WorldDiplomacy,
} from '@empire/shared';
import { emptyResourceStockpile, type GameConfig } from '@empire/shared';
import {
  createExplorationState,
  type EmpireExplorationState,
  grantStartingVision,
} from './exploration.js';
import { createNpcAlienEmpire } from './npc-empire.js';

export interface DevWorld {
  id: string;
  name: string;
  startedAt: string;
  difficulty: NpcDifficulty;
  playerEmpireIds: string[];
  npcEmpire: NpcEmpireState;
  diplomacy: WorldDiplomacy;
  superCityCounts: Record<string, number>;
  empireUnits: Record<string, string[]>;
  buildJobs: BuildJobRecord[];
  empireStockpiles: Record<string, Record<AlienResourceId, number>>;
  settlementDeposits: SettlementDeposits[];
  mapResourceNodes: MapResourceNode[];
  /** empireId → per-empire fog of war */
  explorationByEmpire: Record<string, EmpireExplorationState>;
  /** empire spawn points for starting vision */
  empireSpawn: Record<string, { lat: number; lng: number }>;
}

class WorldStore {
  private worlds = new Map<string, DevWorld>();

  create(input: {
    name: string;
    difficulty: NpcDifficulty;
    playerEmpireIds: string[];
  }): DevWorld {
    const id = crypto.randomUUID();
    const startedAt = new Date().toISOString();
    const npcEmpire = createNpcAlienEmpire(
      id,
      input.difficulty,
      new Date(startedAt),
    );

    const world: DevWorld = {
      id,
      name: input.name,
      startedAt,
      difficulty: input.difficulty,
      playerEmpireIds: input.playerEmpireIds,
      npcEmpire,
      diplomacy: {
        worldId: id,
        relations: [],
      },
      superCityCounts: Object.fromEntries(
        input.playerEmpireIds.map((empireId) => [empireId, 0]),
      ),
      empireUnits: Object.fromEntries(
        input.playerEmpireIds.map((empireId) => [empireId, []]),
      ),
      buildJobs: [],
      empireStockpiles: Object.fromEntries(
        input.playerEmpireIds.map((empireId) => [empireId, emptyResourceStockpile()]),
      ),
      settlementDeposits: [],
      mapResourceNodes: [],
      explorationByEmpire: Object.fromEntries(
        input.playerEmpireIds.map((empireId) => [
          empireId,
          createExplorationState(),
        ]),
      ),
      empireSpawn: {},
    };

    this.worlds.set(id, world);
    return world;
  }

  get(worldId: string): DevWorld | undefined {
    return this.worlds.get(worldId);
  }

  updateNpc(worldId: string, npcEmpire: NpcEmpireState): void {
    const world = this.worlds.get(worldId);
    if (world) {
      world.npcEmpire = npcEmpire;
    }
  }

  updateDiplomacy(worldId: string, diplomacy: WorldDiplomacy): void {
    const world = this.worlds.get(worldId);
    if (world) {
      world.diplomacy = diplomacy;
    }
  }

  countSuperCities(empireId: string): number {
    for (const world of this.worlds.values()) {
      if (world.superCityCounts[empireId] !== undefined) {
        return world.superCityCounts[empireId] ?? 0;
      }
    }

    return 0;
  }

  getEmpireUnits(empireId: string): string[] {
    for (const world of this.worlds.values()) {
      const units = world.empireUnits[empireId];
      if (units) {
        return units;
      }
    }

    return [];
  }

  addBuildJobs(worldId: string, jobs: BuildJobRecord[]): void {
    const world = this.worlds.get(worldId);
    if (world) {
      world.buildJobs.push(...jobs);
    }
  }

  getBuildJobs(worldId: string, empireId?: string): BuildJobRecord[] {
    const world = this.worlds.get(worldId);
    if (!world) {
      return [];
    }

    return empireId
      ? world.buildJobs.filter((job) => job.empireId === empireId)
      : world.buildJobs;
  }

  updateBuildJob(worldId: string, job: BuildJobRecord): void {
    const world = this.worlds.get(worldId);
    if (!world) {
      return;
    }

    const index = world.buildJobs.findIndex((entry) => entry.id === job.id);
    if (index >= 0) {
      world.buildJobs[index] = job;
    }
  }

  getStockpile(worldId: string, empireId: string): Record<AlienResourceId, number> {
    const world = this.worlds.get(worldId);
    return world?.empireStockpiles[empireId] ?? emptyResourceStockpile();
  }

  setStockpile(
    worldId: string,
    empireId: string,
    stockpile: Record<AlienResourceId, number>,
  ): void {
    const world = this.worlds.get(worldId);
    if (world) {
      world.empireStockpiles[empireId] = stockpile;
    }
  }

  addSettlementDeposits(worldId: string, deposits: SettlementDeposits): void {
    const world = this.worlds.get(worldId);
    if (world) {
      world.settlementDeposits.push(deposits);
    }
  }

  findBuildJob(jobId: string): { worldId: string; job: BuildJobRecord } | undefined {
    for (const world of this.worlds.values()) {
      const job = world.buildJobs.find((entry) => entry.id === jobId);
      if (job) {
        return { worldId: world.id, job };
      }
    }

    return undefined;
  }

  getWorldAgeDays(worldId: string): number {
    const world = this.worlds.get(worldId);
    if (!world) {
      return 0;
    }

    return (Date.now() - new Date(world.startedAt).getTime()) / (1000 * 60 * 60 * 24);
  }

  setEmpireSpawn(
    worldId: string,
    empireId: string,
    lat: number,
    lng: number,
    config: GameConfig,
  ): void {
    const world = this.worlds.get(worldId);
    if (!world) {
      return;
    }

    world.empireSpawn[empireId] = { lat, lng };
    const state = world.explorationByEmpire[empireId] ?? createExplorationState();
    grantStartingVision(config, state, lat, lng);
    world.explorationByEmpire[empireId] = state;
  }

  getExplorationState(
    worldId: string,
    empireId: string,
  ): EmpireExplorationState | undefined {
    return this.worlds.get(worldId)?.explorationByEmpire[empireId];
  }

  getMapResourceNodes(worldId: string): MapResourceNode[] {
    return this.worlds.get(worldId)?.mapResourceNodes ?? [];
  }
}

export const worldStore = new WorldStore();
