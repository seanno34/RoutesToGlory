using System;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>Lat/lng forward wedge used by the Pathfinder beam and terrain radar.</summary>
    public static class RtgForwardCorridor
    {
        public static void OffsetMeters(
            double lat,
            double lng,
            float headingRad,
            float forwardM,
            float lateralM,
            out double outLat,
            out double outLng)
        {
            double lngM = LngMetersPerDegree(lat);
            double dLat = (Math.Cos(headingRad) * forwardM - Math.Sin(headingRad) * lateralM) / RtgFogTileMath.LatM;
            double dLng = (Math.Sin(headingRad) * forwardM + Math.Cos(headingRad) * lateralM) / lngM;
            outLat = lat + dLat;
            outLng = lng + dLng;
        }

        public static bool TryCorridorFrame(
            double playerLat,
            double playerLng,
            double obstacleLat,
            double obstacleLng,
            float headingRad,
            out float forwardM,
            out float lateralM)
        {
            double lngM = LngMetersPerDegree(playerLat);
            double dLat = (obstacleLat - playerLat) * RtgFogTileMath.LatM;
            double dLng = (obstacleLng - playerLng) * lngM;

            float sin = (float)Math.Sin(headingRad);
            float cos = (float)Math.Cos(headingRad);
            forwardM = (float)(dLat * cos + dLng * sin);
            lateralM = (float)(-dLat * sin + dLng * cos);
            return true;
        }

        public static bool IsInsideWedge(
            float forwardM,
            float lateralM,
            float maxForwardM,
            float halfWidthNearM,
            float halfWidthFarM,
            float obstacleRadiusM)
        {
            if (forwardM < -obstacleRadiusM || forwardM > maxForwardM + obstacleRadiusM)
                return false;

            float t = maxForwardM > 0.01f ? Mathf.Clamp(forwardM / maxForwardM, 0f, 1f) : 0f;
            float halfWidth = halfWidthNearM + (halfWidthFarM - halfWidthNearM) * t;
            return Math.Abs(lateralM) <= halfWidth + obstacleRadiusM;
        }

        public static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            double avgLatRad = (lat1 + lat2) * 0.5 * Math.PI / 180.0;
            double metersPerDegLng = RtgFogTileMath.LatM * Math.Cos(avgLatRad);
            double dLat = (lat2 - lat1) * RtgFogTileMath.LatM;
            double dLng = (lng2 - lng1) * metersPerDegLng;
            return Math.Sqrt(dLat * dLat + dLng * dLng);
        }

        private static double LngMetersPerDegree(double lat) =>
            RtgFogTileMath.LatM * Math.Cos(lat * Math.PI / 180.0);
    }
}
