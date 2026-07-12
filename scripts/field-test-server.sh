#!/usr/bin/env bash
# Start the local dev API (or full stack) and keep the Mac awake until it stops.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

MODE="${1:-api}"

case "$MODE" in
  api)
    CMD=(pnpm dev)
    ;;
  all)
    CMD=(pnpm dev:all)
    ;;
  web)
    CMD=(pnpm dev:web)
    ;;
  *)
    echo "Usage: $(basename "$0") [api|all|web]" >&2
    echo "  api  — API only (default, for Unity field testing)" >&2
    echo "  all  — API + web in parallel" >&2
    echo "  web  — web client only" >&2
    exit 1
    ;;
esac

if ! command -v caffeinate >/dev/null 2>&1; then
  echo "caffeinate not found (macOS only). Starting without sleep prevention." >&2
  exec "${CMD[@]}"
fi

echo "Field-test server: ${CMD[*]}"
echo "Mac will stay awake until you press Ctrl+C."
echo ""

exec caffeinate -dims "${CMD[@]}"
