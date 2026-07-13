using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>Vaporizable survey-world prop registered by <see cref="RtgTerrainScatter"/>.</summary>
    public class RtgScatterObstacle : MonoBehaviour
    {
        public enum Kind { Tree, Rock, Brush }

        public int obstacleId;
        public string tileId;
        public Kind kind;
        public double lat;
        public double lng;
        public float radiusMeters;
        public float heightMeters;

        public void Configure(
            int id,
            string tile,
            Kind obstacleKind,
            double latitude,
            double longitude,
            float radius,
            float height)
        {
            obstacleId = id;
            tileId = tile;
            kind = obstacleKind;
            lat = latitude;
            lng = longitude;
            radiusMeters = radius;
            heightMeters = height;
        }
    }
}
