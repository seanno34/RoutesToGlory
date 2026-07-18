#!/usr/bin/env bash
# Expose local @empire/api (port 3001) as a public HTTPS URL for off-LAN phone tests.
# Prefer production https://8082ventures.com/rtg_api/api when the SPanel API is up.
# This tunnel is only for testing unreleased local API changes from a real device.
set -euo pipefail

PORT="${RTG_API_PORT:-3001}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "Routes to Glory — field-test tunnel"
echo "Target: http://127.0.0.1:${PORT}  (API must already be running: pnpm dev / pnpm dev:field)"
echo ""
echo "When the tunnel prints an https://… URL, set Unity join-panel API base to:"
echo "  https://YOUR-TUNNEL-HOST/api"
echo "(Cloudflare quick tunnels usually map / → localhost:PORT, so append /api.)"
echo ""

if command -v cloudflared >/dev/null 2>&1; then
  echo "Using Cloudflare Tunnel (cloudflared)…"
  exec cloudflared tunnel --url "http://127.0.0.1:${PORT}"
fi

if command -v ngrok >/dev/null 2>&1; then
  echo "cloudflared not found; using ngrok…"
  exec ngrok http "${PORT}"
fi

cat <<'EOF' >&2
Neither cloudflared nor ngrok is installed.

Install one (recommended: Cloudflare Tunnel):
  brew install cloudflared
  # or: https://developers.cloudflare.com/cloudflare-one/connections/connect-apps/install-and-setup/installation/

Then in one terminal:  pnpm dev:field
And in another:         pnpm dev:tunnel

Paste the printed https://….trycloudflare.com URL + /api into the Unity join panel
(e.g. https://abc.trycloudflare.com/api).

Production (no tunnel needed when SPanel API is healthy):
  https://8082ventures.com/rtg_api/api
  curl https://8082ventures.com/rtg_api/health
EOF
exit 1
