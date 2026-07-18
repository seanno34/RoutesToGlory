import { useCallback, useEffect, useRef, useState } from 'react';
import {
  api,
  type BootstrapWorld,
  type ExplorationState,
  type FogOfWarConfig,
  type MapResourceNode,
  type SavedWorldSummary,
  type Settlement,
} from './lib/api';
import {
  requestInitialPosition,
  startRouteSampler,
  stopRouteSampler,
  forceRefreshPosition,
  type GpsSample,
} from './lib/geolocation';
import { MapView } from './components/MapView';
import { ClaimModal } from './components/ClaimModal';
import { decimatePath } from './lib/route-corridor';
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

function formatSavedGameLabel(world: SavedWorldSummary): string {
  const date = new Date(world.createdAt).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });
  return `${world.accessCode} — ${world.name} (${date})`;
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
  const [savedWorlds, setSavedWorlds] = useState<SavedWorldSummary[]>([]);

  const refreshSavedWorlds = useCallback(async () => {
    try {
      const { worlds } = await api.listSavedWorlds();
      setSavedWorlds(worlds);
    } catch {
      /* list is optional when API offline */
    }
  }, []);

  const resumeGame = useCallback(async (accessCode: string) => {
    if (!accessCode) return;
    setLoading(true);
    try {
      const world = await api.getWorldByCode(accessCode);
      saveBootstrap(world);
      window.location.reload();
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Failed to resume game');
      setLoading(false);
    }
  }, []);

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
        .map((r) => {
          const raw =
            typeof r.path_json === 'string'
              ? (JSON.parse(r.path_json) as ActiveRoute['path_json'])
              : r.path_json;
          const path_json = Array.isArray(raw)
            ? raw.map((p) => ({ lat: Number(p.lat), lng: Number(p.lng) }))
            : [];
          return { ...r, path_json };
        });
      setActiveRoutes(routes);
      setConnectorPaths(routes.map((r) => r.path_json).filter((p) => p.length >= 2));
      await loadExploration(worldId, empireId);
    },
    [loadExploration],
  );

  useEffect(() => {
    void refreshSavedWorlds();
  }, [refreshSavedWorlds]);

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
      await refreshSavedWorlds();
      setStatus(
        `World ready — code ${world.accessCode ?? '???'} · ${world.settlementCount} sites. Save the code or pick it from Resume below.`,
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
      await refreshSavedWorlds();
      setStatus(
        `New test world — code ${world.accessCode ?? '???'} · ${world.settlementCount} sites seeded.`,
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
          minDistanceM: 15,
          onSample: async (sample: GpsSample) => {
            try {
              await applyGpsSample(sample, id);
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
    const endingSessionId = sessionId;
    const pathSnapshot = [...routePath];

    stopRouteSampler();
    setIsRouting(false);

    if (endingSessionId && bootstrap) {
      setLoading(true);
      try {
        if (userPos) {
          try {
            const sample = await forceRefreshPosition();
            await api.appendPoints(endingSessionId, [
              {
                lat: sample.lat,
                lng: sample.lng,
                accuracyM: sample.accuracyM,
                speedMps: sample.speedMps,
                recordedAt: sample.recordedAt,
              },
            ]);
            pathSnapshot.push({ lat: sample.lat, lng: sample.lng });
          } catch {
            /* use path already on screen */
          }
        }

        const result = await api.endSession(
          endingSessionId,
          pathSnapshot.length >= 2 ? pathSnapshot : undefined,
        );
        setSessionId(null);
        setRoutePath([]);

        if (result.saved) {
          await loadMap(bootstrap.id, bootstrap.empireId);
          setStatus('Route saved to your map.');
        } else {
          setStatus(
            result.reason === 'too_few_points'
              ? 'Route ended — not enough GPS points to save.'
              : 'Route ended — could not save to map.',
          );
        }
      } catch (e) {
        setSessionId(null);
        setRoutePath([]);
        setStatus(e instanceof Error ? e.message : 'Failed to end route');
      } finally {
        setLoading(false);
      }
      return;
    }

    setSessionId(null);
    setRoutePath([]);
    setStatus('Route ended.');
  };

  const applyGpsSample = useCallback(
    async (sample: GpsSample, activeSessionId?: string | null) => {
      if (!bootstrap) return;

      setUserPos({ lat: sample.lat, lng: sample.lng });

      const sid = activeSessionId ?? sessionId;
      if (!sid) return;

      setRoutePath((path) => [...path, { lat: sample.lat, lng: sample.lng }]);

      const result = await api.appendPoints(sid, [
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

      await loadExploration(bootstrap.id, bootstrap.empireId);

      const revealed = result.exploration?.newlyRevealedTileIds.length ?? 0;
      if (revealed > 0) {
        setStatus(`Revealed ${revealed} new tiles.`);
      }

      if (result.connected && result.settlement) {
        setStatus(`Connected to ${result.settlement.name}!`);
        stopRouteSampler();
        setIsRouting(false);
        setSessionId(null);
        await loadMap(bootstrap.id, bootstrap.empireId);
      }
    },
    [bootstrap, sessionId, triggerShimmer, loadExploration, loadMap],
  );

  const handleRefreshLocation = async () => {
    if (!bootstrap) return;
    setLoading(true);
    try {
      const sample = await forceRefreshPosition();
      setUserPos({ lat: sample.lat, lng: sample.lng });

      if (isRouting && sessionId) {
        await applyGpsSample(sample, sessionId);
        setStatus(
          `Location updated (${sample.accuracyM.toFixed(0)}m accuracy) — fog synced.`,
        );
      } else if (isRouting) {
        setStatus('Location updated — begin route again to sync exploration.');
      } else {
        setStatus(
          `Location updated (${sample.accuracyM.toFixed(0)}m accuracy). Begin a route to reveal fog while traveling.`,
        );
      }
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Could not refresh GPS');
    } finally {
      setLoading(false);
    }
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
    if (!bootstrap || !canClaim) return;
    setLoading(true);
    try {
      const result = await api.claimNearRoute(bootstrap.id, {
        empireId: bootstrap.empireId,
        sessionId: sessionId ?? undefined,
        useNetworkRoutes: true,
        routePath: claimPath.length > 0 ? decimatePath(claimPath, 64) : undefined,
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
    const owned = Boolean(settlement.owner_empire_id);
    const goodieFlag =
      settlement.is_goodie_hut === true ||
      (settlement.is_goodie_hut as unknown) === 1 ||
      String(settlement.is_goodie_hut) === '1';
    const isUnclaimedGoodie =
      !owned && (goodieFlag || settlement.tier === 'goodie_hut');
    if (isUnclaimedGoodie) {
      setPendingGoodie(settlement);
      return;
    }
    if (owned && (goodieFlag || settlement.tier === 'goodie_hut')) {
      setStatus(`${settlement.name} has already been claimed.`);
      return;
    }
    void runClaim('settlement', settlement.id);
  };

  const handleTapResource = (resource: MapResourceNode) => {
    void runClaim('resource', resource.id);
  };

  const activeAccessCode =
    bootstrap?.accessCode ??
    savedWorlds.find((w) => w.id === bootstrap?.id)?.accessCode;

  const resumeDropdown = (
    <label className="resume-field">
      <span className="resume-label">Resume saved game</span>
      <select
        className="game-select"
        disabled={loading || savedWorlds.length === 0}
        value=""
        onChange={(e) => {
          const code = e.target.value;
          if (code) void resumeGame(code);
        }}
      >
        <option value="">
          {savedWorlds.length === 0 ? 'No saved games yet' : 'Choose a game…'}
        </option>
        {savedWorlds.map((world) => (
          <option key={world.id} value={world.accessCode}>
            {formatSavedGameLabel(world)}
          </option>
        ))}
      </select>
    </label>
  );

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
        {activeAccessCode && (
          <p className="game-code">Active game: <strong>{activeAccessCode}</strong></p>
        )}
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
          <>
            <button type="button" disabled={loading} onClick={() => void handleNewWorld()}>
              Start New World
            </button>
          </>
        )}
        {bootstrap && !isRouting && (
          <>
            <button type="button" disabled={loading || !userPos} onClick={() => void handleBeginRoute()}>
              Begin Route
            </button>
            <button
              type="button"
              className="secondary"
              disabled={loading}
              onClick={() => void handleRefreshLocation()}
            >
              Refresh Location
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
          <>
            <button
              type="button"
              className="secondary"
              disabled={loading}
              onClick={() => void handleRefreshLocation()}
            >
              Refresh Location
            </button>
            <button type="button" className="danger" onClick={() => void handleEndRoute()}>
              End Route
            </button>
          </>
        )}
      </div>

      {!isRouting && resumeDropdown}
    </div>
  );
}
