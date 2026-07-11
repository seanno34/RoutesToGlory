using System.Collections.Generic;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Cached list of map markers so tap-to-connect does not scan the scene every frame.
    /// Refreshed when Echo Sites respawn.
    /// </summary>
    public static class RtgMapMarkerRegistry
    {
        private static readonly List<RtgMapMarker> Markers = new();

        public static IReadOnlyList<RtgMapMarker> All => Markers;

        public static void Refresh()
        {
            Markers.Clear();
#if UNITY_2023_1_OR_NEWER
            RtgMapMarker[] found = Object.FindObjectsByType<RtgMapMarker>(FindObjectsSortMode.None);
#else
            RtgMapMarker[] found = Object.FindObjectsOfType<RtgMapMarker>();
#endif
            foreach (RtgMapMarker marker in found)
            {
                if (marker != null)
                    Markers.Add(marker);
            }
        }

        public static void Register(RtgMapMarker marker)
        {
            if (marker == null || Markers.Contains(marker)) return;
            Markers.Add(marker);
        }

        public static void Unregister(RtgMapMarker marker)
        {
            if (marker == null) return;
            Markers.Remove(marker);
        }

        public static RtgMapMarker FindByTargetId(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return null;
            foreach (RtgMapMarker marker in Markers)
            {
                if (marker != null && marker.targetId == targetId)
                    return marker;
            }
            return null;
        }
    }
}
