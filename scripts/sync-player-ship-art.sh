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

echo "Done. glider_01 is the player ship in Map and Route views. Re-export Unity to Xcode after art changes."
