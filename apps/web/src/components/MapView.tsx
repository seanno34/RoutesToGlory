import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Map, { Layer, Marker, Source } from 'react-map-gl/mapbox';
import type { MapRef } from 'react-map-gl/mapbox';
import { RESOURCE_MAP_ICONS } from '@empire/shared';
import type { AlienResourceId } from '@empire/shared';
import type { Settlement, MapResourceNode, FogOfWarConfig } from '../lib/api';
import {
  buildFogGeoJson,
  boundsFromMap,
  MIN_FOG_ZOOM,
  type FogFeatureCollection,
} from '../lib/fog-geojson';
import { filterByExplored, isPointExplored } from '../lib/exploration-utils';
import { isNearRoute } from '../lib/route-corridor';
import 'mapbox-gl/dist/mapbox-gl.css';

const TIER_COLOR: Record<string, string> = {
  goodie_hut: '#fbbf24',
  settlement: '#94a3b8',
  town: '#60a5fa',
  city: '#a78bfa',
  super_city: '#f472b6',
};

const LOCAL_RADIUS_M = 25_000;
const EMPTY_FOG: FogFeatureCollection = { type: 'FeatureCollection', features: [] };

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

interface MapViewProps {
  token: string;
  empireId?: string;
  settlements: Settlement[];
  resourceNodes: MapResourceNode[];
  exploredTileIds: string[];
  fogConfig: FogOfWarConfig;
  isRouting: boolean;
  canClaim: boolean;
  claimRadiusM: number;
  claimPath: Array<{ lat: number; lng: number }>;
  connectorPaths: Array<Array<{ lat: number; lng: number }>>;
  userLat?: number;
  userLng?: number;
  routePath?: Array<{ lat: number; lng: number }>;
  shimmerResourceIds?: Set<string>;
  onTapSettlement: (settlement: Settlement) => void;
  onTapResource: (resource: MapResourceNode) => void;
}

