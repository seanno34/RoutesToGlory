using System;
using System.Collections.Generic;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Port of packages/shared/src/map/fog-of-war.ts — must stay in sync with the
    /// server so tile ids match GET /worlds/:id/exploration and GPS reveal deltas.
    /// </summary>
    public static class RtgFogTileMath
    {
        public const double LatM = 111_320.0;

        public static string LatLngToTileId(double lat, double lng, float tileSizeM)
        {
            double lngM = LngMetersPerDegree(lat);
            int x = (int)Math.Floor((lng * lngM) / tileSizeM);
            int y = (int)Math.Floor((lat * LatM) / tileSizeM);
            return $"{x}:{y}";
        }

        public static void TileIdToCenter(string tileId, float tileSizeM, out double lat, out double lng)
        {
            string[] parts = tileId.Split(':');
            int x = int.Parse(parts[0]);
            int y = int.Parse(parts[1]);
            lat = ((y + 0.5) * tileSizeM) / LatM;
            double lngM = LngMetersPerDegree(lat);
            lng = ((x + 0.5) * tileSizeM) / lngM;
        }

        public static List<string> TilesInRadius(double lat, double lng, float radiusM, float tileSizeM)
        {
            double lngM = LngMetersPerDegree(lat);
            int tileRadius = (int)Math.Ceiling(radiusM / tileSizeM) + 1;
            int centerX = (int)Math.Floor((lng * lngM) / tileSizeM);
            int centerY = (int)Math.Floor((lat * LatM) / tileSizeM);
            var tiles = new List<string>();

            for (int dy = -tileRadius; dy <= tileRadius; dy++)
            {
                for (int dx = -tileRadius; dx <= tileRadius; dx++)
                {
                    string tileId = $"{centerX + dx}:{centerY + dy}";
                    TileIdToCenter(tileId, tileSizeM, out double cLat, out double cLng);
                    double dLat = (cLat - lat) * LatM;
                    double dLng = (cLng - lng) * lngM;
                    if (Math.Sqrt(dLat * dLat + dLng * dLng) <= radiusM)
                        tiles.Add(tileId);
                }
            }

            return tiles;
        }

        public static bool IsExplored(HashSet<string> explored, double lat, double lng, float tileSizeM) =>
            explored.Contains(LatLngToTileId(lat, lng, tileSizeM));

        private static double LngMetersPerDegree(double lat) =>
            LatM * Math.Cos(lat * Math.PI / 180.0);
    }
}
