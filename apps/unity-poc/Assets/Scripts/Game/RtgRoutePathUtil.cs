using System;
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
        public const int MaxCorridorCheckPoints = 256;
        public const int MaxDisplayPoints = 256;
        public const int MaxSnapCheckPoints = 256;
        public const double DefaultSimplifyToleranceM = 12.0;

        public static List<IReadOnlyList<RtgRouteGeometry.LatLng>> CollectNetworkPaths(
            IReadOnlyList<RtgRouteGeometry.LatLng> activeLeg,
            RtgRoute[] persistedRoutes,
            string playerEmpireId,
            int maxPointsPerPath)
        {
            return CollectNetworkPaths(activeLeg, persistedRoutes, playerEmpireId, maxPointsPerPath, double.NaN, double.NaN, 0);
        }

        /// <param name="probeLat">When set, only include persisted routes whose bbox nears this point.</param>
        /// <param name="probeLng">When set, only include persisted routes whose bbox nears this point.</param>
        /// <param name="probePadM">BBox padding around the probe for persisted-route filtering.</param>
        public static List<IReadOnlyList<RtgRouteGeometry.LatLng>> CollectNetworkPaths(
            IReadOnlyList<RtgRouteGeometry.LatLng> activeLeg,
            RtgRoute[] persistedRoutes,
            string playerEmpireId,
            int maxPointsPerPath,
            double probeLat,
            double probeLng,
            double probePadM)
        {
            var candidates = new List<IReadOnlyList<RtgRouteGeometry.LatLng>>();

            if (activeLeg != null && activeLeg.Count > 0)
                candidates.Add(SliceActiveLegForCorridorCheck(activeLeg, maxPointsPerPath));

            if (persistedRoutes != null)
            {
                bool useProbe = !double.IsNaN(probeLat) && !double.IsNaN(probeLng) && probePadM > 0;
                foreach (RtgRoute route in persistedRoutes)
                {
                    if (route == null || route.path_json == null || route.path_json.Length < 2) continue;
                    if (!string.IsNullOrEmpty(route.empire_id) &&
                        !string.IsNullOrEmpty(playerEmpireId) &&
                        route.empire_id != playerEmpireId)
                        continue;
                    if (!string.IsNullOrEmpty(route.status) && route.status != "active") continue;

                    if (useProbe && !RouteBboxNearPoint(route.path_json, probeLat, probeLng, probePadM))
                        continue;

                    candidates.Add(DecimateUniform(route.path_json, maxPointsPerPath));
                }
            }

            return candidates;
        }

        /// <summary>
        /// Tap checks should weight the recent driven path — not sparse samples from miles ago.
        /// </summary>
        private static IReadOnlyList<RtgRouteGeometry.LatLng> SliceActiveLegForCorridorCheck(
            IReadOnlyList<RtgRouteGeometry.LatLng> activeLeg,
            int maxPoints)
        {
            if (activeLeg.Count <= maxPoints)
            {
                var copy = new List<RtgRouteGeometry.LatLng>(activeLeg.Count);
                for (int i = 0; i < activeLeg.Count; i++)
                    copy.Add(activeLeg[i]);
                return copy;
            }

            int start = activeLeg.Count - maxPoints;
            var tail = new List<RtgRouteGeometry.LatLng>(maxPoints);
            for (int i = start; i < activeLeg.Count; i++)
                tail.Add(activeLeg[i]);
            return tail;
        }

        private static bool RouteBboxNearPoint(
            RtgPathPoint[] path,
            double lat,
            double lng,
            double padM)
        {
            if (path == null || path.Length == 0) return false;

            double south = path[0].lat;
            double north = path[0].lat;
            double west = path[0].lng;
            double east = path[0].lng;

            foreach (RtgPathPoint p in path)
            {
                south = Math.Min(south, p.lat);
                north = Math.Max(north, p.lat);
                west = Math.Min(west, p.lng);
                east = Math.Max(east, p.lng);
            }

            double padLat = padM / MetersPerDegreeLat;
            double padLng = padM / (MetersPerDegreeLat * Math.Cos(lat * Math.PI / 180.0));
            south -= padLat;
            north += padLat;
            west -= padLng;
            east += padLng;

            return lat >= south && lat <= north && lng >= west && lng <= east;
        }

        private const double MetersPerDegreeLat = 111_320.0;

        public static List<RtgRouteGeometry.LatLng> SimplifyRdp(
            IReadOnlyList<RtgRouteGeometry.LatLng> path,
            double toleranceM)
        {
            if (path == null || path.Count <= 2)
            {
                var copy = new List<RtgRouteGeometry.LatLng>();
                if (path != null)
                {
                    for (int i = 0; i < path.Count; i++)
                        copy.Add(path[i]);
                }
                return copy;
            }

            var keep = new bool[path.Count];
            keep[0] = true;
            keep[path.Count - 1] = true;
            SimplifyRdpRange(path, 0, path.Count - 1, toleranceM, keep);

            var result = new List<RtgRouteGeometry.LatLng>();
            for (int i = 0; i < path.Count; i++)
            {
                if (keep[i])
                    result.Add(path[i]);
            }

            return result.Count >= 2 ? result : new List<RtgRouteGeometry.LatLng> { path[0], path[path.Count - 1] };
        }

        private static void SimplifyRdpRange(
            IReadOnlyList<RtgRouteGeometry.LatLng> path,
            int start,
            int end,
            double toleranceM,
            bool[] keep)
        {
            if (end <= start + 1) return;

            double maxDist = 0;
            int index = start;
            RtgRouteGeometry.LatLng a = path[start];
            RtgRouteGeometry.LatLng b = path[end];

            for (int i = start + 1; i < end; i++)
            {
                RtgRouteGeometry.LatLng p = path[i];
                double d = RtgRouteGeometry.DistancePointToSegmentM(
                    p.lat, p.lng, a.lat, a.lng, b.lat, b.lng);
                if (d > maxDist)
                {
                    maxDist = d;
                    index = i;
                }
            }

            if (maxDist > toleranceM)
            {
                keep[index] = true;
                SimplifyRdpRange(path, start, index, toleranceM, keep);
                SimplifyRdpRange(path, index, end, toleranceM, keep);
            }
        }

        public static List<RtgRouteGeometry.LatLng> CleanupForPersist(
            IReadOnlyList<RtgRouteGeometry.LatLng> path,
            double toleranceM = DefaultSimplifyToleranceM)
        {
            if (path == null || path.Count == 0)
                return new List<RtgRouteGeometry.LatLng>();

            var deduped = new List<RtgRouteGeometry.LatLng>(path.Count);
            foreach (RtgRouteGeometry.LatLng point in path)
            {
                if (deduped.Count == 0)
                {
                    deduped.Add(point);
                    continue;
                }

                RtgRouteGeometry.LatLng last = deduped[deduped.Count - 1];
                if (Math.Abs(point.lat - last.lat) > 1e-9 || Math.Abs(point.lng - last.lng) > 1e-9)
                    deduped.Add(point);
            }

            return SimplifyRdp(deduped, toleranceM);
        }

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
