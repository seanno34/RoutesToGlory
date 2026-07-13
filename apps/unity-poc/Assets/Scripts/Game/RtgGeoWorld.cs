using CesiumForUnity;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Reuses a single CesiumGlobeAnchor probe for lat/lng → world-space conversion.
    /// Avoids spawning/destroying a GameObject per vertex when drawing route networks.
    /// </summary>
    public sealed class RtgGeoWorld
    {
        private readonly Transform _geoRoot;
        private readonly RtgTerrainHeight _terrainHeight;
        private readonly double _fallbackHeightMeters;
        private GameObject _probe;
        private CesiumGlobeAnchor _anchor;

        public RtgGeoWorld(Transform geoRoot, RtgTerrainHeight terrainHeight, double fallbackHeightMeters)
        {
            _geoRoot = geoRoot;
            _terrainHeight = terrainHeight;
            _fallbackHeightMeters = fallbackHeightMeters;
        }

        public Vector3 WorldFromLatLng(double lng, double lat, float heightAboveTerrainM)
        {
            EnsureProbe();

            double heightM = _terrainHeight != null
                ? _terrainHeight.GetGroundHeight(lat, lng) + heightAboveTerrainM
                : _fallbackHeightMeters + heightAboveTerrainM;

            _anchor.SetPositionLongitudeLatitudeHeight(lng, lat, heightM);
            return _probe.transform.position;
        }

        /// <summary>
        /// Ellipsoid height for georeferenced lines — avoids using the player's
        /// single cached terrain sample for every vertex on distant routes.
        /// </summary>
        public Vector3 WorldFromLatLngEllipsoid(double lng, double lat, float heightAboveEllipsoidM)
        {
            EnsureProbe();
            _anchor.SetPositionLongitudeLatitudeHeight(lng, lat, _fallbackHeightMeters + heightAboveEllipsoidM);
            return _probe.transform.position;
        }

        public void FillWorldPositions(
            RtgPathPoint[] path,
            float heightAboveTerrainM,
            Vector3[] buffer)
        {
            if (path == null || buffer == null) return;
            int count = Mathf.Min(path.Length, buffer.Length);
            for (int i = 0; i < count; i++)
                buffer[i] = WorldFromLatLng(path[i].lng, path[i].lat, heightAboveTerrainM);
        }

        public void Dispose()
        {
            if (_probe == null) return;
            if (Application.isPlaying) Object.Destroy(_probe);
            else Object.DestroyImmediate(_probe);
            _probe = null;
            _anchor = null;
        }

        private void EnsureProbe()
        {
            if (_probe != null) return;

            _probe = new GameObject("_rtg_geo_probe");
            _probe.hideFlags = HideFlags.HideAndDontSave;
            _probe.transform.SetParent(_geoRoot, false);
            _anchor = _probe.AddComponent<CesiumGlobeAnchor>();
        }
    }
}