export function MapView({
  token,
  empireId,
  settlements,
  resourceNodes,
  exploredTileIds,
  fogConfig,
  isRouting,
  canClaim,
  claimRadiusM,
  claimPath,
  connectorPaths,
  userLat,
  userLng,
  routePath = [],
  shimmerResourceIds = new Set(),
  onTapSettlement,
  onTapResource,
}: MapViewProps) {
  const mapRef = useRef<MapRef>(null);
  const focusedRef = useRef(false);
  const zoomRef = useRef(MIN_FOG_ZOOM);
  const fogTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const exploredSet = useMemo(() => new Set(exploredTileIds), [exploredTileIds]);
  const [fogData, setFogData] = useState<FogFeatureCollection>(EMPTY_FOG);

  const focusPoint =
    userLat !== undefined && userLng !== undefined
      ? { lat: userLat, lng: userLng }
      : undefined;

  const visibleSettlements = useMemo(
    () => filterByExplored(settlements, exploredSet, fogConfig.tileSizeM),
    [settlements, exploredSet, fogConfig.tileSizeM],
  );

  const visibleResources = useMemo(() => {
    const explored = filterByExplored(resourceNodes, exploredSet, fogConfig.tileSizeM);
    if (!focusPoint) return explored;
    return explored.filter(
      (n) =>
        haversineM(focusPoint.lat, focusPoint.lng, Number(n.lat), Number(n.lng)) <=
        LOCAL_RADIUS_M,
    );
  }, [resourceNodes, focusPoint, exploredSet, fogConfig.tileSizeM]);

  const isClaimable = useCallback(
    (lat: number, lng: number) => {
      if (!canClaim || claimPath.length < 1) return false;
      if (!isPointExplored(lat, lng, exploredSet, fogConfig.tileSizeM)) return false;
      return isNearRoute(lat, lng, claimPath, claimRadiusM);
    },
    [canClaim, claimPath, claimRadiusM, exploredSet, fogConfig.tileSizeM],
  );

  const refreshFog = useCallback(() => {
    const map = mapRef.current?.getMap();
    if (!map || !map.isStyleLoaded() || !focusPoint) {
      setFogData(EMPTY_FOG);
      return;
    }

    zoomRef.current = map.getZoom();
    if (zoomRef.current < MIN_FOG_ZOOM) {
      setFogData(EMPTY_FOG);
      return;
    }

    const bounds = boundsFromMap(map);
    setFogData(buildFogGeoJson(exploredSet, bounds, fogConfig.tileSizeM, focusPoint));
  }, [exploredSet, fogConfig.tileSizeM, focusPoint]);

  const scheduleFogRefresh = useCallback(() => {
    if (fogTimerRef.current) clearTimeout(fogTimerRef.current);
    fogTimerRef.current = setTimeout(refreshFog, 150);
  }, [refreshFog]);

  useEffect(() => {
    scheduleFogRefresh();
    return () => {
      if (fogTimerRef.current) clearTimeout(fogTimerRef.current);
    };
  }, [scheduleFogRefresh, exploredTileIds, userLat, userLng]);

  useEffect(() => {
    const map = mapRef.current?.getMap();
    if (!map || !focusPoint || focusedRef.current) return;
    focusedRef.current = true;
    map.flyTo({
      center: [focusPoint.lng, focusPoint.lat],
      zoom: 11,
      duration: 800,
    });
  }, [focusPoint]);

  useEffect(() => {
    if (!isRouting || userLat === undefined || userLng === undefined) return;
    const map = mapRef.current?.getMap();
    if (!map) return;
    map.easeTo({
      center: [userLng, userLat],
      duration: 600,
    });
  }, [isRouting, userLat, userLng]);

  useEffect(() => {
    const map = mapRef.current?.getMap();
    if (!map) return;

    const onMoveEnd = () => scheduleFogRefresh();
    map.on('moveend', onMoveEnd);

    const container = map.getContainer().parentElement;
    const ro = container
      ? new ResizeObserver(() => mapRef.current?.resize())
      : null;
    if (container && ro) ro.observe(container);

    return () => {
      map.off('moveend', onMoveEnd);
      ro?.disconnect();
    };
  }, [scheduleFogRefresh]);

  const handleLoad = () => {
    mapRef.current?.getMap()?.resize();
    scheduleFogRefresh();
  };

  const lineGeoJson = useMemo(
    () => ({
      type: 'Feature' as const,
      geometry: {
        type: 'LineString' as const,
        coordinates: routePath.map((p) => [p.lng, p.lat]),
      },
      properties: {},
    }),
    [routePath],
  );

  const connectorGeoJson = useMemo(
    () => ({
      type: 'FeatureCollection' as const,
      features: connectorPaths.map((path, i) => ({
        type: 'Feature' as const,
        properties: { id: i },
        geometry: {
          type: 'LineString' as const,
          coordinates: path.map((p) => [p.lng, p.lat]),
        },
      })),
    }),
    [connectorPaths],
  );

  const fogOpacity = isRouting
    ? fogConfig.unexploredOpacity
    : Math.min(0.98, fogConfig.unexploredOpacity + 0.05);

  return (
    <Map
      ref={mapRef}
      mapboxAccessToken={token}
      initialViewState={{
        longitude: userLng ?? -105.853,
        latitude: userLat ?? 42.629,
        zoom: userLat && userLng ? 11 : 4,
      }}
      onLoad={handleLoad}
      style={{ width: '100%', height: '100%' }}
      mapStyle="mapbox://styles/mapbox/dark-v11"
    >
      {fogData.features.length > 0 && (
        <Source id="fog" type="geojson" data={fogData}>
          <Layer
            id="fog-fill"
            type="fill"
            paint={{
              'fill-color': '#0f172a',
              'fill-opacity': fogOpacity,
            }}
          />
        </Source>
      )}

      {routePath.length >= 2 && (
        <Source id="route" type="geojson" data={lineGeoJson}>
          <Layer
            id="route-line"
            type="line"
            paint={{
              'line-color': '#38bdf8',
              'line-width': 4,
              'line-opacity': 0.9,
            }}
          />
        </Source>
      )}

      {connectorPaths.length > 0 && (
        <Source id="connectors" type="geojson" data={connectorGeoJson}>
          <Layer
            id="connector-line"
            type="line"
            paint={{
              'line-color': '#38bdf8',
              'line-width': 4,
              'line-opacity': isRouting ? 0.55 : 0.9,
            }}
          />
        </Source>
      )}

      {visibleResources.map((node) => {
        const icon = RESOURCE_MAP_ICONS[node.resourceId as AlienResourceId];
        const shimmer = shimmerResourceIds.has(node.id);
        const lat = Number(node.lat);
        const lng = Number(node.lng);
        const isMine = Boolean(node.ownerEmpireId);
        const isOwnedMine = isMine && node.ownerEmpireId === empireId;
        const claimable = !isMine && isClaimable(lat, lng);
        return (
          <Marker key={node.id} longitude={lng} latitude={lat} anchor="center">
            {isOwnedMine ? (
              <div
                className="resource-mine-marker"
                style={{ borderColor: icon?.glowColor ?? '#22d3ee', boxShadow: `0 0 14px ${icon?.glowColor ?? '#22d3ee'}` }}
                title={`${icon?.emoji ?? '⛏️'} Extractor — +${node.yieldPerDay}/day ${node.resourceId.replace(/_/g, ' ')}`}
              >
                {icon?.emoji ?? '⛏️'}
              </div>
            ) : (
              <button
                type="button"
                className={`resource-marker${shimmer ? ' resource-shimmer' : ''}${claimable ? ' claimable' : ''}`}
                style={{ borderColor: icon?.glowColor ?? '#f97316' }}
                title={
                  claimable
                    ? `Tap to establish ${node.resourceId.replace(/_/g, ' ')} extractor mine`
                    : `${node.resourceId.replace(/_/g, ' ')} (${node.richness})`
                }
                disabled={!claimable}
                onClick={() => claimable && onTapResource(node)}
              >
                {icon?.emoji ?? '💎'}
              </button>
            )}
          </Marker>
        );
      })}

      {visibleSettlements.map((s) => {
        const lat = Number(s.lat);
        const lng = Number(s.lng);
        const owned = Boolean(s.owner_empire_id);
        const goodieFlag =
          s.is_goodie_hut === true ||
          s.is_goodie_hut === 1 ||
          String(s.is_goodie_hut) === '1';
        // One-time claim: owned settlements never reopen the goodie choice modal.
        const isUnclaimedGoodie =
          !owned && (goodieFlag || s.tier === 'goodie_hut');
        const alreadyOwned = owned;
        const claimable = !alreadyOwned && isClaimable(lat, lng);
        return (
          <Marker key={s.id} longitude={lng} latitude={lat} anchor="center">
            {isUnclaimedGoodie ? (
              <button
                type="button"
                className={`goodie-hut-marker${claimable ? ' claimable' : ''}`}
                title={claimable ? `Tap to claim ${s.name}` : `${s.name} — Goodie Hut`}
                disabled={!claimable}
                onClick={() => claimable && onTapSettlement(s)}
              >
                🎁
              </button>
            ) : (
              <button
                type="button"
                className={`settlement-marker${claimable ? ' claimable' : ''}`}
                style={{ borderColor: TIER_COLOR[s.tier] ?? '#fff' }}
                title={claimable ? `Tap to connect ${s.name}` : `${s.name} (${s.terrestrial_label})`}
                disabled={!claimable}
                onClick={() => claimable && onTapSettlement(s)}
              />
            )}
          </Marker>
        );
      })}

      {userLat !== undefined && userLng !== undefined && (
        <Marker longitude={userLng} latitude={userLat} anchor="center">
          <div className="user-marker" />
        </Marker>
      )}
    </Map>
  );
}
