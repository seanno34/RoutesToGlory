#!/usr/bin/env python3
"""Key out magenta windshield fill and copy cockpit art into Unity Resources."""
from __future__ import annotations

from pathlib import Path

from collections import deque

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
IMAGES = ROOT / "apps" / "images"
RES = ROOT / "apps" / "unity-poc" / "Assets" / "Resources" / "RTG_PlayerShip"

COCKPITS = (
    ("glider_cockpit_01.png", "glider_cockpit_landscape_src.png"),
    ("glider_cockpit_portrait_01.png", "glider_cockpit_portrait_src.png"),
)


def is_magenta_glass(r: int, g: int, b: int, a: int) -> bool:
    if a < 64:
        return False
    return r > 150 and b > 150 and g < 160 and (r + b) > g + 180


def is_hud_cyan(r: int, g: int, b: int, a: int) -> bool:
    if a < 64:
        return False
    return b > 120 and b > r + 12 and g >= r - 20


def is_hud_white(r: int, g: int, b: int, a: int) -> bool:
    if a < 64:
        return False
    return min(r, g, b) > 200 and max(r, g, b) - min(r, g, b) < 35


def fill_edge_transparency(img: Image.Image) -> Image.Image:
    """Paint border-connected clear pixels so ScaleAndCrop never reveals the map in margins."""
    w, h = img.size
    px = img.load()
    fill = (22, 24, 30, 255)
    seen: set[tuple[int, int]] = set()
    q: deque[tuple[int, int]] = deque()

    for x in range(w):
        q.append((x, 0))
        q.append((x, h - 1))
    for y in range(h):
        q.append((0, y))
        q.append((w - 1, y))

    while q:
        x, y = q.popleft()
        if (x, y) in seen or x < 0 or x >= w or y < 0 or y >= h:
            continue
        seen.add((x, y))
        if px[x, y][3] > 16:
            continue
        px[x, y] = fill
        q.append((x + 1, y))
        q.append((x - 1, y))
        q.append((x, y + 1))
        q.append((x, y - 1))

    return img


def process(img: Image.Image) -> Image.Image:
    img = img.convert("RGBA")
    w, h = img.size
    px = img.load()

    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if not is_magenta_glass(r, g, b, a):
                continue

            if is_hud_cyan(r, g, b, a) or is_hud_white(r, g, b, a):
                px[x, y] = (r, g, b, min(a, 210))
                continue

            px[x, y] = (r, g, b, 0)

    # Feather anti-aliased magenta fringe on the glass edge.
    for y in range(1, h - 1):
        for x in range(1, w - 1):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            neighbors_clear = 0
            for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                if px[nx, ny][3] == 0:
                    neighbors_clear += 1
            if neighbors_clear == 0:
                continue
            if is_magenta_glass(r, g, b, a) or (r > 120 and b > 120 and g < 180):
                px[x, y] = (r, g, b, max(0, a // 3))

    return fill_edge_transparency(img)


def main() -> None:
    RES.mkdir(parents=True, exist_ok=True)

    for out_name, src_name in COCKPITS:
        src = IMAGES / src_name
        if not src.exists():
            # Allow processing an already-named source file.
            alt = IMAGES / out_name
            if alt.exists():
                src = alt
            else:
                print(f"Skip missing {src_name}")
                continue

        img = process(Image.open(src))
        out_img = IMAGES / out_name
        out_res = RES / out_name
        img.save(out_img)
        img.save(out_res)
        print(f"Wrote {out_img} and {out_res}")


if __name__ == "__main__":
    main()
