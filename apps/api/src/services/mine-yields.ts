import { query } from '../db/client.js';

export interface MineYieldResult {
  accrued: Record<string, number>;
  mineCount: number;
}

/** Accrue passive production from owned extractor mines into the empire stockpile. */
export async function applyMineYields(
  worldId: string,
  empireId: string,
): Promise<MineYieldResult> {
  const mines = await query<{
    id: string;
    resource_id: string;
    yield_per_day: number;
    last_yield_at: Date | string | null;
    claimed_at: Date | string | null;
  }>(
    `SELECT id, resource_id, yield_per_day, last_yield_at, claimed_at
     FROM map_resource_nodes
     WHERE world_id = ? AND owner_empire_id = ?`,
    [worldId, empireId],
  );

  if (mines.rows.length === 0) {
    return { accrued: {}, mineCount: 0 };
  }

  const stockpileResult = await query<{ resources: string }>(
    `SELECT resources FROM empire_stockpiles WHERE empire_id = ?`,
    [empireId],
  );
  if (stockpileResult.rows.length === 0) {
    return { accrued: {}, mineCount: mines.rows.length };
  }

  const stockpile = JSON.parse(stockpileResult.rows[0]?.resources ?? '{}') as Record<
    string,
    number
  >;

  const now = Date.now();
  const dayMs = 86_400_000;
  const accrued: Record<string, number> = {};

  for (const mine of mines.rows) {
    const anchor = mine.last_yield_at ?? mine.claimed_at;
    if (!anchor) continue;

    const elapsedMs = now - new Date(anchor).getTime();
    if (elapsedMs < 60_000) continue;

    const days = elapsedMs / dayMs;
    const amount = Math.floor(mine.yield_per_day * days);
    if (amount <= 0) continue;

    accrued[mine.resource_id] = (accrued[mine.resource_id] ?? 0) + amount;
    stockpile[mine.resource_id] = (stockpile[mine.resource_id] ?? 0) + amount;

    await query(`UPDATE map_resource_nodes SET last_yield_at = NOW() WHERE id = ?`, [
      mine.id,
    ]);
  }

  if (Object.keys(accrued).length > 0) {
    await query(`UPDATE empire_stockpiles SET resources = ? WHERE empire_id = ?`, [
      JSON.stringify(stockpile),
      empireId,
    ]);
  }

  return { accrued, mineCount: mines.rows.length };
}
