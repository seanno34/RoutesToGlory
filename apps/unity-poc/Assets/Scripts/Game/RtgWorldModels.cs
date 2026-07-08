using System;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Plain data models mirroring the JSON returned by @empire/api's
    /// GET /api/worlds/:worldId/map endpoint (see apps/api/src/db/world-repo.ts).
    /// Field names are snake_case on purpose so Unity's JsonUtility maps the
    /// MySQL column names directly. Booleans arrive as 0/1, so is_goodie_hut is
    /// an int. Unknown fields (e.g. routes) are ignored by JsonUtility.
    /// </summary>
    [Serializable]
    public class RtgWorldMap
    {
        public RtgSettlement[] settlements;
        public RtgResourceNode[] resources;
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
    }
}
