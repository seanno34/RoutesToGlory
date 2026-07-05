const API_BASE =
  typeof __API_BASE__ !== 'undefined'
    ? __API_BASE__
    : (import.meta.env.VITE_API_BASE ?? '/rtg/api');

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error((err as { error?: string }).error ?? res.statusText);
  }

  return res.json() as Promise<T>;
}

export interface BootstrapWorld {
  id: string;
  slug: string;
  empireId: string;
  userId: string;
  settlementCount: number;
  storage: string;
}

export interface Settlement {
  id: string;
  name: string;
  planet_display_name: string;
  terrestrial_label: string;
  tier: string;
  alignment: string;
  is_goodie_hut: boolean;
  lat: number;
  lng: number;
  geofence_radius_m: number;
}

export interface WorldMap {
  settlements: Settlement[];
  routes: unknown[];
  resources: unknown[];
}

export const api = {
  createWorld: (body?: {
    name?: string;
    playerName?: string;
    spawnLat?: number;
    spawnLng?: number;
  }) =>
    request<BootstrapWorld>('/worlds', {
      method: 'POST',
      body: JSON.stringify(body ?? {}),
    }),

  getWorldMap: (worldId: string) =>
    request<WorldMap>(`/worlds/${worldId}/map`),

  beginSession: (body: {
    worldId: string;
    empireId: string;
    lat: number;
    lng: number;
    originSettlementId?: string;
    targetSettlementId?: string;
  }) =>
    request<{ sessionId: string }>('/sessions', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  appendPoints: (
    sessionId: string,
    points: Array<{
      lat: number;
      lng: number;
      accuracyM?: number;
      speedMps?: number;
      recordedAt: string;
    }>,
  ) =>
    request<{
      connected: boolean;
      settlement?: Settlement;
      routeId?: string;
    }>(`/sessions/${sessionId}/points`, {
      method: 'POST',
      body: JSON.stringify({ points }),
    }),

  endSession: (sessionId: string) =>
    request<{ ok: boolean }>(`/sessions/${sessionId}/end`, {
      method: 'POST',
      body: JSON.stringify({}),
    }),

  publicConfig: () => request<Record<string, unknown>>('/config/public'),
};
