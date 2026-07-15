using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Screen-drag helpers for positioning exhaust on the glider hull.
    /// Horizontal drag = wings. Vertical drag = height. Shift + vertical drag = depth.
    /// Uses fixed pixel sensitivity so height still works from overhead map view.
    /// </summary>
    public static class RtgExhaustDragTuner
    {
        public const float PickRadiusPixels = 56f;

        /// <summary>Screen pixels to move one full 0–1 anchor unit.</summary>
        public const float PixelsPerAnchorUnit = 140f;

        public static RtgExhaustAnchor ApplyScreenDelta(
            RtgExhaustAnchor anchor,
            Vector2 screenDeltaBottomLeft,
            bool depthAxisDrag)
        {
            anchor = anchor.Clamped();
            float step = 1f / Mathf.Max(24f, PixelsPerAnchorUnit);

            if (depthAxisDrag)
            {
                float depthDelta = (screenDeltaBottomLeft.y + screenDeltaBottomLeft.x * 0.35f) * step;
                return new RtgExhaustAnchor(
                    anchor.span01,
                    anchor.height01,
                    anchor.aftInset01 + depthDelta).Clamped();
            }

            return new RtgExhaustAnchor(
                anchor.span01 + screenDeltaBottomLeft.x * step,
                anchor.height01 + screenDeltaBottomLeft.y * step,
                anchor.aftInset01).Clamped();
        }

        public static bool TryPickEngineNearScreenPoint(
            Camera camera,
            Vector3 mainWorld,
            Vector3 leftWorld,
            Vector3 rightWorld,
            Vector2 screenPosBottomLeft,
            out int engineIndex)
        {
            engineIndex = -1;
            if (camera == null)
                return false;

            float bestDistSq = PickRadiusPixels * PickRadiusPixels;
            int picked = -1;
            TryPickCandidate(camera, 0, mainWorld, screenPosBottomLeft, ref picked, ref bestDistSq);
            TryPickCandidate(camera, 1, leftWorld, screenPosBottomLeft, ref picked, ref bestDistSq);
            TryPickCandidate(camera, 2, rightWorld, screenPosBottomLeft, ref picked, ref bestDistSq);
            engineIndex = picked;
            return engineIndex >= 0;
        }

        private static void TryPickCandidate(
            Camera camera,
            int index,
            Vector3 world,
            Vector2 screenPosBottomLeft,
            ref int picked,
            ref float bestDistSq)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z < 0.5f)
                return;

            float dx = screen.x - screenPosBottomLeft.x;
            float dy = screen.y - screenPosBottomLeft.y;
            float distSq = dx * dx + dy * dy;
            if (distSq > bestDistSq)
                return;

            bestDistSq = distSq;
            picked = index;
        }
    }
}
