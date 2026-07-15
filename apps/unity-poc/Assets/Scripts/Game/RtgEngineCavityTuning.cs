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
        public float plumeMaxLengthMeters;
        public float plumeLengthPower;
        public float plumeBaseWidthScale;
        public float plumeCoreWidthRatio;
        public float plumeOffsetXMeters;
        public float plumeOffsetYMeters;
        public float plumeOffsetZMeters;

        public static RtgEngineCavityTuning Default => new RtgEngineCavityTuning
        {
            sizeMeters = 0.42f,
            offsetXMeters = 0f,
            offsetYMeters = 0f,
            depthOffsetMeters = 0.06f,
            intensity = 1f,
            coreRatio = 0.62f,
            plumeMaxLengthMeters = 6f,
            plumeLengthPower = 1.15f,
            plumeBaseWidthScale = 1f,
            plumeCoreWidthRatio = 0.58f,
            plumeOffsetXMeters = 0f,
            plumeOffsetYMeters = 0f,
            plumeOffsetZMeters = 0f,
        };

        public RtgEngineCavityTuning Clamped()
        {
            return new RtgEngineCavityTuning
            {
                sizeMeters = Mathf.Clamp(sizeMeters, 0.05f, 3f),
                offsetXMeters = Mathf.Clamp(offsetXMeters, -0.35f, 0.35f),
                offsetYMeters = Mathf.Clamp(offsetYMeters, -0.35f, 0.35f),
                depthOffsetMeters = Mathf.Clamp(depthOffsetMeters, -0.5f, 0.5f),
                intensity = Mathf.Clamp(intensity, 0.1f, 5f),
                coreRatio = Mathf.Clamp(coreRatio, 0.15f, 0.95f),
                plumeMaxLengthMeters = Mathf.Clamp(
                    plumeMaxLengthMeters > 0f ? plumeMaxLengthMeters : Default.plumeMaxLengthMeters,
                    0f,
                    24f),
                plumeLengthPower = Mathf.Clamp(
                    plumeLengthPower > 0f ? plumeLengthPower : Default.plumeLengthPower,
                    0.35f,
                    3f),
                plumeBaseWidthScale = Mathf.Clamp(
                    plumeBaseWidthScale > 0f ? plumeBaseWidthScale : Default.plumeBaseWidthScale,
                    0.35f,
                    2f),
                plumeCoreWidthRatio = Mathf.Clamp(
                    plumeCoreWidthRatio > 0f ? plumeCoreWidthRatio : Default.plumeCoreWidthRatio,
                    0.15f,
                    0.95f),
                plumeOffsetXMeters = Mathf.Clamp(plumeOffsetXMeters, -5f, 5f),
                plumeOffsetYMeters = Mathf.Clamp(plumeOffsetYMeters, -5f, 5f),
                plumeOffsetZMeters = Mathf.Clamp(plumeOffsetZMeters, -10f, 10f),
            };
        }

        public static RtgEngineCavityTuning FromLegacy(
            float sizeMeters,
            float depthOffsetMeters,
            float intensity,
            float coreRatio)
        {
            RtgEngineCavityTuning defaults = Default;
            return new RtgEngineCavityTuning
            {
                sizeMeters = sizeMeters > 0f ? sizeMeters : defaults.sizeMeters,
                depthOffsetMeters = depthOffsetMeters,
                intensity = intensity > 0f ? intensity : defaults.intensity,
                coreRatio = coreRatio > 0f ? coreRatio : defaults.coreRatio,
                plumeMaxLengthMeters = defaults.plumeMaxLengthMeters,
                plumeLengthPower = defaults.plumeLengthPower,
                plumeBaseWidthScale = defaults.plumeBaseWidthScale,
                plumeCoreWidthRatio = defaults.plumeCoreWidthRatio,
            }.Clamped();
        }
    }
}
