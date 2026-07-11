using System.Collections.Generic;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Marks map beacons as connected (one route link per object) from the latest
    /// world map snapshot so tap-to-connect can no-op on repeats.
    /// </summary>
    public static class RtgMapConnections
    {
        public static void Apply(RtgWorldMap map, string playerEmpireId)
        {
            if (map == null || string.IsNullOrWhiteSpace(playerEmpireId)) return;

            var connectedSettlements = new HashSet<string>();
            var connectedResources = new HashSet<string>();

            if (map.routes != null)
            {
                foreach (RtgRoute route in map.routes)
                {
                    if (route == null || route.empire_id != playerEmpireId) continue;
                    if (route.status != null && route.status != "active") continue;
                    if (!string.IsNullOrEmpty(route.to_settlement_id))
                        connectedSettlements.Add(route.to_settlement_id);
                }
            }

            if (map.resources != null)
            {
                foreach (RtgResourceNode node in map.resources)
                {
                    if (node != null && node.owner_empire_id == playerEmpireId)
                        connectedResources.Add(node.id);
                }
            }

            RtgMapMarkerRegistry.Refresh();

            foreach (RtgMapMarker marker in RtgMapMarkerRegistry.All)
            {
                if (marker == null) continue;
                bool connected = marker.kind == RtgMapMarker.Kind.Settlement
                    ? connectedSettlements.Contains(marker.targetId)
                    : connectedResources.Contains(marker.targetId);
                marker.SetConnected(connected);
            }
        }
    }
}
