using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Builds drive-to-city Auto Pilot routes at runtime via Nominatim (geocode) + OSRM (roads).
    /// City + state works (e.g. "Casper, WY"); full street addresses are more precise but optional.
    /// </summary>
    public static class RtgAutopilotRouting
    {
        private const string NominatimUrl = "https://nominatim.openstreetmap.org/search";
        private const string OsrmUrl = "https://router.project-osrm.org/route/v1/driving";
        private const string UserAgent = "RoutesToGlory-UnityPOC/1.0 (dev home testing)";

        private static readonly Dictionary<string, (double lat, double lng)> KnownCities =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["casper, wy"] = (42.8501, -106.3251),
                ["casper, wyoming"] = (42.8501, -106.3251),
                ["douglas, wy"] = (42.7597, -105.3819),
                ["denver, co"] = (39.7392, -104.9903),
                ["cheyenne, wy"] = (41.1400, -104.8202),
                ["salt lake city, ut"] = (40.7608, -111.8910),
                ["new york, ny"] = (40.7128, -74.0060),
                ["los angeles, ca"] = (34.0522, -118.2437),
                ["chicago, il"] = (41.8781, -87.6298),
                ["houston, tx"] = (29.7604, -95.3698),
                ["phoenix, az"] = (33.4484, -112.0740),
                ["seattle, wa"] = (47.6062, -122.3321),
                ["austin, tx"] = (30.2672, -97.7431),
                ["boise, id"] = (43.6150, -116.2023),
            };

        public static IEnumerator BuildDriveLoop(
            double startLat,
            double startLng,
            string destinationQuery,
            Action<RtgWaypoint[]> onDone,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(destinationQuery))
            {
                onError?.Invoke("Destination is empty.");
                onDone?.Invoke(null);
                yield break;
            }

            double destLat = 0;
            double destLng = 0;
            string destLabel = destinationQuery.Trim();
            string geocodeError = null;

            yield return GeocodeDestination(
                destinationQuery,
                (lat, lng, label) =>
                {
                    destLat = lat;
                    destLng = lng;
                    destLabel = label;
                },
                err => geocodeError = err);

            if (geocodeError != null)
            {
                onError?.Invoke(geocodeError);
                onDone?.Invoke(null);
                yield break;
            }

            List<RtgWaypoint> outbound = null;
            string routeError = null;
            yield return FetchOsrmRoute(
                startLat,
                startLng,
                destLat,
                destLng,
                pts => outbound = pts,
                err => routeError = err);

            if (routeError != null || outbound == null || outbound.Count < 2)
            {
                onError?.Invoke(routeError ?? "OSRM returned no route.");
                onDone?.Invoke(null);
                yield break;
            }

            RtgWaypoint[] loop = BuildReturnLoop(outbound);
            Debug.Log(
                $"[RTG] Auto Pilot drive loop: ({startLat:F4},{startLng:F4}) → {destLabel} " +
                $"({destLat:F4},{destLng:F4}), {loop.Length} road points.");
            onDone?.Invoke(loop);
        }

        private static IEnumerator GeocodeDestination(
            string query,
            Action<double, double, string> onOk,
            Action<string> onError)
        {
            string normalized = NormalizeCityKey(query);
            if (KnownCities.TryGetValue(normalized, out (double lat, double lng) known))
            {
                onOk?.Invoke(known.lat, known.lng, query.Trim());
                yield break;
            }

            string encoded = UnityWebRequest.EscapeURL($"{query.Trim()}, USA");
            string url = $"{NominatimUrl}?q={encoded}&format=json&limit=1&countrycodes=us";

            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("User-Agent", UserAgent);
            req.timeout = 15;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Geocode failed: {req.error}");
                yield break;
            }

            string json = req.downloadHandler.text;
            if (!TryParseNominatim(json, out double lat, out double lng, out string label))
            {
                onError?.Invoke($"Could not resolve \"{query}\". Try \"City, ST\" (e.g. Casper, WY).");
                yield break;
            }

            onOk?.Invoke(lat, lng, label);
        }

        private static IEnumerator FetchOsrmRoute(
            double startLat,
            double startLng,
            double destLat,
            double destLng,
            Action<List<RtgWaypoint>> onOk,
            Action<string> onError)
        {
            string coords = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1};{2},{3}",
                startLng,
                startLat,
                destLng,
                destLat);
            string url =
                $"{OsrmUrl}/{coords}?overview=simplified&geometries=geojson&steps=false";

            using var req = UnityWebRequest.Get(url);
            req.timeout = 20;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"OSRM failed: {req.error}");
                yield break;
            }

            if (!TryParseOsrmCoordinates(req.downloadHandler.text, out List<RtgWaypoint> points))
            {
                onError?.Invoke("OSRM response could not be parsed.");
                yield break;
            }

            if (points.Count < 2)
            {
                onError?.Invoke("OSRM returned too few points.");
                yield break;
            }

            onOk?.Invoke(Decimate(points, 120));
        }

        private static RtgWaypoint[] BuildReturnLoop(List<RtgWaypoint> outbound)
        {
            var loop = new List<RtgWaypoint>(outbound.Count * 2);
            loop.AddRange(outbound);

            for (int i = outbound.Count - 2; i >= 0; i--)
                loop.Add(outbound[i]);

            return loop.ToArray();
        }

        private static List<RtgWaypoint> Decimate(List<RtgWaypoint> points, int maxPoints)
        {
            if (points.Count <= maxPoints)
                return points;

            var result = new List<RtgWaypoint>(maxPoints) { points[0] };
            int slots = maxPoints - 2;
            for (int i = 1; i <= slots; i++)
            {
                int idx = Mathf.RoundToInt(i * (points.Count - 1) / (float)(slots + 1));
                idx = Mathf.Clamp(idx, 1, points.Count - 2);
                result.Add(points[idx]);
            }

            result.Add(points[points.Count - 1]);
            return result;
        }

        private static string NormalizeCityKey(string query)
        {
            return query.Trim().Replace("  ", " ");
        }

        private static bool TryParseNominatim(string json, out double lat, out double lng, out string label)
        {
            lat = lng = 0;
            label = null;
            if (string.IsNullOrEmpty(json) || json == "[]")
                return false;

            try
            {
                string wrapped = "{\"items\":" + json + "}";
                NominatimWrapper parsed = JsonUtility.FromJson<NominatimWrapper>(wrapped);
                if (parsed?.items == null || parsed.items.Length == 0)
                    return false;

                NominatimHit hit = parsed.items[0];
                if (!double.TryParse(hit.lat, NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
                    || !double.TryParse(hit.lon, NumberStyles.Float, CultureInfo.InvariantCulture, out lng))
                    return false;

                label = string.IsNullOrEmpty(hit.display_name) ? $"{lat},{lng}" : hit.display_name;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseOsrmCoordinates(string json, out List<RtgWaypoint> points)
        {
            points = new List<RtgWaypoint>();
            if (string.IsNullOrEmpty(json))
                return false;

            const string marker = "\"coordinates\":[[";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return false;

            start += marker.Length - 1;
            int end = json.IndexOf("]]", start, StringComparison.Ordinal);
            if (end < 0)
                return false;

            string body = json.Substring(start, end - start + 1);
            var pairs = new List<(double lng, double lat)>();
            int i = 0;
            while (i < body.Length)
            {
                int open = body.IndexOf('[', i);
                if (open < 0) break;
                int close = body.IndexOf(']', open);
                if (close < 0) break;

                string pair = body.Substring(open + 1, close - open - 1);
                string[] parts = pair.Split(',');
                if (parts.Length >= 2
                    && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lng)
                    && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                {
                    pairs.Add((lng, lat));
                }

                i = close + 1;
            }

            foreach ((double lng, double lat) in pairs)
                points.Add(new RtgWaypoint { lat = lat, lng = lng });

            return points.Count >= 2;
        }

        [Serializable]
        private class NominatimWrapper
        {
            public NominatimHit[] items;
        }

        [Serializable]
        private class NominatimHit
        {
            public string lat;
            public string lon;
            public string display_name;
        }
    }
}
