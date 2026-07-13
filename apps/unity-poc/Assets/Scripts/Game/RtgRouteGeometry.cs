using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Mirrors apps/api/src/services/route-geometry.ts so the client can pre-check
    /// tap-to-connect range before calling POST /worlds/:id/claim.
    /// </summary>
    public static class RtgRouteGeometry
    {
        private const double EarthR = 6_371_000;
        private const double MetersPerDegreeLat = 111_320.0;

        public struct LatLng
        {
            public double lat, lng;
            public LatLng(double latitude, double longitude) { lat = latitude; lng = longitude; }
        }

        public static double DistancePointToPathM(double lat, double lng, IReadOnlyList<LatLng> path)
        {
            if (path == null || path.Count == 0) return double.PositiveInfinity;
            if (path.Count == 1) return Haversine(lat, lng, path[0].lat, path[0].lng);

            double min = double.PositiveInfinity;
            for (int i = 1; i < path.Count; i++)
            {
                LatLng a = path[i - 1], b = path[i];
                double d = DistancePointToSegmentM(lat, lng, a.lat, a.lng, b.lat, b.lng);
                if (d < min) min = d;
            }
            return min;
        }

        public static bool IsWithinCorridor(double lat, double lng, IReadOnlyList<LatLng> path, double radiusM) =>
            DistancePointToPathM(lat, lng, path) <= radiusM;

        /// <summary>Closest point on a polyline to (lat, lng).</summary>
        public static LatLng NearestPointOnPath(double lat, double lng, IReadOnlyList<LatLng> path)
        {
            if (path == null || path.Count == 0) return new LatLng(lat, lng);
            if (path.Count == 1) return path[0];

            LatLng best = path[0];
            double bestD = double.PositiveInfinity;
            for (int i = 1; i < path.Count; i++)
            {
                LatLng foot = NearestPointOnSegment(lat, lng, path[i - 1], path[i]);
                double d = Haversine(lat, lng, foot.lat, foot.lng);
                if (d < bestD)
                {
                    bestD = d;
                    best = foot;
                }
            }

            return best;
        }

        public static LatLng NearestPointOnAnyPath(
            double lat,
            double lng,
            IReadOnlyList<IReadOnlyList<LatLng>> paths)
        {
            LatLng best = new LatLng(lat, lng);
            double bestD = double.PositiveInfinity;
            if (paths == null) return best;

            foreach (IReadOnlyList<LatLng> path in paths)
            {
                if (path == null || path.Count == 0) continue;
                LatLng foot = NearestPointOnPath(lat, lng, path);
                double d = Haversine(lat, lng, foot.lat, foot.lng);
                if (d < bestD)
                {
                    bestD = d;
                    best = foot;
                }
            }

            return best;
        }

        public static double DistancePointToSegmentM(
            double pLat, double pLng, double aLat, double aLng, double bLat, double bLng)
        {
            double latM = MetersPerDegreeLat;
            double lngM = MetersPerDegreeLat * Math.Cos(ToRad(pLat));

            double px = pLng * lngM, py = pLat * latM;
            double ax = aLng * lngM, ay = aLat * latM;
            double bx = bLng * lngM, by = bLat * latM;

            double dx = bx - ax, dy = by - ay;
            double lenSq = dx * dx + dy * dy;

            if (lenSq < 1) return Haversine(pLat, pLng, aLat, aLng);

            double t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lenSq));
            double cx = ax + t * dx, cy = ay + t * dy;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        private static LatLng NearestPointOnSegment(
            double pLat, double pLng, LatLng a, LatLng b)
        {
            double latM = MetersPerDegreeLat;
            double lngM = MetersPerDegreeLat * Math.Cos(ToRad(pLat));

            double px = pLng * lngM, py = pLat * latM;
            double ax = a.lng * lngM, ay = a.lat * latM;
            double bx = b.lng * lngM, by = b.lat * latM;

            double dx = bx - ax, dy = by - ay;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1) return a;

            double t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lenSq));
            return new LatLng(
                (ay + t * dy) / latM,
                (ax + t * dx) / lngM);
        }

        private static double Haversine(double lat1, double lng1, double lat2, double lng2)
        {
            double dLat = ToRad(lat2 - lat1);
            double dLng = ToRad(lng2 - lng1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * EarthR * Math.Asin(Math.Sqrt(a));
        }

        private static double ToRad(double d) => d * Math.PI / 180.0;
    }
}
