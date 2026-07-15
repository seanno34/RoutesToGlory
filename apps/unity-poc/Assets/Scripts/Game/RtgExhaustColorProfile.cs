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
        public Color plumeOuter;
        public Color plumeCore;
        public float plumeMaxLengthMeters;
        public float plumeLengthScale;

        public RtgExhaustColorStop(
            float mph,
            Color cavityOuterColor,
            Color cavityCoreColor,
            Color flameColor,
            Color glowColor,
            float plumeMaxLength = 6f,
            float plumeScale = 1f)
        {
            speedMph = mph;
            cavityOuter = cavityOuterColor;
            cavityCore = cavityCoreColor;
            flame = flameColor;
            glow = glowColor;
            plumeOuter = flameColor;
            plumeCore = glowColor;
            plumeMaxLengthMeters = plumeMaxLength;
            plumeLengthScale = plumeScale;
        }
    }

    public static class RtgExhaustColorProfile
    {
        public const int StopCount = 4;
        public const float DefaultPlumeMaxLengthMeters = 6f;
        public const float DefaultPlumeLengthScale = 1f;

        public static RtgExhaustColorStop[] CreateDefaultStops()
        {
            return new[]
            {
                new RtgExhaustColorStop(
                    0f,
                    new Color(1f, 0.12f, 0f),
                    new Color(1f, 0.28f, 0.02f),
                    new Color(1f, 0.38f, 0.02f),
                    new Color(1f, 0.3f, 0.01f),
                    plumeMaxLength: 1.5f,
                    plumeScale: 0.55f),
                new RtgExhaustColorStop(
                    40f,
                    new Color(1f, 0.35f, 0.04f),
                    new Color(1f, 0.45f, 0.08f),
                    new Color(1f, 0.5f, 0.1f),
                    new Color(0.35f, 0.75f, 0.95f),
                    plumeMaxLength: 4f,
                    plumeScale: 0.85f),
                new RtgExhaustColorStop(
                    70f,
                    new Color(1f, 0.42f, 0.08f),
                    new Color(1f, 0.55f, 0.12f),
                    new Color(1f, 0.55f, 0.08f),
                    new Color(1f, 0.55f, 0.2f),
                    plumeMaxLength: 5.5f,
                    plumeScale: 1f),
                new RtgExhaustColorStop(
                    99f,
                    new Color(0.08f, 0.78f, 1f),
                    new Color(0.45f, 0.98f, 1f),
                    new Color(0.55f, 0.95f, 1f),
                    new Color(0.12f, 0.62f, 1f),
                    plumeMaxLength: 6f,
                    plumeScale: 1.15f),
            };
        }

        public static RtgExhaustColorStop[] NormalizeStops(RtgExhaustColorStop[] stops, float maxMph)
        {
            maxMph = Mathf.Max(1f, maxMph);
            if (stops == null || stops.Length == 0)
                stops = CreateDefaultStops();

            var normalized = new RtgExhaustColorStop[StopCount];
            RtgExhaustColorStop[] defaults = CreateDefaultStops();
            for (int i = 0; i < StopCount; i++)
            {
                RtgExhaustColorStop source = i < stops.Length
                    ? stops[i]
                    : defaults[Mathf.Min(i, StopCount - 1)];
                RtgExhaustColorStop fallback = defaults[Mathf.Min(i, StopCount - 1)];
                normalized[i] = new RtgExhaustColorStop
                {
                    speedMph = Mathf.Clamp(source.speedMph, 0f, maxMph),
                    cavityOuter = ClampColor(source.cavityOuter),
                    cavityCore = ClampColor(source.cavityCore),
                    flame = ClampColor(source.flame),
                    glow = ClampColor(source.glow),
                    plumeOuter = ClampColor(ResolvePlumeOuter(source)),
                    plumeCore = ClampColor(ResolvePlumeCore(source)),
                    plumeMaxLengthMeters = Mathf.Clamp(
                        source.plumeMaxLengthMeters > 0f
                            ? source.plumeMaxLengthMeters
                            : fallback.plumeMaxLengthMeters,
                        0f,
                        24f),
                    plumeLengthScale = Mathf.Clamp(
                        source.plumeLengthScale > 0f
                            ? source.plumeLengthScale
                            : fallback.plumeLengthScale,
                        0.15f,
                        2.5f),
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

        public static float SampleFloat(
            float mph,
            RtgExhaustColorStop[] stops,
            float maxMph,
            Func<RtgExhaustColorStop, float> pickValue)
        {
            if (stops == null || stops.Length == 0 || pickValue == null)
                return 0f;

            stops = SortForSampling(stops, maxMph);

            if (stops.Length == 1)
                return pickValue(stops[0]);

            if (mph <= stops[0].speedMph)
                return pickValue(stops[0]);

            for (int i = 1; i < stops.Length; i++)
            {
                RtgExhaustColorStop upper = stops[i];
                if (mph > upper.speedMph)
                    continue;

                RtgExhaustColorStop lower = stops[i - 1];
                float span = Mathf.Max(0.001f, upper.speedMph - lower.speedMph);
                float t = Mathf.Clamp01((mph - lower.speedMph) / span);
                return Mathf.Lerp(pickValue(lower), pickValue(upper), t);
            }

            return pickValue(stops[stops.Length - 1]);
        }

        public static float SamplePlumeMaxLengthMeters(
            float mph,
            RtgExhaustColorStop[] stops,
            float maxMph)
        {
            return SampleFloat(
                mph,
                stops,
                maxMph,
                stop => stop.plumeMaxLengthMeters > 0f
                    ? stop.plumeMaxLengthMeters
                    : DefaultPlumeMaxLengthMeters);
        }

        public static float SamplePlumeLengthScale(
            float mph,
            RtgExhaustColorStop[] stops,
            float maxMph)
        {
            return SampleFloat(
                mph,
                stops,
                maxMph,
                stop => stop.plumeLengthScale > 0f
                    ? stop.plumeLengthScale
                    : DefaultPlumeLengthScale);
        }

        public static float GetPlumeMaxLengthMeters(RtgExhaustColorStop stop)
        {
            return stop.plumeMaxLengthMeters > 0f
                ? stop.plumeMaxLengthMeters
                : DefaultPlumeMaxLengthMeters;
        }

        public static float GetPlumeLengthScale(RtgExhaustColorStop stop)
        {
            return stop.plumeLengthScale > 0f
                ? stop.plumeLengthScale
                : DefaultPlumeLengthScale;
        }

        private static Color ResolvePlumeOuter(RtgExhaustColorStop stop)
        {
            return HasVisibleColor(stop.plumeOuter) ? stop.plumeOuter : stop.flame;
        }

        private static Color ResolvePlumeCore(RtgExhaustColorStop stop)
        {
            return HasVisibleColor(stop.plumeCore) ? stop.plumeCore : stop.glow;
        }

        private static bool HasVisibleColor(Color color)
        {
            return color.r + color.g + color.b > 0.01f;
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
