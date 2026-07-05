#!/bin/bash
# SPanel startup wrapper — loads .env then starts Node
# Usage: set SPanel startup to: bash /home/ventures/rtg_api/start.sh
cd "$(dirname "$0")"
set -a
[ -f .env ] && source .env
set +a
exec node index.js
