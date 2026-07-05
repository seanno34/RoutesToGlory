import '../load-env.js';
import { isDatabaseEnabled, runMigrations, closePool } from '../db/client.js';

async function main(): Promise<void> {
  if (!isDatabaseEnabled()) {
    console.error('MySQL env vars not found.');
    console.error('Create /home/ventures/rtg_api/.env with MYSQL_HOST, MYSQL_USER, MYSQL_PASSWORD, MYSQL_DATABASE');
    console.error('Or run: export MYSQL_HOST=localhost MYSQL_USER=... MYSQL_PASSWORD=... MYSQL_DATABASE=ventures_rtg_test');
    process.exit(1);
  }
  await runMigrations();
  console.log('Done.');
  await closePool();
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
