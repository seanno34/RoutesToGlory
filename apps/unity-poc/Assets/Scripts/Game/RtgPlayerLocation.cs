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

        // FixedLoop = walk the small hand-authored rectangle (or the `route` field).
        // TourNearbySites = auto-build a loop that threads past every Echo Site /
        // resource node near the play area so you can sight-see the whole map.
        public enum RouteMode { FixedLoop, TourNearbySites }

        [Header("Source")]
        public LocationSource source = LocationSource.SimulatedRoute;

        [Tooltip("FixedLoop = small rectangle. TourNearbySites = auto loop past every nearby Echo Site + resource node.")]
        public RouteMode routeMode = RouteMode.TourNearbySites;

        [Tooltip("Walking speed for the fixed loop (m/s). ~1.4 = real walk; higher is easier to watch.")]
        public float simulatedSpeed = 15f;

        [Tooltip("Travel speed for the 'tour nearby sites' loop (m/s). Higher = quicker sight-seeing across the whole area.")]
        public float tourSpeed = 90f;

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

        [Tooltip("Seconds to wait for Echo Sites to load before falling back to the fixed loop.")]
        public float tourLoadTimeoutSeconds = 12f;

        [Header("Placement")]
        [Tooltip("Approx. ground height (m above ellipsoid) near Douglas, WY.")]
        public double groundHeightMeters = 1476.0;

        [Tooltip("Meters the player marker's center sits above the ground.")]
        public float markerHeight = 15f;

        [Header("Camera follow")]
        [Tooltip("If enabled, the camera stays overhead and auto-tracks the player (Google-Maps style). Untick for free-fly.")]
        public bool followWithCamera = true;

        [Tooltip("Meters the camera sits above the player.")]
        public float followHeightMeters = 300f;

        [Tooltip("Meters the camera trails south of the player (0 = straight down; larger = more tilted/overhead-behind).")]
        public float followDistanceMeters = 120f;

        [Tooltip("Follow smoothing (0 = instant/locked; higher = smoother lag). ~5 feels like Google Maps.")]
        public float followSmoothing = 6f;

        [Header("Zoom (scroll wheel while following)")]
        [Tooltip("How much each scroll step zooms. Raise for faster zoom.")]
        public float zoomSensitivity = 0.12f;

        [Tooltip("Closest zoom (multiplier on follow height/distance).")]
        public float minZoom = 0.15f;

        [Tooltip("Farthest zoom (multiplier on follow height/distance).")]
        public float maxZoom = 12f;

        [Tooltip("Flip if the scroll wheel zooms the wrong way (e.g. natural scrolling).")]
        public bool invertZoom = false;

        private float _zoom = 1f;

        [Header("Pan (drag the map)")]
        [Tooltip("Drag pan speed. The map moves under your finger/cursor; higher = faster.")]
        public float panSpeed = 2f;

        // When the user drags, the camera's focus point stops tracking the player and a
        // "Center" button appears; tapping it snaps the focus back to the pin.
        private bool _panned;
        private Vector3 _focus;        // smoothed point the camera hovers over
        private Vector3 _focusTarget;  // where the focus wants to be (player, or panned point)
        private Vector2 _lastPointer;
        private bool _wasPointerDown;

        private IRtgLocationProvider _provider;
        private Transform _marker;
        private CesiumGlobeAnchor _markerAnchor;
        private Material _markerMaterial;

        private Camera _camera;
        private CesiumCameraController _cameraController;
        private CesiumOriginShift _cameraOriginShift;
        private bool _followActive;

        private void Start()
        {
            EnsureMarker();
            CacheCamera();

            // The tour route depends on the Echo Sites, which may still be loading
            // (async in Play mode for live data), so build it in a coroutine that
            // waits for them. Everything else starts a provider immediately.
            if (source == LocationSource.SimulatedRoute && routeMode == RouteMode.TourNearbySites)
            {
                StartCoroutine(BeginTourWhenSitesReady());
            }
            else
            {
                _provider = CreateProvider();
                _provider.Begin();
            }
        }

        private void OnDisable()
        {
            _provider?.End();
        }

        private void Update()
        {
            if (_provider == null) return;

            _provider.Tick(Time.deltaTime);
            if (_provider.TryGetLatLng(out double lat, out double lng))
            {
                _markerAnchor.SetPositionLongitudeLatitudeHeight(
                    lng, lat, groundHeightMeters + markerHeight);
            }
        }

        private void LateUpdate()
        {
            UpdateCameraFollow();
        }

        // ------------------------------------------------------------------ //
        // Editor preview
        // ------------------------------------------------------------------ //

        /// <summary>Editor helper: create the marker and place it at the route start.</summary>
        public void EditorPlaceAtStart()
        {
            EnsureMarker();

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
            return new RtgSimulatedLocationProvider(ResolveRoute(), simulatedSpeed);
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

        // ------------------------------------------------------------------ //
        // Tour of nearby sites
        // ------------------------------------------------------------------ //

        private IEnumerator BeginTourWhenSitesReady()
        {
            RtgEchoSiteLoader loader =
#if UNITY_2023_1_OR_NEWER
                Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
                Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif

            // Wait for the Echo Sites to finish loading (live data is fetched async).
            float waited = 0f;
            while ((loader == null || loader.LastMap == null) && waited < tourLoadTimeoutSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            RtgWaypoint[] tour = loader != null && loader.LastMap != null
                ? BuildTourRoute(loader.LastMap)
                : null;

            if (tour == null || tour.Length < 2)
            {
                Debug.LogWarning(
                    "[RTG] Tour route unavailable (no nearby sites loaded) — falling back to the fixed loop. " +
                    "Load Echo Sites first, or widen Tour Radius.");
                tour = ResolveRoute();
            }
            else
            {
                Debug.Log($"[RTG] Touring {tour.Length - 1} nearby site(s) around the play area.");
            }

            _provider = new RtgSimulatedLocationProvider(tour, tourSpeed);
            _provider.Begin();
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

            if (root.transform.Find("Beacon") == null)
                BuildMarkerVisual(root.transform);
        }

        private void BuildMarkerVisual(Transform root)
        {
            Color playerColor = new Color(1.0f, 0.92f, 0.45f); // bright gold — clearly "you"

            GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            beacon.name = "Beacon";
            Collider col = beacon.GetComponent<Collider>();
            if (col != null) DestroyImmediateSafe(col);

            beacon.transform.SetParent(root, false);
            beacon.transform.localScale = new Vector3(10f, 18f, 10f);

            _markerMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = "RTG_Player"
            };
            _markerMaterial.SetColor("_BaseColor", playerColor);
            _markerMaterial.EnableKeyword("_EMISSION");
            _markerMaterial.SetColor("_EmissionColor", playerColor * 2.5f);
            _markerMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            var mr = beacon.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _markerMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            AddLabel(root, "You (GPS)", 40f);
        }

        private static readonly Vector2[] OutlineDirections =
        {
            new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f),
            new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f),
        };

        private static void AddLabel(Transform root, string text, float charSize)
        {
            var pivot = new GameObject("Label");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, 50f, 0f);
            pivot.AddComponent<RtgBillboard>();

            float outline = charSize * 0.08f;
            foreach (Vector2 dir in OutlineDirections)
            {
                Vector2 o = dir.normalized * outline;
                CreateText(pivot.transform, text, charSize, Color.black, new Vector3(o.x, o.y, 0f));
            }
            CreateText(pivot.transform, text, charSize, new Color(1f, 0.95f, 0.6f),
                new Vector3(0f, 0f, -charSize * 0.5f));
        }

        private static void CreateText(
            Transform parent, string text, float charSize, Color color, Vector3 localPosition)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = text;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = charSize;
            tm.color = color;

            var mr = go.GetComponent<MeshRenderer>();
            if (font != null) mr.sharedMaterial = font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
            if (!ReadPointer(out Vector2 pos, out bool isDown))
            {
                _wasPointerDown = false;
                return;
            }

            if (isDown && _wasPointerDown)
            {
                Vector2 delta = pos - _lastPointer;
                if (delta.sqrMagnitude > 4f) // ignore <2px jitter / taps
                {
                    _panned = true;

                    // Meters moved per screen pixel ≈ how tall the view is / screen height,
                    // so the map tracks the cursor/finger consistently at any zoom.
                    float metersPerPixel =
                        (followHeightMeters * _zoom) / Mathf.Max(1, Screen.height) * panSpeed;

                    // World axes near the origin: +X = east, +Z = north. Drag the map with
                    // the cursor, so the focus moves opposite the drag direction.
                    _focusTarget += (-delta.x * Vector3.right - delta.y * Vector3.forward) * metersPerPixel;
                }
            }

            _lastPointer = pos;
            _wasPointerDown = isDown;
        }

        private void RecenterOnPlayer()
        {
            _panned = false; // focus glides back to the pin via smoothing
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !followWithCamera || !_panned) return;

            const float w = 130f, h = 46f, margin = 24f;
            var rect = new Rect(Screen.width - w - margin, Screen.height - h - margin, w, h);

            var prev = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = 18;
            if (GUI.Button(rect, "Center")) RecenterOnPlayer();
            GUI.skin.button.fontSize = prev;
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
        // +Z is north. So "overhead, trailing south" = up * height - north * distance,
        // scaled by the current zoom so the angle stays constant as you zoom (Google-Maps style).
        private Vector3 DesiredCameraPosition(Vector3 target)
        {
            return target
                + Vector3.up * (followHeightMeters * _zoom)
                - Vector3.forward * (followDistanceMeters * _zoom);
        }

        private void UpdateZoom()
        {
            float scroll = ReadScroll();
            if (Mathf.Abs(scroll) < 0.001f) return;

            // Clamp to normalize wildly different scroll magnitudes (Input System reports
            // ~120/notch; legacy reports ~1). Positive scroll = zoom in by default.
            float step = Mathf.Clamp(scroll, -1f, 1f) * (invertZoom ? -1f : 1f);
            _zoom = Mathf.Clamp(_zoom * Mathf.Exp(-step * zoomSensitivity), minZoom, maxZoom);
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
                _camera.transform.position = DesiredCameraPosition(_focus);
                _camera.transform.LookAt(_focus, Vector3.up);
            }
        }

        private static void DestroyImmediateSafe(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
