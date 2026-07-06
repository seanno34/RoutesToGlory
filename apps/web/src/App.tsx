import { useCallback, useEffect, useRef, useState } from 'react';
import {
  api,
  type BootstrapWorld,
  type ExplorationState,
  type FogOfWarConfig,
  type MapResourceNode,
  type Settlement,
} from './lib/api';
import {
  requestInitialPosition,
  startRouteSampler,
  stopRouteSampler,
  type GpsSample,
} from './lib/geolocation';
import { MapView } from './components/MapView';
import { ClaimModal } from './components/ClaimModal';
import type { ActiveRoute } from './lib/api';

const STORAGE_KEY = 'rtg.bootstrap';

const DEFAULT_FOG: FogOfWarConfig = {
  tileSizeM: 400,
  revealRadiusM: 150,
  startingVisionRadiusM: 2000,
  unexploredOpacity: 0.92,
  exploredOpacity: 0,
  resourceShimmerDurationMs: 8000,
};

function loadBootstrap(): BootstrapWorld | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as BootstrapWorld) : null;
  } catch {
    return null;
  }
}

function saveBootstrap(data: BootstrapWorld): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
}

function clearBootstrap(): void {
  localStorage.removeItem(STORAGE_KEY);
}

export function App() {
  const mapboxToken = import.meta.env.VITE_MAPBOX_TOKEN ?? '';
  const [bootstrap, setBootstrap] = useState<BootstrapWorld | null>(loadBootstrap);
  const [settlements, setSettlements] = useState<Settlement[]>([]);
  const [exploration, setExploration] = useState<ExplorationState | null>(null);
  const [fogConfig, setFogConfig] = useState<FogOfWarConfig>(DEFAULT_FOG);
  const [pollIntervalMs, setPollIntervalMs] = useState(3_000);
  const [shimmerIds, setShimmerIds] = useState<Set<string>>(new Set());
  const shimmerTimers = useRef<Map<string, ReturnType<typeof setTimeout>>>(new Map());
  const [userPos, setUserPos] = useState<{ lat: number; lng: number } | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [routePath, setRoutePath] = useState<Array<{ lat: number; lng: number }>>([]);
  const [status, setStatus] = useState('Welcome, Explorer.');
  const [isRouting, setIsRouting] = useState(false);
  const [loading, setLoading] = useState(false);
  const [activeRoutes, setActiveRoutes] = useState<ActiveRoute[]>([]);
  const [connectorPaths, setConnectorPaths] = useState<
    Array<Array<{ lat: number; lng: number }>>
  >([]);
  const [claimRadiusM, setClaimRadiusM] = useState(1_000);
  const [pendingGoodie, setPendingGoodie] = useState<Settlement | null>(null);

  const triggerShimmer = useCallback((resourceIds: string[]) => {
    if (resourceIds.length === 0) return;
    setShimmerIds((prev) => {
      const next = new Set(prev);
      for (const id of resourceIds) next.add(id);
      return next;
    });
    for (const id of resourceIds) {
      const existing = shimmerTimers.current.get(id);
      if (existing) clearTimeout(existing);
      shimmerTimers.current.set(
        id,
        setTimeout(() => {
          setShimmerIds((prev) => {
            const next = new Set(prev);
            next.delete(id);
            return next;
          });
          shimmerTimers.current.delete(id);
        }, fogConfig.resourceShimmerDurationMs),
      );
    }
  }, [fogConfig.resourceShimmerDurationMs]);

  const loadExploration = useCallback(
    async (worldId: string, empireId: string) => {
      const state = await api.getExploration(worldId, empireId);
      setExploration(state);
      if (state.fogOfWar) setFogConfig(state.fogOfWar);
      const accrued = (state as { mineYieldAccrued?: Record<string, number> }).mineYieldAccrued;
      if (accrued && Object.keys(accrued).length > 0) {
        const parts = Object.entries(accrued).map(
          ([id, amt]) => `+${amt} ${id.replace(/_/g, ' ')}`,
        );
        setStatus((prev) =>
          prev.startsWith('Established') || prev.startsWith('Founded') || prev.startsWith('Claimed')
            ? `${prev} (${parts.join(', ')} mined)`
            : `Extractor mines produced ${parts.join(', ')}.`,
        );
      }
      return state;
    },
    [],
  );

  const loadMap = useCallback(
    async (worldId: string, empireId: string) => {
      const map = await api.getWorldMap(worldId);
      setSettlements(map.settlements);
      const routes = map.routes
        .filter((r) => r.status === 'active')
        .map((r) => ({
          ...r,
          path_json:
            typeof r.path_json === 'string'
              ? (JSON.parse(r.path_json) as ActiveRoute['path_json'])
              : r.path_json,
        }));
      setActiveRoutes(routes);
      setConnectorPaths(routes.map((r) => r.path_json).filter((p) => p.length >= 2));
      await loadExploration(worldId, empireId);
    },
    [loadExploration],
  );

  useEffect(() => {
    void api
      .publicConfig()
      .then((cfg) => {
        if (cfg.fogOfWar) setFogConfig(cfg.fogOfWar);
        if (cfg.sampling?.pollIntervalMs) setPollIntervalMs(cfg.sampling.pollIntervalMs);
        if (cfg.routes?.minConnectDistanceM) setClaimRadiusM(cfg.routes.minConnectDistanceM);
      })
      .catch((e) => {
        setStatus(e instanceof Error ? e.message : 'Could not reach game API.');
      });
  }, []);

  useEffect(() => {
    void requestInitialPosition()
      .then((pos) => setUserPos({ lat: pos.lat, lng: pos.lng }))
      .catch(() => setStatus('Enable location to play Routes to Glory.'));
  }, []);

  useEffect(() => {
    if (bootstrap?.id && bootstrap.empireId) {
      void loadMap(bootstrap.id, bootstrap.empireId).catch((e) =>
        setStatus(e instanceof Error ? e.message : 'Failed to load map'),
      );
    }
  }, [bootstrap, loadMap]);

  const createWorldAtGps = async () => {
    const pos = await requestInitialPosition();
    setUserPos({ lat: pos.lat, lng: pos.lng });
    const world = await api.createWorld({
      name: 'Survey World',
      playerName: 'Explorer',
      spawnLat: pos.lat,
      spawnLng: pos.lng,
    });
    saveBootstrap(world);
    setBootstrap(world);
    setRoutePath([]);
    setConnectorPaths([]);
    setSessionId(null);
    setIsRouting(false);
    stopRouteSampler();
    await loadMap(world.id, world.empireId);
    return world;
  };

  const handleNewWorld = async () => {
    setLoading(true);
    try {
      const world = await createWorldAtGps();
      setStatus(
        `World ready — ${world.settlementCount} sites (30 metros + local area). Begin a route to explore.`,
      );
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Failed to create world');
    } finally {
      setLoading(false);
    }
  };

  const handleResetWorld = async () => {
    setLoading(true);
    try {
      clearBootstrap();
      setBootstrap(null);
      setSettlements([]);
      setExploration(null);
      setRoutePath([]);
      setConnectorPaths([]);
      setSessionId(null);
      setIsRouting(false);
      stopRouteSampler();

      const world = await createWorldAtGps();
      setStatus(
        `New test world — ${world.settlementCount} sites, local goodie huts & resources seeded.`,
      );
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Failed to reset world');
    } finally {
      setLoading(false);
    }
  };

  const handleBeginRoute = async () => {
    if (!bootstrap || !userPos) return;
    setLoading(true);
    try {
      const { sessionId: id } = await api.beginSession({
        worldId: bootstrap.id,
        empireId: bootstrap.empireId,
        lat: userPos.lat,
        lng: userPos.lng,
      });
      setSessionId(id);
      setRoutePath([{ lat: userPos.lat, lng: userPos.lng }]);
      setIsRouting(true);
      setStatus('Route active — the map reveals as you travel.');

      startRouteSampler(
        {
          pollIntervalMs,
          onSample: async (sample: GpsSample) => {
          setUserPos({ lat: sample.lat, lng: sample.lng });
          setRoutePath((path) => [...path, { lat: sample.lat, lng: sample.lng }]);
          try {
            const result = await api.appendPoints(id, [
              {
                lat: sample.lat,
                lng: sample.lng,
                accuracyM: sample.accuracyM,
                speedMps: sample.speedMps,
                recordedAt: sample.recordedAt,
              },
            ]);

            if (result.exploration?.newResourceNodeIds.length) {
              triggerShimmer(result.exploration.newResourceNodeIds);
            }

            if (bootstrap.empireId && result.exploration) {
              const { newlyRevealedTileIds, newResourceNodeIds } = result.exploration;
              if (newlyRevealedTileIds.length > 0 || newResourceNodeIds.length > 0) {
                setExploration((prev) => {
                  if (!prev) return prev;
                  const explored = new Set(prev.exploredTileIds);
                  for (const t of newlyRevealedTileIds) explored.add(t);
                  return {
                    ...prev,
                    exploredTileIds: [...explored],
                  };
                });
              }
              if (newResourceNodeIds.length > 0) {
                await loadExploration(bootstrap.id, bootstrap.empireId);
              }
              if (newlyRevealedTileIds.length > 0) {
                setStatus(`Revealed ${newlyRevealedTileIds.length} new tiles.`);
              }
            }

            if (result.connected && result.settlement) {
              setStatus(`Connected to ${result.settlement.name}!`);
              stopRouteSampler();
              setIsRouting(false);
              setSessionId(null);
              await loadMap(bootstrap.id, bootstrap.empireId);
            }
          } catch (err) {
            setStatus(err instanceof Error ? err.message : 'Failed to sync GPS');
          }
        },
        onError: (msg: string) => setStatus(msg),
        },
      );
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Failed to begin route');
    } finally {
      setLoading(false);
    }
  };

  const handleEndRoute = async () => {
    stopRouteSampler();
    setIsRouting(false);
    if (sessionId) {
      await api.endSession(sessionId);
      setSessionId(null);
    }
    setStatus('Route ended.');
  };

  const claimPath = routePath.length > 0
    ? routePath
    : activeRoutes.flatMap((r) => r.path_json ?? []);

  const canClaim = isRouting || activeRoutes.length > 0;

  const runClaim = async (
    targetKind: 'settlement' | 'resource',
    targetId: string,
    goodieChoice?: 'found_town' | 'claim_reward',
  ) => {
    if (!bootstrap || claimPath.length < 1) return;
    setLoading(true);
    try {
      const result = await api.claimNearRoute(bootstrap.id, {
        empireId: bootstrap.empireId,
        sessionId: sessionId ?? undefined,
        routePath: claimPath,
        playerLat: userPos?.lat,
        playerLng: userPos?.lng,
        targetKind,
        targetId,
        goodieChoice,
      });
      setStatus(result.message);
      await loadMap(bootstrap.id, bootstrap.empireId);
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Claim failed');
    } finally {
      setLoading(false);
      setPendingGoodie(null);
    }
  };

  const handleTapSettlement = (settlement: Settlement) => {
    if (settlement.is_goodie_hut || settlement.tier === 'goodie_hut') {
      setPendingGoodie(settlement);
      return;
    }
    void runClaim('settlement', settlement.id);
  };

  const handleTapResource = (resource: MapResourceNode) => {
    void runClaim('resource', resource.id);
  };

  if (!mapboxToken) {
    return (
      <div className="shell">
        <header>
          <h1>Routes to Glory</h1>
        </header>
        <p className="status">
          Set <code>VITE_MAPBOX_TOKEN</code> in <code>apps/web/.env</code> to load the map.
        </p>
      </div>
    );
  }

  const resourceNodes: MapResourceNode[] = exploration?.resourceNodes ?? [];

  return (
    <div className="shell">
      <header>
        <h1>Routes to Glory</h1>
        <p className="subtitle">Survey Worlds · Real routes · Eternal empires</p>
      </header>

      <div className="map-container">
        <MapView
          key={bootstrap?.id ?? 'no-world'}
          token={mapboxToken}
          empireId={bootstrap?.empireId}
          settlements={settlements}
          resourceNodes={resourceNodes}
          exploredTileIds={exploration?.exploredTileIds ?? []}
          fogConfig={fogConfig}
          isRouting={isRouting}
          canClaim={canClaim}
          claimRadiusM={claimRadiusM}
          claimPath={claimPath}
          connectorPaths={connectorPaths}
          userLat={userPos?.lat}
          userLng={userPos?.lng}
          routePath={routePath}
          shimmerResourceIds={shimmerIds}
          onTapSettlement={handleTapSettlement}
          onTapResource={handleTapResource}
        />
      </div>

      {pendingGoodie && (
        <ClaimModal
          settlementName={pendingGoodie.name}
          onFoundTown={() => void runClaim('settlement', pendingGoodie.id, 'found_town')}
          onClaimReward={() => void runClaim('settlement', pendingGoodie.id, 'claim_reward')}
          onCancel={() => setPendingGoodie(null)}
        />
      )}

      <p className="status">{status}</p>

      <div className="actions">
        {!bootstrap && (
          <button type="button" disabled={loading} onClick={() => void handleNewWorld()}>
            Start New World
          </button>
        )}
        {bootstrap && !isRouting && (
          <>
            <button type="button" disabled={loading || !userPos} onClick={() => void handleBeginRoute()}>
              Begin Route
            </button>
            <button
              type="button"
              className="secondary"
              disabled={loading || !userPos}
              onClick={() => void handleResetWorld()}
            >
              New World
            </button>
          </>
        )}
        {isRouting && (
          <button type="button" className="danger" onClick={() => void handleEndRoute()}>
            End Route
          </button>
        )}
      </div>
    </div>
  );
}
