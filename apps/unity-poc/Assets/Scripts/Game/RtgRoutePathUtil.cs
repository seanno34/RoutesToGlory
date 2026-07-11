using System.Collections.Generic;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Path simplification for tap checks, claim payloads, and map rendering.
    /// Keeps corridor math accurate while bounding CPU and JSON size.
    /// </summary>
    public static class RtgRoutePathUtil
    {
        public const int MaxClaimPoints = 64;
        public const int MaxCorridorCheckPoints = 128;
        public const int MaxDisplayPoints = 256;

        public static List<RtgRouteGeometry.LatLng> DecimateUniform(
            IReadOnlyList<RtgRouteGeometry.LatLng> path,
            int maxPoints)
        {
            var result = new List<RtgRouteGeometry.LatLng>();
            if (path == null || path.Count == 0) return result;
            if (path.Count <= maxPoints || maxPoints < 2)
            {
                for (int i = 0; i < path.Count; i++)
                    result.Add(path[i]);
                return result;
            }

            double step = (path.Count - 1) / (double)(maxPoints - 1);
            for (int i = 0; i < maxPoints; i++)
            {
                int idx = System.Math.Min(path.Count - 1, (int)System.Math.Round(i * step));
                result.Add(path[idx]);
            }
            return result;
        }

        public static List<RtgRouteGeometry.LatLng> DecimateUniform(
            RtgPathPoint[] path,
            int maxPoints)
        {
            if (path == null || path.Length == 0)
                return new List<RtgRouteGeometry.LatLng>();

            var wrapped = new List<RtgRouteGeometry.LatLng>(path.Length);
            foreach (RtgPathPoint p in path)
                wrapped.Add(new RtgRouteGeometry.LatLng(p.lat, p.lng));
            return DecimateUniform(wrapped, maxPoints);
        }

        public static List<RtgRouteGeometry.LatLng> DecimateForClaim(IReadOnlyList<RtgRouteGeometry.LatLng> path) =>
            DecimateUniform(path, MaxClaimPoints);

        public static List<RtgRouteGeometry.LatLng> DecimateForCorridorCheck(IReadOnlyList<RtgRouteGeometry.LatLng> path) =>
            DecimateUniform(path, MaxCorridorCheckPoints);

        public static List<RtgRouteGeometry.LatLng> DecimateForCorridorCheck(RtgPathPoint[] path) =>
            DecimateUniform(path, MaxCorridorCheckPoints);

        public static RtgPathPoint[] DecimatePathPointsForDisplay(RtgPathPoint[] path)
        {
            List<RtgRouteGeometry.LatLng> simplified = DecimateUniform(path, MaxDisplayPoints);
            var result = new RtgPathPoint[simplified.Count];
            for (int i = 0; i < simplified.Count; i++)
                result[i] = new RtgPathPoint { lat = simplified[i].lat, lng = simplified[i].lng };
            return result;
        }

        /// <summary>Minimum distance from a point to any candidate path (meters).</summary>
        public static double MinDistanceToPaths(
            double lat,
            double lng,
            IReadOnlyList<IReadOnlyList<RtgRouteGeometry.LatLng>> paths)
        {
            double min = double.PositiveInfinity;
            if (paths == null) return min;

            foreach (IReadOnlyList<RtgRouteGeometry.LatLng> path in paths)
            {
                if (path == null || path.Count == 0) continue;
                double d = RtgRouteGeometry.DistancePointToPathM(lat, lng, path);
                if (d < min) min = d;
            }
            return min;
        }
    }
}
