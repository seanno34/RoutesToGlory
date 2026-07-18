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
import {
  ensureUserPinSchema,
  findOrCreateUserByPin,
  getUserByPin,
  normalizeUserPin,
  tryClaimUserPin,
  worldMatchesPinOwnership,
} from './user-pin.js';

export interface CreateWorldInput {
  name: string;
  slug?: string;
  difficulty?: 'slow' | 'normal' | 'fast';
  playerName?: string;
  spawnLat?: number;
  spawnLng?: number;
  /** When set, reuse/create the user for this 4-digit PIN instead of a fresh email user. */
  pin?: string;
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

/** Unity POC play area (Douglas / Orin Junction, WY) — matches sample-world-map + Echo Sites. */
const POC_DEFAULT_SPAWN_LAT = 42.7597;
const POC_DEFAULT_SPAWN_LNG = -105.3819;

export async function createWorldInDb(
  input: CreateWorldInput,
  config: GameConfig = DEFAULT_GAME_CONFIG,
): Promise<WorldBootstrap> {
  const slug = input.slug ?? `${slugify(input.name)}-${Date.now().toString(36)}`;
  // Prefer caller GPS (web / Unity New Game). Default is Orin Junction — NOT Denver —
  // so seeded xenite lands under the Unity camera when spawnLat/Lng are omitted.
  const spawnLat = input.spawnLat ?? POC_DEFAULT_SPAWN_LAT;
  const spawnLng = input.spawnLng ?? POC_DEFAULT_SPAWN_LNG;

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

  const pin = normalizeUserPin(input.pin);
  let userId: string;
  const playerName = input.playerName ?? 'Explorer';

  if (pin) {
    const user = await findOrCreateUserByPin(pin, playerName);
    userId = user.id;
  } else {
    userId = newId();
    await query(`INSERT INTO users (id, display_name, email) VALUES (?, ?, ?)`, [
      userId,
      playerName,
      `dev-${slug}@rtg.local`,
    ]);
  }

  const empireId = newId();
  await query(
    `INSERT INTO empires (id, world_id, user_id, name, color, spawn_lat, spawn_lng)
     VALUES (?, ?, ?, ?, '#3b82f6', ?, ?)`,
    [
      empireId,
      worldId,
      userId,
      `${playerName} Empire`,
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

export async function listSavedWorlds(
  pin?: string | null,
): Promise<SavedWorldSummary[]> {
  await backfillMissingAccessCodes();
  await ensureUserPinSchema();

  const normalizedPin = normalizeUserPin(pin);
  // Same ownership rule as join: matching PIN, or unbound legacy (NULL pin).
  // Unbound rows are claimable on first successful by-code join with that PIN.
  const params: string[] = [];
  let pinClause = '';
  if (normalizedPin) {
    pinClause = ' AND (u.pin = ? OR u.pin IS NULL)';
    params.push(normalizedPin);
  }

  const result = await query<{
    access_code: string;
    world_id: string;
    slug: string;
    name: string;
    empire_id: string;
    user_id: string;
    player_name: string;
    user_pin: string | null;
    settlement_count: number;
    created_at: string;
  }>(
    `SELECT w.access_code, w.id AS world_id, w.slug, w.name, w.created_at,
            e.id AS empire_id, e.user_id, u.display_name AS player_name, u.pin AS user_pin,
            (SELECT COUNT(*) FROM settlements s WHERE s.world_id = w.id) AS settlement_count
     FROM worlds w
     JOIN empires e ON e.world_id = w.id
     JOIN users u ON u.id = e.user_id
     WHERE w.status = 'active'${pinClause}
     ORDER BY w.created_at DESC`,
    params,
  );

  return result.rows
    .filter((row) => worldMatchesPinOwnership(row.user_pin, normalizedPin))
    .map((row) => ({
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

export type WorldBootstrapLookup =
  | { ok: true; world: SavedWorldSummary }
  | { ok: false; reason: 'not_found' | 'pin_mismatch' | 'pin_claim_conflict' };

export async function getWorldBootstrapByAccessCode(
  code: string,
  pin?: string | null,
): Promise<WorldBootstrapLookup> {
  await backfillMissingAccessCodes();
  await ensureUserPinSchema();

  const normalized = code.trim().toUpperCase();
  const result = await query<{
    access_code: string;
    world_id: string;
    slug: string;
    name: string;
    empire_id: string;
    user_id: string;
    player_name: string;
    user_pin: string | null;
    settlement_count: number;
    created_at: string;
  }>(
    `SELECT w.access_code, w.id AS world_id, w.slug, w.name, w.created_at,
            e.id AS empire_id, e.user_id, u.display_name AS player_name, u.pin AS user_pin,
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
    return { ok: false, reason: 'not_found' };
  }

  const normalizedPin = normalizeUserPin(pin);
  const ownerPin = normalizeUserPin(row.user_pin);
  let userId = row.user_id;

  if (normalizedPin) {
    if (ownerPin === normalizedPin) {
      // already owned by this PIN
    } else if (ownerPin == null) {
      // Legacy unbound world (dev-…@rtg.local / pre-PIN): claim or attach.
      const claimed = await tryClaimUserPin(row.user_id, normalizedPin);
      if (!claimed) {
        const pinUser = await getUserByPin(normalizedPin);
        if (!pinUser) {
          return { ok: false, reason: 'pin_claim_conflict' };
        }
        // PIN already belongs to another user — move this empire under that user
        // so Join works for the PIN holder without stealing other bound worlds.
        await query(`UPDATE empires SET user_id = ? WHERE id = ? AND user_id = ?`, [
          pinUser.id,
          row.empire_id,
          row.user_id,
        ]);
        userId = pinUser.id;
      }
    } else {
      return { ok: false, reason: 'pin_mismatch' };
    }
  }

  return {
    ok: true,
    world: {
      accessCode: row.access_code,
      id: row.world_id,
      slug: row.slug,
      name: row.name,
      empireId: row.empire_id,
      userId,
      playerName: row.player_name,
      settlementCount: Number(row.settlement_count),
      createdAt: row.created_at,
    },
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
    `SELECT id, tile_id, resource_id, richness, yield_per_day, owner_empire_id, lat, lng
     FROM map_resource_nodes WHERE world_id = ?`,
    [worldId],
  );

  // MySQL returns DECIMAL columns (lat/lng, distance_m) as strings to preserve
  // precision. Coerce to numbers so the JSON contract is numeric for every
  // client (Unity's JsonUtility can't parse a string into a double, and the web
  // map wants numbers too).
  return {
    settlements: settlements.rows.map((r) => coerceCoords(r)),
    routes: routes.rows.map((r) => ({
      ...coerceCoords(r, ['distance_m']),
      path_json: coercePathJson(r.path_json),
    })),
    resources: resources.rows.map((r) => coerceCoords(r)),
  };
}

type PathPoint = { lat: number; lng: number };

/** Normalize route path vertices — MySQL JSON may store DECIMAL lat/lng as strings. */
function coercePathJson(pathJson: unknown): PathPoint[] {
  if (!Array.isArray(pathJson)) return [];
  return pathJson.map((pt) => {
    const p = pt as Record<string, unknown>;
    return {
      lat: typeof p.lat === 'string' ? Number(p.lat) : Number(p.lat),
      lng: typeof p.lng === 'string' ? Number(p.lng) : Number(p.lng),
    };
  });
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
