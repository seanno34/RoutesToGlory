import type { GameConfig, GoodieHutChoice } from '@empire/shared';
import { ALIEN_RESOURCES } from '@empire/shared';
import { resolveGoodieHutConnect } from './goodie-hut.js';
import { configStore } from './config-store.js';
import { query, newId } from '../db/client.js';
import {
  haversineM,
  isWithinRouteCorridor,
  nearestPointOnPath,
  type PathPoint,
} from './route-geometry.js';

export interface ClaimNearRouteInput {
  worldId: string;
  empireId: string;
  sessionId?: string;
  routePath: PathPoint[];
  playerLat?: number;
  playerLng?: number;
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

  if (input.routePath.length < 1) {
    throw Object.assign(new Error('Active route path required'), { statusCode: 400 });
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

  const lat = Number(site.lat);
  const lng = Number(site.lng);

  if (!isWithinRouteCorridor(lat, lng, input.routePath, radiusM)) {
    throw Object.assign(
      new Error(`Target must be within ${radiusM}m of your active route`),
      { statusCode: 400 },
    );
  }

  if (await isSettlementAlreadyConnected(input.worldId, input.empireId, site.id)) {
    throw Object.assign(new Error('Settlement already connected to your network'), {
      statusCode: 409,
    });
  }

  const anchor = nearestPointOnPath(lat, lng, input.routePath);
  const connectorPath: PathPoint[] = [anchor, { lat, lng }];

  let message = `Connected to ${site.name}.`;
  let reward: unknown;

  if (site.is_goodie_hut) {
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

    if (choice === 'found_town') {
      const newName = site.name.replace(/^Goodie Hut/i, 'Town');
      const tier = resolution.choice === 'found_town' ? resolution.tier : 'town';
      await query(
        `UPDATE settlements
         SET tier = ?, is_goodie_hut = 0, owner_empire_id = ?,
             name = ?, planet_display_name = ?
         WHERE id = ?`,
        [tier, input.empireId, newName, newName, site.id],
      );
      message = `Founded ${newName} — connector route established.`;
    } else if (resolution.choice === 'claim_reward') {
      reward = resolution.reward;
      await query(
        `UPDATE settlements SET owner_empire_id = ?, is_goodie_hut = 0, tier = 'settlement'
         WHERE id = ?`,
        [input.empireId, site.id],
      );
      message = `Claimed reward at ${site.name}.`;
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

  const lat = Number(node.lat);
  const lng = Number(node.lng);

  if (!isWithinRouteCorridor(lat, lng, input.routePath, radiusM)) {
    throw Object.assign(
      new Error(`Resource must be within ${radiusM}m of your active route`),
      { statusCode: 400 },
    );
  }

  // Connector runs from the nearest point on the active route — not the player's
  // live GPS pin — so tap-to-connect works when you've passed nearby but aren't
  // standing on the resource.
  const anchor = nearestPointOnPath(lat, lng, input.routePath);
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
