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
                // Emphasize mid-range so phone speakers can actually reproduce the hum.
                float hum = Mathf.Sin(2f * Mathf.PI * 420f * t) * 0.24f
                            + Mathf.Sin(2f * Mathf.PI * 840f * t) * 0.14f
                            + Mathf.Sin(2f * Mathf.PI * 1260f * t) * 0.07f
                            + Mathf.Sin(2f * Mathf.PI * 95f * t) * 0.05f;
                float shimmer = Mathf.Sin(t * 17.3f) * Mathf.Sin(t * 31.7f) * 0.05f;
                samples[i] = Mathf.Clamp(hum + shimmer, -1f, 1f) * 0.72f;
            }

            var clip = AudioClip.Create("RTG_PathfinderHum", sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static AudioClip CreateBeamArmChirp()
        {
            const float duration = 0.1f;
            int sampleCount = Mathf.RoundToInt(SampleRate * duration);
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.SmoothStep(1f, 0f, t / duration);
                float tone = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(620f, 980f, t / duration) * t);
                samples[i] = tone * env * 0.65f;
            }

            var clip = AudioClip.Create("RTG_PathfinderArm", sampleCount, 1, SampleRate, false);
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
                float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1400f, 420f, t / duration) * t);
                samples[i] = (noise * 0.45f + sweep * 0.45f) * env;
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
