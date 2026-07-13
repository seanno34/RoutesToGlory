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
        private readonly bool _loopRoute;
        private float _speedMetersPerSecond;

        private int _segment;
        private double _distanceIntoSegment;
        private bool _hasFix;
        private double _lat, _lng;

        public RtgSimulatedLocationProvider(RtgWaypoint[] route, float speedMetersPerSecond, bool loopRoute = true)
        {
            _route = route;
            _loopRoute = loopRoute;
            _speedMetersPerSecond = Mathf.Max(0f, speedMetersPerSecond);
        }

        public float SpeedMetersPerSecond
        {
            get => _speedMetersPerSecond;
            set => _speedMetersPerSecond = Mathf.Max(0f, value);
        }

        public string Status => $"Simulated · {_speedMetersPerSecond:0.#} m/s";

        public void Begin() => Restart();

        public void Restart()
        {
            if (_route != null && _route.Length > 0)
                BeginAt(_route[0].lat, _route[0].lng);
            else
            {
                _segment = 0;
                _distanceIntoSegment = 0.0;
                _hasFix = false;
            }
        }

        /// <summary>
        /// Start (or resume) walking the route from the nearest point to the given fix.
        /// Keeps the pin at its current location instead of jumping to route[0].
        /// </summary>
        public void BeginAt(double lat, double lng)
        {
            _segment = 0;
            _distanceIntoSegment = 0.0;

            if (_route == null || _route.Length == 0)
            {
                _hasFix = false;
                return;
            }

            if (_route.Length == 1)
            {
                _lat = _route[0].lat;
                _lng = _route[0].lng;
                _hasFix = true;
                return;
            }

            if (!TryProjectOntoRoute(lat, lng, out int seg, out double intoSeg, out double projLat, out double projLng))
            {
                _lat = _route[0].lat;
                _lng = _route[0].lng;
                _segment = 0;
                _distanceIntoSegment = 0.0;
                _hasFix = true;
                return;
            }

            _segment = seg;
            _distanceIntoSegment = intoSeg;
            _lat = projLat;
            _lng = projLng;
            _hasFix = true;
        }

        public void RestartAt(double lat, double lng) => BeginAt(lat, lng);

        public void Tick(float deltaTime)
        {
            if (_route == null || _route.Length < 2) return;

            _distanceIntoSegment += _speedMetersPerSecond * deltaTime;

            int segmentCount = _loopRoute ? _route.Length : _route.Length - 1;
            if (segmentCount <= 0) return;

            // Walk forward across as many segments as this frame's distance covers.
            for (int guard = 0; guard < segmentCount + 1; guard++)
            {
                RtgWaypoint a = _route[_segment];
                int nextIndex = _loopRoute ? (_segment + 1) % _route.Length : _segment + 1;
                if (nextIndex >= _route.Length)
                {
                    _lat = a.lat;
                    _lng = a.lng;
                    _hasFix = true;
                    return;
                }

                RtgWaypoint b = _route[nextIndex];
                double segLength = SegmentLengthMeters(a, b);

                // Skip duplicate consecutive waypoints (e.g. loop start/end both "home").
                if (segLength < 0.5)
                {
                    _distanceIntoSegment = 0.0;
                    if (_loopRoute)
                        _segment = (_segment + 1) % _route.Length;
                    else if (_segment + 1 < _route.Length - 1)
                        _segment++;
                    else
                        return;
                    continue;
                }

                if (_distanceIntoSegment < segLength)
                {
                    double t = segLength < 0.001 ? 0.0 : _distanceIntoSegment / segLength;
                    _lat = a.lat + (b.lat - a.lat) * t;
                    _lng = a.lng + (b.lng - a.lng) * t;
                    _hasFix = true;
                    return;
                }

                _distanceIntoSegment -= segLength;
                if (_loopRoute)
                    _segment = (_segment + 1) % _route.Length;
                else if (_segment + 1 < _route.Length - 1)
                    _segment++;
                else
                {
                    _lat = b.lat;
                    _lng = b.lng;
                    _hasFix = true;
                    return;
                }
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

        private bool TryProjectOntoRoute(
            double lat,
            double lng,
            out int segment,
            out double distanceIntoSegment,
            out double projLat,
            out double projLng)
        {
            segment = 0;
            distanceIntoSegment = 0.0;
            projLat = lat;
            projLng = lng;

            if (_route == null || _route.Length < 2)
                return false;

            int segmentCount = _loopRoute ? _route.Length : _route.Length - 1;
            double bestDist = double.MaxValue;
            int bestSeg = 0;
            double bestInto = 0.0;
            double bestLat = lat;
            double bestLng = lng;

            for (int i = 0; i < segmentCount; i++)
            {
                RtgWaypoint a = _route[i];
                int j = _loopRoute ? (i + 1) % _route.Length : i + 1;
                RtgWaypoint b = _route[j];
                double segLen = SegmentLengthMeters(a, b);
                if (segLen < 0.5) continue;

                double t = ProjectParameter(lat, lng, a, b);
                double pLat = a.lat + (b.lat - a.lat) * t;
                double pLng = a.lng + (b.lng - a.lng) * t;
                double dist = DistanceMeters(lat, lng, pLat, pLng);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestSeg = i;
                    bestInto = t * segLen;
                    bestLat = pLat;
                    bestLng = pLng;
                }
            }

            segment = bestSeg;
            distanceIntoSegment = bestInto;
            projLat = bestLat;
            projLng = bestLng;
            return true;
        }

        private static double ProjectParameter(double lat, double lng, RtgWaypoint a, RtgWaypoint b)
        {
            double avgLatRad = (a.lat + b.lat) * 0.5 * Mathf.Deg2Rad;
            double metersPerDegLng = MetersPerDegreeLat * Math.Cos(avgLatRad);
            double ax = a.lng * metersPerDegLng;
            double ay = a.lat * MetersPerDegreeLat;
            double bx = b.lng * metersPerDegLng;
            double by = b.lat * MetersPerDegreeLat;
            double px = lng * metersPerDegLng;
            double py = lat * MetersPerDegreeLat;
            double dx = bx - ax;
            double dy = by - ay;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-8) return 0.0;
            double t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
            return Math.Max(0.0, Math.Min(1.0, t));
        }

        private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            double avgLatRad = (lat1 + lat2) * 0.5 * Mathf.Deg2Rad;
            double metersPerDegLng = MetersPerDegreeLat * Math.Cos(avgLatRad);
            double dLat = (lat2 - lat1) * MetersPerDegreeLat;
            double dLng = (lng2 - lng1) * metersPerDegLng;
            return Math.Sqrt(dLat * dLat + dLng * dLng);
        }
    }

    /// <summary>
    /// Real device GPS via Unity's LocationService (Input.location). Smooths fixes with
    /// velocity prediction so the glider glides between 1–5 s hardware updates instead
    /// of jumping.
    /// </summary>
    public class RtgDeviceLocationProvider : IRtgLocationProvider
    {
        private const double MetersPerDegreeLat = 111320.0;
        private const double MinVelocitySampleMeters = 2.0;

        private float _smoothing = 10f;
        private float _updateDistanceMeters = 1f;
        private float _maxSnapMeters = 120f;

        private bool _started;
        private string _status = "Device GPS: not started";

        private bool _hasRawFix;
        private bool _hasDisplay;
        private double _rawLat;
        private double _rawLng;
        private double _displayLat;
        private double _displayLng;
        private double _velLatPerSec;
        private double _velLngPerSec;
        private float _lastRawFixTime;

        public string Status => _status;

        public void Configure(float smoothing, float updateDistanceMeters, float maxSnapMeters)
        {
            float newSmoothing = Mathf.Max(0.5f, smoothing);
            float newUpdateDistance = Mathf.Max(0.5f, updateDistanceMeters);
            float newMaxSnap = Mathf.Max(10f, maxSnapMeters);

            bool restartForDistance = _started
                && !Mathf.Approximately(_updateDistanceMeters, newUpdateDistance);

            _smoothing = newSmoothing;
            _updateDistanceMeters = newUpdateDistance;
            _maxSnapMeters = newMaxSnap;

            if (restartForDistance)
            {
                Input.location.Stop();
                Input.location.Start(5f, _updateDistanceMeters);
            }
        }

        public void Begin()
        {
            _hasRawFix = false;
            _hasDisplay = false;
            _velLatPerSec = 0.0;
            _velLngPerSec = 0.0;
            _lastRawFixTime = 0f;

            try
            {
                if (!Input.location.isEnabledByUser)
                {
                    _status = "Device GPS: disabled by user";
                    return;
                }

                // 5 m desired accuracy; request fixes every ~1 m while driving.
                Input.location.Start(5f, _updateDistanceMeters);
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
            if (!_started)
                return;

            if (Input.location.status != LocationServiceStatus.Running)
            {
                _status = $"Device GPS: {Input.location.status}";
                return;
            }

            PollRawFix();
            AdvanceSmoothing(deltaTime);
            _status = $"Device GPS: {Input.location.status}";
        }

        public bool TryGetLatLng(out double latitude, out double longitude)
        {
            latitude = _displayLat;
            longitude = _displayLng;
            return _hasDisplay;
        }

        public void End()
        {
            if (_started)
            {
                Input.location.Stop();
                _started = false;
            }
        }

        private void PollRawFix()
        {
            LocationInfo data = Input.location.lastData;
            double lat = data.latitude;
            double lng = data.longitude;

            if (_hasRawFix
                && Math.Abs(lat - _rawLat) < 1e-8
                && Math.Abs(lng - _rawLng) < 1e-8)
            {
                return;
            }

            float now = Time.time;
            if (_hasRawFix)
            {
                float dt = now - _lastRawFixTime;
                if (dt > 0.05f)
                {
                    double moved = DistanceMeters(_rawLat, _rawLng, lat, lng);
                    if (moved >= MinVelocitySampleMeters)
                    {
                        _velLatPerSec = (lat - _rawLat) / dt;
                        _velLngPerSec = (lng - _rawLng) / dt;
                    }
                    else
                    {
                        _velLatPerSec = 0.0;
                        _velLngPerSec = 0.0;
                    }
                }
            }

            if (_hasDisplay)
            {
                double jump = DistanceMeters(_displayLat, _displayLng, lat, lng);
                if (jump > _maxSnapMeters)
                {
                    _displayLat = lat;
                    _displayLng = lng;
                    _velLatPerSec = 0.0;
                    _velLngPerSec = 0.0;
                }
            }

            _rawLat = lat;
            _rawLng = lng;
            _lastRawFixTime = now;
            _hasRawFix = true;

            if (!_hasDisplay)
            {
                _displayLat = lat;
                _displayLng = lng;
                _hasDisplay = true;
            }
        }

        private void AdvanceSmoothing(float deltaTime)
        {
            if (!_hasDisplay || !_hasRawFix || deltaTime <= 0f)
                return;

            float elapsed = Time.time - _lastRawFixTime;
            if (elapsed > 4f)
            {
                _velLatPerSec = 0.0;
                _velLngPerSec = 0.0;
            }

            double targetLat = _rawLat + _velLatPerSec * elapsed;
            double targetLng = _rawLng + _velLngPerSec * elapsed;

            float t = 1f - Mathf.Exp(-_smoothing * deltaTime);
            _displayLat += (targetLat - _displayLat) * t;
            _displayLng += (targetLng - _displayLng) * t;
        }

        private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            double avgLatRad = (lat1 + lat2) * 0.5 * Mathf.Deg2Rad;
            double metersPerDegLng = MetersPerDegreeLat * Math.Cos(avgLatRad);
            double dLat = (lat2 - lat1) * MetersPerDegreeLat;
            double dLng = (lng2 - lng1) * metersPerDegLng;
            return Math.Sqrt(dLat * dLat + dLng * dLng);
        }
    }
}
