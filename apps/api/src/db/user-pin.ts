import { query, newId } from './client.js';

let schemaReady = false;

/** Idempotent — safe if migration 004 was not uploaded or failed to apply. */
export async function ensureUserPinSchema(): Promise<void> {
  if (schemaReady) return;

  const colCheck = await query<{ cnt: number }>(
    `SELECT COUNT(*) AS cnt FROM information_schema.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'users' AND COLUMN_NAME = 'pin'`,
  );
  const hasColumn = Number(colCheck.rows[0]?.cnt ?? 0) > 0;

  if (!hasColumn) {
    await query(`ALTER TABLE users ADD COLUMN pin CHAR(4) NULL AFTER display_name`);
    console.log('Added users.pin column (runtime schema heal)');
  }

  const idxCheck = await query<{ cnt: number }>(
    `SELECT COUNT(*) AS cnt FROM information_schema.STATISTICS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'users' AND INDEX_NAME = 'uq_users_pin'`,
  );
  const hasIndex = Number(idxCheck.rows[0]?.cnt ?? 0) > 0;

  if (!hasIndex) {
    await query(`ALTER TABLE users ADD UNIQUE KEY uq_users_pin (pin)`);
    console.log('Added users.uq_users_pin index (runtime schema heal)');
  }

  schemaReady = true;
}

/** Normalize to 4-digit PIN (`0000`–`9999`) or null if not exactly 4 digits. */
export function normalizeUserPin(raw: unknown): string | null {
  if (raw == null) return null;
  const digits = String(raw).replace(/\D/g, '');
  if (digits.length !== 4) return null;
  if (!/^\d{4}$/.test(digits)) return null;
  return digits;
}

export interface PinUser {
  id: string;
  displayName: string;
  pin: string;
}

/** Look up an existing PIN user or create one (POC: PIN is the credential). */
export async function findOrCreateUserByPin(
  pin: string,
  displayName = 'Explorer',
): Promise<PinUser> {
  await ensureUserPinSchema();
  const normalized = normalizeUserPin(pin);
  if (!normalized) {
    throw Object.assign(new Error('PIN must be 4 digits (0000–9999)'), {
      statusCode: 400,
    });
  }

  const existing = await query<{ id: string; display_name: string; pin: string }>(
    `SELECT id, display_name, pin FROM users WHERE pin = ? LIMIT 1`,
    [normalized],
  );
  const row = existing.rows[0];
  if (row) {
    return { id: row.id, displayName: row.display_name, pin: row.pin };
  }

  const userId = newId();
  await query(
    `INSERT INTO users (id, display_name, email, pin) VALUES (?, ?, ?, ?)`,
    [userId, displayName, `pin-${normalized}@rtg.local`, normalized],
  );

  return { id: userId, displayName, pin: normalized };
}

export async function getUserByPin(pin: string): Promise<PinUser | null> {
  await ensureUserPinSchema();
  const normalized = normalizeUserPin(pin);
  if (!normalized) return null;

  const existing = await query<{ id: string; display_name: string; pin: string }>(
    `SELECT id, display_name, pin FROM users WHERE pin = ? LIMIT 1`,
    [normalized],
  );
  const row = existing.rows[0];
  if (!row) return null;
  return { id: row.id, displayName: row.display_name, pin: row.pin };
}

/**
 * Bind a 4-digit PIN onto a legacy user who has no PIN yet.
 * Succeeds only when the PIN is unused (or already on this user) and the row
 * is still unbound. Safe under the unique `uq_users_pin` index.
 */
export async function tryClaimUserPin(
  userId: string,
  pin: string,
): Promise<boolean> {
  await ensureUserPinSchema();
  const normalized = normalizeUserPin(pin);
  if (!normalized || !userId) return false;

  const existing = await getUserByPin(normalized);
  if (existing) {
    return existing.id === userId;
  }

  try {
    await query(`UPDATE users SET pin = ? WHERE id = ? AND pin IS NULL`, [
      normalized,
      userId,
    ]);
  } catch {
    // Unique race: another request claimed this PIN first.
    const afterRace = await getUserByPin(normalized);
    return afterRace?.id === userId;
  }

  const self = await query<{ pin: string | null }>(
    `SELECT pin FROM users WHERE id = ? LIMIT 1`,
    [userId],
  );
  return normalizeUserPin(self.rows[0]?.pin) === normalized;
}

/**
 * Shared list/join filter: a world belongs to `filterPin` when the owner PIN
 * matches, or the owner is still unbound (legacy pre-PIN world — join may claim).
 * With no filter PIN, every world matches (legacy/web list-all).
 */
export function worldMatchesPinOwnership(
  ownerPin: string | null | undefined,
  filterPin: string | null | undefined,
): boolean {
  if (!filterPin) return true;
  const normalizedOwner = normalizeUserPin(ownerPin);
  if (normalizedOwner === filterPin) return true;
  if (normalizedOwner == null) return true;
  return false;
}
