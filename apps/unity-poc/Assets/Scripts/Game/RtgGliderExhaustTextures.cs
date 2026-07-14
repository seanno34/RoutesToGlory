using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Procedural soft flame / glow sprites for exhaust VFX (no external art required).
    /// </summary>
    public static class RtgGliderExhaustTextures
    {
        private static Texture2D _flameStreak;
        private static Texture2D _softGlow;
        private static Texture2D _flameFlipbook;
        private static Texture2D _cavityFill;
        private static Texture2D _cavityCore;

        public static Texture2D FlameStreak => _flameStreak ??= CreateFlameStreak(128, 256);
        public static Texture2D SoftGlow => _softGlow ??= CreateRadialGlow(128);
        public static Texture2D FlameFlipbook => _flameFlipbook ??= CreateFlameFlipbook(256, 256, columns: 4, rows: 4);
        public static Texture2D CavityFill => _cavityFill ??= CreateSolidDiscGlow(128, innerSolid: 0.72f, edgeSoftness: 0.18f);
        public static Texture2D CavityCore => _cavityCore ??= CreateSolidDiscGlow(96, innerSolid: 0.88f, edgeSoftness: 0.08f);

        /// <summary>Vertical soft flame streak (hot core top, soft tail bottom).</summary>
        public static Texture2D CreateFlameStreak(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "RTG_FlameStreak",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float cx = width * 0.5f;
            for (int y = 0; y < height; y++)
            {
                float v = y / (height - 1f);
                float along = Mathf.SmoothStep(0f, 1f, v);
                float tailFade = 1f - Mathf.Pow(v, 1.35f);

                for (int x = 0; x < width; x++)
                {
                    float nx = (x - cx) / cx;
                    float edge = Mathf.Exp(-nx * nx * (2.6f + along * 2.2f));
                    float flicker = 0.9f + 0.1f * Mathf.PerlinNoise(x * 0.11f, y * 0.07f);
                    float alpha = edge * tailFade * flicker;
                    alpha = Mathf.Pow(Mathf.Clamp01(alpha), 1.15f);

                    float r = Mathf.Lerp(1f, 0.45f, v);
                    float g = Mathf.Lerp(0.92f, 0.65f, v);
                    float b = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0.35f, 1f, v));
                    texture.SetPixel(x, y, new Color(r, g, b, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        public static Texture2D CreateRadialGlow(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RTG_ExhaustGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float radius = size * 0.5f;
            var center = new Vector2(radius, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float t = 1f - Vector2.Distance(new Vector2(x, y), center) / radius;
                    float alpha = Mathf.Clamp01(t);
                    alpha = alpha * alpha * alpha;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        /// <summary>Solid nozzle fill: bright center, soft rim only at the edge.</summary>
        public static Texture2D CreateSolidDiscGlow(int size, float innerSolid, float edgeSoftness)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RTG_CavityDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float radius = size * 0.5f;
            var center = new Vector2(radius, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float t = 1f - Vector2.Distance(new Vector2(x, y), center) / radius;
                    float alpha = t <= innerSolid
                        ? 1f
                        : Mathf.InverseLerp(innerSolid, innerSolid + edgeSoftness, t);
                    alpha = Mathf.Clamp01(alpha);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        /// <summary>4x4 flipbook of soft flame blobs for texture-sheet animation.</summary>
        public static Texture2D CreateFlameFlipbook(int width, int height, int columns, int rows)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "RTG_FlameFlipbook",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            int cellW = width / columns;
            int cellH = height / rows;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    float seed = col * 0.37f + row * 1.13f;
                    DrawFlipbookCell(texture, col * cellW, row * cellH, cellW, cellH, seed);
                }
            }

            texture.Apply();
            return texture;
        }

        private static void DrawFlipbookCell(Texture2D texture, int ox, int oy, int w, int h, float seed)
        {
            float cx = ox + w * 0.5f;
            float cy = oy + h * (0.42f + seed * 0.08f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float px = ox + x;
                    float py = oy + y;
                    float nx = (px - cx) / (w * 0.5f);
                    float ny = (py - cy) / (h * 0.55f);
                    float d = nx * nx + ny * ny * 1.35f;
                    float alpha = Mathf.Exp(-d * (2.2f + seed));
                    alpha *= 0.85f + 0.15f * Mathf.PerlinNoise(px * 0.08f + seed, py * 0.06f);
                    alpha = Mathf.Clamp01(alpha);
                    texture.SetPixel((int)px, (int)py, new Color(1f, 0.92f, 0.7f, alpha));
                }
            }
        }
    }
}
