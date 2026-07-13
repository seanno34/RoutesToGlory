using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>Procedural fallback clips for the Pathfinder beam (POC — swap for real assets later).</summary>
    internal static class RtgPathfinderBeamSfx
    {
        private const int SampleRate = 44100;

        public static AudioClip CreateBeamHumLoop()
        {
            const float duration = 2f;
            int sampleCount = Mathf.RoundToInt(SampleRate * duration);
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float hum = Mathf.Sin(2f * Mathf.PI * 68f * t) * 0.34f
                            + Mathf.Sin(2f * Mathf.PI * 136f * t) * 0.16f
                            + Mathf.Sin(2f * Mathf.PI * 204f * t) * 0.06f;
                float shimmer = Mathf.Sin(t * 17.3f) * Mathf.Sin(t * 31.7f) * 0.07f;
                samples[i] = Mathf.Clamp(hum + shimmer, -1f, 1f) * 0.55f;
            }

            var clip = AudioClip.Create("RTG_PathfinderHum", sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static AudioClip CreateVaporizeZap()
        {
            const float duration = 0.14f;
            int sampleCount = Mathf.RoundToInt(SampleRate * duration);
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 42f);
                float noise = PseudoNoise(i * 0.37f + t * 900f);
                float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(920f, 180f, t / duration) * t);
                samples[i] = (noise * 0.55f + sweep * 0.35f) * env;
            }

            var clip = AudioClip.Create("RTG_PathfinderZap", sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float PseudoNoise(float seed)
        {
            float v = Mathf.Sin(seed * 12.9898f) * 43758.5453f;
            return (v - Mathf.Floor(v)) * 2f - 1f;
        }
    }
}
