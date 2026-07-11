using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Drives a georeferenced "You" marker from a location provider (simulated route
    /// in the editor, or real device GPS), and optionally makes the fly camera follow
    /// it. This is the Step 4 "real movement to position" plumbing: real-world
    /// movement → lat/lng → CesiumGlobeAnchor → world position.
    /// </summary>
    public class RtgPlayerLocation : MonoBehaviour
    {
        public enum LocationSource { SimulatedRoute, DeviceGps }

        // Map = overhead (Google Maps). LowAngle = ~20° behind the pin.
        public enum CameraPerspective { Map, LowAngle }

        // FixedLoop = walk the small hand-authored rectangle (or the `route` field).
        // TourNearbySites = auto-build a loop that threads past every Echo Site /
        // resource node near the play area so you can sight-see the whole map.
        // HomeToCasper = simulated drive from home (10 Tiffany Ln) to Casper, WY
        // and back — for terrain / fog / tap-claim testing.
        public enum RouteMode { FixedLoop, TourNearbySites, HomeToCasper }

        public enum PlayerMarkerStyle { SpaceshipSprite, GoldPin }

        [Header("Player ship")]
        [Tooltip("SpaceshipSprite uses concept art from Resources/RTG_PlayerShip/. GoldPin is the legacy sphere.")]
        public PlayerMarkerStyle markerStyle = PlayerMarkerStyle.SpaceshipSprite;

        [Tooltip("Optional override. If unset, loads Resources/RTG_PlayerShip/glider_01.")]
        public Texture2D shipTexture;

        [Tooltip("Wingspan of the top-down ship sprite in meters.")]
        public float shipSizeMeters = 24f;

        [Tooltip("Tune if the ship nose points backward (180 = flip).")]
        public float shipHeadingOffsetDegrees = 0f;

        [Header("Source")]
        public LocationSource source = LocationSource.SimulatedRoute;

        [Tooltip("FixedLoop = small rectangle. TourNearbySites = auto loop past every nearby Echo Site + resource node.")]
        public RouteMode routeMode = RouteMode.TourNearbySites;

        [Tooltip("Walking speed for the fixed loop (m/s). ~1.4 = real walk; higher is easier to watch.")]
        public float simulatedSpeed = 15f;

        [Tooltip("Travel speed for the 'tour nearby sites' loop (m/s). Higher = quicker sight-seeing across the whole area.")]
        public float tourSpeed = 90f;

        [Header("Throttle (runtime UI)")]
        [Tooltip("Lowest multiplier on the in-game throttle lever.")]
        [Min(0.01f)]
        public float minSpeedThrottle = 0.25f;

        [Tooltip("Highest multiplier on the in-game throttle lever.")]
        [Min(0.02f)]
        public float maxSpeedThrottle = 4f;

        [Tooltip("Starting throttle multiplier. Clamped between min and max.")]
        public float speedThrottle = 1f;

        [Tooltip("Looping real-world route for FixedLoop mode. Defaults to a loop around Douglas, WY if empty.")]
        public RtgWaypoint[] route;

        [Header("Tour nearby sites")]
        [Tooltip("Only tour sites within this many meters of the tour center (keeps far-off metro Echo Sites out).")]
        public float tourRadiusMeters = 15000f;

        [Tooltip("Center the tour filters around (defaults to the Douglas, WY origin).")]
        public double tourCenterLatitude = 42.7597;

        public double tourCenterLongitude = -105.3819;

        [Tooltip("Safety cap on how many stops the tour visits.")]
        public int maxTourStops = 80;

        [Tooltip("If off, the pin follows a fixed road corridor loop instead of driving to every beacon — required for tap-to-connect testing.")]
        public bool tourVisitsBeacons = false;

        [Tooltip("Seconds to wait for Echo Sites to load before falling back to the fixed loop.")]
        public float tourLoadTimeoutSeconds = 12f;

        [Header("Home ↔ Casper test drive")]
        [Tooltip("Simulated speed for HomeToCasper mode (mph). 80 ≈ quick terrain test.")]
        public float homeTestSpeedMph = 80f;

        [Header("Placement")]
        [Tooltip("Approx. ground height (m above ellipsoid) near Douglas, WY.")]
        public double groundHeightMeters = 1476.0;

        [Tooltip("Meters the player marker's center sits above the ground.")]
        public float markerHeight = 15f;

        [Header("Light Road")]
        [Tooltip("Draw a glowing Light Road trail behind the player as it moves.")]
        public bool drawLightRoad = true;

        [Tooltip("Road width in meters.")]
        public float roadWidth = 8f;

        [Tooltip("Meters above the ground the road ribbon sits.")]
        public float roadHeightMeters = 3f;

        [Tooltip("Record a road point after the player moves this many meters.")]
        public float roadPointSpacing = 12f;

        [Tooltip("Light Road color (bright energy ribbon).")]
        public Color roadColor = new Color(0.45f, 0.95f, 1.00f);

        [Header("Route session")]
        [Tooltip("Stream movement to the route-session API so a Light Road persists as a real route (needs live world via '6b').")]
        public bool recordRouteSessions = true;

        [Header("Camera follow")]
        [Tooltip("If enabled, the camera stays overhead and auto-tracks the player (Google-Maps style). Untick for free-fly.")]
        public bool followWithCamera = true;

        [Tooltip("Meters the camera sits above the player.")]
        public float followHeightMeters = 300f;

        [Tooltip("Meters the camera trails south of the player (0 = straight down; larger = more tilted/overhead-behind).")]
        public float followDistanceMeters = 120f;

        [Tooltip("Follow smoothing (0 = instant/locked; higher = smoother lag). ~5 feels like Google Maps.")]
        public float followSmoothing = 6f;

        [Header("Camera perspectives")]
        [Tooltip("Map = overhead. LowAngle = ~20° behind the pin (route-following view). Toggle in Play with the Route View / Map View button.")]
        public CameraPerspective perspective = CameraPerspective.Map;

        [Tooltip("Ground distance (m) the low-angle camera trails behind the player.")]
        public float perspectiveTrailMeters = 180f;

        [Tooltip("Elevation angle (degrees above the horizon) for the low-angle view.")]
        public float perspectiveElevationDegrees = 20f;

        [Tooltip("How quickly the camera swings behind the player's travel direction. Higher = snappier.")]
        public float headingSmoothing = 10f;

        [Tooltip("Ignore movement smaller than this (m) when updating travel heading — reduces GPS jitter.")]
        public float minHeadingMoveMeters = 3f;

        [Header("Zoom (scroll wheel while following)")]
        [Tooltip("How much each scroll step zooms. Raise for faster zoom.")]
        public float zoomSensitivity = 0.12f;

        [Tooltip("Closest zoom (multiplier on follow height/distance).")]
        public float minZoom = 0.15f;

        [Tooltip("Farthest zoom (multiplier on follow height/distance).")]
        public float maxZoom = 12f;

        [Tooltip("Flip if the scroll wheel zooms the wrong way (e.g. natural scrolling).")]
        public bool invertZoom = false;

        [Tooltip("How quickly pinch/button zoom catches up (higher = snappier).")]
        public float zoomSmoothing = 10f;

        [Tooltip("Scale pan speed on phones/tablets.")]
        [Range(0.2f, 1f)]
        public float mobilePanScale = 0.55f;

        private float _zoom = 1f;
        private float _zoomTarget = 1f;
        private CesiumGeoreference _georeference;

        [Header("Pan (drag the map)")]
        [Tooltip("Drag pan speed. The map moves under your finger/cursor; higher = faster.")]
        public float panSpeed = 2f;

        private readonly List<Rect> _gameUiRects = new();

        // When the user drags, the camera's focus point stops tracking the player and a
        // "Center" button appears; tapping it snaps the focus back to the pin.
        private bool _panned;
        private Vector3 _focus;        // smoothed point the camera hovers over
        private Vector3 _focusTarget;  // where the focus wants to be (player, or panned point)
        private Vector2 _lastPointer;
        private bool _wasPointerDown;

        private IRtgLocationProvider _provider;
        private RtgWaypoint[] _cachedSimulatedWaypoints;
        private Coroutine _tourCoroutine;
        private Transform _marker;
        private CesiumGlobeAnchor _markerAnchor;
        private Material _markerMaterial;
        private RtgLightRoad _lightRoad;
        private bool _roadStarted;
        private RtgRouteSession _routeSession;
        private RtgCesiumCreditsToggle _creditsToggle;
        private RtgTerrainHeight _terrainHeight;
        private RtgPlayerShipVisual _shipVisual;

        private Camera _camera;
        private CesiumCameraController _cameraController;
        private CesiumOriginShift _cameraOriginShift;
        private bool _followActive;

        // Travel heading for chase-cam: camera sits behind the direction of movement
        // (+X east, +Z north). Radians; 0 = heading north.
        private float _travelHeadingRad;
        private bool _hasHeadingSample;
        private Vector3 _lastHeadingSamplePos;

        private void Start()
        {
            ClampSpeedThrottle();
            EnsureMarker();
            RefreshMarkerVisual();
            EnsureTerrainHeight();
            EnsureLightRoad();
            EnsureRouteSession();
            EnsureTapToConnect();
            EnsureCesiumCreditsToggle();
            CacheCamera();

#if !UNITY_EDITOR
            if (Application.isMobilePlatform)
            {
                _zoom = Mathf.Clamp(4f, minZoom, maxZoom);
                _zoomTarget = _zoom;
            }
#endif

            // The tour route depends on the Echo Sites, which may still be loading
            // (async in Play mode for live data), so build it in a coroutine that
            // waits for them. Everything else starts a provider immediately.
            BeginLocationProvider();
        }

        private void OnDisable()
        {
            if (_tourCoroutine != null)
            {
                StopCoroutine(_tourCoroutine);
                _tourCoroutine = null;
            }
            _provider?.End();
        }

        private void Update()
        {
            if (_provider == null) return;

            _provider.Tick(Time.deltaTime);
            if (_provider.TryGetLatLng(out double lat, out double lng))
            {
                double heightM = _terrainHeight != null
                    ? _terrainHeight.GetPlacementHeight(lat, lng)
                    : groundHeightMeters + markerHeight;

                _markerAnchor.SetPositionLongitudeLatitudeHeight(lng, lat, heightM);

                // First real fix — begin tracing the Light Road (avoids a stray
                // segment from wherever the marker sat before we had a position).
                if (_lightRoad != null && !_roadStarted)
                {
                    _lightRoad.StartRecording();
                    _roadStarted = true;
                }

                // Feed the position to the route session (it only streams while a
                // route is actively being recorded via the Begin Route button).
                if (_routeSession != null) _routeSession.NotifyPosition(lat, lng);
            }
        }

        private void LateUpdate()
        {
            if (_marker != null)
            {
                UpdateTravelHeading();
                UpdateShipHeading();
            }

            UpdateCameraFollow();
        }

        // ------------------------------------------------------------------ //
        // Editor preview
        // ------------------------------------------------------------------ //

        /// <summary>Editor helper: create the marker and place it at the route start.</summary>
        public void EditorPlaceAtStart()
        {
            EnsureMarker();
            RefreshMarkerVisual();

            // Tour mode's full route is built at Play time from the loaded sites, so in
            // the editor just drop the pin at the tour center for a sensible preview.
            if (routeMode == RouteMode.TourNearbySites)
            {
                _markerAnchor.SetPositionLongitudeLatitudeHeight(
                    tourCenterLongitude, tourCenterLatitude, groundHeightMeters + markerHeight);
                return;
            }

            RtgWaypoint[] r = ResolveRoute();
            if (r.Length > 0)
            {
                _markerAnchor.SetPositionLongitudeLatitudeHeight(
                    r[0].lng, r[0].lat, groundHeightMeters + markerHeight);
            }
        }

        // ------------------------------------------------------------------ //
        // Provider / route
        // ------------------------------------------------------------------ //

        private IRtgLocationProvider CreateProvider()
        {
            if (source == LocationSource.DeviceGps)
                return new RtgDeviceLocationProvider();

            if (routeMode == RouteMode.HomeToCasper)
                return new RtgSimulatedLocationProvider(HomeToCasperRoute(), EffectiveSimulatedSpeed());

            if (routeMode == RouteMode.TourNearbySites
                && _cachedSimulatedWaypoints != null
                && _cachedSimulatedWaypoints.Length >= 2)
            {
                return new RtgSimulatedLocationProvider(_cachedSimulatedWaypoints, EffectiveSimulatedSpeed());
            }

            return new RtgSimulatedLocationProvider(ResolveRoute(), EffectiveSimulatedSpeed());
        }

        private float EffectiveSimulatedSpeed()
        {
            return MphToMps(EffectiveSimulatedSpeedMph());
        }

        private float BaseSimulatedSpeedMph()
        {
            return routeMode switch
            {
                RouteMode.HomeToCasper => homeTestSpeedMph,
                RouteMode.TourNearbySites => MpsToMph(tourSpeed),
                _ => MpsToMph(simulatedSpeed),
            };
        }

        private void OnValidate()
        {
            if (maxSpeedThrottle < minSpeedThrottle + 0.01f)
                maxSpeedThrottle = minSpeedThrottle + 0.01f;
            speedThrottle = Mathf.Clamp(speedThrottle, minSpeedThrottle, maxSpeedThrottle);
        }

        private void ClampSpeedThrottle()
        {
            float min = Mathf.Max(0.01f, minSpeedThrottle);
            float max = Mathf.Max(min + 0.01f, maxSpeedThrottle);
            minSpeedThrottle = min;
            maxSpeedThrottle = max;
            speedThrottle = Mathf.Clamp(speedThrottle, min, max);
        }

        private float EffectiveSimulatedSpeedMph()
        {
            return BaseSimulatedSpeedMph() * speedThrottle;
        }

        private static float MpsToMph(float mps) => mps * 3600f / 1609.344f;

        private void ApplyThrottleToProvider()
        {
            if (_provider is RtgSimulatedLocationProvider sim)
                sim.SpeedMetersPerSecond = EffectiveSimulatedSpeed();
        }

        private void BeginLocationProvider()
        {
            if (source == LocationSource.SimulatedRoute && routeMode == RouteMode.TourNearbySites
                && (_cachedSimulatedWaypoints == null || _cachedSimulatedWaypoints.Length < 2))
            {
                if (_tourCoroutine != null)
                    StopCoroutine(_tourCoroutine);
                _tourCoroutine = StartCoroutine(BeginTourWhenSitesReady());
                return;
            }

            _provider = CreateProvider();
            _provider.Begin();

            if (source == LocationSource.SimulatedRoute && routeMode == RouteMode.HomeToCasper)
            {
                Debug.Log(
                    $"[RTG] Home ↔ Casper test drive at {FormatSpeedLabel()} " +
                    $"({EffectiveSimulatedSpeed():0.#} m/s), {HomeToCasperRoute().Length} OSRM road points.");
            }
        }

        private void ToggleLocationSource()
        {
            LocationSource next = source == LocationSource.DeviceGps
                ? LocationSource.SimulatedRoute
                : LocationSource.DeviceGps;
            SetLocationSource(next);
        }

        private void SetLocationSource(LocationSource newSource)
        {
            if (source == newSource && _provider != null) return;

            if (_tourCoroutine != null)
            {
                StopCoroutine(_tourCoroutine);
                _tourCoroutine = null;
            }

            _provider?.End();
            _provider = null;
            source = newSource;

            if (_lightRoad != null)
            {
                _lightRoad.ClearRoad();
                _roadStarted = false;
            }

            _hasHeadingSample = false;
            _panned = false;

            BeginLocationProvider();
            Debug.Log($"[RTG] Location source → {(newSource == LocationSource.DeviceGps ? "GPS" : "Plan Route")}.");
        }

        private RtgWaypoint[] ResolveRoute()
        {
            if (route != null && route.Length >= 2) return route;
            return DefaultRoute();
        }

        // A ~450 m loop around the Douglas, WY origin, threading past the sample sites.
        private static RtgWaypoint[] DefaultRoute()
        {
            return new[]
            {
                new RtgWaypoint { lat = 42.7597, lng = -105.3819 },
                new RtgWaypoint { lat = 42.7609, lng = -105.3819 },
                new RtgWaypoint { lat = 42.7609, lng = -105.3800 },
                new RtgWaypoint { lat = 42.7590, lng = -105.3800 },
                new RtgWaypoint { lat = 42.7590, lng = -105.3819 },
            };
        }

        /// <summary>
        /// ~3 km rectangular corridor through Douglas. The pin follows this road instead
        /// of visiting every beacon, so scattered markers sit beside the route for
        /// tap-to-connect testing.
        /// </summary>
        private static RtgWaypoint[] CorridorTourLoop()
        {
            const double cLat = 42.7597;
            const double cLng = -105.3819;
            const double dLat = 0.012;  // ~1.3 km north/south
            const double dLng = 0.016;  // ~1.3 km east/west at this latitude

            return new[]
            {
                new RtgWaypoint { lat = cLat,       lng = cLng },
                new RtgWaypoint { lat = cLat + dLat, lng = cLng },
                new RtgWaypoint { lat = cLat + dLat, lng = cLng + dLng },
                new RtgWaypoint { lat = cLat,       lng = cLng + dLng },
                new RtgWaypoint { lat = cLat - dLat, lng = cLng + dLng },
                new RtgWaypoint { lat = cLat - dLat, lng = cLng },
                new RtgWaypoint { lat = cLat,       lng = cLng },
            };
        }

        /// <summary>Home (10 Tiffany Ln) → Casper, WY → home via OSRM road geometry.</summary>
        public static RtgWaypoint[] HomeToCasperRoute() => RtgRoadRoutes.HomeToCasperLoop();

        private static float MphToMps(float mph) => mph * 1609.344f / 3600f;

        // ------------------------------------------------------------------ //
        // Tour of nearby sites
        // ------------------------------------------------------------------ //

        private IEnumerator BeginTourWhenSitesReady()
        {
            RtgEchoSiteLoader loader =
#if UNITY_2023_1_OR_NEWER
                UnityEngine.Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
                UnityEngine.Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif

            // Wait for the Echo Sites to finish loading (live data is fetched async).
            float waited = 0f;
            while ((loader == null || loader.LastMap == null) && waited < tourLoadTimeoutSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            RtgWaypoint[] tour = null;
            if (loader != null && loader.LastMap != null)
            {
                tour = tourVisitsBeacons
                    ? BuildTourRoute(loader.LastMap)
                    : CorridorTourLoop();
            }

            if (tour == null || tour.Length < 2)
            {
                Debug.LogWarning(
                    "[RTG] Tour route unavailable — falling back to the fixed loop. " +
                    "Load Echo Sites first, or widen Tour Radius.");
                tour = ResolveRoute();
            }
            else
            {
                Debug.Log(tourVisitsBeacons
                    ? $"[RTG] Touring {tour.Length - 1} nearby site(s) around the play area."
                    : $"[RTG] Following corridor loop ({tour.Length} waypoints) — beacons are offset for tap testing.");
            }

            _cachedSimulatedWaypoints = tour;
            _provider = new RtgSimulatedLocationProvider(tour, EffectiveSimulatedSpeed());
            _provider.Begin();
            _tourCoroutine = null;
        }

        // Collect every settlement + resource within tourRadius of the center, order
        // them nearest-neighbour starting from the center for a smooth non-crossing
        // path, and prepend the center so the loop opens and closes there.
        private RtgWaypoint[] BuildTourRoute(RtgWorldMap map)
        {
            var candidates = new List<RtgWaypoint>();

            void Consider(double lat, double lng)
            {
                if (DistanceMeters(tourCenterLatitude, tourCenterLongitude, lat, lng) <= tourRadiusMeters)
                    candidates.Add(new RtgWaypoint { lat = lat, lng = lng });
            }

            if (map.settlements != null)
                foreach (RtgSettlement s in map.settlements) Consider(s.lat, s.lng);
            if (map.resources != null)
                foreach (RtgResourceNode r in map.resources) Consider(r.lat, r.lng);

            if (candidates.Count == 0) return null;

            // Nearest-neighbour ordering from the center outward.
            var ordered = new List<RtgWaypoint>(candidates.Count);
            double curLat = tourCenterLatitude, curLng = tourCenterLongitude;
            while (candidates.Count > 0 && ordered.Count < maxTourStops)
            {
                int best = 0;
                double bestDist = double.MaxValue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    double d = DistanceMeters(curLat, curLng, candidates[i].lat, candidates[i].lng);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                RtgWaypoint next = candidates[best];
                candidates.RemoveAt(best);
                ordered.Add(next);
                curLat = next.lat;
                curLng = next.lng;
            }

            // Start (and, via the provider's looping, end) at the tour center.
            var route = new List<RtgWaypoint>(ordered.Count + 1)
            {
                new RtgWaypoint { lat = tourCenterLatitude, lng = tourCenterLongitude },
            };
            route.AddRange(ordered);
            return route.ToArray();
        }

        private const double MetersPerDegreeLat = 111320.0;

        private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            double avgLatRad = (lat1 + lat2) * 0.5 * Mathf.Deg2Rad;
            double metersPerDegLng = MetersPerDegreeLat * System.Math.Cos(avgLatRad);
            double dLat = (lat2 - lat1) * MetersPerDegreeLat;
            double dLng = (lng2 - lng1) * metersPerDegLng;
            return System.Math.Sqrt(dLat * dLat + dLng * dLng);
        }

        // ------------------------------------------------------------------ //
        // Marker
        // ------------------------------------------------------------------ //

        private void EnsureMarker()
        {
            if (_marker != null)
            {
                if (_markerAnchor == null)
                    _markerAnchor = _marker.GetComponent<CesiumGlobeAnchor>();
                return;
            }

            Transform existing = transform.Find("Player Marker");
            GameObject root = existing != null ? existing.gameObject : null;
            if (root == null)
            {
                root = new GameObject("Player Marker");
                root.transform.SetParent(transform, false);
            }
            _marker = root.transform;

            _markerAnchor = root.GetComponent<CesiumGlobeAnchor>();
            if (_markerAnchor == null) _markerAnchor = root.AddComponent<CesiumGlobeAnchor>();

            Transform staleLabel = root.transform.Find("Label");
            if (staleLabel != null) DestroyImmediateSafe(staleLabel.gameObject);

            if (root.transform.Find("Beacon") == null && root.transform.Find("Ship") == null)
                BuildMarkerVisual(root.transform);
        }

        /// <summary>Rebuild pin/ship from current marker style and texture.</summary>
        public void RefreshMarkerVisual()
        {
            if (_marker == null) return;
            SyncMarkerVisual(_marker);
        }

        private void SyncMarkerVisual(Transform root)
        {
            Transform beacon = root.Find("Beacon");
            Transform ship = root.Find("Ship");

            if (markerStyle == PlayerMarkerStyle.SpaceshipSprite)
            {
                if (beacon != null) DestroyImmediateSafe(beacon.gameObject);
                BuildShipVisual(root);
                return;
            }

            if (ship != null) DestroyImmediateSafe(ship.gameObject);
            _shipVisual = null;
            if (beacon != null) DestroyImmediateSafe(beacon.gameObject);
            BuildGoldPinVisual(root);
        }

        private void EnsureLightRoad()
        {
            if (!drawLightRoad || !Application.isPlaying || _marker == null) return;
            if (_lightRoad != null) return;

            var go = new GameObject("Light Road");
            go.SetActive(false);
            go.transform.SetParent(transform, false);

            RtgLightRoad road = go.AddComponent<RtgLightRoad>();
            road.target = _marker;
            road.widthMeters = roadWidth;
            road.pointSpacingMeters = roadPointSpacing;
            road.roadColor = roadColor;
            // The marker floats markerHeight above ground; drop the road so it sits
            // roadHeightMeters above the terrain instead of level with the pin.
            road.verticalOffset = roadHeightMeters - markerHeight;

            go.SetActive(true);
            _lightRoad = road;
        }

        private void EnsureTerrainHeight()
        {
            if (!Application.isPlaying) return;
            if (_terrainHeight != null) return;

            _terrainHeight = RtgTerrainHeight.FindOrCreate();
            if (_terrainHeight == null)
            {
                Debug.LogWarning("[RTG] No Cesium3DTileset found — spaceship will use flat ground height.");
                return;
            }

            _terrainHeight.Configure(groundHeightMeters, markerHeight);
        }

        // Creates the route-session driver and hands it the live-API config from the
        // Echo Site loader (set by "6b. Connect Echo Sites to Live API"). In sample
        // mode the world/empire ids are blank, so Begin Route reports that it needs
        // a live world instead of failing silently.
        private void EnsureRouteSession()
        {
            if (!recordRouteSessions || !Application.isPlaying) return;
            if (_routeSession != null) return;

            var go = new GameObject("Route Session");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            RtgRouteSession session = go.AddComponent<RtgRouteSession>();

#if UNITY_2023_1_OR_NEWER
            RtgEchoSiteLoader loader = UnityEngine.Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
            RtgEchoSiteLoader loader = UnityEngine.Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
            if (loader != null)
            {
                session.apiBaseUrl = loader.apiBaseUrl;
                session.worldId = loader.worldId;
                session.empireId = loader.empireId;
            }

            go.SetActive(true);
            _routeSession = session;
        }

        private void EnsureTapToConnect()
        {
            if (!Application.isPlaying) return;
            if (GetComponent<RtgTapToConnect>() != null) return;
            gameObject.AddComponent<RtgTapToConnect>();
        }

        private void EnsureCesiumCreditsToggle()
        {
            if (!Application.isPlaying) return;
            _creditsToggle = GetComponent<RtgCesiumCreditsToggle>();
            if (_creditsToggle == null)
                _creditsToggle = gameObject.AddComponent<RtgCesiumCreditsToggle>();
        }

        private void BuildMarkerVisual(Transform root)
        {
            if (markerStyle == PlayerMarkerStyle.SpaceshipSprite)
                BuildShipVisual(root);
            else
                BuildGoldPinVisual(root);
        }

        private void BuildShipVisual(Transform root)
        {
            Transform existingShip = root.Find("Ship");
            if (existingShip != null) DestroyImmediateSafe(existingShip.gameObject);
            _shipVisual = null;

            Texture2D tex = ResolveShipTexture();
            if (tex == null)
            {
                Debug.LogWarning(
                    "[RTG] Ship texture missing — falling back to gold pin. " +
                    "Run Routes to Glory → 8b. Sync Player Ship Art.");
                BuildGoldPinVisual(root);
                return;
            }

            var shipGo = new GameObject("Ship");
            shipGo.transform.SetParent(root, false);
            _shipVisual = shipGo.AddComponent<RtgPlayerShipVisual>();
            _shipVisual.Configure(tex, shipSizeMeters, shipHeadingOffsetDegrees);

            if (!_shipVisual.IsReady)
            {
                Debug.LogWarning("[RTG] Ship visual failed — falling back to gold pin.");
                DestroyImmediateSafe(shipGo);
                _shipVisual = null;
                BuildGoldPinVisual(root);
            }
        }

        private void BuildGoldPinVisual(Transform root)
        {
            Transform existingBeacon = root.Find("Beacon");
            if (existingBeacon != null) DestroyImmediateSafe(existingBeacon.gameObject);
            Color playerColor = new Color(1.0f, 0.92f, 0.45f); // bright gold — clearly "you"

            _markerMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = "RTG_Player"
            };
            _markerMaterial.SetColor("_BaseColor", playerColor);
            _markerMaterial.EnableKeyword("_EMISSION");
            _markerMaterial.SetColor("_EmissionColor", playerColor * 2.5f);
            _markerMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            GameObject beacon = RtgMeshPrimitives.CreateMeshObject(
                "Beacon", RtgMeshPrimitives.Sphere, _markerMaterial, root);
            beacon.transform.localScale = new Vector3(10f, 18f, 10f);
        }

        private Texture2D ResolveShipTexture()
        {
            if (shipTexture != null) return shipTexture;
            return Resources.Load<Texture2D>("RTG_PlayerShip/glider_01");
        }

        private void UpdateShipHeading()
        {
            if (_shipVisual == null) return;
            _shipVisual.SetHeadingRadians(_travelHeadingRad);
        }

        /// <summary>Travel direction on the ground plane (+Z = north). Radians.</summary>
        public float TravelHeadingRadians => _travelHeadingRad;

        // ------------------------------------------------------------------ //
        // Public focus queries (fog sheet, tap-connect, etc.)
        // ------------------------------------------------------------------ //

        /// <summary>Player pin lat/lng from the globe anchor.</summary>
        public bool TryGetPlayerLatLng(out double lat, out double lng)
        {
            lat = lng = 0;
            if (_markerAnchor == null) return false;
            lat = _markerAnchor.latitude;
            lng = _markerAnchor.longitude;
            return true;
        }

        /// <summary>
        /// Camera look-at point in lat/lng. Tracks panned map focus so fog can cover
        /// whatever is on screen, not just the area around the player pin.
        /// </summary>
        public bool TryGetViewCenterLatLng(out double lat, out double lng)
        {
            lat = lng = 0;
            if (!TryGetPlayerLatLng(out double playerLat, out double playerLng))
                return false;

            if (_marker == null) return false;

            Vector3 delta = _focus - _marker.position;
            delta.y = 0f;
            double lngM = 111_320.0 * System.Math.Cos(playerLat * System.Math.PI / 180.0);
            lat = playerLat + delta.z / 111_320.0;
            lng = playerLng + delta.x / lngM;
            return true;
        }

        // ------------------------------------------------------------------ //
        // Camera follow
        // ------------------------------------------------------------------ //

        private void CacheCamera()
        {
            _camera = Camera.main;
            if (_camera == null) return;
            _cameraController = _camera.GetComponent<CesiumCameraController>();
            _cameraOriginShift = _camera.GetComponent<CesiumOriginShift>();
            _georeference = _camera.GetComponentInParent<CesiumGeoreference>();
            if (_georeference == null)
            {
#if UNITY_2023_1_OR_NEWER
                _georeference = UnityEngine.Object.FindFirstObjectByType<CesiumGeoreference>();
#else
                _georeference = UnityEngine.Object.FindObjectOfType<CesiumGeoreference>();
#endif
            }
        }

        private void UpdateCameraFollow()
        {
            if (_camera == null || _marker == null) return;

            if (!followWithCamera)
            {
                if (_followActive) SetFollowActive(false);
                return;
            }

            if (!_followActive) SetFollowActive(true);

            UpdateZoom();
            SmoothZoom();
            HandlePanInput();

            // Focus tracks the player unless the user has dragged the map away.
            if (!_panned) _focusTarget = _marker.position;

            // Smooth the focus so both normal tracking and re-centering glide.
            if (followSmoothing > 0f)
            {
                float t = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
                _focus = Vector3.Lerp(_focus, _focusTarget, t);
            }
            else
            {
                _focus = _focusTarget;
            }

            _camera.transform.position = DesiredCameraPosition(_focus);
            _camera.transform.LookAt(_focus, Vector3.up);
        }

        private void HandlePanInput()
        {
            if (IsMultiTouchActive())
            {
                _wasPointerDown = false;
                return;
            }

            if (!ReadPointer(out Vector2 pos, out bool isDown))
            {
                _wasPointerDown = false;
                return;
            }

            if (IsOverGameUi(pos))
            {
                _wasPointerDown = false;
                return;
            }

            // New press — anchor pointer so the first drag frame doesn't jump from (0,0).
            if (isDown && !_wasPointerDown)
            {
                _lastPointer = pos;
            }
            else if (isDown && _wasPointerDown)
            {
                Vector2 delta = pos - _lastPointer;
                const float minPanPixels = 14f;
                if (delta.sqrMagnitude > minPanPixels * minPanPixels)
                {
                    _panned = true;

                    float metersPerPixel =
                        (EffectiveFollowHeight() * _zoom) / Mathf.Max(1, Screen.height) * panSpeed;
                    if (Application.isMobilePlatform)
                        metersPerPixel *= mobilePanScale;

                    _focusTarget += ScreenPanDeltaToWorld(delta, metersPerPixel);
                }
            }

            _lastPointer = pos;
            _wasPointerDown = isDown;
        }

        /// <summary>
        /// Converts a screen-space drag (pixels) into a world-space focus shift on the
        /// ground plane, using the camera orientation so panning feels like Google Maps
        /// regardless of chase-cam / travel heading.
        /// </summary>
        private Vector3 ScreenPanDeltaToWorld(Vector2 screenDelta, float metersPerPixel)
        {
            Vector3 right = _camera.transform.right;
            Vector3 forward = _camera.transform.forward;
            right.y = 0f;
            forward.y = 0f;

            if (right.sqrMagnitude < 1e-8f)
                right = Vector3.right;
            else
                right.Normalize();

            if (forward.sqrMagnitude < 1e-8f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            return (-screenDelta.x * right - screenDelta.y * forward) * metersPerPixel;
        }

        /// <summary>
        /// Counts pressed touches only — Touchscreen.touches.Count is the slot count (~10),
        /// not how many fingers are down, so a naive &gt;= 2 check blocks pan forever.
        /// </summary>
        private static bool IsMultiTouchActive()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current == null) return false;
            int active = 0;
            foreach (var touch in Touchscreen.current.touches)
            {
                if (touch.press.isPressed)
                    active++;
            }
            return active >= 2;
#else
            return Input.touchCount >= 2;
#endif
        }

        private bool IsOverGameUi(Vector2 screenPos)
        {
            // IMGUI rects use y from top; Input System touch y is from bottom.
            Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (Rect rect in _gameUiRects)
            {
                if (rect.Contains(guiPos))
                    return true;
            }
            return false;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            _gameUiRects.Clear();

            const float margin = 24f;
            const float gap = 12f;
            const float zoomW = 144f;
            const float zoomH = 92f;
            const float wideW = 280f;
            const float wideH = 92f;

            DrawMovementControls();

            if (source == LocationSource.SimulatedRoute && routeMode == RouteMode.HomeToCasper)
                DrawRestartRouteButton();

            var prev = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = 28;

            float right = Screen.width - zoomW - margin;
            float wideRight = Screen.width - wideW - margin;
            float midY = Screen.height * 0.5f;

            var zoomInRect = new Rect(right, midY - zoomH - gap * 0.5f, zoomW, zoomH);
            var zoomOutRect = new Rect(right, midY + gap * 0.5f, zoomW, zoomH);

            if (followWithCamera)
            {
                var viewRect = new Rect(wideRight, zoomInRect.yMin - gap - wideH, wideW, wideH);
                Rect centerRect = default;
                if (_panned)
                    centerRect = new Rect(wideRight, viewRect.yMin - gap - wideH, wideW, wideH);

                if (_panned && GUI.Button(centerRect, "Center")) RecenterOnPlayer();

                string viewLabel = perspective == CameraPerspective.Map ? "Route View" : "Map View";
                if (GUI.Button(viewRect, viewLabel)) TogglePerspective();

                if (GUI.Button(zoomInRect, "+"))
                    _zoomTarget = Mathf.Clamp(_zoomTarget / 1.35f, minZoom, maxZoom);
                if (GUI.Button(zoomOutRect, "−"))
                    _zoomTarget = Mathf.Clamp(_zoomTarget * 1.35f, minZoom, maxZoom);

                if (_panned) _gameUiRects.Add(centerRect);
                _gameUiRects.Add(viewRect);
            }

            if (source == LocationSource.SimulatedRoute)
            {
                float throttleTop = followWithCamera
                    ? zoomOutRect.yMax + gap
                    : midY + zoomH + gap;
                DrawThrottleLever(right, throttleTop, zoomW);
            }

            if (followWithCamera)
            {
                _gameUiRects.Add(zoomInRect);
                _gameUiRects.Add(zoomOutRect);
            }

            const float infoSize = 92f;
            var infoRect = new Rect(margin, Screen.height - margin - infoSize, infoSize, infoSize);
            if (GUI.Button(infoRect, _creditsToggle != null && _creditsToggle.IsVisible ? "×" : "i"))
                _creditsToggle?.Toggle();
            _gameUiRects.Add(infoRect);

            GUI.skin.button.fontSize = prev;
        }

        private void DrawMovementControls()
        {
            const float margin = 24f;
            const float gap = 12f;
            const float wideW = 280f;
            const float btnH = 92f;

            float infoTop = Screen.height - margin - 92f;
            float sourceY = infoTop - gap - btnH;

            var prevFont = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = 28;

            string sourceLabel = source == LocationSource.DeviceGps ? "GPS" : "Plan Route";
            var sourceRect = new Rect(margin, sourceY, wideW, btnH);
            if (GUI.Button(sourceRect, sourceLabel))
                ToggleLocationSource();
            _gameUiRects.Add(sourceRect);

            GUI.skin.button.fontSize = prevFont;
        }

        private static GUIStyle BrightLabel(int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            return new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize,
                fontStyle = fontStyle,
                clipping = TextClipping.Clip,
                wordWrap = false,
                normal = { textColor = color },
                hover = { textColor = color },
                active = { textColor = color },
                focused = { textColor = color },
            };
        }

        private void DrawThrottleLever(float panelX, float panelY, float panelW)
        {
            float minThrottle = Mathf.Max(0.01f, minSpeedThrottle);
            float maxThrottle = Mathf.Max(minThrottle + 0.01f, maxSpeedThrottle);
            const float desiredPanelH = 520f;
            const float margin = 24f;

            float width = Mathf.Max(panelW, 168f);
            float availableH = Screen.height - margin - panelY;
            float panelH = Mathf.Min(desiredPanelH, availableH);
            const float headerH = 80f;
            float footerH = Mathf.Max(96f, panelH * 0.22f);
            var panelRect = new Rect(panelX - (width - panelW), panelY, width, panelH);
            _gameUiRects.Add(panelRect);

            Color prevColor = GUI.color;
            var prevLabel = GUI.skin.label.fontSize;

            GUI.color = new Color(0.06f, 0.09f, 0.18f, 0.94f);
            GUI.Box(panelRect, GUIContent.none);

            Color titleColor = new Color(0.97f, 0.99f, 1f);
            Color subColor = new Color(0.93f, 0.97f, 1f);
            Color tierColor = Color.Lerp(new Color(0.98f, 0.99f, 1f), ThrottleGlowColor(speedThrottle), 0.4f);
            Color mphColor = new Color(0.98f, 1f, 1f);

            var titleStyle = BrightLabel(20, titleColor, FontStyle.Bold);
            var tierStyle = BrightLabel(22, tierColor, FontStyle.Bold);
            var speedStyle = BrightLabel(28, mphColor, FontStyle.Bold);
            var mphRangeStyle = BrightLabel(16, subColor);
            var cruiseStyle = BrightLabel(17, subColor);

            float baseMph = BaseSimulatedSpeedMph();
            float minMph = baseMph * minThrottle;
            float maxMph = baseMph * maxThrottle;
            float currentMph = EffectiveSimulatedSpeedMph();
            float footerTop = panelRect.yMax - footerH;

            GUI.Label(new Rect(panelRect.x, panelRect.y + 6f, panelRect.width, 22f), "THROTTLE", titleStyle);
            GUI.Label(
                new Rect(panelRect.x, panelRect.y + 26f, panelRect.width, 34f),
                $"{currentMph:0} MPH",
                speedStyle);

            var trackRect = new Rect(
                panelRect.x + 28f,
                panelRect.y + headerH,
                48f,
                panelRect.height - headerH - footerH);
            GUI.Label(
                new Rect(panelRect.x + 82f, trackRect.y - 2f, 56f, 24f),
                $"{maxMph:0}",
                mphRangeStyle);
            GUI.Label(
                new Rect(panelRect.x + 82f, trackRect.yMax - 22f, 56f, 24f),
                $"{minMph:0}",
                mphRangeStyle);
            float fillT = Mathf.InverseLerp(minThrottle, maxThrottle, speedThrottle);
            float fillH = trackRect.height * fillT;
            var fillRect = new Rect(trackRect.x + 4f, trackRect.yMax - fillH - 4f, trackRect.width - 8f, fillH);

            GUI.color = new Color(0.12f, 0.18f, 0.28f, 1f);
            GUI.Box(trackRect, GUIContent.none);
            GUI.color = ThrottleGlowColor(speedThrottle);
            GUI.Box(fillRect, GUIContent.none);

            // Bright cap at current power level
            if (fillH > 6f)
            {
                GUI.color = Color.Lerp(ThrottleGlowColor(speedThrottle), Color.white, 0.45f);
                GUI.Box(new Rect(fillRect.x, fillRect.y - 3f, fillRect.width, 6f), GUIContent.none);
            }

            GUI.color = Color.white;
            float newThrottle = GUI.VerticalSlider(trackRect, speedThrottle, minThrottle, maxThrottle);
            if (!Mathf.Approximately(newThrottle, speedThrottle))
            {
                speedThrottle = Mathf.Clamp(newThrottle, minThrottle, maxThrottle);
                ApplyThrottleToProvider();
            }

            GUI.Label(
                new Rect(panelRect.x, footerTop + 8f, panelRect.width, 28f),
                ThrottleTierLabel(speedThrottle),
                tierStyle);
            GUI.Label(
                new Rect(panelRect.x, footerTop + 38f, panelRect.width, 24f),
                $"cruise {baseMph:0} · {speedThrottle:0.0}×",
                cruiseStyle);

            GUI.color = prevColor;
            GUI.skin.label.fontSize = prevLabel;
        }

        private static string ThrottleTierLabel(float throttle)
        {
            if (throttle >= 2.5f) return "WARP";
            if (throttle >= 1.5f) return "BURN";
            if (throttle >= 0.75f) return "FAST";
            return "CRUISE";
        }

        private static Color ThrottleGlowColor(float throttle)
        {
            if (throttle >= 2.5f) return new Color(1f, 0.35f, 0.95f);
            if (throttle >= 1.5f) return new Color(1f, 0.55f, 0.2f);
            if (throttle >= 0.75f) return new Color(0.3f, 0.95f, 1f);
            return new Color(0.35f, 0.75f, 0.95f);
        }

        private string FormatSpeedLabel()
        {
            if (source == LocationSource.DeviceGps)
                return _provider != null ? _provider.Status : "GPS";

            return $"{EffectiveSimulatedSpeedMph():0} MPH";
        }

        private void RecenterOnPlayer()
        {
            _panned = false; // focus glides back to the pin via smoothing
        }

        private void TogglePerspective()
        {
            perspective = perspective == CameraPerspective.Map
                ? CameraPerspective.LowAngle
                : CameraPerspective.Map;

            if (_followActive && _camera != null)
            {
                _camera.transform.position = DesiredCameraPosition(_focus);
                _camera.transform.LookAt(_focus, Vector3.up);
            }
        }

        private static bool ReadPointer(out Vector2 position, out bool isDown)
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                isDown = true;
                return true;
            }
            if (Mouse.current != null)
            {
                position = Mouse.current.position.ReadValue();
                isDown = Mouse.current.leftButton.isPressed;
                return true;
            }
            position = default;
            isDown = false;
            return false;
