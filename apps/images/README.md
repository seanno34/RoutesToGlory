# Player ship concept art

Staging folder for spaceship / glider inspiration before Unity import.

| File | View | Best use |
|------|------|----------|
| `glider_01.png` | Top-down (dark bg) | **Default Unity map pin** — nose up, reads well overhead |
| `glider_02.png` | Isometric 3/4 | Ship select, loading, 3D model reference |
| `glider_03.png` | Side profile | Hangar / profile UI |
| `glider_04.png` | Top-down (white bg) | Alternate map pin; good for alpha cutout pass |

## Unity import

**Unity menu:** Routes to Glory → **8b. Sync Player Ship Art**

Or from terminal:

```bash
./scripts/sync-player-ship-art.sh
```

Copies PNGs to `apps/unity-poc/Assets/Resources/RTG_PlayerShip/`. Unity loads `glider_01` by default via `RtgPlayerLocation` (`PlayerMarkerStyle.SpaceshipSprite`).

In the Inspector on **RTG Player** you can:
- Assign another texture (e.g. `glider_04`) on **Ship Texture**
- Tune **Ship Size Meters** (default 42 m wingspan)
- Set **Ship Heading Offset Degrees** if the nose isn't aligned
- Switch **Marker Style** back to `GoldPin` for the legacy sphere

## Next steps (optional)

1. Cut transparent backgrounds on `glider_01` / `glider_04` in an image editor
2. Render a 16–32 frame heading sprite sheet from Blender for smoother rotation
3. Export GLB from `glider_02`/`glider_03` for hangar UI (not the live map pin)
