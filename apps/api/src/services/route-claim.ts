import type { GameConfig, GoodieHutChoice } from '@empire/shared';
import { ALIEN_RESOURCES } from '@empire/shared';
import type { ResultSetHeader } from 'mysql2';
import { resolveGoodieHutConnect } from './goodie-hut.js';
import { configStore } from './config-store.js';
import { getPool, query, newId } from '../db/client.js';
import {
  bboxesOverlap,
  decimatePath,
  expandBboxAroundPoint,
  haversineM,
  isWithinAnyRouteCorridor,
  nearestPointOnAnyPath,
  nearestPointOnPath,
  pathBbox,
  type PathPoint,
} from './route-geometry.js';

/** MySQL TINYINT / string / Buffer → real 0|1 for goodie-hut checks. */
function goodieHutFlag(value: unknown): number {
  if (value == null) return 0;
  if (typeof value === 'boolean') return value ? 1 : 0;
  if (typeof value === 'number') return value === 0 ? 0 : 1;
  if (typeof value === 'string') {
    const trimmed = value.trim().toLowerCase();
    if (trimmed === '' || trimmed === '0' || trimmed === 'false') return 0;
    return 1;
  }
  if (Buffer.isBuffer(value)) return value.length > 0 && value[0] !== 0 ? 1 : 0;
  return Number(value) ? 1 : 0;
}

function isUnclaimedGoodieHut(site: {
  is_goodie_hut: unknown;
  tier: string;
  owner_empire_id: string | null;
}): boolean {
  if (site.owner_empire_id) return false;
  return goodieHutFlag(site.is_goodie_hut) === 1 || site.tier === 'goodie_hut';
}

export interface ClaimNearRouteInput {
  worldId: string;
  empireId: string;
  sessionId?: string;
  /** Optional active-leg hint (decimated client-side). Omit when useNetworkRoutes is true. */
  routePath?: PathPoint[];
  /** When true, corridor checks use persisted empire routes from the DB instead of a huge client upload. */
  useNetworkRoutes?: boolean;
  playerLat?: number;
  playerLng?: number;
  approachLat?: number;
  approachLng?: number;
  targetKind: 'settlement' | 'resource';
  targetId: string;
  goodieChoice?: GoodieHutChoice;
}

export interface ClaimResult {
  ok: true;
  connectorRouteId: string;
  linkedRouteId?: string;
  connectorPath: PathPoint[];
  message: string;
  settlement?: Record<string, unknown>;
  reward?: unknown;
}

function claimRadiusM(config: GameConfig): number {
  return config.routes.minConnectDistanceM ?? 800;
}

const MAX_CLIENT_PATH_POINTS = 256;
const MAX_NETWORK_PATH_POINTS = 512;

async function loadNetworkRoutePaths(
  worldId: string,
  empireId: string,
  probeLat: number,
  probeLng: number,
  radiusM: number,
): Promise<PathPoint[][]> {
  const result = await query<{ path_json: string }>(
    `SELECT path_json FROM routes
     WHERE world_id = ? AND empire_id = ? AND status = 'active'`,
    [worldId, empireId],
  );

  const probeBbox = expandBboxAroundPoint(probeLat, probeLng, radiusM * 1.5);
  const paths: PathPoint[][] = [];

  for (const row of result.rows) {
    let path: PathPoint[];
    try {
      path = JSON.parse(row.path_json) as PathPoint[];
    } catch {
      continue;
    }
    if (!Array.isArray(path) || path.length < 1) continue;

    const simplified = decimatePath(path, MAX_NETWORK_PATH_POINTS);
    const bbox = pathBbox(simplified);
    if (!bbox || !bboxesOverlap(bbox, probeBbox)) continue;
    paths.push(simplified);
  }

  return paths;
}

