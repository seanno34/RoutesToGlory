import type { FastifyPluginAsync } from 'fastify';

/** Upstream XYZ tiles — Carto Voyager (Cesium requests {z}/{x}/{y}). */
const UPSTREAM_BASE =
  'https://a.basemaps.cartocdn.com/rastertiles/voyager';

const TILE_USER_AGENT =
  'RoutesToGlory-API-TileProxy/1.0 (+terrain-tiles; dev)';

/**
 * Proxies terrain raster tiles through the game API so Unity/Cesium avoids
 * direct HTTP/2 curl errors (PROTOCOL_ERROR) from some public CDNs.
 */
export const terrainTileRoutes: FastifyPluginAsync = async (app) => {
  app.get('/tiles/terrain/:z/:x/:y.png', async (request, reply) => {
    const { z, x, y } = request.params as { z: string; x: string; y: string };
    const zi = Number(z);
    const xi = Number(x);
    const yi = Number(y);

    if (!Number.isFinite(zi) || !Number.isFinite(xi) || !Number.isFinite(yi)) {
      return reply.status(400).send({ error: 'invalid_tile_coords' });
    }

    if (zi < 0 || zi > 22) {
      return reply.status(400).send({ error: 'zoom_out_of_range' });
    }

    const maxIndex = Math.pow(2, zi);
    if (xi < 0 || xi >= maxIndex || yi < 0 || yi >= maxIndex) {
      return reply.status(400).send({ error: 'tile_index_out_of_range' });
    }

    const url = `${UPSTREAM_BASE}/${zi}/${xi}/${yi}.png`;

    try {
      const res = await fetch(url, {
        headers: {
          'User-Agent': TILE_USER_AGENT,
          Accept: 'image/png,image/*,*/*',
          Connection: 'close',
        },
      });

      if (!res.ok) {
        request.log.warn({ url, status: res.status }, 'terrain tile upstream failed');
        return reply.status(res.status === 404 ? 404 : 502).send({
          error: 'upstream_tile_failed',
          status: res.status,
        });
      }

      const body = Buffer.from(await res.arrayBuffer());
      return reply
        .header('Content-Type', res.headers.get('content-type') ?? 'image/png')
        .header('Cache-Control', 'public, max-age=604800')
        .send(body);
    } catch (err) {
      request.log.error({ err, url }, 'terrain tile proxy error');
      return reply.status(502).send({ error: 'tile_proxy_error' });
    }
  });
};
