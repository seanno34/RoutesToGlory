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
    const text = await res.text();
    let message = res.statusText;
    try {
      const err = JSON.parse(text) as { error?: string };
      if (err.error) message = err.error;
    } catch {
      if (res.status === 503) {
        message =
          'Game API is offline. In SPanel → NodeJS Manager → rtg_api → Restart, then check View Logs.';
      }
    }
    throw new Error(message || `Request failed (${res.status})`);
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
  owner_empire_id?: string | null;
  lat: number;
  lng: number;
  geofence_radius_m: number;
}

export interface MapResourceNode {
  id: string;
  worldId: string;
  tileId: string;
  resourceId: string;
  lat: number;
  lng: number;
  richness: string;
  yieldPerDay: number;
  ownerEmpireId?: string;
  routeId?: string;
  iconSpriteId: string;
  glowColor: string;
}

export interface FogOfWarConfig {
  tileSizeM: number;
  revealRadiusM: number;
  startingVisionRadiusM: number;
  unexploredOpacity: number;
  exploredOpacity: number;
  resourceShimmerDurationMs: number;
}

export interface ExplorationState {
  worldId: string;
  empireId: string;
  exploredTileIds: string[];
  resourceNodes: MapResourceNode[];
  fogOfWar: FogOfWarConfig;
}

export interface ActiveRoute {
  id: string;
  path_json: Array<{ lat: number; lng: number }>;
  from_settlement_id: string;
  to_settlement_id: string;
  status: string;
}

export interface WorldMap {
  settlements: Settlement[];
  routes: ActiveRoute[];
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

  getExploration: (worldId: string, empireId: string) =>
    request<ExplorationState>(`/worlds/${worldId}/exploration/${empireId}`),

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
      exploration?: {
        newlyRevealedTileIds: string[];
        newResourceNodeIds: string[];
      };
    }>(`/sessions/${sessionId}/points`, {
      method: 'POST',
      body: JSON.stringify({ points }),
    }),

  endSession: (sessionId: string) =>
    request<{ ok: boolean }>(`/sessions/${sessionId}/end`, {
      method: 'POST',
      body: JSON.stringify({}),
    }),

  publicConfig: () =>
    request<{
      fogOfWar: FogOfWarConfig;
      sampling: { pollIntervalMs: number };
      routes: { minConnectDistanceM: number };
    }>('/config/public'),

  claimNearRoute: (
    worldId: string,
    body: {
      empireId: string;
      sessionId?: string;
      routePath: Array<{ lat: number; lng: number }>;
      playerLat?: number;
      playerLng?: number;
      targetKind: 'settlement' | 'resource';
      targetId: string;
      goodieChoice?: 'found_town' | 'claim_reward';
    },
  ) =>
    request<{
      ok: boolean;
      message: string;
      connectorRouteId: string;
      linkedRouteId?: string;
      connectorPath: Array<{ lat: number; lng: number }>;
      reward?: unknown;
    }>(`/worlds/${worldId}/claim`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
};