async function resolveCorridorPaths(
  input: ClaimNearRouteInput,
  probeLat: number,
  probeLng: number,
  radiusM: number,
): Promise<PathPoint[][]> {
  const paths: PathPoint[][] = [];

  if (input.routePath && input.routePath.length > 0) {
    const hint =
      input.routePath.length > MAX_CLIENT_PATH_POINTS
        ? decimatePath(input.routePath, MAX_CLIENT_PATH_POINTS)
        : input.routePath;
    paths.push(hint);
  }

  if (input.useNetworkRoutes) {
    const network = await loadNetworkRoutePaths(
      input.worldId,
      input.empireId,
      probeLat,
      probeLng,
      radiusM,
    );
    paths.push(...network);
  }

  return paths;
}

function assertWithinCorridor(
  lat: number,
  lng: number,
  paths: PathPoint[][],
  radiusM: number,
  label: string,
): void {
  if (paths.length === 0) {
    throw Object.assign(new Error('Active route path required'), { statusCode: 400 });
  }

  if (!isWithinAnyRouteCorridor(lat, lng, paths, radiusM)) {
    throw Object.assign(
      new Error(`${label} must be within ${radiusM}m of your route network`),
      { statusCode: 400 },
    );
  }
}

function corridorAnchor(lat: number, lng: number, paths: PathPoint[][]): PathPoint {
  return paths.length === 1
    ? nearestPointOnPath(lat, lng, paths[0]!)
    : nearestPointOnAnyPath(lat, lng, paths);
}

function rewardSummary(reward: unknown): string {
  if (!reward || typeof reward !== 'object') return '';
  const r = reward as Record<string, unknown>;
  if (r.type === 'gold' && typeof r.amount === 'number') return ` +${r.amount} gold`;
  if (r.type === 'tech' && typeof r.name === 'string') return ` Unlocked ${r.name}.`;
  if (r.type === 'unit') {
    const unit = r.unit as { name?: string } | undefined;
    const label = unit?.name ?? 'alien unit';
    return r.upgraded ? ` Upgraded to ${label}.` : ` Gained ${label}.`;
  }
  return '';
}

async function isSettlementAlreadyConnected(
  worldId: string,
  empireId: string,
  settlementId: string,
): Promise<boolean> {
  const result = await query<{ id: string }>(
    `SELECT id FROM routes
     WHERE world_id = ? AND empire_id = ? AND status = 'active'
       AND to_settlement_id = ?
     LIMIT 1`,
    [worldId, empireId, settlementId],
  );
  return result.rows.length > 0;
}

async function getOwnedSettlements(worldId: string, empireId: string) {
  const result = await query<{
    id: string;
    name: string;
    tier: string;
    lat: number;
    lng: number;
  }>(
    `SELECT id, name, tier, lat, lng FROM settlements
     WHERE world_id = ? AND owner_empire_id = ?`,
    [worldId, empireId],
  );
  return result.rows;
}

async function createConnectorRoute(
  worldId: string,
  empireId: string,
  fromSettlementId: string,
  toSettlementId: string,
  path: PathPoint[],
): Promise<string> {
  let distanceM = 0;
  for (let i = 1; i < path.length; i += 1) {
    distanceM += haversineM(
      path[i - 1]!.lat,
      path[i - 1]!.lng,
      path[i]!.lat,
      path[i]!.lng,
    );
  }

  const routeId = newId();
  await query(
    `INSERT INTO routes (
       id, world_id, empire_id, session_id, from_settlement_id, to_settlement_id,
       path_json, distance_m, status
     ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'active')`,
    [
      routeId,
      worldId,
      empireId,
      // Connector routes are instant paths, not GPS-recorded sessions (session_id is 1:1).
      null,
      fromSettlementId,
      toSettlementId,
      JSON.stringify(path),
      distanceM,
    ],
  );

  await query(
    `INSERT INTO world_events (id, world_id, type, payload)
     VALUES (?, ?, 'route_established', ?)`,
    [
      newId(),
      worldId,
      JSON.stringify({
        routeId,
        empireId,
        fromSettlementId,
        toSettlementId,
        connector: true,
      }),
    ],
  );

  return routeId;
}

function extractorName(resourceId: string): string {
  const def = ALIEN_RESOURCES[resourceId as keyof typeof ALIEN_RESOURCES];
  return def ? `${def.name} Extractor` : `${resourceId.replace(/_/g, ' ')} Extractor`;
}

