#!/usr/bin/env bash
# Copies concept art from apps/images into Unity Resources for the player ship pin.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/apps/images"
DST="$ROOT/apps/unity-poc/Assets/Resources/RTG_PlayerShip"

mkdir -p "$DST"

for f in glider_01.png glider_02.png glider_03.png glider_04.png; do
  if [[ -f "$SRC/$f" ]]; then
    cp "$SRC/$f" "$DST/$f"
    echo "Copied $f"
  else
    echo "Skip missing $f"
  fi
done

if command -v python3 >/dev/null 2>&1; then
  python3 "$ROOT/scripts/process-cockpit-transparency.py"
else
  echo "python3 not found — cockpit windshield keying skipped"
fi

echo "Done. glider_01 = map pin; glider_cockpit_01 / glider_cockpit_portrait_01 = cockpit overlays."
echo "Re-export Unity to Xcode after art changes."
