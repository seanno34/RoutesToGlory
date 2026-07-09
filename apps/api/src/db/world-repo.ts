import type { GameConfig } from '@empire/shared';
import { DEFAULT_GAME_CONFIG } from '@empire/shared';
import { query, newId } from './client.js';
import { seedWorldSettlements } from './world-seed.js';
import { seedPlayArea } from './spawn-seed.js';
import { grantStartingVision } from './exploration-repo.js';
import {
  backfillMissingAccessCodes,
  generateUniqueAccessCode,
} from './access-code.js';

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
  accessCode: string;
  empireId: string;
  userId: string;
  settlementCount: number;
  metroEchoSites?: number;
  localGoodieHuts?: number;
  localResources?: number;
  roadPointsSampled?: number;
}

export interface SavedWorldSummary {
  accessCode: string;
  id: string;
  slug: string;
  name: string;
  empireId: string;
  userId: string;
  playerName: string;
  settlementCount: number;
  createdAt: string;
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
  const accessCode = await generateUniqueAccessCode();
  await query(
    `INSERT INTO worlds (id, slug, access_code, name, difficulty, config)
     VALUES (?, ?, ?, ?, ?, ?)`,
    [
      worldId,
      slug,
      accessCode,
      input.name,
      input.difficulty ?? 'normal',
      JSON.stringify(config),
    ],
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

  const metroCount = await seedWorldSettlements(worldId, config);
  const localSeed = await seedPlayArea(worldId, spawnLat, spawnLng, config);
  await grantStartingVision(worldId, empireId, spawnLat, spawnLng, config);

  return {
    worldId,
    slug,
    accessCode,
    empireId,
    userId,
    settlementCount: metroCount + localSeed.goodieHuts,
    metroEchoSites: metroCount,
    localGoodieHuts: localSeed.goodieHuts,
    localResources: localSeed.resources,
  };
}

export async function listSavedWorlds(): Promise<SavedWorldSummary[]> {
  await backfillMissingAccessCodes();

  const result = await query<{
    access_code: string;
    world_id: string;
    slug: string;
    name: string;
    empire_id: string;
    user_id: string;
    player_name: string;
    settlement_count: number;
    created_at: string;
  }>(
    `SELECT w.access_code, w.id AS world_id, w.slug, w.name, w.created_at,
            e.id AS empire_id, e.user_id, u.display_name AS player_name,
            (SELECT COUNT(*) FROM settlements s WHERE s.world_id = w.id) AS settlement_count
     FROM worlds w
     JOIN empires e ON e.world_id = w.id
     JOIN users u ON u.id = e.user_id
     WHERE w.status = 'active'
     ORDER BY w.created_at DESC`,
  );

  return result.rows.map((row) => ({
    accessCode: row.access_code,
    id: row.world_id,
    slug: row.slug,
    name: row.name,
    empireId: row.empire_id,
    userId: row.user_id,
    playerName: row.player_name,
    settlementCount: Number(row.settlement_count),
    createdAt: row.created_at,
  }));
}

export async function getWorldBootstrapByAccessCode(
  code: string,
): Promise<SavedWorldSummary | null> {
  await backfillMissingAccessCodes();

  const normalized = code.trim().toUpperCase();
  const result = await query<{
    access_code: string;
    world_id: string;
    slug: string;
    name: string;
    empire_id: string;
    user_id: string;
    player_name: string;
    settlement_count: number;
    created_at: string;
  }>(
    `SELECT w.access_code, w.id AS world_id, w.slug, w.name, w.created_at,
            e.id AS empire_id, e.user_id, u.display_name AS player_name,
            (SELECT COUNT(*) FROM settlements s WHERE s.world_id = w.id) AS settlement_count
     FROM worlds w
     JOIN empires e ON e.world_id = w.id
     JOIN users u ON u.id = e.user_id
     WHERE w.access_code = ? AND w.status = 'active'
     LIMIT 1`,
    [normalized],
  );

  const row = result.rows[0];
  if (!row) {
    return null;
  }

  return {
    accessCode: row.access_code,
    id: row.world_id,
    slug: row.slug,
    name: row.name,
    empireId: row.empire_id,
    userId: row.user_id,
    playerName: row.player_name,
    settlementCount: Number(row.settlement_count),
    createdAt: row.created_at,
  };
}

export async function getWorldMap(worldId: string) {
  const settlements = await query<Record<string, unknown>>(
    `SELECT id, slug, name, planet_display_name, terrestrial_label, tier, alignment,
            is_goodie_hut, owner_empire_id, geofence_radius_m, lat, lng
     FROM settlements WHERE world_id = ?`,
    [worldId],
  );

  const routes = await query<Record<string, unknown>>(
    `SELECT r.id, r.empire_id, r.from_settlement_id, r.to_settlement_id,
            r.distance_m, r.status, r.path_json, e.color AS empire_color
     FROM routes r
     JOIN empires e ON e.id = r.empire_id
     WHERE r.world_id = ? AND r.status = 'active'`,
    [worldId],
  );

  const resources = await query<Record<string, unknown>>(
    `SELECT id, tile_id, resource_id, richness, yield_per_day, lat, lng
     FROM map_resource_nodes WHERE world_id = ?`,
    [worldId],
  );

  // MySQL returns DECIMAL columns (lat/lng, distance_m) as strings to preserve
  // precision. Coerce to numbers so the JSON contract is numeric for every
  // client (Unity's JsonUtility can't parse a string into a double, and the web
  // map wants numbers too).
  return {
    settlements: settlements.rows.map((r) => coerceCoords(r)),
    routes: routes.rows.map((r) => coerceCoords(r, ['distance_m'])),
    resources: resources.rows.map((r) => coerceCoords(r)),
  };
}

/** Convert lat/lng (+ any extra decimal columns) from DECIMAL strings to numbers. */
function coerceCoords<T extends Record<string, unknown>>(
  row: T,
  extraKeys: string[] = [],
): T {
  const out: Record<string, unknown> = { ...row };
  for (const key of ['lat', 'lng', ...extraKeys]) {
    if (typeof out[key] === 'string') {
      out[key] = Number(out[key]);
    }
  }
  return out as T;
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
