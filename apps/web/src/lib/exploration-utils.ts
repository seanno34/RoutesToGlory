import { latLngToTileId } from '@empire/shared';

export function isPointExplored(
  lat: number,
  lng: number,
  explored: Set<string>,
  tileSizeM: number,
): boolean {
  return explored.has(latLngToTileId(lat, lng, tileSizeM));
}

export function filterByExplored<T extends { lat: number; lng: number }>(
  items: T[],
  explored: Set<string>,
  tileSizeM: number,
): T[] {
  return items.filter((item) =>
    isPointExplored(Number(item.lat), Number(item.lng), explored, tileSizeM),
  );
}
