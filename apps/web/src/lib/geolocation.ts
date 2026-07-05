import type { GameConfig } from '@empire/shared';

export interface GpsSample {
  lat: number;
  lng: number;
  accuracyM: number;
  speedMps?: number;
  recordedAt: string;
}

export interface SamplerOptions {
  onSample: (sample: GpsSample) => void;
  onError?: (message: string) => void;
  pollIntervalMs?: number;
  minDistanceM?: number;
}

let watchId: number | null = null;
let pollTimer: ReturnType<typeof setInterval> | null = null;
let lastSample: { lat: number; lng: number } | null = null;

function haversineM(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const R = 6_371_000;
  const toRad = (d: number) => (d * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLng = toRad(lng2 - lng1);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(a));
}

function getCurrentPosition(): Promise<GeolocationPosition> {
  return new Promise((resolve, reject) => {
    navigator.geolocation.getCurrentPosition(resolve, reject, {
      enableHighAccuracy: false,
      maximumAge: 30_000,
      timeout: 10_000,
    });
  });
}

async function pollOnce(
  options: SamplerOptions,
  minDistanceM: number,
): Promise<void> {
  try {
    const pos = await getCurrentPosition();
    const lat = pos.coords.latitude;
    const lng = pos.coords.longitude;
    const accuracyM = pos.coords.accuracy;
    const speedMps = pos.coords.speed ?? undefined;

    if (lastSample) {
      const dist = haversineM(lastSample.lat, lastSample.lng, lat, lng);
      if (dist < minDistanceM && (speedMps ?? 0) < 0.5) {
        return;
      }
      if (dist < 30 && (speedMps ?? 0) < 0.5) {
        return;
      }
    }

    lastSample = { lat, lng };
    options.onSample({
      lat,
      lng,
      accuracyM,
      speedMps,
      recordedAt: new Date().toISOString(),
    });
  } catch {
    options.onError?.('Unable to read GPS location');
  }
}

export function startRouteSampler(
  options: SamplerOptions,
  config?: Pick<GameConfig['sampling'], 'pollIntervalMs' | 'minDistanceM'>,
): void {
  stopRouteSampler();
  const pollIntervalMs = options.pollIntervalMs ?? config?.pollIntervalMs ?? 60_000;
  const minDistanceM = options.minDistanceM ?? config?.minDistanceM ?? 100;

  void pollOnce(options, minDistanceM);
  pollTimer = setInterval(() => {
    void pollOnce(options, minDistanceM);
  }, pollIntervalMs);
}

export function stopRouteSampler(): void {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
  if (watchId !== null) {
    navigator.geolocation.clearWatch(watchId);
    watchId = null;
  }
  lastSample = null;
}

export function requestInitialPosition(): Promise<GpsSample> {
  return getCurrentPosition().then((pos) => ({
    lat: pos.coords.latitude,
    lng: pos.coords.longitude,
    accuracyM: pos.coords.accuracy,
    speedMps: pos.coords.speed ?? undefined,
    recordedAt: new Date().toISOString(),
  }));
}
