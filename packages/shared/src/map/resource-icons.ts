import type { AlienResourceId } from '../types/enums.js';

/**
 * Map-embeddable icon definitions for each resource.
 * `svgPath` renders inside a 24×24 viewBox for Mapbox symbol layers or HTML markers.
 */
export interface ResourceMapIcon {
  resourceId: AlienResourceId;
  spriteId: string;
  glowColor: string;
  shimmerColor: string;
  emoji: string;
  svgPath: string;
}

export const RESOURCE_MAP_ICONS: Record<AlienResourceId, ResourceMapIcon> = {
  xenite: {
    resourceId: 'xenite',
    spriteId: 'res-xenite',
    glowColor: '#f97316',
    shimmerColor: '#fdba74',
    emoji: '⛽',
    svgPath: 'M12 2 L16 10 L14 22 L10 22 L8 10 Z M12 6 L10 14 L14 14 Z',
  },
  solari_dust: {
    resourceId: 'solari_dust',
    spriteId: 'res-solari',
    glowColor: '#eab308',
    shimmerColor: '#fef08a',
    emoji: '✨',
    svgPath: 'M12 2 L14 8 L20 8 L15 12 L17 20 L12 16 L7 20 L9 12 L4 8 L10 8 Z',
  },
  ferracite: {
    resourceId: 'ferracite',
    spriteId: 'res-ferracite',
    glowColor: '#78716c',
    shimmerColor: '#d6d3d1',
    emoji: '🪨',
    svgPath: 'M4 18 L8 8 L14 6 L20 14 L16 22 L6 22 Z',
  },
  lumin_spring: {
    resourceId: 'lumin_spring',
    spriteId: 'res-lumin',
    glowColor: '#06b6d4',
    shimmerColor: '#67e8f9',
    emoji: '💧',
    svgPath: 'M12 3 C8 9 5 12 5 16 A7 7 0 0 0 19 16 C19 12 16 9 12 3 Z',
  },
  quantium_shard: {
    resourceId: 'quantium_shard',
    spriteId: 'res-quantium',
    glowColor: '#8b5cf6',
    shimmerColor: '#c4b5fd',
    emoji: '🔮',
    svgPath: 'M12 2 L18 8 L15 22 L9 22 L6 8 Z M12 6 L9 14 L15 14 Z',
  },
  voidglass: {
    resourceId: 'voidglass',
    spriteId: 'res-voidglass',
    glowColor: '#6366f1',
    shimmerColor: '#a5b4fc',
    emoji: '🌌',
    svgPath: 'M4 12 A8 8 0 0 1 20 12 A8 8 0 0 1 4 12 M8 12 A4 4 0 0 0 16 12',
  },
  mycelium_core: {
    resourceId: 'mycelium_core',
    spriteId: 'res-mycelium',
    glowColor: '#22c55e',
    shimmerColor: '#86efac',
    emoji: '🍄',
    svgPath: 'M6 14 Q12 4 18 14 L20 18 Q12 16 4 18 Z M10 18 L10 22 M14 18 L14 22',
  },
  chrono_moss: {
    resourceId: 'chrono_moss',
    spriteId: 'res-chrono',
    glowColor: '#14b8a6',
    shimmerColor: '#5eead4',
    emoji: '⏳',
    svgPath: 'M12 2 A10 10 0 1 1 12 22 M12 6 L12 12 L16 14',
  },
  aegis_bark: {
    resourceId: 'aegis_bark',
    spriteId: 'res-aegis',
    glowColor: '#b45309',
    shimmerColor: '#fcd34d',
    emoji: '🛡️',
    svgPath: 'M12 2 L20 6 L20 14 Q20 20 12 22 Q4 20 4 14 L4 6 Z M12 8 L12 16',
  },
  nebula_pearl: {
    resourceId: 'nebula_pearl',
    spriteId: 'res-nebula',
    glowColor: '#ec4899',
    shimmerColor: '#f9a8d4',
    emoji: '🫧',
    svgPath: 'M12 4 A8 8 0 1 1 12 20 A8 8 0 1 1 12 4 M8 10 A2 2 0 0 0 10 12',
  },
};

export function resourceIconSvg(icon: ResourceMapIcon, size = 24): string {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 24 24" fill="${icon.glowColor}"><path d="${icon.svgPath}"/></svg>`;
}
