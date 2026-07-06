import mysql from 'mysql2/promise';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

let pool: mysql.Pool | null = null;

export interface QueryResult<T> {
  rows: T[];
}

function resolvePoolConfig(): mysql.PoolOptions {
  const url = process.env.DATABASE_URL;
  if (url?.startsWith('mysql://')) {
    return { uri: url, waitForConnections: true, connectionLimit: 10 };
  }

  const host = process.env.MYSQL_HOST;
  const user = process.env.MYSQL_USER;
  const database = process.env.MYSQL_DATABASE;

  if (!host || !user || !database) {
    throw new Error(
      'Set DATABASE_URL=mysql://... or MYSQL_HOST, MYSQL_USER, MYSQL_PASSWORD, MYSQL_DATABASE',
    );
  }

  return {
    host,
    user,
    password: process.env.MYSQL_PASSWORD ?? '',
    database,
    port: Number(process.env.MYSQL_PORT ?? 3306),
    waitForConnections: true,
    connectionLimit: 10,
  };
}

export function isDatabaseEnabled(): boolean {
  if (process.env.DATABASE_URL?.startsWith('mysql://')) {
    return true;
  }
  return Boolean(
    process.env.MYSQL_HOST && process.env.MYSQL_USER && process.env.MYSQL_DATABASE,
  );
}

export function getPool(): mysql.Pool {
  if (!pool) {
    pool = mysql.createPool(resolvePoolConfig());
  }
  return pool;
}

export async function closePool(): Promise<void> {
  if (pool) {
    await pool.end();
    pool = null;
  }
}

const migrationsDir = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../../migrations',
);

function isMysqlErrno(error: unknown, errno: number): boolean {
  return (
    typeof error === 'object' &&
    error !== null &&
    'errno' in error &&
    (error as { errno: number }).errno === errno
  );
}

async function columnExists(
  conn: mysql.Connection | mysql.Pool,
  table: string,
  column: string,
): Promise<boolean> {
  const [rows] = await conn.execute<mysql.RowDataPacket[]>(
    `SELECT 1 AS ok FROM information_schema.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = ? AND COLUMN_NAME = ?`,
    [table, column],
  );
  return rows.length > 0;
}

/** Recover from MySQL DDL auto-commit leaving columns but no schema_migrations row. */
async function healPartialMigrations(db: mysql.Pool): Promise<void> {
  const version = '002_resource_mines';
  const [applied] = await db.execute<mysql.RowDataPacket[]>(
    'SELECT 1 AS ok FROM schema_migrations WHERE version = ?',
    [version],
  );
  if (applied.length > 0) {
    return;
  }

  if (!(await columnExists(db, 'map_resource_nodes', 'owner_empire_id'))) {
    return;
  }

  console.warn(
    `Detected partial ${version} migration — finishing idempotent steps and recording version`,
  );

  const sqlPath = path.join(migrationsDir, `${version}.sql`);
  const sql = await readFile(sqlPath, 'utf8');
  const conn = await mysql.createConnection({
    ...resolvePoolConfig(),
    multipleStatements: true,
  });
  try {
    await conn.query(sql);
    await conn.execute('INSERT IGNORE INTO schema_migrations (version) VALUES (?)', [
      version,
    ]);
    console.log(`Healed migration ${version}`);
  } finally {
    await conn.end();
  }
}

export async function runMigrations(): Promise<void> {
  const db = getPool();

  await db.execute(`
    CREATE TABLE IF NOT EXISTS schema_migrations (
      version VARCHAR(64) PRIMARY KEY,
      applied_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
    )
  `);

  await healPartialMigrations(db);

  const files = (await readdir(migrationsDir))
    .filter((f) => f.endsWith('.sql'))
    .sort();

  for (const file of files) {
    const version = file.replace(/\.sql$/, '');
    const [applied] = await db.execute<mysql.RowDataPacket[]>(
      'SELECT 1 AS ok FROM schema_migrations WHERE version = ?',
      [version],
    );
    if (applied.length > 0) {
      continue;
    }

    const sql = await readFile(path.join(migrationsDir, file), 'utf8');
    const conn = await mysql.createConnection({
      ...resolvePoolConfig(),
      multipleStatements: true,
    });
    try {
      await conn.beginTransaction();
      await conn.query(sql);
      await conn.execute('INSERT INTO schema_migrations (version) VALUES (?)', [
        version,
      ]);
      await conn.commit();
      console.log(`Applied migration ${version}`);
    } catch (error) {
      await conn.rollback();
      if (
        version === '002_resource_mines' &&
        isMysqlErrno(error, 1060) &&
        (await columnExists(conn, 'map_resource_nodes', 'owner_empire_id'))
      ) {
        console.warn(
          `Migration ${version} partially applied — recording version and continuing`,
        );
        await conn.execute('INSERT IGNORE INTO schema_migrations (version) VALUES (?)', [
          version,
        ]);
        continue;
      }
      throw error;
    } finally {
      await conn.end();
    }
  }
}

export async function query<T = mysql.RowDataPacket>(
  sql: string,
  params: mysql.ExecuteValues = [],
): Promise<QueryResult<T>> {
  const [rows] = await getPool().execute(sql, params);
  return { rows: rows as T[] };
}

export function newId(): string {
  return crypto.randomUUID();
}
