/**
 * mysql2 returns MySQL JSON columns as already-parsed objects/arrays.
 * Some drivers / CAST paths still yield strings — accept both.
 *
 * Calling JSON.parse on an object coerces via ToString → "[object Object]"
 * and throws: `"[object Object]" is not valid JSON`.
 */
export function parseMysqlJson<T>(value: unknown, fallback: T): T {
  if (value == null) return fallback;
  if (typeof value === 'object') return value as T;
  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (!trimmed) return fallback;
    try {
      return JSON.parse(trimmed) as T;
    } catch {
      return fallback;
    }
  }
  return fallback;
}