async function createExtractorSettlement(
  worldId: string,
  empireId: string,
  resourceId: string,
  lat: number,
  lng: number,
): Promise<string> {
  const id = newId();
  const name = extractorName(resourceId);
  const slug = `extractor-${resourceId.slice(0, 8)}-${Date.now().toString(36)}`;

  await query(
    `INSERT INTO settlements (
       id, world_id, slug, name, planet_display_name, terrestrial_label,
       tier, alignment, is_goodie_hut, owner_empire_id, lat, lng,
       geofence_radius_m, base_defense
     ) VALUES (?, ?, ?, ?, ?, ?, 'settlement', 'friendly', 0, ?, ?, ?, 120, 10)`,
    [id, worldId, slug, name, name, name, empireId, lat, lng],
  );

  return id;
}

export async function claimNearRoute(input: ClaimNearRouteInput): Promise<ClaimResult> {
  const config = configStore.get();
  const radiusM = claimRadiusM(config);

  const hasClientPath = (input.routePath?.length ?? 0) > 0;
  if (!hasClientPath && !input.useNetworkRoutes) {
    throw Object.assign(new Error('routePath or useNetworkRoutes required'), { statusCode: 400 });
  }

  if (input.targetKind === 'settlement') {
    return claimSettlement(input, config, radiusM);
  }

  return claimResource(input, config, radiusM);
}

