using System;
using UnityEngine;

namespace RoutesToGlory.Game
{
    [Serializable]
    public struct RtgExhaustColorStop
    {
        public float speedMph;
        public Color cavityOuter;
        public Color cavityCore;
        public Color flame;
        public Color glow;

        public RtgExhaustColorStop(
            float mph,
            Color cavityOuterColor,
            Color cavityCoreColor,
            Color flameColor,
            Color glowColor)
        {
            speedMph = mph;
            cavityOuter = cavityOuterColor;
            cavityCore = cavityCoreColor;
            flame = flameColor;
            glow = glowColor;
        }
    }

    public static class RtgExhaustColorProfile
    {
        public const int StopCount = 4;

        public static RtgExhaustColorStop[] CreateDefaultStops()
        {
            return new[]
            {
                new RtgExhaustColorStop(
                    0f,
                    new Color(1f, 0.12f, 0f),
                    new Color(1f, 0.28f, 0.02f),
                    new Color(1f, 0.38f, 0.02f),
                    new Color(1f, 0.3f, 0.01f)),
                new RtgExhaustColorStop(
                    40f,
                    new Color(1f, 0.35f, 0.04f),
                    new Color(1f, 0.45f, 0.08f),
                    new Color(1f, 0.5f, 0.1f),
                    new Color(0.35f, 0.75f, 0.95f)),
                new RtgExhaustColorStop(
                    70f,
                    new Color(1f, 0.42f, 0.08f),
                    new Color(1f, 0.55f, 0.12f),
                    new Color(1f, 0.55f, 0.08f),
                    new Color(1f, 0.55f, 0.2f)),
                new RtgExhaustColorStop(
                    99f,
                    new Color(0.08f, 0.78f, 1f),
                    new Color(0.45f, 0.98f, 1f),
                    new Color(0.55f, 0.95f, 1f),
                    new Color(0.12f, 0.62f, 1f)),
            };
        }

        public static RtgExhaustColorStop[] NormalizeStops(RtgExhaustColorStop[] stops, float maxMph)
        {
            maxMph = Mathf.Max(1f, maxMph);
            if (stops == null || stops.Length == 0)
                stops = CreateDefaultStops();

            var normalized = new RtgExhaustColorStop[StopCount];
            for (int i = 0; i < StopCount; i++)
            {
                RtgExhaustColorStop source = i < stops.Length
                    ? stops[i]
                    : CreateDefaultStops()[Mathf.Min(i, StopCount - 1)];
                normalized[i] = new RtgExhaustColorStop
                {
                    speedMph = Mathf.Clamp(source.speedMph, 0f, maxMph),
                    cavityOuter = ClampColor(source.cavityOuter),
                    cavityCore = ClampColor(source.cavityCore),
                    flame = ClampColor(source.flame),
                    glow = ClampColor(source.glow),
                };
            }

            return normalized;
        }

        public static RtgExhaustColorStop[] SortForSampling(RtgExhaustColorStop[] stops, float maxMph)
        {
            RtgExhaustColorStop[] normalized = NormalizeStops(stops, maxMph);
            Array.Sort(normalized, (a, b) => a.speedMph.CompareTo(b.speedMph));
            return normalized;
        }

        public static Color Sample(
            float mph,
            RtgExhaustColorStop[] stops,
            float maxMph,
            Func<RtgExhaustColorStop, Color> pickColor)
        {
            if (stops == null || stops.Length == 0 || pickColor == null)
                return Color.white;

            stops = SortForSampling(stops, maxMph);

            if (stops.Length == 1)
                return pickColor(stops[0]);

            if (mph <= stops[0].speedMph)
                return pickColor(stops[0]);

            for (int i = 1; i < stops.Length; i++)
            {
                RtgExhaustColorStop upper = stops[i];
                if (mph > upper.speedMph)
                    continue;

                RtgExhaustColorStop lower = stops[i - 1];
                float span = Mathf.Max(0.001f, upper.speedMph - lower.speedMph);
                float t = Mathf.Clamp01((mph - lower.speedMph) / span);
                return Color.Lerp(pickColor(lower), pickColor(upper), t);
            }

            return pickColor(stops[stops.Length - 1]);
        }

        private static Color ClampColor(Color color)
        {
            return new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                Mathf.Clamp01(color.a <= 0f ? 1f : color.a));
        }
    }
}
