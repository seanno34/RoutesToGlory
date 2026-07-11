using System.Collections.Generic;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Real-world driving paths from OSRM (OpenStreetMap road network).
    /// </summary>
    public static class RtgRoadRoutes
    {
        // OSRM driving: home (10 Tiffany Ln area) → Casper, WY city center (~83 km).
        private static readonly (double lat, double lng)[] HomeToCasperOutbound =
        {
            (42.741278, -105.398316),
            (42.741041, -105.386878),
            (42.748518, -105.386846),
            (42.751420, -105.406324),
            (42.762027, -105.406620),
            (42.761821, -105.424868),
            (42.780282, -105.445613),
            (42.781379, -105.515805),
            (42.792716, -105.554274),
            (42.794009, -105.603384),
            (42.800664, -105.651581),
            (42.821046, -105.719104),
            (42.818874, -105.754303),
            (42.827180, -105.821738),
            (42.837159, -105.859336),
            (42.841608, -105.928988),
            (42.835067, -105.983579),
            (42.840577, -106.044209),
            (42.839108, -106.196661),
            (42.856070, -106.278743),
            (42.857256, -106.325181),
            (42.850082, -106.325100),
        };

        public static RtgWaypoint[] HomeToCasperLoop()
        {
            var route = new List<RtgWaypoint>(HomeToCasperOutbound.Length * 2);

            foreach ((double lat, double lng) in HomeToCasperOutbound)
                route.Add(new RtgWaypoint { lat = lat, lng = lng });

            for (int i = HomeToCasperOutbound.Length - 2; i >= 0; i--)
            {
                (double lat, double lng) = HomeToCasperOutbound[i];
                route.Add(new RtgWaypoint { lat = lat, lng = lng });
            }

            return route.ToArray();
        }

        /// <summary>Legacy alias — Douglas downtown loop replaced by Casper route.</summary>
        public static RtgWaypoint[] HomeToDowntownLoop() => HomeToCasperLoop();
    }
}
