using System;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Exhaust nozzle position as fractions of the glider mesh bounds (0–1).
    /// One coordinate system: always near the hull, sliders always have visible effect.
    /// </summary>
    [Serializable]
    public struct RtgExhaustAnchor
    {
        [Tooltip("0 = left wing tip, 0.5 = centerline, 1 = right wing tip.")]
        public float span01;

        [Tooltip("0 = bottom of mesh bounds, 1 = top.")]
        public float height01;

        [Tooltip("0 = trailing edge (aft), 1 = toward nose. Keep low (0.05–0.2) for nozzle mouths.")]
        public float aftInset01;

        public RtgExhaustAnchor(float span, float height, float aftInset)
        {
            span01 = span;
            height01 = height;
            aftInset01 = aftInset;
        }

        public RtgExhaustAnchor Clamped()
        {
            return new RtgExhaustAnchor(
                Mathf.Clamp01(span01),
                Mathf.Clamp01(height01),
                Mathf.Clamp01(aftInset01));
        }
    }

    public static class RtgGliderExhaustAnchors
    {
        public static readonly RtgExhaustAnchor DefaultMain = new(0.5f, 0.38f, 0.07f);
        public static readonly RtgExhaustAnchor DefaultLeft = new(0.24f, 0.36f, 0.10f);
        public static readonly RtgExhaustAnchor DefaultRight = new(0.76f, 0.36f, 0.10f);

        public static Vector3 ToMeshLocal(Bounds bounds, RtgExhaustAnchor anchor)
        {
            anchor = anchor.Clamped();
            return new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, anchor.span01),
                Mathf.Lerp(bounds.min.y, bounds.max.y, anchor.height01),
                Mathf.Lerp(bounds.min.z, bounds.max.z, anchor.aftInset01));
        }

        public static RtgExhaustAnchor FromMeshLocal(Bounds bounds, Vector3 local)
        {
            if (bounds.size.sqrMagnitude < 1e-8f)
                return DefaultMain;

            return new RtgExhaustAnchor(
                SafeInverseLerp(bounds.min.x, bounds.max.x, local.x),
                SafeInverseLerp(bounds.min.y, bounds.max.y, local.y),
                SafeInverseLerp(bounds.min.z, bounds.max.z, local.z)).Clamped();
        }

        public static RtgExhaustAnchor UpdateAnchorWingsAndHeight(
            Bounds bounds,
            RtgExhaustAnchor current,
            Vector3 meshLocalHit)
        {
            current = current.Clamped();
            return new RtgExhaustAnchor(
                SafeInverseLerp(bounds.min.x, bounds.max.x, meshLocalHit.x),
                SafeInverseLerp(bounds.min.y, bounds.max.y, meshLocalHit.y),
                current.aftInset01).Clamped();
        }

        public static RtgExhaustAnchor UpdateAnchorDepth(
            Bounds bounds,
            RtgExhaustAnchor current,
            Vector3 meshLocalHit)
        {
            current = current.Clamped();
            return new RtgExhaustAnchor(
                current.span01,
                current.height01,
                SafeInverseLerp(bounds.min.z, bounds.max.z, meshLocalHit.z)).Clamped();
        }

        public static bool HasSavedData(RtgExhaustAnchor anchor)
        {
            return anchor.span01 > 0.001f
                || anchor.height01 > 0.001f
                || anchor.aftInset01 > 0.001f;
        }

        public static RtgGliderEngineMounts ToMounts(
            Bounds bounds,
            RtgExhaustAnchor main,
            RtgExhaustAnchor left,
            RtgExhaustAnchor right)
        {
            return new RtgGliderEngineMounts(
                ToMeshLocal(bounds, main),
                ToMeshLocal(bounds, left),
                ToMeshLocal(bounds, right));
        }

        public static void FromMounts(
            Bounds bounds,
            RtgGliderEngineMounts mounts,
            out RtgExhaustAnchor main,
            out RtgExhaustAnchor left,
            out RtgExhaustAnchor right)
        {
            main = FromMeshLocal(bounds, mounts.Main);
            left = FromMeshLocal(bounds, mounts.Left);
            right = FromMeshLocal(bounds, mounts.Right);
        }

        /// <summary>
        /// Pull a vertex-mined aft position inward so it sits on the nozzle band, not the tail tip.
        /// </summary>
        public static RtgExhaustAnchor SoftenTailSnap(Bounds bounds, Vector3 meshLocal)
        {
            RtgExhaustAnchor anchor = FromMeshLocal(bounds, meshLocal);
            anchor.aftInset01 = Mathf.Clamp(anchor.aftInset01 + 0.08f, 0.05f, 0.28f);
            return anchor.Clamped();
        }

        private static float SafeInverseLerp(float min, float max, float value)
        {
            float span = max - min;
            if (Mathf.Abs(span) < 1e-6f)
                return 0.5f;
            return Mathf.Clamp01((value - min) / span);
        }
    }
}