async function claimSettlement(
  input: ClaimNearRouteInput,
  config: GameConfig,
  radiusM: number,
): Promise<ClaimResult> {
  const settlementResult = await query<{
    id: string;
    name: string;
    tier: string;
    is_goodie_hut: number;
    owner_empire_id: string | null;
    lat: number;
    lng: number;
  }>(`SELECT * FROM settlements WHERE id = ? AND world_id = ?`, [
    input.targetId,
    input.worldId,
  ]);

  const site = settlementResult.rows[0];
  if (!site) {
    throw Object.assign(new Error('Settlement not found'), { statusCode: 404 });
  }

  if (site.owner_empire_id && site.owner_empire_id !== input.empireId) {
    throw Object.assign(new Error('Settlement owned by another empire'), { statusCode: 403 });
  }

  const unclaimedGoodie = isUnclaimedGoodieHut(site);

  // Already owned by this empire — heal leftover goodie flags, never re-roll rewards.
  if (site.owner_empire_id === input.empireId) {
    const wasGoodie =
      goodieHutFlag(site.is_goodie_hut) === 1 || site.tier === 'goodie_hut';
    if (wasGoodie) {
      await getPool().execute(
        `UPDATE settlements SET is_goodie_hut = 0,
            tier = IF(tier = 'goodie_hut', 'settlement', tier)
         WHERE id = ? AND owner_empire_id = ?`,
        [site.id, input.empireId],
      );
    }
    if (await isSettlementAlreadyConnected(input.worldId, input.empireId, site.id)) {
      throw Object.assign(
        new Error(
          wasGoodie
            ? 'Goodie hut already claimed'
            : 'Settlement already connected to your network',
        ),
        { statusCode: 409 },
      );
    }
  }

  let lat = Number(site.lat);
  let lng = Number(site.lng);

  // Unity POC pins goodie huts on the tour corridor for testing; validate the
  // pin against the route, then persist those coords so connectors line up.
  const APPROACH_MAX_DRIFT_M = 5000;
  let corridorLat = lat;
  let corridorLng = lng;
  const useApproach =
    unclaimedGoodie &&
    input.approachLat != null &&
    input.approachLng != null;

  if (useApproach) {
    const drift = haversineM(lat, lng, input.approachLat!, input.approachLng!);
    if (drift > APPROACH_MAX_DRIFT_M) {
      throw Object.assign(
        new Error('Pinned map marker is too far from this goodie hut record'),
        { statusCode: 400 },
      );
    }
    corridorLat = input.approachLat!;
    corridorLng = input.approachLng!;
    lat = corridorLat;
    lng = corridorLng;
  }

  const corridorPaths = await resolveCorridorPaths(input, corridorLat, corridorLng, radiusM);
  assertWithinCorridor(corridorLat, corridorLng, corridorPaths, radiusM, 'Target');

  if (await isSettlementAlreadyConnected(input.worldId, input.empireId, site.id)) {
    throw Object.assign(new Error('Settlement already connected to your network'), {
      statusCode: 409,
    });
  }

  const anchor = corridorAnchor(lat, lng, corridorPaths);
  const connectorPath: PathPoint[] = [anchor, { lat, lng }];

  let message = `Connected to ${site.name}.`;
  let reward: unknown;

  if (unclaimedGoodie) {
    const choice = input.goodieChoice ?? 'found_town';
    const resolution = resolveGoodieHutConnect(
      config,
      {
        sessionId: input.sessionId ?? newId(),
        worldId: input.worldId,
        settlementId: site.id,
        empireId: input.empireId,
        choice,
      },
      {
        settlementId: site.id,
        empireId: input.empireId,
        worldId: input.worldId,
        ownedUnitIds: [],
      },
    );

    // Atomic claim: only one successful goodie conversion per hut.
    let claimed = false;
    if (choice === 'found_town') {
      const newName = site.name.replace(/^Goodie Hut/i, 'Town');
      const tier = resolution.choice === 'found_town' ? resolution.tier : 'town';
      const [header] = await getPool().execute<ResultSetHeader>(
        `UPDATE settlements
         SET tier = ?, is_goodie_hut = 0, owner_empire_id = ?,
             name = ?, planet_display_name = ?, lat = ?, lng = ?
         WHERE id = ? AND world_id = ? AND is_goodie_hut = 1 AND owner_empire_id IS NULL`,
        [tier, input.empireId, newName, newName, lat, lng, site.id, input.worldId],
      );
      claimed = (header.affectedRows ?? 0) > 0;
      message = `Founded ${newName} — connector route established.`;
    } else if (resolution.choice === 'claim_reward') {
      reward = resolution.reward;
      const [header] = await getPool().execute<ResultSetHeader>(
        `UPDATE settlements
         SET owner_empire_id = ?, is_goodie_hut = 0, tier = 'settlement',
             lat = ?, lng = ?
         WHERE id = ? AND world_id = ? AND is_goodie_hut = 1 AND owner_empire_id IS NULL`,
        [input.empireId, lat, lng, site.id, input.worldId],
      );
      claimed = (header.affectedRows ?? 0) > 0;
      message = `Claimed reward at ${site.name}.${rewardSummary(reward)}`;
    }

    if (!claimed) {
      throw Object.assign(new Error('Goodie hut already claimed'), { statusCode: 409 });
    }
  } else if (!site.owner_empire_id) {
    await query(`UPDATE settlements SET owner_empire_id = ? WHERE id = ?`, [
      input.empireId,
      site.id,
    ]);
    message = `Claimed ${site.name}.`;
  }

  const owned = await getOwnedSettlements(input.worldId, input.empireId);
  const fromSite =
    owned.find((s) => s.id !== site.id) ??
    owned[0] ?? {
      id: site.id,
      lat,
      lng,
      name: site.name,
      tier: site.tier,
    };

  const connectorRouteId = await createConnectorRoute(
    input.worldId,
    input.empireId,
    fromSite.id,
    site.id,
    connectorPath,
  );

  let linkedRouteId: string | undefined;
  const otherOwned = owned.filter((s) => s.id !== site.id);
  if (otherOwned.length > 0) {
    const nearest = otherOwned.sort(
      (a, b) =>
        haversineM(lat, lng, Number(a.lat), Number(a.lng)) -
        haversineM(lat, lng, Number(b.lat), Number(b.lng)),
    )[0]!;

    if (nearest.id !== fromSite.id) {
      linkedRouteId = await createConnectorRoute(
        input.worldId,
        input.empireId,
        nearest.id,
        site.id,
        [
          { lat: Number(nearest.lat), lng: Number(nearest.lng) },
          { lat, lng },
        ],
      );
      message += ` Trade route linked to ${nearest.name}.`;
    }
  }

  const updated = await query(`SELECT * FROM settlements WHERE id = ?`, [site.id]);

  return {
    ok: true,
    connectorRouteId,
    linkedRouteId,
    connectorPath,
    message,
    settlement: updated.rows[0] as Record<string, unknown>,
    reward,
  };
}

