import { useCallback, useEffect, useState } from 'react';
import { api, type BootstrapWorld, type Settlement } from './lib/api';
import {
  requestInitialPosition,
  startRouteSampler,
  stopRouteSampler,
  type GpsSample,
} from './lib/geolocation';
import { MapView } from './components/MapView';

const STORAGE_KEY = 'rtg.bootstrap';

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

export function App() {
  const mapboxToken = import.meta.env.VITE_MAPBOX_TOKEN ?? '';
  const [bootstrap, setBootstrap] = useState<BootstrapWorld | null>(loadBootstrap);
  const [settlements, setSettlements] = useState<Settlement[]>([]);
  const [userPos, setUserPos] = useState<{ lat: number; lng: number } | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [routePath, setRoutePath] = useState<Array<{ lat: number; lng: number }>>([]);
  const [status, setStatus] = useState('Welcome, Explorer.');
  const [isRouting, setIsRouting] = useState(false);
  const [loading, setLoading] = useState(false);

  const loadMap = useCallback(async (worldId: string) => {
    const map = await api.getWorldMap(worldId);
    setSettlements(map.settlements);
  }, []);

  useEffect(() => {
    void requestInitialPosition()
      .then((pos) => setUserPos({ lat: pos.lat, lng: pos.lng }))
      .catch(() => setStatus('Enable location to play Routes to Glory.'));
  }, []);

  useEffect(() => {
    if (bootstrap?.id) {
      void loadMap(bootstrap.id).catch((e) =>
        setStatus(e instanceof Error ? e.message : 'Failed to load map'),
      );
    }
  }, [bootstrap, loadMap]);

  const handleNewWorld = async () => {
    setLoading(true);
    try {
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
      await loadMap(world.id);
      setStatus(`World ready — ${world.settlementCount} Echo Sites seeded.`);
    } catch (e) {
      setStatus(e instanceof Error ? e.message : 'Failed to create world');
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
      setStatus('Route active — keep this app open while traveling.');

      startRouteSampler({
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
            if (result.connected && result.settlement) {
              setStatus(`Connected to ${result.settlement.name}!`);
              stopRouteSampler();
              setIsRouting(false);
              setSessionId(null);
              await loadMap(bootstrap.id);
            }
          } catch (err) {
            setStatus(err instanceof Error ? err.message : 'Failed to sync GPS');
          }
        },
        onError: (msg: string) => setStatus(msg),
      });
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

  return (
    <div className="shell">
      <header>
        <h1>Routes to Glory</h1>
        <p className="subtitle">Survey Worlds · Real routes · Eternal empires</p>
      </header>

      <div className="map-container">
        <MapView
          token={mapboxToken}
          settlements={settlements}
          userLat={userPos?.lat}
          userLng={userPos?.lng}
          routePath={routePath}
        />
      </div>

      <p className="status">{status}</p>

      <div className="actions">
        {!bootstrap && (
          <button type="button" disabled={loading} onClick={() => void handleNewWorld()}>
            Start New World
          </button>
        )}
        {bootstrap && !isRouting && (
          <button type="button" disabled={loading || !userPos} onClick={() => void handleBeginRoute()}>
            Begin Route
          </button>
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
