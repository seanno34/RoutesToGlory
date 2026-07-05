import './load-env.js';
import Fastify from 'fastify';
import cors from '@fastify/cors';
import { configStore } from './services/config-store.js';
import { godModeRoutes, worldRoutes } from './routes/game.js';
import { sessionRoutes } from './routes/sessions.js';
import { isDatabaseEnabled, runMigrations } from './db/client.js';

const PORT = Number(process.env.PORT ?? 3001);

async function main(): Promise<void> {
  await configStore.load();

  if (isDatabaseEnabled()) {
    console.log('Database enabled — running migrations…');
    await runMigrations();
    console.log('Migrations complete.');
  } else {
    console.log('MySQL not configured — using in-memory stores only.');
  }

  const app = Fastify({ logger: true });
  await app.register(cors, { origin: true });

  app.get('/health', async () => ({
    ok: true,
    database: isDatabaseEnabled(),
  }));

  await app.register(worldRoutes, { prefix: '/api' });
  await app.register(sessionRoutes, { prefix: '/api' });
  await app.register(godModeRoutes, { prefix: '/api/god' });

  await app.listen({ port: PORT, host: '0.0.0.0' });
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
