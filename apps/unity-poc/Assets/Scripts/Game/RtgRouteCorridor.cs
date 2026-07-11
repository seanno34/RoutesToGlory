using System.Collections.Generic;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Client-side corridor checks against the active leg and persisted route network.
    /// </summary>
    public static class RtgRouteCorridor
    {
        public static bool IsWithinNetwork(
            double lat,
            double lng,
            IReadOnlyList<RtgRouteGeometry.LatLng> activeLeg,
            RtgRoute[] persistedRoutes,
            string playerEmpireId,
            double radiusM,
            out double distanceM)
        {
            var candidates = new List<IReadOnlyList<RtgRouteGeometry.LatLng>>();

            if (activeLeg != null && activeLeg.Count > 0)
                candidates.Add(RtgRoutePathUtil.DecimateForCorridorCheck(activeLeg));

            if (persistedRoutes != null)
            {
                foreach (RtgRoute route in persistedRoutes)
                {
                    if (route == null || route.path_json == null || route.path_json.Length < 2) continue;
                    if (!string.IsNullOrEmpty(route.empire_id) &&
                        !string.IsNullOrEmpty(playerEmpireId) &&
                        route.empire_id != playerEmpireId)
                        continue;
                    if (!string.IsNullOrEmpty(route.status) && route.status != "active") continue;
                    candidates.Add(RtgRoutePathUtil.DecimateForCorridorCheck(route.path_json));
                }
            }

            distanceM = RtgRoutePathUtil.MinDistanceToPaths(lat, lng, candidates);
            return distanceM <= radiusM;
        }
    }
}
