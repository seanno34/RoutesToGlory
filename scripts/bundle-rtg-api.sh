#!/usr/bin/env bash
# Build a self-contained rtg_api folder ready to upload to ScalaHosting.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/deploy/rtg_api_bundle"

echo "Building packages…"
cd "$ROOT"
pnpm --filter @empire/shared build
pnpm --filter @empire/api build

echo "Bundling to $OUT …"
rm -rf "$OUT"
mkdir -p "$OUT/vendor/@empire/shared"

cp -R "$ROOT/apps/api/dist" "$OUT/dist"
cp "$ROOT/apps/api/index.js" "$OUT/index.js"
cp "$ROOT/apps/api/app.yml" "$OUT/app.yml"
cp -R "$ROOT/apps/api/migrations" "$OUT/migrations"
cp "$ROOT/packages/shared/package.json" "$OUT/vendor/@empire/shared/"
cp "$ROOT/packages/shared/index.js" "$OUT/vendor/@empire/shared/"
cp -R "$ROOT/packages/shared/dist" "$OUT/vendor/@empire/shared/dist"

cat > "$OUT/package.json" <<'EOF'
{
  "name": "rtg_api",
  "private": true,
  "type": "module",
  "scripts": {
    "start": "node index.js"
  },
  "dependencies": {
    "@empire/shared": "file:./vendor/@empire/shared",
    "@fastify/cors": "^11.0.0",
    "dotenv": "^16.4.7",
    "fastify": "^5.2.1",
    "mysql2": "^3.14.0",
    "zod": "^3.24.2"
  }
}
EOF

echo "Installing production node_modules in bundle…"
cd "$OUT"
npm install --omit=dev --no-package-lock

echo ""
echo "Done. Upload everything inside:"
echo "  $OUT/"
echo "to /home/ventures/rtg_api/ (merge/replace dist, index.js, migrations, package.json, node_modules)"
echo "Keep your existing .env on the server."
