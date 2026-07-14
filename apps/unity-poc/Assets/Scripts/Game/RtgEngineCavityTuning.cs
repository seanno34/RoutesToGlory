using UnityEngine;

namespace RoutesToGlory.Game
{
    [System.Serializable]
    public struct RtgEngineCavityTuning
    {
        public float sizeMeters;
        public float offsetXMeters;
        public float offsetYMeters;
        public float depthOffsetMeters;
        public float intensity;
        public float coreRatio;

        public static RtgEngineCavityTuning Default => new RtgEngineCavityTuning
        {
            sizeMeters = 0.42f,
            offsetXMeters = 0f,
            offsetYMeters = 0f,
            depthOffsetMeters = 0.06f,
            intensity = 1f,
            coreRatio = 0.62f,
        };

        public RtgEngineCavityTuning Clamped()
        {
            return new RtgEngineCavityTuning
            {
                sizeMeters = Mathf.Clamp(sizeMeters, 0.05f, 3f),
                offsetXMeters = Mathf.Clamp(offsetXMeters, -5f, 5f),
                offsetYMeters = Mathf.Clamp(offsetYMeters, -5f, 5f),
                depthOffsetMeters = Mathf.Clamp(depthOffsetMeters, -50f, 50f),
                intensity = Mathf.Clamp(intensity, 0.1f, 5f),
                coreRatio = Mathf.Clamp(coreRatio, 0.15f, 0.95f),
            };
        }

        public static RtgEngineCavityTuning FromLegacy(
            float sizeMeters,
            float depthOffsetMeters,
            float intensity,
            float coreRatio)
        {
            return new RtgEngineCavityTuning
            {
                sizeMeters = sizeMeters > 0f ? sizeMeters : Default.sizeMeters,
                depthOffsetMeters = depthOffsetMeters,
                intensity = intensity > 0f ? intensity : Default.intensity,
                coreRatio = coreRatio > 0f ? coreRatio : Default.coreRatio,
            }.Clamped();
        }
    }
}
