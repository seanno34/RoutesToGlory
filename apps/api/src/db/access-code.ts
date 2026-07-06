import { query } from '../db/client.js';

/** Unambiguous chars — no 0/O, 1/I/L. */
const CHARSET = '23456789ABCDEFGHJKMNPQRSTUVWXYZ';

let schemaReady = false;

/** Idempotent — safe if migration 003 was not uploaded or failed to apply. */
export async function ensureAccessCodeSchema(): Promise<void> {
  if (schemaReady) return;

  const colCheck = await query<{ cnt: number }>(
    `SELECT COUNT(*) AS cnt FROM information_schema.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'worlds' AND COLUMN_NAME = 'access_code'`,
  );
  const hasColumn = Number(colCheck.rows[0]?.cnt ?? 0) > 0;

  if (!hasColumn) {
    await query(`ALTER TABLE worlds ADD COLUMN access_code VARCHAR(8) NULL AFTER slug`);
    console.log('Added worlds.access_code column (runtime schema heal)');
  }

  const idxCheck = await query<{ cnt: number }>(
    `SELECT COUNT(*) AS cnt FROM information_schema.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'worlds' AND INDEX_NAME = 'uq_world_access_code'`,
  );
  const hasIndex = Number(idxCheck.rows[0]?.cnt ?? 0) > 0;

  if (!hasIndex) {
    await query(`ALTER TABLE worlds ADD UNIQUE KEY uq_world_access_code (access_code)`);
    console.log('Added worlds.uq_world_access_code index (runtime schema heal)');
  }

  schemaReady = true;
}

export function generateAccessCode(length = 6): string {
  let code = '';
  for (let i = 0; i < length; i += 1) {
    code += CHARSET[Math.floor(Math.random() * CHARSET.length)]!;
  }
  return code;
}

export async function generateUniqueAccessCode(): Promise<string> {
  await ensureAccessCodeSchema();

  for (let attempt = 0; attempt < 12; attempt += 1) {
    const code = generateAccessCode(attempt > 8 ? 8 : 6);
    const existing = await query<{ ok: number }>(
      `SELECT 1 AS ok FROM worlds WHERE access_code = ?`,
      [code],
    );
    if (existing.rows.length === 0) {
      return code;
    }
  }
  throw new Error('Failed to generate unique access code');
}

/** Backfill resume codes for worlds created before migration 003. */
export async function backfillMissingAccessCodes(): Promise<number> {
  await ensureAccessCodeSchema();

  const missing = await query<{ id: string }>(
    `SELECT id FROM worlds WHERE access_code IS NULL OR access_code = ''`,
  );

  let count = 0;
  for (const row of missing.rows) {
    const code = await generateUniqueAccessCode();
    await query(`UPDATE worlds SET access_code = ? WHERE id = ?`, [code, row.id]);
    count += 1;
  }
  return count;
}
