using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Detects and repairs exhaust tuning saved in legacy coordinate spaces.
    /// </summary>
    public static class RtgGliderExhaustTuningSanitizer
    {
        public const float MaxReasonableCavityDepthMeters = 1f;

        public static bool LooksCorruptCavityDepth(RtgEngineCavityTuning tuning)
        {
            return Mathf.Abs(tuning.depthOffsetMeters) > MaxReasonableCavityDepthMeters;
        }

        public static RtgEngineCavityTuning ResetCavityDepth(RtgEngineCavityTuning tuning)
        {
            tuning.depthOffsetMeters = 0f;
            tuning.offsetXMeters = 0f;
            tuning.offsetYMeters = 0f;
            tuning.plumeOffsetXMeters = 0f;
            tuning.plumeOffsetYMeters = 0f;
            tuning.plumeOffsetZMeters = 0f;
            return tuning.Clamped();
        }

        public static bool TrySanitizePlayerExhaust(
            RtgPlayerLocation player,
            RtgPlayerShipVisual shipVisual,
            out bool anchorsReset,
            out bool cavityDepthReset)
        {
            anchorsReset = false;
            cavityDepthReset = false;
            if (player == null)
                return false;

            bool cavityCorrupt = LooksCorruptCavityDepth(player.shipMainCavity)
                || LooksCorruptCavityDepth(player.shipLeftCavity)
                || LooksCorruptCavityDepth(player.shipRightCavity);

            bool hasSavedAnchors = RtgGliderExhaustAnchors.HasSavedData(player.shipMainExhaustAnchor)
                || RtgGliderExhaustAnchors.HasSavedData(player.shipLeftExhaustAnchor)
                || RtgGliderExhaustAnchors.HasSavedData(player.shipRightExhaustAnchor);

            if (!cavityCorrupt && hasSavedAnchors)
                return false;

            if (!hasSavedAnchors)
            {
                player.shipMainExhaustAnchor = RtgGliderExhaustAnchors.DefaultMain;
                player.shipLeftExhaustAnchor = RtgGliderExhaustAnchors.DefaultLeft;
                player.shipRightExhaustAnchor = RtgGliderExhaustAnchors.DefaultRight;
                player.shipUseCustomEnginePorts = true;
                anchorsReset = true;
            }

            if (cavityCorrupt)
            {
                player.shipMainCavity = ResetCavityDepth(player.shipMainCavity);
                player.shipLeftCavity = ResetCavityDepth(player.shipLeftCavity);
                player.shipRightCavity = ResetCavityDepth(player.shipRightCavity);
                cavityDepthReset = true;
            }

            if (anchorsReset || cavityDepthReset)
            {
                Debug.LogWarning(
                    "[RTG] Migrated legacy exhaust tuning. Use Settings → Exhaust position (X/Y/Z meters), then Save tuning.");
            }

            return anchorsReset || cavityDepthReset;
        }
    }
}
