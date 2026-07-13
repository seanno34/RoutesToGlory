using System.Collections.Generic;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Client-side corridor checks and snap-to-route for the persisted network.
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
            return IsWithinNetwork(
                lat, lng, activeLeg, persistedRoutes, playerEmpireId, radiusM, out distanceM,
                probePadM: radiusM * 2.0);
        }

        public static bool IsWithinNetwork(
            double lat,
            double lng,
            IReadOnlyList<RtgRouteGeometry.LatLng> activeLeg,
            RtgRoute[] persistedRoutes,
            string playerEmpireId,
            double radiusM,
            out double distanceM,
            double probePadM)
        {
            List<IReadOnlyList<RtgRouteGeometry.LatLng>> candidates = RtgRoutePathUtil.CollectNetworkPaths(
                activeLeg,
                persistedRoutes,
                playerEmpireId,
                RtgRoutePathUtil.MaxCorridorCheckPoints,
                lat,
                lng,
                probePadM);

            distanceM = RtgRoutePathUtil.MinDistanceToPaths(lat, lng, candidates);
            return distanceM <= radiusM;
        }

        /// <summary>
        /// When the glider is near an owned route corridor, snap to the nearest point on that path.
        /// </summary>
        public static bool TrySnapToNetwork(
            double lat,
            double lng,
            IReadOnlyList<RtgRouteGeometry.LatLng> activeLeg,
            RtgRoute[] persistedRoutes,
            string playerEmpireId,
            double snapRadiusM,
            out double snappedLat,
            out double snappedLng,
            out double distanceM)
        {
            snappedLat = lat;
            snappedLng = lng;
            distanceM = double.PositiveInfinity;

            if (snapRadiusM <= 0)
                return false;

            List<IReadOnlyList<RtgRouteGeometry.LatLng>> candidates = RtgRoutePathUtil.CollectNetworkPaths(
                activeLeg,
                persistedRoutes,
                playerEmpireId,
                RtgRoutePathUtil.MaxSnapCheckPoints);

            if (candidates.Count == 0)
                return false;

            distanceM = RtgRoutePathUtil.MinDistanceToPaths(lat, lng, candidates);
            if (distanceM > snapRadiusM)
                return false;

            RtgRouteGeometry.LatLng foot = RtgRouteGeometry.NearestPointOnAnyPath(lat, lng, candidates);
            snappedLat = foot.lat;
            snappedLng = foot.lng;
            return true;
        }
    }
}
