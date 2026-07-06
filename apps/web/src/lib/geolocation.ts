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

interface PositionOptions {
  forceFresh?: boolean;
  highAccuracy?: boolean;
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

function positionToSample(pos: GeolocationPosition): GpsSample {
  return {
    lat: pos.coords.latitude,
    lng: pos.coords.longitude,
    accuracyM: pos.coords.accuracy,
    speedMps: pos.coords.speed ?? undefined,
    recordedAt: new Date().toISOString(),
  };
}

function getCurrentPosition(options: PositionOptions = {}): Promise<GeolocationPosition> {
  const { forceFresh = false, highAccuracy = false } = options;
  return new Promise((resolve, reject) => {
    navigator.geolocation.getCurrentPosition(resolve, reject, {
      enableHighAccuracy: highAccuracy,
      maximumAge: forceFresh ? 0 : 2_000,
      timeout: forceFresh ? 15_000 : 10_000,
    });
  });
}

async function pollOnce(
  options: SamplerOptions,
  minDistanceM: number,
  highAccuracy: boolean,
): Promise<void> {
  try {
    const pos = await getCurrentPosition({ highAccuracy });
    const sample = positionToSample(pos);
    const { lat, lng, speedMps } = sample;

    if (lastSample) {
      const dist = haversineM(lastSample.lat, lastSample.lng, lat, lng);
      const moving = (speedMps ?? 0) >= 0.5;
      if (!moving && dist < minDistanceM) {
        return;
      }
      if (!moving && dist < 15) {
        return;
      }
    }

    lastSample = { lat, lng };
    options.onSample(sample);
  } catch {
    options.onError?.('Unable to read GPS location');
  }
}

export function startRouteSampler(
  options: SamplerOptions,
  config?: Pick<GameConfig['sampling'], 'pollIntervalMs' | 'minDistanceM'>,
): void {
  stopRouteSampler();
  const pollIntervalMs = options.pollIntervalMs ?? config?.pollIntervalMs ?? 3_000;
  const minDistanceM = options.minDistanceM ?? config?.minDistanceM ?? 15;

  void pollOnce(options, minDistanceM, true);
  pollTimer = setInterval(() => {
    void pollOnce(options, minDistanceM, true);
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

/** Force a fresh GPS fix (bypasses browser position cache). */
export async function forceRefreshPosition(): Promise<GpsSample> {
  const pos = await getCurrentPosition({ forceFresh: true, highAccuracy: true });
  const sample = positionToSample(pos);
  lastSample = { lat: sample.lat, lng: sample.lng };
  return sample;
}

export function requestInitialPosition(): Promise<GpsSample> {
  return getCurrentPosition({ highAccuracy: true }).then(positionToSample);
}