async function claimResource(
  input: ClaimNearRouteInput,
  _config: GameConfig,
  radiusM: number,
): Promise<ClaimResult> {
  const nodeResult = await query<{
    id: string;
    resource_id: string;
    lat: number;
    lng: number;
    yield_per_day: number;
    richness: string;
    owner_empire_id: string | null;
  }>(`SELECT * FROM map_resource_nodes WHERE id = ? AND world_id = ?`, [
    input.targetId,
    input.worldId,
  ]);

  const node = nodeResult.rows[0];
  if (!node) {
    throw Object.assign(new Error('Resource node not found'), { statusCode: 404 });
  }

  if (node.owner_empire_id) {
    if (node.owner_empire_id === input.empireId) {
      throw Object.assign(new Error('Resource already connected to your network'), {
        statusCode: 409,
      });
    }
    throw Object.assign(new Error('Resource claimed by another empire'), { statusCode: 403 });
  }

  let lat = Number(node.lat);
  let lng = Number(node.lng);

  const APPROACH_MAX_DRIFT_M = 5000;
  if (input.approachLat != null && input.approachLng != null) {
    const drift = haversineM(lat, lng, input.approachLat, input.approachLng);
    if (drift <= APPROACH_MAX_DRIFT_M) {
      lat = input.approachLat;
      lng = input.approachLng;
    }
  }

  const corridorPaths = await resolveCorridorPaths(input, lat, lng, radiusM);
  assertWithinCorridor(lat, lng, corridorPaths, radiusM, 'Resource');

  // Connector runs from the nearest point on the route network — not the player's
  // live GPS pin — so tap-to-connect works when you've passed nearby but aren't
  // standing on the resource.
  const anchor = corridorAnchor(lat, lng, corridorPaths);
  const connectorPath: PathPoint[] = [anchor, { lat, lng }];
  const extractorId = await createExtractorSettlement(
    input.worldId,
    input.empireId,
    node.resource_id,
    lat,
    lng,
  );

  const owned = await getOwnedSettlements(input.worldId, input.empireId);
  const fromSite = owned.find((s) => s.id !== extractorId) ?? {
    id: extractorId,
    lat,
    lng,
    name: extractorName(node.resource_id),
    tier: 'settlement',
  };

  const connectorRouteId = await createConnectorRoute(
    input.worldId,
    input.empireId,
    fromSite.id,
    extractorId,
    connectorPath,
  );

  await query(
    `UPDATE map_resource_nodes
     SET owner_empire_id = ?, route_id = ?, claimed_at = NOW(), last_yield_at = NOW()
     WHERE id = ?`,
    [input.empireId, connectorRouteId, node.id],
  );

  await query(
    `INSERT INTO world_events (id, world_id, type, payload)
     VALUES (?, ?, 'resource_mine_claimed', ?)`,
    [
      newId(),
      input.worldId,
      JSON.stringify({
        nodeId: node.id,
        empireId: input.empireId,
        resourceId: node.resource_id,
        routeId: connectorRouteId,
        yieldPerDay: node.yield_per_day,
        extractorSettlementId: extractorId,
      }),
    ],
  );

  const resourceLabel = node.resource_id.replace(/_/g, ' ');

  return {
    ok: true,
    connectorRouteId,
    connectorPath,
    message: `Established ${extractorName(node.resource_id)} — +${node.yield_per_day} ${resourceLabel}/day via permanent route.`,
    reward: {
      resourceId: node.resource_id,
      yieldPerDay: node.yield_per_day,
      extractorSettlementId: extractorId,
    },
  };
}
