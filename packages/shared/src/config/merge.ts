import type { GameConfig } from './schema.js';

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Deep-merge source into target; later values win. Arrays replace, not concat. */
export function deepMerge<T extends Record<string, unknown>>(
  target: T,
  source: Record<string, unknown>,
): T {
  const output = { ...target } as Record<string, unknown>;

  for (const [key, value] of Object.entries(source)) {
    const existing = output[key];

    if (isPlainObject(existing) && isPlainObject(value)) {
      output[key] = deepMerge(existing, value);
    } else {
      output[key] = value;
    }
  }

  return output as T;
}

/** Apply dot-path override e.g. "growth.superCityPerEmpireCap.base" → 8 */
export function setByPath(
  config: GameConfig,
  path: string,
  value: unknown,
): GameConfig {
  const keys = path.split('.');
  if (keys.length === 0) {
    return config;
  }

  const clone = structuredClone(config) as Record<string, unknown>;
  let cursor: Record<string, unknown> = clone;

  for (let i = 0; i < keys.length - 1; i += 1) {
    const key = keys[i]!;
    const next = cursor[key];

    if (!isPlainObject(next)) {
      cursor[key] = {};
    }

    cursor = cursor[key] as Record<string, unknown>;
  }

  cursor[keys.at(-1)!] = value;
  return clone as GameConfig;
}

export function getByPath(config: GameConfig, path: string): unknown {
  return path.split('.').reduce<unknown>((acc, key) => {
    if (isPlainObject(acc)) {
      return acc[key];
    }

    return undefined;
  }, config);
}

/** Flatten config leaves to dot-paths for God mode UI listing. */
export function flattenConfigPaths(
  value: unknown,
  prefix = '',
): Array<{ path: string; value: unknown }> {
  if (!isPlainObject(value)) {
    return prefix ? [{ path: prefix, value }] : [];
  }

  const entries: Array<{ path: string; value: unknown }> = [];

  for (const [key, child] of Object.entries(value)) {
    const nextPath = prefix ? `${prefix}.${key}` : key;
    entries.push(...flattenConfigPaths(child, nextPath));
  }

  return entries;
}

/**
 * Super city cap for an empire given player count in world.
 * 1v1 (2 players): base 6 + perOpponent 0 = 6 each.
 */
export function superCityCapForEmpire(
  config: GameConfig,
  playerCountInWorld: number,
): number {
  const { base, perOpponent } = config.growth.superCityPerEmpireCap;
  const opponents = Math.max(0, playerCountInWorld - 1);
  return base + perOpponent * opponents;
}