#else
            position = Input.mousePosition;
            isDown = Input.GetMouseButton(0);
            return true;
#endif
        }

        // Near the georeference origin the local frame is axis-aligned: +Y is up,
        // +X east, +Z north. Both perspectives trail behind the player's travel
        // heading (chase cam), not a fixed south offset.
        private Vector3 DesiredCameraPosition(Vector3 target)
        {
            float zoom = _zoom;
            Vector3 behind = -TravelDirectionXZ() * (TrailDistanceMeters(zoom));

            if (perspective == CameraPerspective.Map)
            {
                return target + Vector3.up * (followHeightMeters * zoom) + behind;
            }

            float trail = perspectiveTrailMeters * zoom;
            float height = trail * Mathf.Tan(perspectiveElevationDegrees * Mathf.Deg2Rad);
            return target + Vector3.up * height + behind;
        }

        private float TrailDistanceMeters(float zoom)
        {
            return perspective == CameraPerspective.Map
                ? followDistanceMeters * zoom
                : perspectiveTrailMeters * zoom;
        }

        /// <summary>Unit vector on the ground plane pointing where the player is moving.</summary>
        private Vector3 TravelDirectionXZ()
        {
            float sin = Mathf.Sin(_travelHeadingRad);
            float cos = Mathf.Cos(_travelHeadingRad);
            return new Vector3(sin, 0f, cos);
        }

        private void UpdateTravelHeading()
        {
            if (_marker == null) return;

            Vector3 pos = _marker.position;
            if (!_hasHeadingSample)
            {
                _lastHeadingSamplePos = pos;
                _hasHeadingSample = true;
                return;
            }

            Vector3 delta = pos - _lastHeadingSamplePos;
            delta.y = 0f;
            if (delta.sqrMagnitude < minHeadingMoveMeters * minHeadingMoveMeters)
                return;

            float targetHeading = Mathf.Atan2(delta.x, delta.z);
            _lastHeadingSamplePos = pos;

            if (headingSmoothing > 0f)
            {
                float t = 1f - Mathf.Exp(-headingSmoothing * Time.deltaTime);
                float curDeg = _travelHeadingRad * Mathf.Rad2Deg;
                float tgtDeg = targetHeading * Mathf.Rad2Deg;
                _travelHeadingRad = Mathf.LerpAngle(curDeg, tgtDeg, t) * Mathf.Deg2Rad;
            }
            else
            {
                _travelHeadingRad = targetHeading;
            }
        }

        private float EffectiveFollowHeight()
        {
            if (perspective == CameraPerspective.Map) return followHeightMeters;
            return perspectiveTrailMeters * Mathf.Tan(perspectiveElevationDegrees * Mathf.Deg2Rad);
        }

        private void UpdateZoom()
        {
            float scroll = ReadScroll();
            if (Mathf.Abs(scroll) < 0.001f) return;
            ApplyZoomStep(scroll);
        }

        private void SmoothZoom()
        {
            if (Mathf.Abs(_zoom - _zoomTarget) < 0.0001f)
            {
                _zoom = _zoomTarget;
                return;
            }

            float t = zoomSmoothing > 0f
                ? 1f - Mathf.Exp(-zoomSmoothing * Time.deltaTime)
                : 1f;
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, t);
        }

        private void ApplyZoomStep(float step)
        {
            float clamped = Mathf.Clamp(step, -1f, 1f) * (invertZoom ? -1f : 1f);
            _zoomTarget = Mathf.Clamp(
                _zoomTarget * Mathf.Exp(-clamped * zoomSensitivity), minZoom, maxZoom);
        }

        private static float ReadScroll()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
            return Input.mouseScrollDelta.y;
