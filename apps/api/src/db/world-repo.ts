import type { GameConfig } from '@empire/shared';
import { DEFAULT_GAME_CONFIG } from '@empire/shared';
import { query, newId } from './client.js';
import { seedWorldSettlements } from './world-seed.js';

export interface CreateWorldInput {
  name: string;
  slug?: string;
  difficulty?: 'slow' | 'normal' | 'fast';
  playerName?: string;
  spawnLat?: number;
  spawnLng?: number;
}

export interface WorldBootstrap {
  worldId: string;
  slug: string;
  empireId: string;
  userId: string;
  settlementCount: number;
}

function slugify(name: string): string {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '')
    .slice(0, 48);
}

export async function createWorldInDb(
  input: CreateWorldInput,
  config: GameConfig = DEFAULT_GAME_CONFIG,
): Promise<WorldBootstrap> {
  const slug = input.slug ?? `${slugify(input.name)}-${Date.now().toString(36)}`;
  const spawnLat = input.spawnLat ?? 39.7392;
  const spawnLng = input.spawnLng ?? -104.9903;

  const worldId = newId();
  await query(
    `INSERT INTO worlds (id, slug, name, difficulty, config)
     VALUES (?, ?, ?, ?, ?)`,
    [worldId, slug, input.name, input.difficulty ?? 'normal', JSON.stringify(config)],
  );

  await query(
    `INSERT INTO npc_empires (world_id, name, difficulty)
     VALUES (?, 'The Obsidian Concord', ?)`,
    [worldId, input.difficulty ?? 'normal'],
  );

  const userId = newId();
  await query(`INSERT INTO users (id, display_name, email) VALUES (?, ?, ?)`, [
    userId,
    input.playerName ?? 'Explorer',
    `dev-${slug}@rtg.local`,
  ]);

  const empireId = newId();
  await query(
    `INSERT INTO empires (id, world_id, user_id, name, color, spawn_lat, spawn_lng)
     VALUES (?, ?, ?, ?, '#3b82f6', ?, ?)`,
    [
      empireId,
      worldId,
      userId,
      `${input.playerName ?? 'Explorer'} Empire`,
      spawnLat,
      spawnLng,
    ],
  );

  await query(`INSERT INTO empire_stockpiles (empire_id, resources) VALUES (?, ?)`, [
    empireId,
    JSON.stringify({
      xenite: 20,
      solari_dust: 30,
      ferracite: 40,
      lumin_spring: 15,
      quantium_shard: 5,
      voidglass: 0,
      mycelium_core: 0,
      chrono_moss: 0,
      aegis_bark: 0,
      nebula_pearl: 0,
    }),
  ]);

  const settlementCount = await seedWorldSettlements(worldId, config);

  return { worldId, slug, empireId, userId, settlementCount };
}

export async function getWorldMap(worldId: string) {
  const settlements = await query(
    `SELECT id, slug, name, planet_display_name, terrestrial_label, tier, alignment,
            is_goodie_hut, owner_empire_id, geofence_radius_m, lat, lng
     FROM settlements WHERE world_id = ?`,
    [worldId],
  );

  const routes = await query(
    `SELECT r.id, r.empire_id, r.from_settlement_id, r.to_settlement_id,
            r.distance_m, r.status, r.path_json, e.color AS empire_color
     FROM routes r
     JOIN empires e ON e.id = r.empire_id
     WHERE r.world_id = ? AND r.status = 'active'`,
    [worldId],
  );

  const resources = await query(
    `SELECT id, tile_id, resource_id, richness, yield_per_day, lat, lng
     FROM map_resource_nodes WHERE world_id = ?`,
    [worldId],
  );

  return { settlements: settlements.rows, routes: routes.rows, resources: resources.rows };
}

export async function getEmpireContext(worldId: string, empireId: string) {
  const empire = await query(
    `SELECT e.*, e.spawn_lat, e.spawn_lng, s.resources AS stockpile
     FROM empires e
     LEFT JOIN empire_stockpiles s ON s.empire_id = e.id
     WHERE e.id = ? AND e.world_id = ?`,
    [empireId, worldId],
  );

  return empire.rows[0] ?? null;
}
