import dotenv from 'dotenv';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));

for (const envPath of [
  path.join(process.cwd(), '.env'),
  path.join(here, '../.env'),
  path.join(here, '../../../.env'),
]) {
  if (existsSync(envPath)) {
    dotenv.config({ path: envPath });
    break;
  }
}
