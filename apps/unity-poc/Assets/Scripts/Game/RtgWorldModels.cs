using System;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Plain data models mirroring the JSON returned by @empire/api's
    /// GET /api/worlds/:worldId/map endpoint (see apps/api/src/db/world-repo.ts).
    /// Field names are snake_case on purpose so Unity's JsonUtility maps the
    /// MySQL column names directly. Booleans arrive as 0/1, so is_goodie_hut is
    /// an int.
    /// </summary>
    [Serializable]
    public class RtgWorldMap
    {
        public RtgSettlement[] settlements;
        public RtgRoute[] routes;
        public RtgResourceNode[] resources;
    }

    /// <summary>Persisted GPS path. Settlement anchors are set only when the leg touches a node geofence.</summary>
    [Serializable]
    public class RtgRoute
    {
        public string id;
        public string empire_id;
        public string from_settlement_id;
        public string to_settlement_id;
        public double distance_m;
        public string status;
        public string empire_color;
        public RtgPathPoint[] path_json;
    }

    [Serializable]
    public class RtgPathPoint
    {
        public double lat;
        public double lng;
    }

    /// <summary>An "Echo Site" — a settlement/town/city grown from routes.</summary>
    [Serializable]
    public class RtgSettlement
    {
        public string id;
        public string name;
        public string planet_display_name;
        public string tier;       // goodie_hut | settlement | town | city | super_city
        public string alignment;  // friendly | neutral | hostile | alien_enclave
        public int is_goodie_hut; // 0/1
        public double lat;
        public double lng;
        public int geofence_radius_m;
        public string owner_empire_id;
    }

    /// <summary>A harvestable resource node on the map (map_resource_nodes).</summary>
    [Serializable]
    public class RtgResourceNode
    {
        public string id;
        public string resource_id; // xenite | solari_dust | ... (see alien-resources.ts)
        public string richness;    // sparse | moderate | rich
        public int yield_per_day;
        public double lat;
        public double lng;
        public string owner_empire_id;
    }
}