#endif
        }

        // Hand control to the follow cam (or give it back to free-fly). We disable the
        // fly controller and origin shift while following so direct transform control is
        // stable; both are safe to leave off at this small play scale.
        private void SetFollowActive(bool active)
        {
            _followActive = active;
            if (_cameraController != null) _cameraController.enabled = !active;
            if (_cameraOriginShift != null) _cameraOriginShift.enabled = !active;

            // Snap immediately on the first follow frame (ignore smoothing) so we don't
            // slowly drift in from wherever the fly camera was.
            if (active && _marker != null)
            {
                _panned = false;
                _focusTarget = _marker.position;
                _focus = _focusTarget;
                _hasHeadingSample = false;
                _camera.transform.position = DesiredCameraPosition(_focus);
                _camera.transform.LookAt(_focus, Vector3.up);
            }
        }

        private static void DestroyImmediateSafe(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        /// <summary>Dev helper: jump back to route start and clear the live Light Road.</summary>
        public void RestartSimulatedRoute()
        {
            if (_lightRoad != null)
            {
                _lightRoad.ClearRoad();
                _roadStarted = false;
            }

            if (_provider is RtgSimulatedLocationProvider sim)
                sim.Restart();

            _panned = false;
            _hasHeadingSample = false;
            if (_marker != null)
            {
                _focusTarget = _marker.position;
                _focus = _focusTarget;
            }

            Debug.Log("[RTG] Simulated route restarted from home.");
        }

        private void DrawRestartRouteButton()
        {
            const float margin = 24f;
            const float wideW = 280f;
            const float wideH = 92f;
            float wideRight = Screen.width - wideW - margin;
            float y = Screen.height - margin - wideH;

            var prev = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = 28;
            var restartRect = new Rect(wideRight, y, wideW, wideH);
            if (GUI.Button(restartRect, "Restart Route"))
                RestartSimulatedRoute();
            _gameUiRects.Add(restartRect);
            GUI.skin.button.fontSize = prev;
        }
    }
}
