import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import {
  DEFAULT_GAME_CONFIG,
  GameConfigSchema,
  deepMerge,
  getByPath,
  setByPath,
  type GameConfig,
} from '@empire/shared';

const RUNTIME_CONFIG_FILE = path.join(process.cwd(), '.runtime-config.json');

export class ConfigStore {
  private config: GameConfig;
  private overrides: Record<string, unknown> = {};

  constructor(initial?: GameConfig) {
    this.config = initial ?? structuredClone(DEFAULT_GAME_CONFIG);
  }

  async load(): Promise<void> {
    const defaults = structuredClone(DEFAULT_GAME_CONFIG);
    try {
      const raw = await readFile(RUNTIME_CONFIG_FILE, 'utf8');
      const parsed = JSON.parse(raw) as Record<string, unknown>;
      this.overrides = parsed;
      this.config = GameConfigSchema.parse(
        deepMerge(defaults as Record<string, unknown>, parsed),
      );
    } catch (err) {
      try {
        this.config = GameConfigSchema.parse(defaults);
      } catch {
        throw err;
      }
      if (
        err &&
        typeof err === 'object' &&
        'code' in err &&
        err.code !== 'ENOENT'
      ) {
        console.warn(
          '[config-store] Invalid .runtime-config.json; using defaults.',
          err,
        );
      }
    }
  }

  get(): GameConfig {
    return this.config;
  }

  getOverrides(): Record<string, unknown> {
    return structuredClone(this.overrides);
  }

  async patch(pathKey: string, value: unknown): Promise<GameConfig> {
    const candidate = setByPath(this.config, pathKey, value);
    this.config = GameConfigSchema.parse(candidate);
    this.overrides = deepMerge(this.overrides, buildNestedObject(pathKey, value));

    if (this.config.godMode.persistOverridesToDisk) {
      await this.persist();
    }

    return this.config;
  }

  async reset(): Promise<GameConfig> {
    this.overrides = {};
    this.config = GameConfigSchema.parse(structuredClone(DEFAULT_GAME_CONFIG));

    if (this.config.godMode.persistOverridesToDisk) {
      await this.persist();
    }

    return this.config;
  }

  resolve<T>(pathKey: string): T {
    return getByPath(this.config, pathKey) as T;
  }

  private async persist(): Promise<void> {
    await mkdir(path.dirname(RUNTIME_CONFIG_FILE), { recursive: true });
    await writeFile(
      RUNTIME_CONFIG_FILE,
      JSON.stringify(this.overrides, null, 2),
      'utf8',
    );
  }
}

function buildNestedObject(
  dotPath: string,
  value: unknown,
): Record<string, unknown> {
  const keys = dotPath.split('.');
  if (keys.length === 1) {
    return { [keys[0]!]: value };
  }

  return {
    [keys[0]!]: buildNestedObject(keys.slice(1).join('.'), value),
  };
}

export const configStore = new ConfigStore();
