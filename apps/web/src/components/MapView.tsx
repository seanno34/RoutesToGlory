import { useEffect, useRef } from 'react';
import Map, { Layer, Marker, Source } from 'react-map-gl/mapbox';
import type { MapRef } from 'react-map-gl/mapbox';
import type { Settlement } from '../lib/api';
import 'mapbox-gl/dist/mapbox-gl.css';

const TIER_COLOR: Record<string, string> = {
  goodie_hut: '#fbbf24',
  settlement: '#94a3b8',
  town: '#60a5fa',
  city: '#a78bfa',
  super_city: '#f472b6',
};

interface MapViewProps {
  token: string;
  settlements: Settlement[];
  userLat?: number;
  userLng?: number;
  routePath?: Array<{ lat: number; lng: number }>;
}

export function MapView({
  token,
  settlements,
  userLat,
  userLng,
  routePath = [],
}: MapViewProps) {
  const mapRef = useRef<MapRef>(null);

  useEffect(() => {
    const map = mapRef.current?.getMap();
    if (!map || settlements.length === 0) return;

    const lngs = settlements.map((s) => Number(s.lng));
    const lats = settlements.map((s) => Number(s.lat));
    const minLng = Math.min(...lngs);
    const maxLng = Math.max(...lngs);
    const minLat = Math.min(...lats);
    const maxLat = Math.max(...lats);

    map.fitBounds(
      [
        [minLng, minLat],
        [maxLng, maxLat],
      ],
      { padding: 48, maxZoom: 5, duration: 0 },
    );
  }, [settlements]);

  const handleLoad = () => {
    const map = mapRef.current?.getMap();
    if (!map) return;

    map.resize();

    const container = map.getContainer().parentElement;
    if (!container) return;

    const ro = new ResizeObserver(() => mapRef.current?.resize());
    ro.observe(container);
  };

  const lineGeoJson = {
    type: 'Feature' as const,
    geometry: {
      type: 'LineString' as const,
      coordinates: routePath.map((p) => [p.lng, p.lat]),
    },
    properties: {},
  };

  return (
    <Map
      ref={mapRef}
      mapboxAccessToken={token}
      initialViewState={{
        longitude: userLng ?? -98.5795,
        latitude: userLat ?? 39.8283,
        zoom: userLat && userLng ? 11 : 3.5,
      }}
      onLoad={handleLoad}
      style={{ width: '100%', height: '100%' }}
      mapStyle="mapbox://styles/mapbox/dark-v11"
    >
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

      {settlements.map((s) => (
        <Marker
          key={s.id}
          longitude={Number(s.lng)}
          latitude={Number(s.lat)}
          anchor="center"
        >
          <div
            className="settlement-marker"
            style={{ borderColor: TIER_COLOR[s.tier] ?? '#fff' }}
            title={`${s.name} (${s.terrestrial_label})`}
          />
        </Marker>
      ))}

      {userLat !== undefined && userLng !== undefined && (
        <Marker longitude={userLng} latitude={userLat} anchor="center">
          <div className="user-marker" />
        </Marker>
      )}
    </Map>
  );
}
