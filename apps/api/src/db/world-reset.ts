/**
 * Dev helper: wipe player progress on a world (routes, claims, sessions) while
 * keeping the world, map seed, and empire record intact.
 */
import type { ResultSetHeader } from 'mysql2';
import { getPool, query } from './client.js';

export interface ResetWorldProgressInput {
  worldId: string;
  empireId?: string;
}

export interface ResetWorldProgressResult {
  ok: true;
  worldId: string;
  empireId?: string;
  routesDeleted: number;
  sessionsDeleted: number;
  extractorsDeleted: number;
  goodieHutsRestored: number;
  resourcesUnclaimed: number;
  settlementsUnowned: number;
}

export async function resetWorldProgress(
  input: ResetWorldProgressInput,
): Promise<ResetWorldProgressResult> {
  const { worldId, empireId } = input;

  const world = await query<{ id: string }>(
    `SELECT id FROM worlds WHERE id = ?`,
    [worldId],
  );
  if (!world.rows[0]) {
    throw Object.assign(new Error('World not found'), { statusCode: 404 });
  }

  if (empireId) {
    const empire = await query<{ id: string }>(
      `SELECT id FROM empires WHERE id = ? AND world_id = ?`,
      [empireId, worldId],
    );
    if (!empire.rows[0]) {
      throw Object.assign(new Error('Empire not found in this world'), { statusCode: 404 });
    }
  }

  const routesDeleted = await deleteRows(
    `DELETE FROM routes WHERE world_id = ?${empireClause(empireId)}`,
    bind(worldId, empireId),
  );

  const sessionsDeleted = await deleteRows(
    `DELETE FROM route_sessions WHERE world_id = ?${empireClause(empireId)}`,
    bind(worldId, empireId),
  );

  await query(
    `DELETE FROM build_jobs WHERE world_id = ?${empireClause(empireId)}`,
    bind(worldId, empireId),
  );

  await query(`DELETE FROM world_events WHERE world_id = ?`, [worldId]);

  const extractorsDeleted = await deleteRows(
    `DELETE FROM settlements
     WHERE world_id = ? AND slug LIKE 'extractor-%'${empireId ? ' AND owner_empire_id = ?' : ''}`,
    bind(worldId, empireId),
  );

  const resourcesUnclaimed = await updateRows(
    `UPDATE map_resource_nodes
     SET owner_empire_id = NULL, route_id = NULL, claimed_at = NULL, last_yield_at = NULL
     WHERE world_id = ?${empireId ? ' AND owner_empire_id = ?' : ' AND owner_empire_id IS NOT NULL'}`,
    bind(worldId, empireId),
  );

  const goodieHutsRestored = await updateRows(
    `UPDATE settlements SET
       tier = 'goodie_hut',
       is_goodie_hut = 1,
       owner_empire_id = NULL,
       name = REPLACE(name, 'Town ', 'Goodie Hut '),
       planet_display_name = REPLACE(planet_display_name, 'Town ', 'Goodie Hut ')
     WHERE world_id = ? AND slug LIKE 'gh-%'`,
    [worldId],
  );

  const settlementsUnowned = await updateRows(
    `UPDATE settlements SET owner_empire_id = NULL
     WHERE world_id = ? AND owner_empire_id IS NOT NULL${empireId ? ' AND owner_empire_id = ?' : ''}`,
    bind(worldId, empireId),
  );

  if (empireId) {
    await query(
      `DELETE FROM explored_tiles WHERE world_id = ? AND empire_id = ?`,
      [worldId, empireId],
    );
  } else {
    await query(`DELETE FROM explored_tiles WHERE world_id = ?`, [worldId]);
  }

  return {
    ok: true,
    worldId,
    empireId,
    routesDeleted,
    sessionsDeleted,
    extractorsDeleted,
    goodieHutsRestored,
    resourcesUnclaimed,
    settlementsUnowned,
  };
}

function empireClause(empireId?: string): string {
  return empireId ? ' AND empire_id = ?' : '';
}

function bind(worldId: string, empireId?: string): Array<string> {
  return empireId ? [worldId, empireId] : [worldId];
}

async function deleteRows(sql: string, params: Array<string>): Promise<number> {
  const [result] = await getPool().execute(sql, params);
  return (result as ResultSetHeader).affectedRows ?? 0;
}

async function updateRows(sql: string, params: Array<string>): Promise<number> {
  const [result] = await getPool().execute(sql, params);
  return (result as ResultSetHeader).affectedRows ?? 0;
}
