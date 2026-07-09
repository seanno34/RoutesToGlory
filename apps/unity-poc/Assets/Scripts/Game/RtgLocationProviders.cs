using System;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// A source of the player's real-world position. The rest of the game only ever
    /// sees latitude/longitude through this seam, so the simulated (editor) and real
    /// device-GPS providers are fully interchangeable — Step 4's whole point is that
    /// swapping to real GPS on-device requires no changes to the position plumbing.
    /// </summary>
    public interface IRtgLocationProvider
    {
        /// <summary>Called once when tracking starts (e.g. to start the GPS service).</summary>
        void Begin();

        /// <summary>Advance internal state by dt seconds (simulated providers use this).</summary>
        void Tick(float deltaTime);

        /// <summary>Latest fix, if available. Returns false until a position is known.</summary>
        bool TryGetLatLng(out double latitude, out double longitude);

        /// <summary>Called when tracking stops (e.g. to stop the GPS service).</summary>
        void End();

        /// <summary>Human-readable status for on-screen/debug display.</summary>
        string Status { get; }
    }

    [Serializable]
    public struct RtgWaypoint
    {
        public double lat;
        public double lng;
    }

    /// <summary>
    /// Simulates GPS by walking a looping route of real-world waypoints at a fixed
    /// ground speed, using an equirectangular meters-to-degrees approximation (plenty
    /// accurate over the small distances a player walks).
    /// </summary>
    public class RtgSimulatedLocationProvider : IRtgLocationProvider
    {
        private const double MetersPerDegreeLat = 111320.0;

        private readonly RtgWaypoint[] _route;
        private readonly float _speedMetersPerSecond;

        private int _segment;
        private double _distanceIntoSegment;
        private bool _hasFix;
        private double _lat, _lng;

        public RtgSimulatedLocationProvider(RtgWaypoint[] route, float speedMetersPerSecond)
        {
            _route = route;
            _speedMetersPerSecond = Mathf.Max(0f, speedMetersPerSecond);
        }

        public string Status => $"Simulated · {_speedMetersPerSecond:0.#} m/s";

        public void Begin()
        {
            _segment = 0;
            _distanceIntoSegment = 0.0;
            if (_route != null && _route.Length > 0)
            {
                _lat = _route[0].lat;
                _lng = _route[0].lng;
                _hasFix = true;
            }
        }

        public void Tick(float deltaTime)
        {
            if (_route == null || _route.Length < 2) return;

            _distanceIntoSegment += _speedMetersPerSecond * deltaTime;

            // Walk forward across as many segments as this frame's distance covers.
            for (int guard = 0; guard < _route.Length + 1; guard++)
            {
                RtgWaypoint a = _route[_segment];
                RtgWaypoint b = _route[(_segment + 1) % _route.Length];
                double segLength = SegmentLengthMeters(a, b);

                if (segLength < 0.001 || _distanceIntoSegment < segLength)
                {
                    double t = segLength < 0.001 ? 0.0 : _distanceIntoSegment / segLength;
                    _lat = a.lat + (b.lat - a.lat) * t;
                    _lng = a.lng + (b.lng - a.lng) * t;
                    _hasFix = true;
                    return;
                }

                _distanceIntoSegment -= segLength;
                _segment = (_segment + 1) % _route.Length;
            }
        }

        public bool TryGetLatLng(out double latitude, out double longitude)
        {
            latitude = _lat;
            longitude = _lng;
            return _hasFix;
        }

        public void End() { }

        private static double SegmentLengthMeters(RtgWaypoint a, RtgWaypoint b)
        {
            double avgLatRad = (a.lat + b.lat) * 0.5 * Mathf.Deg2Rad;
            double metersPerDegLng = MetersPerDegreeLat * Math.Cos(avgLatRad);
            double dLat = (b.lat - a.lat) * MetersPerDegreeLat;
            double dLng = (b.lng - a.lng) * metersPerDegLng;
            return Math.Sqrt(dLat * dLat + dLng * dLng);
        }
    }

    /// <summary>
    /// Real device GPS via Unity's LocationService (Input.location). Only produces
    /// fixes on a device where location is enabled and permission is granted; in the
    /// editor it simply reports "unavailable" so callers fall back gracefully.
    ///
    /// On-device requirements:
    ///  - iOS: set "Location Usage Description" in Player Settings.
    ///  - Android: the FINE/COARSE location permission is added automatically; request
    ///    it at runtime on Android 6+.
    /// </summary>
    public class RtgDeviceLocationProvider : IRtgLocationProvider
    {
        private bool _started;
        private string _status = "Device GPS: not started";

        public string Status => _status;

        public void Begin()
        {
            try
            {
                if (!Input.location.isEnabledByUser)
                {
                    _status = "Device GPS: disabled by user";
                    return;
                }
                Input.location.Start(5f, 5f);
                _started = true;
                _status = "Device GPS: starting…";
            }
            catch (Exception e)
            {
                _status = $"Device GPS unavailable: {e.Message}";
            }
        }

        public void Tick(float deltaTime)
        {
            if (!_started) return;
            _status = $"Device GPS: {Input.location.status}";
        }

        public bool TryGetLatLng(out double latitude, out double longitude)
        {
            latitude = 0;
            longitude = 0;
            if (!_started || Input.location.status != LocationServiceStatus.Running) return false;

            LocationInfo data = Input.location.lastData;
            latitude = data.latitude;
            longitude = data.longitude;
            return true;
        }

        public void End()
        {
            if (_started)
            {
                Input.location.Stop();
                _started = false;
            }
        }
    }
}
