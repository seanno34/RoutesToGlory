using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    ///
    /// <para><b>Terrain elevation pipeline</b> (see <see cref="RtgTerrainElevationGuards"/>):
    /// Update queues corridor samples; LateUpdate applies glider height then feeds Light Road.
    /// Do not set marker ellipsoid height in Update — causes bounce vs Cesium tiles.</para>
    /// </summary>
    [DefaultExecutionOrder(RtgTerrainElevationGuards.PlayerLocationExecutionOrder)]
    public class RtgPlayerLocation : MonoBehaviour
    {
        // Manual = real device GPS (ship only moves when you move).
        // AutoPilot = simulated auto-route (HomeToCasper, tour, etc.).
        public enum LocationSource
        {
            Manual = 0,
            AutoPilot = 1,
        }

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

        [Tooltip("Optional cockpit frame overlay (Resources/RTG_PlayerShip/glider_cockpit_01).")]
        public Texture2D cockpitTexture;

        [Tooltip("Optional portrait cockpit overlay (Resources/RTG_PlayerShip/glider_cockpit_portrait_01).")]
        public Texture2D cockpitPortraitTexture;

        [Tooltip("Wingspan of the top-down ship sprite in meters.")]
        public float shipSizeMeters = 24f;

        [Tooltip("Optional Tripo FBX/prefab. When unset, RtgPlayerShipVisual auto-loads the default Tripo import.")]
        public GameObject shipHullPrefab;

        [Tooltip("Fine-tune rotation after auto-orient (usually leave at 0,0,0).")]
        public Vector3 shipHullEulerOffset = Vector3.zero;

        [Tooltip("Euler rotation for Tripo xenite deposits at spawn (Settings → Xenite deposit).")]
        public Vector3 xeniteDepositEulerOffset = RtgXeniteDepositTuningConfig.DefaultEulerOffset;

        [Tooltip("Tripo hull auto-orient. Disable when tuning orientation manually.")]
        public bool shipAutoOrientImportedHull = true;

        [Tooltip("Tune if the ship nose points backward (180 = flip).")]
        public float shipHeadingOffsetDegrees = 0f;

        [Tooltip("When true, exhaust positions use saved hull-relative anchors (0–1).")]
        public bool shipUseCustomEnginePorts;

        public RtgExhaustAnchor shipMainExhaustAnchor = RtgGliderExhaustAnchors.DefaultMain;
        public RtgExhaustAnchor shipLeftExhaustAnchor = RtgGliderExhaustAnchors.DefaultLeft;
        public RtgExhaustAnchor shipRightExhaustAnchor = RtgGliderExhaustAnchors.DefaultRight;

        [HideInInspector] public Vector3 shipMainEngineLocal;
        [HideInInspector] public Vector3 shipLeftEngineLocal;
        [HideInInspector] public Vector3 shipRightEngineLocal;
        [HideInInspector] public bool shipEnginePortsMeshLocal = true;

        [Tooltip("Legacy global plume scale (migrated into per-speed-stop plumeLengthScale on load).")]
        [Range(0.15f, 2.5f)]
        public float shipExhaustLengthScale = 1f;

        [Tooltip("Extra cone plume length on mobile. Cavity heads stay in tuned meters.")]
        [Range(1f, 3f)]
        public float mobilePlumeVisibilityBoost = 1.35f;

        public RtgEngineCavityTuning shipMainCavity = RtgEngineCavityTuning.Default;
        public RtgEngineCavityTuning shipLeftCavity = RtgEngineCavityTuning.Default;
        public RtgEngineCavityTuning shipRightCavity = RtgEngineCavityTuning.Default;

        [Tooltip("Speed-color stops for exhaust/cavity (4 stops, interpolated by mph).")]
        public RtgExhaustColorStop[] shipExhaustColorStops;

        [Tooltip("Mph at which exhaust colors reach full heat (100+ mph stays at this color).")]
        public float shipExhaustColorMaxMph = 99f;

        [Header("Source")]
        [Tooltip("Manual = real GPS (stationary until you move). Auto Pilot = simulated auto-route.")]
        private LocationSource _activeSource = LocationSource.Manual;

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

        [Tooltip("Highest multiplier on the in-game throttle lever (testing; color heat caps at 99 mph).")]
        [Min(0.02f)]
        public float maxSpeedThrottle = 800f;

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

        [Tooltip("Meters the player marker's center sits above the ground. Keep above Light Road / persisted travel clearance.")]
        public float markerHeight = RtgTerrainElevationGuards.GliderClearanceM;

        [Header("Light Road")]
        [Tooltip("Draw a glowing Light Road trail behind the player as it moves.")]
        public bool drawLightRoad = true;

        [Tooltip("Road width in meters.")]
        public float roadWidth = 8f;

        [Tooltip("Meters above the ground the road ribbon sits. Must stay below markerHeight so the glider reads above roads.")]
        public float roadHeightMeters = RtgTerrainElevationGuards.TravelRoadClearanceM;

        [Tooltip("Record a road point after the player moves this many meters.")]
        public float roadPointSpacing = 12f;

        [Tooltip("Light Road color (bright energy ribbon).")]
        public Color roadColor = new Color(0.45f, 0.95f, 1.00f);

        [Header("Route session")]
        [Tooltip("Stream movement to the route-session API so a Light Road persists as a real route (needs live world via '6b').")]
        public bool recordRouteSessions = true;

        [Header("Auto Pilot (home testing)")]
        [Tooltip("When on, Auto Pilot uses the same route snap, geofence auto-connect, and checkpoint saves as Manual — full feature parity for home dev testing.")]
        public bool autopilotRealDriveParity = true;

        [Tooltip("Drive-to-city destination (HomeToCasper route mode). City + state works, e.g. Casper, WY. Full street address is optional and more precise.")]
        public string autopilotDestinationCity = "Casper, WY";

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

        [Header("Position smoothing (map pin)")]
        [Tooltip("How quickly the glider catches up to the target position (GPS or simulated). Higher = snappier; lower = smoother. Applies in Manual and Auto Pilot.")]
        public float gpsSmoothing = 10f;

        [Tooltip("Manual GPS only: request a new hardware fix after this many meters of movement.")]
        public float gpsUpdateDistanceMeters = 1f;

        [Tooltip("Manual GPS only: snap instead of smoothing if a fix jumps farther than this (GPS re-acquire).")]
        public float gpsMaxSnapMeters = 120f;

        [Header("Cockpit view")]
        [Tooltip("Eye height (m) above the player anchor in cockpit mode.")]
        public float cockpitEyeHeightMeters = 3.5f;

        [Tooltip("Pitch (degrees) in landscape cockpit. Positive = look down toward the map.")]
        public float cockpitLandscapePitchDegrees = 2f;

        [Tooltip("Pitch (degrees) in portrait cockpit. Raise if the windshield still shows too much sky.")]
        public float cockpitPortraitPitchDegrees = 22f;

        [Tooltip("Runtime pitch slider range (degrees).")]
        public float cockpitPitchMinDegrees = 0f;

        public float cockpitPitchMaxDegrees = 40f;

        [Tooltip("Zoom smoothing while the cockpit button animates to max zoom-in.")]
        public float cockpitZoomSmoothing = 28f;

        [Tooltip("Extra yaw (degrees) added to cockpit rig alignment if the view faces backward.")]
        public float cockpitHeadingOffsetDegrees = 0f;

        [Header("Cockpit look-around")]
        [Tooltip("Horizontal FOV while in cockpit (wider ≈ more glass-canopy immersion).")]
        [Range(55f, 110f)]
        public float cockpitFieldOfView = 90f;

        [Tooltip("Max yaw (degrees) left/right from travel heading (135° ≈ 270° canopy arc).")]
        public float cockpitLookYawMaxDegrees = 135f;

        [Tooltip("Extra pitch down (degrees) when drag-looking.")]
        public float cockpitLookPitchMinDegrees = -12f;

        [Tooltip("Extra pitch up (degrees) when drag-looking (sky through open glass top).")]
        public float cockpitLookPitchMaxDegrees = 42f;

        [Tooltip("Yaw degrees per screen pixel while drag-looking.")]
        public float cockpitLookYawSensitivity = 0.16f;

        [Tooltip("Pitch degrees per screen pixel while drag-looking.")]
        public float cockpitLookPitchSensitivity = 0.12f;

        [Tooltip("How quickly drag-look catches up (higher = snappier).")]
        public float cockpitLookSmoothing = 16f;

        [Tooltip("Show look-yaw / marker-movement debug readout while in cockpit.")]
        public bool cockpitLookDebugHud = true;

        [Header("Pathfinder beam")]
        [Tooltip("Beam arms when scatter props enter this distance (m).")]
        public float pathfinderDetectionRangeM = 115f;

        [Tooltip("Map/route view beam width at the glider (m).")]
        public float pathfinderMapBeamWidthM = 22f;

        [Tooltip("Use a narrow camera-aligned beam in cockpit view.")]
        public bool pathfinderUseCockpitBeam = true;

        [Tooltip("Cockpit beam width (m).")]
        public float pathfinderCockpitBeamWidthM = 5f;

        [Tooltip("Cockpit beam reach (m).")]
        public float pathfinderCockpitBeamLengthM = 50f;

        [Tooltip("Cockpit beam glow multiplier.")]
        public float pathfinderCockpitGlowMultiplier = 1.6f;

        private RtgCockpitView _cockpitView;
        private RtgCockpitRearCamera _cockpitRearCamera;
        private bool _cockpitEntryPending;
        private bool _cockpitFastZoom;
        private float _cockpitLookYawDeg;
        private float _cockpitLookPitchDeg;
        private float _cockpitLookYawTargetDeg;
        private float _cockpitLookPitchTargetDeg;
        private bool _cockpitLookPointerDown;
        private Vector2 _cockpitLookLastPointer;
        private Vector3 _cockpitDebugLastMarkerPos;
        private Vector3 _cockpitDebugLastCamPos;
        private bool _cockpitDebugHasMarkerPos;
        private bool _cockpitDebugHasCamPos;
        private float _savedCameraFov = 60f;
        private RtgCameraManager _cameraManager;

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
        private bool _settingsOpen;
        private static Texture2D _settingsGearIcon;
        private static Texture2D _leverKnobGlowTexture;
        private static Texture2D _leverKnobCoreTexture;
        private static readonly int ThrottleLeverHint = "RtgThrottleLever".GetHashCode();
        private static readonly int PitchLeverHint = "RtgPitchLever".GetHashCode();
        private int _activeLeverHint = -1;
        private Rect _activeLeverTrack;
        private float _clearRoutesConfirmUntil;
        private string _destinationDraft;
        private int _exhaustEngineIndex;
        private int _exhaustColorStopEditIndex;
        private int _exhaustColorChannelIndex;
        private float _exhaustColorPreviewMph = 40f;
        private bool _exhaustFlamePreviewEnabled = true;
        private int _exhaustColorLastSyncedStopIndex = -1;
        private static int _activeSettingSliderHint = -1;
        private string _cavityDepthDraft;
        private int _cavityDepthDraftEngineIndex = -1;
        private Vector2 _settingsScrollPosition;
        private Rect? _activeSettingsScrollViewport;
        private Coroutine _routeBuildCoroutine;
        private RtgWaypoint[] _cachedDriveRoute;

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
        private RtgPathfinderBeam _pathfinderBeam;
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

        private double _smoothDisplayLat;
        private double _smoothDisplayLng;
        private bool _hasSmoothDisplay;

        private double _anchorLat;
        private double _anchorLng;
        private bool _hasAnchorPosition;

        private Vector3 _lastMotionSamplePos;
        private bool _hasMotionSample;
        private float _groundSpeedMps;
        private float _prevTravelHeadingRad;

        private void Awake()
        {
            _activeSource = LocationSource.Manual;
        }

        private void Reset()
        {
            _activeSource = LocationSource.Manual;
        }

        private void OnValidate()
        {
            if (maxSpeedThrottle < minSpeedThrottle + 0.01f)
                maxSpeedThrottle = minSpeedThrottle + 0.01f;
            speedThrottle = Mathf.Clamp(speedThrottle, minSpeedThrottle, maxSpeedThrottle);
            ClampSpeedThrottle();
            SyncPathfinderBeamSettings();
        }

        private void Start()
        {
            ClampSpeedThrottle();
            EnsureExhaustColorStops();
            EnsureMarker();
            EnsureDefaultShipArt();
            if (RtgShipTuningConfig.TryLoad(out RtgShipTuningConfig.ShipTuningFile tuning))
            {
                RtgShipTuningConfig.ApplyTo(this, tuning);
                Debug.Log(
                    $"[RTG] Loaded ship tuning — hullEuler={shipHullEulerOffset} " +
                    $"autoOrient={shipAutoOrientImportedHull} heading={shipHeadingOffsetDegrees} " +
                    $"main={shipMainEngineLocal} left={shipLeftEngineLocal} right={shipRightEngineLocal}");
            }
            if (RtgXeniteDepositTuningConfig.TryLoad(out RtgXeniteDepositTuningConfig.XeniteDepositTuningFile xeniteTuning))
                xeniteDepositEulerOffset = xeniteTuning.depositEulerOffset;
            else
                RtgXeniteDepositTuningConfig.ApplyRuntimeEuler(xeniteDepositEulerOffset);
            RefreshMarkerVisual();
            EnsureTerrainHeight();
            EnsureLightRoad();
            EnsureRouteSession();
            SyncRouteSessionSnapMode();
            EnsureTapToConnect();
            EnsureMissionProgress();
            EnsureCesiumCreditsToggle();
            EnsureCockpitView();
            EnsureCockpitRearCamera();
            EnsurePathfinderBeam();
            EnsureCameraManager();

            _destinationDraft = autopilotDestinationCity;

#if !UNITY_EDITOR
            if (Application.isMobilePlatform)
            {
                _zoom = Mathf.Clamp(2f, minZoom, maxZoom);
                _zoomTarget = _zoom;
            }
#endif

            // The tour route depends on the Echo Sites, which may still be loading
            // (async in Play mode for live data), so build it in a coroutine that
            // waits for them. Everything else starts a provider immediately — unless
            // login is still blocking (no world / ship play until Join).
            Debug.Log($"[RTG] Location source at launch: {LocationSourceLabel(_activeSource)}");
            if (!RtgGameSessionLogin.IsPlayBlocked())
                BeginLocationProvider();
            else
                StartCoroutine(BeginLocationProviderWhenSessionReady());
        }

        private IEnumerator BeginLocationProviderWhenSessionReady()
        {
            while (RtgGameSessionLogin.IsPlayBlocked())
                yield return null;
            if (!isActiveAndEnabled) yield break;
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

        private void FixedUpdate()
        {
            if (RtgGameSessionLogin.IsPlayBlocked()) return;
            if (IsCockpitCameraActive() && _cameraManager != null)
                _cameraManager.SetGameplayCameraOwnership(true);
        }

        private void Update()
        {
            if (RtgGameSessionLogin.IsPlayBlocked()) return;
            if (_provider == null) return;

            if (IsCockpitCameraActive() && _cameraManager != null)
            {
                _cameraManager.SetMode(RtgCameraManager.CameraMode.Cockpit);
                _cameraManager.SetGameplayCameraOwnership(true);
                HandleCockpitLookInput();
                SmoothCockpitLook();
            }

            _provider.Tick(Time.deltaTime);
            if (_routeSession != null && _activeSource == LocationSource.AutoPilot)
                _routeSession.SetFabricatedGpsSpeedMps(EffectiveSimulatedSpeed());

            if (_provider.TryGetLatLng(out double lat, out double lng))
            {
                double targetLat = lat;
                double targetLng = lng;
                if (_routeSession != null)
                    _routeSession.TryBlendSnapForMovement(ref targetLat, ref targetLng, Time.deltaTime);

                ApplySmoothedDisplayPosition(targetLat, targetLng, out double displayLat, out double displayLng);

                _anchorLat = displayLat;
                _anchorLng = displayLng;
                _hasAnchorPosition = true;

                if (_terrainHeight == null)
                    EnsureTerrainHeight();

                if (_terrainHeight != null)
                {
                    // Queue async Cesium samples ahead of LateUpdate clearance pass.
                    // REGRESSION: do not set marker height here — ApplyMarkerTerrainHeight runs in LateUpdate.
                    _terrainHeight.QueueCorridorSamplesIfNeeded(
                        displayLat, displayLng, _travelHeadingRad);
                }

                // First real fix — begin tracing the Light Road (avoids a stray
                // segment from wherever the marker sat before we had a position).
                if (_lightRoad != null && !_roadStarted)
                {
                    _lightRoad.StartRecording();
                    _roadStarted = true;
                }

                // Record the same smoothed position shown on the map so the trail
                // and camera do not diverge from the persisted route geometry.
                if (_routeSession != null) _routeSession.NotifyPosition(displayLat, displayLng);

                if (_routeSession != null)
                    _routeSession.SyncConfigFromEchoLoader();
            }
        }

        private void ResetDisplayPositionSmoothing()
        {
            _hasSmoothDisplay = false;
            _hasAnchorPosition = false;
        }

        private void ApplySmoothedDisplayPosition(
            double targetLat,
            double targetLng,
            out double displayLat,
            out double displayLng)
        {
            if (!_hasSmoothDisplay)
            {
                _smoothDisplayLat = targetLat;
                _smoothDisplayLng = targetLng;
                _hasSmoothDisplay = true;
            }
            else
            {
                float t = gpsSmoothing > 0f
                    ? 1f - Mathf.Exp(-gpsSmoothing * Time.deltaTime)
                    : 1f;
                _smoothDisplayLat += (targetLat - _smoothDisplayLat) * t;
                _smoothDisplayLng += (targetLng - _smoothDisplayLng) * t;
            }

            displayLat = _smoothDisplayLat;
            displayLng = _smoothDisplayLng;
        }

        /// <summary>
        /// Terrain elevation order (regression-sensitive):
        /// 1. ApplyMarkerTerrainHeight — glider corridor commitment
        /// 2. UpdateTravelHeading / UpdateShipMotion — speed for corridor min-distance
        /// 3. Light Road SetMovementContext — trail runs at LightRoadExecutionOrder after this
        /// </summary>
        private void LateUpdate()
        {
            if (RtgGameSessionLogin.IsPlayBlocked()) return;

            if (_marker != null)
            {
                ApplyMarkerTerrainHeight();
                UpdateTravelHeading();
                UpdateShipMotion();
                if (_lightRoad != null)
                    _lightRoad.SetMovementContext(_travelHeadingRad, _groundSpeedMps);
            }

            UpdateCameraFollow();

            if (_marker != null)
                TickPathfinderBeam();
        }

        /// <summary>
        /// Glider ellipsoid height. REGRESSION: use GetClearancePlacementHeight only —
        /// not GetGroundHeight, raw raycast, or instant snap. See RtgTerrainElevationGuards.
        /// </summary>
        private void ApplyMarkerTerrainHeight()
        {
            if (!_hasAnchorPosition || _markerAnchor == null) return;

            double heightM = groundHeightMeters + markerHeight;
            if (_terrainHeight != null)
            {
                heightM = _terrainHeight.GetClearancePlacementHeight(
                    _anchorLat, _anchorLng, _travelHeadingRad, _groundSpeedMps);
            }

            _markerAnchor.SetPositionLongitudeLatitudeHeight(_anchorLng, _anchorLat, heightM);
        }

        private void UpdateShipMotion()
        {
            if (_shipVisual == null) return;

            _shipVisual.SetHeadingRadians(_travelHeadingRad);

            Vector3 pos = _marker.position;
            if (_hasMotionSample)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                Vector3 delta = pos - _lastMotionSamplePos;
                delta.y = 0f;
                _groundSpeedMps = delta.magnitude / dt;
            }

            _lastMotionSamplePos = pos;
            _hasMotionSample = true;

            float turnRate = 0f;
            if (Time.deltaTime > 1e-4f)
            {
                float deltaDeg = Mathf.DeltaAngle(
                    _prevTravelHeadingRad * Mathf.Rad2Deg,
                    _travelHeadingRad * Mathf.Rad2Deg);
                turnRate = deltaDeg * Mathf.Deg2Rad / Time.deltaTime;
            }

            _prevTravelHeadingRad = _travelHeadingRad;

            bool lowAngle = perspective == CameraPerspective.LowAngle;
            float mobilePlumeScale = 1f;
#if !UNITY_EDITOR
            if (Application.isMobilePlatform)
                mobilePlumeScale = Mathf.Sqrt(_zoom) * mobilePlumeVisibilityBoost;
#endif
            _shipVisual.SetPresentation(
                _camera,
                _zoom,
                minZoom,
                maxZoom,
                lowAngle,
                mobilePlumeScale);

            EnsureExhaustColorStops();
            bool exhaustTunePreview = _settingsOpen
                && markerStyle == PlayerMarkerStyle.SpaceshipSprite;
            float exhaustPreviewMph = exhaustTunePreview
                ? Mathf.Max(_exhaustColorPreviewMph, shipExhaustColorStops[1].speedMph)
                : ResolveExhaustColorHeatMph();
            float previewThrust = exhaustTunePreview
                ? Mathf.Max(ResolveShipThrust01(), 0.35f)
                : ResolveShipThrust01();
            _shipVisual.SetCavityPreview(exhaustTunePreview, exhaustPreviewMph, _exhaustColorStopEditIndex);
            _shipVisual.SetFlamePreview(
                exhaustTunePreview && _exhaustFlamePreviewEnabled,
                exhaustPreviewMph);
            _shipVisual.SetMotionState(previewThrust, turnRate, exhaustPreviewMph);
        }

        private float ResolveThrottleHeat01()
        {
            return Mathf.Clamp01(ResolveExhaustColorHeatMph() / Mathf.Max(1f, shipExhaustColorMaxMph));
        }

        /// <summary>Mph used for exhaust/cavity color (0 = deep orange, 99+ = full neon blue).</summary>
        private float ResolveExhaustColorHeatMph()
        {
            if (_activeSource == LocationSource.Manual)
                return MpsToMph(Mathf.Max(0f, _groundSpeedMps));

            return EffectiveSimulatedSpeedMph();
        }

        private float ResolveShipThrust01()
        {
            float throttle01 = Mathf.InverseLerp(
                minSpeedThrottle,
                maxSpeedThrottle,
                speedThrottle);

            float referenceSpeedMps = Mathf.Max(6f, EffectiveSimulatedSpeed());
            float speed01 = Mathf.Clamp01(_groundSpeedMps / referenceSpeedMps);
            return Mathf.Clamp01(Mathf.Max(throttle01, speed01));
        }

        private void TickPathfinderBeam()
        {
            if (_pathfinderBeam == null || _marker == null) return;
            if (!TryGetPlayerLatLng(out double lat, out double lng)) return;

            bool cockpit = _cockpitView != null && _cockpitView.IsActive;
            Transform beamAnchor = cockpit || _shipVisual == null || !_shipVisual.IsReady
                ? _marker
                : _shipVisual.transform;
            _pathfinderBeam.Tick(
                lat,
                lng,
                _travelHeadingRad,
                beamAnchor,
                _terrainHeight,
                cockpit,
                _camera);
        }

        private void EnsurePathfinderBeam()
        {
            _pathfinderBeam = GetComponent<RtgPathfinderBeam>();
            if (_pathfinderBeam == null)
                _pathfinderBeam = RtgPathfinderBeam.Ensure(this);
            SyncPathfinderBeamSettings();
        }

        /// <summary>Editor/menu: ensure beam component exists and push proxy settings.</summary>
        public void EditorApplyPathfinderBeamSettings()
        {
            EnsurePathfinderBeam();
        }

        private void SyncPathfinderBeamSettings()
        {
            if (_pathfinderBeam == null)
                _pathfinderBeam = GetComponent<RtgPathfinderBeam>();
            if (_pathfinderBeam == null) return;

            _pathfinderBeam.detectionRangeM = pathfinderDetectionRangeM;
            _pathfinderBeam.beamWidthStartM = pathfinderMapBeamWidthM;
            _pathfinderBeam.useCockpitBeam = pathfinderUseCockpitBeam;
            _pathfinderBeam.cockpitBeamWidthStartM = pathfinderCockpitBeamWidthM;
            _pathfinderBeam.cockpitBeamLengthM = pathfinderCockpitBeamLengthM;
            _pathfinderBeam.cockpitBeamGlowMultiplier = pathfinderCockpitGlowMultiplier;
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

        private void ApplyGpsSmoothingToProvider()
        {
            if (_provider is RtgDeviceLocationProvider gps)
                gps.Configure(gpsSmoothing, gpsUpdateDistanceMeters, gpsMaxSnapMeters);
        }

        private IRtgLocationProvider CreateProvider()
        {
            if (_activeSource == LocationSource.Manual)
            {
                var gps = new RtgDeviceLocationProvider();
                gps.Configure(gpsSmoothing, gpsUpdateDistanceMeters, gpsMaxSnapMeters);
                return gps;
            }

            if (routeMode == RouteMode.HomeToCasper)
            {
                RtgWaypoint[] route = _cachedDriveRoute != null && _cachedDriveRoute.Length >= 2
                    ? _cachedDriveRoute
                    : HomeToCasperRoute();
                return new RtgSimulatedLocationProvider(route, EffectiveSimulatedSpeed());
            }

            if (routeMode == RouteMode.TourNearbySites
                && _cachedSimulatedWaypoints != null
                && _cachedSimulatedWaypoints.Length >= 2)
            {
                return new RtgSimulatedLocationProvider(_cachedSimulatedWaypoints, EffectiveSimulatedSpeed());
            }

            return new RtgSimulatedLocationProvider(ResolveRoute(), EffectiveSimulatedSpeed());
        }

        private void BeginSimulatedProviderAtPin(IRtgLocationProvider provider)
        {
            if (provider is RtgSimulatedLocationProvider sim)
            {
                if (TryGetPlayerLatLng(out double lat, out double lng))
                    sim.BeginAt(lat, lng);
                else
                    sim.Begin();
            }
            else
            {
                provider.Begin();
            }
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
            if (_activeSource == LocationSource.AutoPilot && routeMode == RouteMode.HomeToCasper)
            {
                if (_routeBuildCoroutine != null)
                    StopCoroutine(_routeBuildCoroutine);
                _routeBuildCoroutine = StartCoroutine(BeginDriveRouteWhenReady());
                return;
            }

            if (_activeSource == LocationSource.AutoPilot && routeMode == RouteMode.TourNearbySites
                && (_cachedSimulatedWaypoints == null || _cachedSimulatedWaypoints.Length < 2))
            {
                if (_tourCoroutine != null)
                    StopCoroutine(_tourCoroutine);
                _tourCoroutine = StartCoroutine(BeginTourWhenSitesReady());
                return;
            }

            _provider = CreateProvider();
            BeginSimulatedProviderAtPin(_provider);
        }

        private IEnumerator BeginDriveRouteWhenReady()
        {
            double startLat = tourCenterLatitude;
            double startLng = tourCenterLongitude;
            if (TryGetPlayerLatLng(out double pinLat, out double pinLng))
            {
                startLat = pinLat;
                startLng = pinLng;
            }

            string error = null;
            RtgWaypoint[] route = null;
            yield return RtgAutopilotRouting.BuildDriveLoop(
                startLat,
                startLng,
                autopilotDestinationCity,
                built => route = built,
                err => error = err);

            if (route == null || route.Length < 2)
            {
                Debug.LogWarning(
                    $"[RTG] Drive route to \"{autopilotDestinationCity}\" failed ({error}) — " +
                    "falling back to baked Casper loop.");
                route = HomeToCasperRoute();
            }

            _cachedDriveRoute = route;
            _provider = new RtgSimulatedLocationProvider(route, EffectiveSimulatedSpeed());
            BeginSimulatedProviderAtPin(_provider);
            _routeBuildCoroutine = null;

            Debug.Log(
                $"[RTG] Auto Pilot drive to \"{autopilotDestinationCity}\" at {FormatSpeedLabel()} " +
                $"({EffectiveSimulatedSpeed():0.#} m/s), {route.Length} road points from current pin.");
        }

        private void ApplyAutopilotDestination()
        {
            autopilotDestinationCity = (_destinationDraft ?? string.Empty).Trim();
            _destinationDraft = autopilotDestinationCity;

            if (_activeSource != LocationSource.AutoPilot || routeMode != RouteMode.HomeToCasper)
                return;

            _cachedDriveRoute = null;
            _provider?.End();
            _provider = null;

            if (_routeBuildCoroutine != null)
                StopCoroutine(_routeBuildCoroutine);
            _routeBuildCoroutine = StartCoroutine(BeginDriveRouteWhenReady());
        }

        private void ToggleLocationSource()
        {
            LocationSource next = _activeSource == LocationSource.AutoPilot
                ? LocationSource.Manual
                : LocationSource.AutoPilot;
            SetLocationSource(next);
        }

        private void SetLocationSource(LocationSource newSource)
        {
            if (_activeSource == newSource && _provider != null) return;

            if (_tourCoroutine != null)
            {
                StopCoroutine(_tourCoroutine);
                _tourCoroutine = null;
            }

            if (_routeBuildCoroutine != null)
            {
                StopCoroutine(_routeBuildCoroutine);
                _routeBuildCoroutine = null;
            }

            _provider?.End();
            _provider = null;
            _activeSource = newSource;

            _routeSession?.OnLocationSourceChanged();
            SyncRouteSessionSnapMode();

            if (_lightRoad != null)
            {
                _lightRoad.ClearRoad();
                _roadStarted = false;
            }

            _hasHeadingSample = false;
            _panned = false;
            ResetDisplayPositionSmoothing();

            BeginLocationProvider();
            Debug.Log($"[RTG] Location source → {LocationSourceLabel(newSource)}.");
        }

        private static string LocationSourceLabel(LocationSource src)
        {
            return src == LocationSource.AutoPilot ? "Auto Pilot" : "Manual";
        }

        private static string ActiveModeButtonLabel(LocationSource src)
        {
            return src == LocationSource.AutoPilot ? "Mode: Auto Pilot" : "Mode: Manual";
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
            BeginSimulatedProviderAtPin(_provider);
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

        private static bool HasLegacyFlatShipMesh(Transform shipRoot)
        {
            Transform mesh = shipRoot != null ? shipRoot.Find("Mesh") : null;
            if (mesh == null) return false;

            Vector3 scale = mesh.localScale;
            return scale.y <= 2f && (scale.x >= 12f || scale.z >= 12f);
        }

        /// <summary>Rebuild pin/ship from current marker style and texture.</summary>
        public void RefreshMarkerVisual()
        {
            if (_marker == null) return;
            SyncMarkerVisual(_marker);
        }

        /// <summary>
        /// Re-establish camera follow after marker reload. Does not rebuild the ship
        /// unless the visual is missing (avoids stacking gliders on Clear Routes).
        /// </summary>
        public void RefreshAfterWorldReset()
        {
            EnsureDefaultShipArt();

            if (_cockpitView != null && _cockpitView.IsActive)
                ExitCockpit(immediate: true);

            EnsureMarker();
            if (!HasHealthyShipVisual())
                RefreshMarkerVisual();
            else
                SetShipVisible(true);

            if (!Application.isPlaying)
                return;

            EnsureTerrainHeight();
            EnsureCameraManager();

            _panned = false;
            _hasHeadingSample = false;
            _hasMotionSample = false;

            if (_marker != null)
            {
                _focusTarget = _marker.position;
                _focus = _focusTarget;
            }

            if (followWithCamera)
                SetFollowActive(true);
        }

        private bool HasHealthyShipVisual()
        {
            if (_marker == null)
                return false;

            Transform ship = _marker.Find("Ship");
            if (ship == null)
                return false;

            if (_shipVisual == null)
                _shipVisual = ship.GetComponent<RtgPlayerShipVisual>();

            return _shipVisual != null && _shipVisual.IsReady;
        }

        /// <summary>
        /// Full glider rebuild: hull prefab, tuning, marker position, exhaust, and camera follow.
        /// Used by Routes to Glory → Regenerate Playable World and world-reset flows.
        /// </summary>
        public void RegeneratePresentation()
        {
            if (RtgShipTuningConfig.TryLoad(out RtgShipTuningConfig.ShipTuningFile shipTuning))
                RtgShipTuningConfig.ApplyTo(this, shipTuning);

            if (RtgXeniteDepositTuningConfig.TryLoad(out RtgXeniteDepositTuningConfig.XeniteDepositTuningFile xeniteTuning))
                RtgXeniteDepositTuningConfig.ApplyTo(this, xeniteTuning);
            else
                RtgXeniteDepositTuningConfig.ApplyRuntimeEuler(xeniteDepositEulerOffset);

            EnsureDefaultShipArt();
            EnsureExhaustColorStops();
            EditorApplyPathfinderBeamSettings();

            EnsureMarker();
            ClearMarkerVisualChildren(_marker);
            RefreshMarkerVisual();
            EditorPlaceAtStart();

            if (_cockpitView != null && _cockpitView.IsActive)
                ExitCockpit(immediate: true);
            else
                SetShipVisible(true);

            if (!Application.isPlaying)
                return;

            EnsureTerrainHeight();
            EnsureCameraManager();
            EnsureCockpitView();
            EnsureCockpitRearCamera();
            EnsurePathfinderBeam();
            ApplyShipExhaustColors();

            _panned = false;
            _hasHeadingSample = false;
            _hasMotionSample = false;

            if (_marker != null)
            {
                _focusTarget = _marker.position;
                _focus = _focusTarget;
            }

            if (followWithCamera)
                SetFollowActive(true);
        }

        /// <summary>
        /// Editor prefers TripoModels FBX (textured). Device/player uses Resources only — see TRIPO HULL
        /// GUARDRAILS on <see cref="RtgPlayerShipVisual"/>.
        /// </summary>
        private void EnsureDefaultShipHullPrefab()
        {
#if UNITY_EDITOR
            GameObject sourceHull = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/TripoModels/futuristic_fighter_3d_model/futuristic_fighter_3d_model.fbx");
            if (RtgPlayerShipVisual.IsValidHullPrefab(sourceHull))
            {
                shipHullPrefab = sourceHull;
                return;
            }
#else
            if (shipHullPrefab != null && !RtgPlayerShipVisual.IsValidHullPrefab(shipHullPrefab))
                shipHullPrefab = null;
#endif

            GameObject resourcesHull = RtgPlayerShipVisual.LoadResourcesHullPrefab();
            if (RtgPlayerShipVisual.IsValidHullPrefab(resourcesHull))
            {
                shipHullPrefab = resourcesHull;
                return;
            }

            if (RtgPlayerShipVisual.IsValidHullPrefab(shipHullPrefab))
                return;

            shipHullPrefab = null;
#if UNITY_EDITOR
            GameObject editorHull = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/RTG_PlayerShip/TripoGlider/TripoGlider.prefab");
            if (RtgPlayerShipVisual.IsValidHullPrefab(editorHull))
            {
                shipHullPrefab = editorHull;
                return;
            }

            editorHull = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/RTG_PlayerShip/TripoGlider/futuristic_fighter_3d_model.fbx");
            if (RtgPlayerShipVisual.IsValidHullPrefab(editorHull))
            {
                shipHullPrefab = editorHull;
                return;
            }

            editorHull = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/TripoModels/futuristic_fighter_3d_model/futuristic_fighter_3d_model.fbx");
            if (RtgPlayerShipVisual.IsValidHullPrefab(editorHull))
                shipHullPrefab = editorHull;
#endif
        }

        private void EnsureDefaultShipArt()
        {
            EnsureDefaultShipHullPrefab();
            if (shipTexture == null)
                shipTexture = Resources.Load<Texture2D>(RtgPlayerShipVisual.ResourcesGliderTexturePath);
            if (cockpitTexture == null)
                cockpitTexture = Resources.Load<Texture2D>(RtgPlayerShipVisual.ResourcesCockpitTexturePath);
            if (cockpitPortraitTexture == null)
            {
                cockpitPortraitTexture = Resources.Load<Texture2D>(
                    RtgPlayerShipVisual.ResourcesCockpitPortraitTexturePath);
            }
        }

        private bool CanBuildImportedShip()
        {
            EnsureDefaultShipHullPrefab();
            if (RtgPlayerShipVisual.IsValidHullPrefab(shipHullPrefab))
                return true;
            return RtgPlayerShipVisual.HasResourcesHull();
        }

        private void SyncMarkerVisual(Transform root)
        {
            Transform beacon = root.Find("Beacon");
            Transform ship = root.Find("Ship");

            if (markerStyle == PlayerMarkerStyle.SpaceshipSprite)
            {
                ClearMarkerVisualChildren(root);
                BuildShipVisual(root);
                return;
            }

            ClearMarkerVisualChildren(root);
            BuildGoldPinVisual(root);
        }

        private void ClearMarkerVisualChildren(Transform root)
        {
            if (root == null)
                return;

            _shipVisual = null;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.name == "Ship" || child.name == "Beacon")
                    DestroyVisualImmediate(child.gameObject);
            }
        }

        private static void DestroyVisualImmediate(UnityEngine.Object obj)
        {
            if (obj == null)
                return;
            DestroyImmediate(obj);
        }

        private void EnsureLightRoad()
        {
            if (!drawLightRoad || !Application.isPlaying || _marker == null) return;
            if (_lightRoad != null) return;

            // Light Road uses DefaultExecutionOrder(LightRoadExecutionOrder) — must trail glider LateUpdate.
            var go = new GameObject("Light Road");
            go.SetActive(false);
            go.transform.SetParent(transform, false);

            RtgLightRoad road = go.AddComponent<RtgLightRoad>();
            road.target = _marker;
            road.widthMeters = roadWidth;
            road.pointSpacingMeters = roadPointSpacing;
            road.roadColor = roadColor;
            road.roadClearanceMeters = roadHeightMeters;

            go.SetActive(true);
            _lightRoad = road;
        }

        private void EnsureTerrainHeight()
        {
            if (!Application.isPlaying) return;

            if (_terrainHeight == null)
                _terrainHeight = RtgTerrainHeight.FindOrCreate();

            if (_terrainHeight == null)
            {
                Debug.LogWarning("[RTG] No Cesium3DTileset found — spaceship will use flat ground height.");
                return;
            }

            _terrainHeight.Configure(groundHeightMeters, markerHeight);
            ApplyTerrainClearanceTuning();
        }

        private void ApplyTerrainClearanceTuning()
        {
            if (_terrainHeight == null) return;

            if (RtgTerrainClearanceTuningConfig.TryLoad(
                    out RtgTerrainClearanceTuningConfig.TerrainClearanceTuningFile tuning))
            {
                RtgTerrainClearanceTuningConfig.ApplyTo(_terrainHeight, tuning);
            }
        }

        private void SaveTerrainClearanceTuning()
        {
            if (_terrainHeight == null)
                EnsureTerrainHeight();
            if (_terrainHeight == null) return;

            if (RtgTerrainClearanceTuningConfig.TrySave(
                    RtgTerrainClearanceTuningConfig.CaptureFrom(_terrainHeight),
                    out string savedPath))
            {
                Debug.Log($"[RTG] Saved terrain clearance tuning → {savedPath}");
            }
        }

        private void ReloadTerrainClearanceTuning()
        {
            if (_terrainHeight == null)
                EnsureTerrainHeight();
            if (_terrainHeight == null) return;

            if (!RtgTerrainClearanceTuningConfig.TryLoad(
                    out RtgTerrainClearanceTuningConfig.TerrainClearanceTuningFile tuning))
            {
                RtgTerrainClearanceTuningConfig.ApplyTo(
                    _terrainHeight,
                    RtgTerrainClearanceTuningConfig.Defaults());
                Debug.LogWarning(
                    $"[RTG] No {RtgTerrainClearanceTuningConfig.FileName} found — applied built-in defaults.");
                return;
            }

            RtgTerrainClearanceTuningConfig.ApplyTo(_terrainHeight, tuning);
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

        private void SyncRouteSessionSnapMode()
        {
            if (_routeSession == null) return;
            bool manual = _activeSource == LocationSource.Manual;
            bool realDrive = manual || autopilotRealDriveParity;
            _routeSession.SetMovementSnapEnabled(realDrive);
            _routeSession.SetSkipGeofenceConnect(!realDrive);
            _routeSession.SetAutopilotTestMode(!manual && !autopilotRealDriveParity);
            _routeSession.SetFabricatedGpsSpeedMps(
                manual ? 12f : EffectiveSimulatedSpeed());
        }

        private void EnsureTapToConnect()
        {
            if (!Application.isPlaying) return;
            if (GetComponent<RtgTapToConnect>() != null) return;
            gameObject.AddComponent<RtgTapToConnect>();
        }

        private void EnsureMissionProgress()
        {
            if (!Application.isPlaying) return;
            if (GetComponent<RtgMissionProgress>() != null) return;
            gameObject.AddComponent<RtgMissionProgress>();
        }

        private void EnsureCockpitView()
        {
            if (!Application.isPlaying) return;
            EnsureDefaultShipArt();
            _cockpitView = GetComponent<RtgCockpitView>();
            if (_cockpitView == null)
                _cockpitView = gameObject.AddComponent<RtgCockpitView>();
            if (cockpitTexture != null)
                _cockpitView.cockpitTexture = cockpitTexture;
            if (cockpitPortraitTexture != null)
                _cockpitView.cockpitPortraitTexture = cockpitPortraitTexture;
            _cockpitView.useGlassCanopyOverlay = true;
        }

        private void EnsureCockpitRearCamera()
        {
            if (!Application.isPlaying) return;
            _cockpitRearCamera = GetComponent<RtgCockpitRearCamera>();
            if (_cockpitRearCamera == null)
                _cockpitRearCamera = gameObject.AddComponent<RtgCockpitRearCamera>();
            _cockpitRearCamera.eyeHeightMeters = cockpitEyeHeightMeters;
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
            ClearMarkerVisualChildren(root);

            EnsureDefaultShipArt();
            Texture2D tex = ResolveShipTexture();
            if (tex == null && !CanBuildImportedShip())
            {
                Debug.LogWarning(
                    "[RTG] Ship texture and Tripo hull missing — falling back to gold pin. " +
                    "Run Routes to Glory → Regenerate Playable World before building for device.");
                BuildGoldPinVisual(root);
                return;
            }

            var shipGo = new GameObject("Ship");
            shipGo.transform.SetParent(root, false);
            _shipVisual = shipGo.AddComponent<RtgPlayerShipVisual>();

            _shipVisual.Configure(
                tex,
                shipSizeMeters,
                shipHeadingOffsetDegrees,
                shipHullPrefab,
                shipHullEulerOffset,
                shipAutoOrientImportedHull,
                new RtgGliderEngineMounts(
                    shipMainEngineLocal,
                    shipLeftEngineLocal,
                    shipRightEngineLocal),
                shipUseCustomEnginePorts,
                shipMainExhaustAnchor,
                shipLeftExhaustAnchor,
                shipRightExhaustAnchor);

            _shipVisual.ApplyCavityTuning(shipMainCavity, shipLeftCavity, shipRightCavity);

            if (RtgGliderExhaustTuningSanitizer.TrySanitizePlayerExhaust(this, _shipVisual, out _, out _))
            {
                _shipVisual.ApplyExhaustAnchors(
                    shipMainExhaustAnchor,
                    shipLeftExhaustAnchor,
                    shipRightExhaustAnchor);
                _shipVisual.ApplyCavityTuning(shipMainCavity, shipLeftCavity, shipRightCavity);
            }
            else
            {
                _shipVisual.ApplyExhaustAnchors(
                    shipMainExhaustAnchor,
                    shipLeftExhaustAnchor,
                    shipRightExhaustAnchor);
            }

            var savedMounts = new RtgGliderEngineMounts(
                shipMainEngineLocal,
                shipLeftEngineLocal,
                shipRightEngineLocal);
            if (RtgGliderEngineMounts.HasSavedPositions(savedMounts))
                _shipVisual.ApplyEngineMounts(savedMounts);

            ApplyShipExhaustColors();

            if (!_shipVisual.IsReady)
            {
                Debug.LogWarning("[RTG] Ship visual failed — falling back to gold pin.");
                DestroyImmediateSafe(shipGo);
                _shipVisual = null;
                BuildGoldPinVisual(root);
                return;
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
            return Resources.Load<Texture2D>(RtgPlayerShipVisual.ResourcesGliderTexturePath);
        }

        private void ApplyShipHullTuning()
        {
            if (_shipVisual == null)
                return;

            _shipVisual.ApplyHullTuning(shipHullEulerOffset, shipHeadingOffsetDegrees);
        }

        private void ApplyShipExhaustAnchors(bool refreshCavity = false)
        {
            if (_shipVisual == null)
                return;

            shipUseCustomEnginePorts = true;
            _shipVisual.ApplyExhaustAnchors(
                shipMainExhaustAnchor,
                shipLeftExhaustAnchor,
                shipRightExhaustAnchor);
            SyncExhaustPositionsFromShipVisual();
            if (refreshCavity)
                ApplyShipCavityTuning();
            else
                ApplyShipExhaustColors();
        }

        private void ApplySelectedEngineMount(Vector3 meshLocal, int engineIndex = -1)
        {
            int index = engineIndex >= 0 ? engineIndex : _exhaustEngineIndex;
            switch (index)
            {
                case 1:
                    shipLeftEngineLocal = meshLocal;
                    break;
                case 2:
                    shipRightEngineLocal = meshLocal;
                    break;
                default:
                    shipMainEngineLocal = meshLocal;
                    break;
            }

            shipUseCustomEnginePorts = true;
            shipEnginePortsMeshLocal = true;
            _shipVisual?.ApplySingleEngineMount(index, meshLocal);
            ApplyShipExhaustColors();
        }

        private Vector3 GetSelectedEngineMount()
        {
            return _exhaustEngineIndex switch
            {
                1 => shipLeftEngineLocal,
                2 => shipRightEngineLocal,
                _ => shipMainEngineLocal,
            };
        }

        private void SyncExhaustAnchorsFromEngineMounts()
        {
            // Anchors are legacy JSON fields; socket local positions are authoritative.
        }

        private void SyncExhaustPositionsFromShipVisual()
        {
            if (_shipVisual == null || !_shipVisual.TryGetEngineMounts(out RtgGliderEngineMounts mounts))
                return;

            shipMainEngineLocal = mounts.Main;
            shipLeftEngineLocal = mounts.Left;
            shipRightEngineLocal = mounts.Right;
            shipUseCustomEnginePorts = true;
            shipEnginePortsMeshLocal = true;
        }

        private void ApplySelectedExhaustAnchor(RtgExhaustAnchor anchor, int engineIndex = -1)
        {
            anchor = anchor.Clamped();
            int index = engineIndex >= 0 ? engineIndex : _exhaustEngineIndex;
            switch (index)
            {
                case 1:
                    shipLeftExhaustAnchor = anchor;
                    break;
                case 2:
                    shipRightExhaustAnchor = anchor;
                    break;
                default:
                    shipMainExhaustAnchor = anchor;
                    break;
            }

            shipUseCustomEnginePorts = true;
            _shipVisual?.ApplySingleExhaustAnchor(index, anchor);
            if (_shipVisual != null && _shipVisual.TryGetEngineMounts(out RtgGliderEngineMounts mounts))
            {
                switch (index)
                {
                    case 1:
                        shipLeftEngineLocal = mounts.Left;
                        break;
                    case 2:
                        shipRightEngineLocal = mounts.Right;
                        break;
                    default:
                        shipMainEngineLocal = mounts.Main;
                        break;
                }
            }

            ApplyShipExhaustColors();
        }

        private void SyncExhaustAnchorsFromShipVisual()
        {
            SyncExhaustPositionsFromShipVisual();
        }

        private void ApplyShipExhaustColors()
        {
            if (_shipVisual == null)
                return;

            EnsureExhaustColorStops();
            _shipVisual.ApplyExhaustColorProfile(shipExhaustColorStops, shipExhaustColorMaxMph);

            bool exhaustTunePreview = _settingsOpen
                && markerStyle == PlayerMarkerStyle.SpaceshipSprite;
            if (exhaustTunePreview)
            {
                _shipVisual.SetCavityPreview(
                    true,
                    _exhaustColorPreviewMph,
                    _exhaustColorStopEditIndex);
                _shipVisual.SetFlamePreview(_exhaustFlamePreviewEnabled, _exhaustColorPreviewMph);
            }
        }

        private void EnsureExhaustColorStops()
        {
            shipExhaustColorMaxMph = Mathf.Clamp(shipExhaustColorMaxMph, 1f, 200f);
            shipExhaustColorStops = RtgExhaustColorProfile.NormalizeStops(
                shipExhaustColorStops,
                shipExhaustColorMaxMph);
        }

        private void ApplyShipCavityTuning()
        {
            if (_shipVisual == null)
                return;

            shipMainCavity = shipMainCavity.Clamped();
            shipLeftCavity = shipLeftCavity.Clamped();
            shipRightCavity = shipRightCavity.Clamped();
            _exhaustFlamePreviewEnabled = true;
            _shipVisual.ApplyCavityTuning(shipMainCavity, shipLeftCavity, shipRightCavity);
            if (_settingsOpen)
            {
                _shipVisual.SetCavityPreview(true, _exhaustColorPreviewMph, _exhaustColorStopEditIndex);
                _shipVisual.SetFlamePreview(true, _exhaustColorPreviewMph);
            }
        }

        private RtgEngineCavityTuning GetSelectedCavityTuning()
        {
            return _exhaustEngineIndex switch
            {
                1 => shipLeftCavity,
                2 => shipRightCavity,
                _ => shipMainCavity,
            };
        }

        private void SetSelectedCavityTuning(RtgEngineCavityTuning tuning)
        {
            switch (_exhaustEngineIndex)
            {
                case 1:
                    shipLeftCavity = tuning;
                    break;
                case 2:
                    shipRightCavity = tuning;
                    break;
                default:
                    shipMainCavity = tuning;
                    break;
            }
        }

        private void SaveShipHullTuning()
        {
            SyncExhaustAnchorsFromShipVisual();
            if (RtgShipTuningConfig.TrySave(RtgShipTuningConfig.CaptureFrom(this), out string savedPath))
            {
                Debug.Log(
                    $"[RTG] Saved exhaust — main={shipMainEngineLocal} left={shipLeftEngineLocal} " +
                    $"right={shipRightEngineLocal} → {savedPath}");
            }
        }

        private void ReloadShipHullTuning()
        {
            if (!RtgShipTuningConfig.TryLoad(out RtgShipTuningConfig.ShipTuningFile tuning))
            {
                Debug.LogWarning("[RTG] No rtg-ship-tuning.json found to reload.");
                return;
            }

            RtgShipTuningConfig.ApplyTo(this, tuning);
            RefreshMarkerVisual();
            ApplyShipExhaustColors();
        }

        private void SaveXeniteDepositTuning()
        {
            if (RtgXeniteDepositTuningConfig.TrySave(
                    RtgXeniteDepositTuningConfig.CaptureFrom(this),
                    out string savedPath))
            {
                Debug.Log(
                    $"[RTG] Saved xenite deposit euler={xeniteDepositEulerOffset} → {savedPath}");
            }
        }

        private void ReloadXeniteDepositTuning()
        {
            if (!RtgXeniteDepositTuningConfig.TryLoad(
                    out RtgXeniteDepositTuningConfig.XeniteDepositTuningFile tuning))
            {
                Debug.LogWarning("[RTG] No rtg-xenite-deposit-tuning.json found to reload.");
                return;
            }

            RtgXeniteDepositTuningConfig.ApplyTo(this, tuning);
            ApplyXeniteDepositTuning();
        }

        private void ApplyXeniteDepositTuning()
        {
            RtgXeniteDepositTuningConfig.ApplyRuntimeEuler(xeniteDepositEulerOffset);
#if UNITY_2023_1_OR_NEWER
            RtgEchoSiteLoader loader = Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
            RtgEchoSiteLoader loader = Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
            loader?.RefreshResourceDepositsOnly();
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

        public bool IsAutoPilotActive => _activeSource == LocationSource.AutoPilot;

        /// <summary>True when screenPos (bottom-left origin) is over an in-game IMGUI control.</summary>
        public bool IsScreenPointOverGameUi(Vector2 screenPos)
        {
            Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (Rect rect in _gameUiRects)
            {
                if (rect.Contains(guiPos))
                    return true;
            }

            var missions = GetComponent<RtgMissionProgress>();
            if (missions != null && missions.IsGuiPointOverHud(guiPos))
                return true;

            return false;
        }

        /// <summary>Allow other IMGUI systems (missions HUD) to block map taps.</summary>
        public void RegisterExternalGameUiRect(Rect rect)
        {
            RegisterGameUiRect(rect);
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

        private void EnsureCameraManager()
        {
            if (!Application.isPlaying) return;

            _cameraManager = GetComponent<RtgCameraManager>();
            if (_cameraManager == null)
                _cameraManager = gameObject.AddComponent<RtgCameraManager>();

            _cameraManager.EnsureInitialized(_marker);
            _camera = _cameraManager.CesiumCamera;
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
            if (_marker == null) return;
            if (_cameraManager == null)
                EnsureCameraManager();
            if (_cameraManager == null) return;

            _camera = _cameraManager.ActiveGameplayCamera ?? _cameraManager.CesiumCamera;
            if (_camera == null) return;

            UpdateZoom();
            SmoothZoom();
            TryCompleteCockpitEntry();

            bool cockpitCamera = IsCockpitCameraActive();

            if (cockpitCamera)
            {
                _cameraManager.SetMode(RtgCameraManager.CameraMode.Cockpit);
                _cameraManager.SetGameplayCameraOwnership(true);

                _panned = false;
                _focusTarget = _marker.position;
                _focus = _marker.position;

                ApplyCockpitCamera();
                TickCockpitRearCamera();
                return;
            }

            bool blockMapPan = BlocksMapPan();
            HandlePanInput(blockMapPan);

            _cameraManager.SetMode(RtgCameraManager.CameraMode.Chase);

            if (!followWithCamera)
            {
                if (_followActive) SetFollowActive(false);
                return;
            }

            if (!_followActive) SetFollowActive(true);

            if (!_panned) _focusTarget = _marker.position;

            if (followSmoothing > 0f)
            {
                float t = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
                _focus = Vector3.Lerp(_focus, _focusTarget, t);
            }
            else
            {
                _focus = _focusTarget;
            }

            _cameraManager.ApplyChaseLookAt(DesiredCameraPosition(_focus), _focus);
        }

        private bool IsCockpitCameraActive()
        {
            if (_cockpitView == null)
                return false;

            return _cockpitView.IsActive || _cockpitView.Blend > 0.02f;
        }

        private bool BlocksMapPan()
        {
            return _cockpitEntryPending || IsCockpitCameraActive();
        }

        private void HandlePanInput(bool blockMapPan = false)
        {
            if (IsCockpitCameraActive() || blockMapPan || BlocksMapPan())
            {
                _wasPointerDown = false;
                return;
            }

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

        private readonly struct RightButtonLayout
        {
            public readonly Rect ZoomIn;
            public readonly Rect ZoomOut;
            public readonly Rect View;
            public readonly Rect Cockpit;
            public readonly Rect Center;
            public readonly bool HasCenter;

            public RightButtonLayout(Rect zoomIn, Rect zoomOut, Rect view, Rect cockpit, Rect center, bool hasCenter)
            {
                ZoomIn = zoomIn;
                ZoomOut = zoomOut;
                View = view;
                Cockpit = cockpit;
                Center = center;
                HasCenter = hasCenter;
            }
        }

        private RightButtonLayout LayoutRightButtons(
            float margin, float gap, float zoomW, float zoomH, float wideW, float wideH)
        {
            float right = Screen.width - zoomW - margin;
            float wideRight = Screen.width - wideW - margin;
            bool hasCenter = _panned;
            int wideCount = 2 + (hasCenter ? 1 : 0);
            int itemCount = wideCount + 2;
            float stackH = wideCount * wideH + zoomH * 2f + (itemCount - 1) * gap;

            float stackTop;
            if (Screen.width > Screen.height)
            {
                stackTop = (Screen.height - stackH) * 0.5f;
                stackTop = Mathf.Clamp(stackTop, margin, Mathf.Max(margin, Screen.height - stackH - margin));
            }
            else
            {
                float midY = Screen.height * 0.5f;
                stackTop = midY - zoomH - gap * 0.5f - wideCount * (wideH + gap);
            }

            float y = stackTop;
            Rect center = default;
            if (hasCenter)
            {
                center = new Rect(wideRight, y, wideW, wideH);
                y += wideH + gap;
            }

            var cockpit = new Rect(wideRight, y, wideW, wideH);
            y += wideH + gap;
            var view = new Rect(wideRight, y, wideW, wideH);
            y += wideH + gap;
            var zoomIn = new Rect(right, y, zoomW, zoomH);
            y += zoomH + gap;
            var zoomOut = new Rect(right, y, zoomW, zoomH);

            return new RightButtonLayout(zoomIn, zoomOut, view, cockpit, center, hasCenter);
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (RtgGameSessionLogin.IsPlayBlocked()) return;

            _gameUiRects.Clear();

            const float margin = 24f;
            const float gap = 12f;
            const float zoomW = 144f;
            const float zoomH = 92f;
            const float wideW = 280f;
            const float wideH = 92f;

            float right = Screen.width - zoomW - margin;
            float midY = Screen.height * 0.5f;
            RightButtonLayout rightButtons = LayoutRightButtons(margin, gap, zoomW, zoomH, wideW, wideH);

            _cockpitView?.DrawOverlay();

            bool inCockpit = _cockpitView != null && _cockpitView.IsActive;
            if (inCockpit && cockpitLookDebugHud && _marker != null)
                DrawCockpitLookDebugHud();

            if (inCockpit && _cockpitRearCamera != null)
            {
                _cockpitRearCamera.DrawInset(_cockpitView, _cockpitView.Blend);
                if (_cockpitRearCamera.LastScreenRect.width > 1f)
                    _gameUiRects.Add(_cockpitRearCamera.LastScreenRect);
            }

            DrawMovementControls();

            if (_activeSource == LocationSource.AutoPilot)
                DrawAutopilotTestControls();

            var prev = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = 28;

            if (followWithCamera)
            {
                if (rightButtons.HasCenter && GUI.Button(rightButtons.Center, "Center"))
                    RecenterOnPlayer();

                string viewLabel = perspective == CameraPerspective.Map ? "Route View" : "Map View";
                if (GUI.Button(rightButtons.View, viewLabel))
                    TogglePerspective();

                string cockpitLabel = inCockpit ? "Exit Cockpit" : "Cockpit";
                if (GUI.Button(rightButtons.Cockpit, cockpitLabel))
                    RequestCockpit(fastZoom: true);

                if (GUI.Button(rightButtons.ZoomIn, "+"))
                    ApplyZoomInButton();
                if (GUI.Button(rightButtons.ZoomOut, "−"))
                    ApplyZoomOutButton();

                if (rightButtons.HasCenter) _gameUiRects.Add(rightButtons.Center);
                _gameUiRects.Add(rightButtons.View);
                _gameUiRects.Add(rightButtons.Cockpit);
                _gameUiRects.Add(rightButtons.ZoomIn);
                _gameUiRects.Add(rightButtons.ZoomOut);
            }

            const float infoSize = 92f;
            var infoRect = new Rect(margin, Screen.height - margin - infoSize, infoSize, infoSize);
            if (GUI.Button(infoRect, _creditsToggle != null && _creditsToggle.IsVisible ? "×" : "i"))
                _creditsToggle?.Toggle();
            _gameUiRects.Add(infoRect);

            GUI.skin.button.fontSize = prev;

            DrawSettingsGearAndPanel(margin, midY);

            if (_activeSource == LocationSource.AutoPilot)
            {
                if (inCockpit
                    && _cockpitView.TryMapAnchorToScreen(
                        RtgCockpitView.JoystickAnchor(Screen.height > Screen.width),
                        out Rect joystickRect))
                {
                    DrawThrottleLever(joystickRect, cockpitAnchored: true);
                }
                else
                {
                    float throttleTop = followWithCamera
                        ? rightButtons.ZoomOut.yMax + gap
                        : midY + zoomH + gap;
                    DrawThrottleLever(new Rect(right, throttleTop, zoomW, 0f), cockpitAnchored: false);
                }
            }
        }

        private Rect ToScreenGuiRect(Rect contentRect)
        {
            if (!_activeSettingsScrollViewport.HasValue)
                return contentRect;

            Rect viewport = _activeSettingsScrollViewport.Value;
            return new Rect(
                viewport.x + contentRect.x,
                viewport.y + contentRect.y - _settingsScrollPosition.y,
                contentRect.width,
                contentRect.height);
        }

        private void RegisterGameUiRect(Rect rect)
        {
            _gameUiRects.Add(ToScreenGuiRect(rect));
        }

        private void DrawSettingsGearAndPanel(float margin, float midY)
        {
            const float scale = 2f;
            const float gearSize = 72f * scale;
            var gearRect = new Rect(margin, midY - gearSize * 0.5f, gearSize, gearSize);

            // Exit session — sits just above Gear so testers can return to PIN / session UI.
            const float exitGap = 10f * scale;
            var exitRect = new Rect(margin, gearRect.y - gearSize - exitGap, gearSize, gearSize);
            var prevFont = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = Mathf.RoundToInt(22f * scale);
            if (GUI.Button(exitRect, "Exit"))
            {
                var login = RtgGameSessionLogin.FindActive();
                if (login != null)
                    login.ExitSessionToLogin();
                else
                    Debug.LogWarning("[RTG] Exit pressed but RtgGameSessionLogin is missing.");
            }
            _gameUiRects.Add(exitRect);

            GUI.skin.button.fontSize = Mathf.RoundToInt(34f * scale);
            if (GUI.Button(gearRect, GUIContent.none))
                _settingsOpen = !_settingsOpen;
            _gameUiRects.Add(gearRect);

            if (_settingsOpen)
                DrawCloseGlyph(gearRect);
            else
                DrawGearGlyph(gearRect);

            GUI.skin.button.fontSize = prevFont;

            if (!_settingsOpen)
                return;

            bool inCockpit = _cockpitView != null && _cockpitView.IsActive;
            bool showPitch = inCockpit;
            const bool showGps = true;
            bool showAutopilotParity = _activeSource == LocationSource.AutoPilot;
            bool showDeviceGpsTuning = _activeSource == LocationSource.Manual;
            bool showHullTuning = markerStyle == PlayerMarkerStyle.SpaceshipSprite;
            bool showTerrainClearance = Application.isPlaying;
            bool showXeniteTuning = Application.isPlaying;
            bool showMissionDev = Application.isPlaying;

            const float panelWidthDesired = 300f * scale;
            const float rowH = 56f * scale;
            const float pitchH = 240f * scale;
            const float headerH = 34f * scale;
            const float pad = 12f * scale;
            const float destFieldH = 44f * scale;
            const float hullSectionHeaderH = 28f * scale;
            const float hullButtonRowH = 44f * scale;

            float scrollContentH = pad;
            if (showPitch) scrollContentH += pitchH + pad;
            if (showAutopilotParity) scrollContentH += rowH * 2f + destFieldH + rowH + pad;
            if (showGps) scrollContentH += rowH * (showDeviceGpsTuning ? 3f : 1f) + pad;
            if (showTerrainClearance)
                scrollContentH += CalculateTerrainClearanceScrollHeight(
                    rowH, hullSectionHeaderH, hullButtonRowH, pad);
            if (showXeniteTuning)
                scrollContentH += CalculateXeniteDepositTuningScrollHeight(
                    rowH, hullSectionHeaderH, hullButtonRowH, pad);
            if (showMissionDev)
                scrollContentH += pad * 0.5f + hullSectionHeaderH + hullButtonRowH + pad;
            if (showHullTuning)
                scrollContentH += CalculateHullTuningScrollHeight(rowH, hullSectionHeaderH, hullButtonRowH, scale, pad);

            float maxPanelH = Mathf.Min(Screen.height * 0.82f, Screen.height - margin * 2f);
            float idealPanelH = headerH + scrollContentH;
            float visiblePanelH = Mathf.Min(idealPanelH, maxPanelH);
            float scrollViewH = Mathf.Max(48f, visiblePanelH - headerH);

            float panelX = gearRect.xMax + 8f * scale;
            float panelWidth = Mathf.Min(panelWidthDesired, Screen.width - panelX - margin);
            if (panelX + panelWidth > Screen.width - margin)
                panelX = Mathf.Max(margin, Screen.width - margin - panelWidth);
            float panelY = Mathf.Clamp(midY - visiblePanelH * 0.5f, margin, Screen.height - margin - visiblePanelH);
            var panelRect = new Rect(panelX, panelY, panelWidth, visiblePanelH);
            _gameUiRects.Add(panelRect);

            Color prevColor = GUI.color;
            GUI.color = new Color(0.06f, 0.09f, 0.18f, 0.92f);
            GUI.Box(panelRect, GUIContent.none);
            GUI.color = prevColor;

            var titleStyle = BrightLabel(Mathf.RoundToInt(16f * scale), new Color(0.97f, 0.99f, 1f), FontStyle.Bold);
            GUI.Label(
                new Rect(panelRect.x + pad, panelRect.y + 8f * scale, panelRect.width - pad * 2f, 22f * scale),
                "Settings",
                titleStyle);

            var scrollViewport = new Rect(panelRect.x, panelRect.y + headerH, panelWidth, scrollViewH);
            _activeSettingsScrollViewport = scrollViewport;
            _settingsScrollPosition = GUI.BeginScrollView(
                scrollViewport,
                _settingsScrollPosition,
                new Rect(0, 0, panelWidth, scrollContentH),
                false,
                true);

            float innerW = panelWidth - pad * 2f;
            var contentPanel = new Rect(pad, 0f, innerW, scrollContentH);
            float y = pad * 0.5f;

            if (showPitch)
            {
                DrawPitchLever(contentPanel.x, y, contentPanel.width, pitchH, scale);
                y += pitchH;
            }

            if (showAutopilotParity)
            {
                y += pad * 0.5f;
                bool newParity = DrawSettingToggle(
                    new Rect(contentPanel.x, y, contentPanel.width, rowH),
                    "Real drive parity",
                    autopilotRealDriveParity,
                    scale);
                y += rowH;
                if (newParity != autopilotRealDriveParity)
                {
                    autopilotRealDriveParity = newParity;
                    SyncRouteSessionSnapMode();
                    Debug.Log($"[RTG] Auto Pilot real drive parity → {(newParity ? "ON" : "OFF")}");
                }

                _destinationDraft = DrawSettingTextField(
                    new Rect(contentPanel.x, y, contentPanel.width, destFieldH),
                    "Destination (City, ST)",
                    _destinationDraft ?? autopilotDestinationCity,
                    scale);
                y += destFieldH;

                float applyW = 132f * scale;
                var applyRect = new Rect(contentPanel.x, y, applyW, rowH - 8f * scale);
                var hintRect = new Rect(applyRect.xMax + 8f * scale, y, contentPanel.width - applyW - 8f * scale, rowH - 8f * scale);
                RegisterGameUiRect(applyRect);
                RegisterGameUiRect(hintRect);

                var prevBtn = GUI.skin.button.fontSize;
                GUI.skin.button.fontSize = Mathf.RoundToInt(14f * scale);
                if (GUI.Button(applyRect, "Apply route"))
                    ApplyAutopilotDestination();
                GUI.skin.button.fontSize = prevBtn;

                var hintStyle = BrightLabel(Mathf.RoundToInt(12f * scale), new Color(0.82f, 0.9f, 0.98f));
                GUI.Label(hintRect, "HomeToCasper mode · city+state OK", hintStyle);
                y += rowH;
            }

            if (showGps)
            {
                y += pad * 0.5f;
                float newSmoothing = DrawSettingSlider(
                    new Rect(contentPanel.x, y, contentPanel.width, rowH),
                    "Position smooth",
                    gpsSmoothing,
                    2f,
                    24f,
                    scale: scale);
                y += rowH;

                if (showDeviceGpsTuning)
                {
                    float newUpdateDistance = DrawSettingSlider(
                        new Rect(contentPanel.x, y, contentPanel.width, rowH),
                        "GPS update (m)",
                        gpsUpdateDistanceMeters,
                        0.5f,
                        10f,
                        scale: scale);
                    y += rowH;
                    float newMaxSnap = DrawSettingSlider(
                        new Rect(contentPanel.x, y, contentPanel.width, rowH),
                        "GPS max snap (m)",
                        gpsMaxSnapMeters,
                        30f,
                        300f,
                        "0",
                        scale: scale);

                    if (!Mathf.Approximately(newSmoothing, gpsSmoothing)
                        || !Mathf.Approximately(newUpdateDistance, gpsUpdateDistanceMeters)
                        || !Mathf.Approximately(newMaxSnap, gpsMaxSnapMeters))
                    {
                        gpsSmoothing = newSmoothing;
                        gpsUpdateDistanceMeters = newUpdateDistance;
                        gpsMaxSnapMeters = newMaxSnap;
                        ApplyGpsSmoothingToProvider();
                    }
                }
                else if (!Mathf.Approximately(newSmoothing, gpsSmoothing))
                {
                    gpsSmoothing = newSmoothing;
                }
            }

            if (showTerrainClearance)
            {
                y += pad * 0.5f;
                y = DrawTerrainClearanceSection(
                    contentPanel, y, rowH, hullSectionHeaderH, hullButtonRowH, scale);
            }

            if (showXeniteTuning)
            {
                y += pad * 0.5f;
                y = DrawXeniteDepositTuningSection(
                    contentPanel, y, rowH, hullSectionHeaderH, hullButtonRowH, scale);
            }

            if (showMissionDev)
            {
                y += pad * 0.5f;
                y = DrawMissionDevSection(contentPanel, y, hullSectionHeaderH, hullButtonRowH, scale);
            }

            if (showHullTuning)
            {
                y += pad * 0.5f;
                y = DrawHullTuningSection(contentPanel, y, rowH, hullSectionHeaderH, hullButtonRowH, scale);
                y = DrawExhaustColorSection(contentPanel, y, rowH, hullSectionHeaderH, hullButtonRowH, scale);
                y = DrawEnginePortTuningSection(contentPanel, y, rowH, hullSectionHeaderH, hullButtonRowH, scale);
            }

            GUI.EndScrollView();
            _activeSettingsScrollViewport = null;
        }

        private static float CalculateTerrainClearanceScrollHeight(
            float rowH,
            float sectionHeaderH,
            float buttonRowH,
            float pad)
        {
            return pad * 0.5f + sectionHeaderH + rowH * 10f + buttonRowH + pad;
        }

        private float DrawTerrainClearanceSection(
            Rect panelRect,
            float y,
            float rowH,
            float sectionHeaderH,
            float buttonRowH,
            float scale)
        {
            if (_terrainHeight == null)
                EnsureTerrainHeight();
            if (_terrainHeight == null)
                return y;

            var sectionStyle = BrightLabel(
                Mathf.RoundToInt(14f * scale),
                new Color(0.88f, 0.94f, 1f),
                FontStyle.Bold);
            GUI.Label(
                new Rect(panelRect.x, y, panelRect.width, sectionHeaderH),
                "Terrain clearance (corridor)",
                sectionStyle);
            // REGRESSION: sliders map to RtgTerrainHeight corridor fields — glider only.
            // Light Road height is governed by RtgTerrainElevationGuards / RtgLightRoad.
            y += sectionHeaderH;

            _terrainHeight.corridorSampleSpacingM = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Sample spacing (m)",
                _terrainHeight.corridorSampleSpacingM,
                4f,
                30f,
                "0",
                scale);
            y += rowH;

            _terrainHeight.corridorLookAheadM = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Look-ahead (m)",
                _terrainHeight.corridorLookAheadM,
                24f,
                120f,
                "0",
                scale);
            y += rowH;

            _terrainHeight.consistencyBandM = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Flat band (m)",
                _terrainHeight.consistencyBandM,
                0.3f,
                4f,
                "0.00",
                scale);
            y += rowH;

            _terrainHeight.minConsistentDistanceSlowM = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Min distance slow (m)",
                _terrainHeight.minConsistentDistanceSlowM,
                12f,
                80f,
                "0",
                scale);
            y += rowH;

            _terrainHeight.minConsistentDistanceFastM = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Min distance fast (m)",
                _terrainHeight.minConsistentDistanceFastM,
                8f,
                60f,
                "0",
                scale);
            y += rowH;

            _terrainHeight.consistencyFullSpeedMps = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Full-speed (m/s)",
                _terrainHeight.consistencyFullSpeedMps,
                5f,
                60f,
                "0",
                scale);
            y += rowH;

            _terrainHeight.minLevelChangeM = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Min level change (m)",
                _terrainHeight.minLevelChangeM,
                0.25f,
                6f,
                "0.00",
                scale);
            y += rowH;

            _terrainHeight.committedBlendUpSeconds = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Blend up (s)",
                _terrainHeight.committedBlendUpSeconds,
                0.05f,
                2f,
                "0.00",
                scale);
            y += rowH;

            _terrainHeight.committedBlendDownSeconds = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Blend down (s)",
                _terrainHeight.committedBlendDownSeconds,
                0.05f,
                2f,
                "0.00",
                scale);
            y += rowH;

            bool raycastFallback = DrawSettingToggle(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Raycast fallback",
                _terrainHeight.useRaycastFallback,
                scale);
            if (raycastFallback != _terrainHeight.useRaycastFallback)
                _terrainHeight.useRaycastFallback = raycastFallback;
            y += rowH;

            float buttonW = (panelRect.width - 8f * scale) * 0.5f;
            var saveRect = new Rect(panelRect.x, y, buttonW, buttonRowH - 8f * scale);
            var reloadRect = new Rect(saveRect.xMax + 8f * scale, y, buttonW, buttonRowH - 8f * scale);
            RegisterGameUiRect(saveRect);
            RegisterGameUiRect(reloadRect);

            var prevBtn = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = Mathf.RoundToInt(14f * scale);
            if (GUI.Button(saveRect, "Save tuning"))
                SaveTerrainClearanceTuning();
            if (GUI.Button(reloadRect, "Reload"))
                ReloadTerrainClearanceTuning();
            GUI.skin.button.fontSize = prevBtn;
            y += buttonRowH;

            return y;
        }

        private float DrawMissionDevSection(
            Rect panelRect,
            float y,
            float sectionHeaderH,
            float buttonRowH,
            float scale)
        {
            var sectionStyle = BrightLabel(
                Mathf.RoundToInt(14f * scale),
                new Color(0.88f, 0.94f, 1f),
                FontStyle.Bold);
            GUI.Label(
                new Rect(panelRect.x, y, panelRect.width, sectionHeaderH),
                "Missions (dev)",
                sectionStyle);
            y += sectionHeaderH;

            var missions = GetComponent<RtgMissionProgress>();
            bool canAccelerate = missions != null && missions.IsMissionCActive;

            float buttonW = (panelRect.width - 8f * scale) * 0.5f;
            var nearRect = new Rect(panelRect.x, y, buttonW, buttonRowH - 8f * scale);
            var finishRect = new Rect(nearRect.xMax + 8f * scale, y, buttonW, buttonRowH - 8f * scale);
            RegisterGameUiRect(nearRect);
            RegisterGameUiRect(finishRect);

            var prevBtn = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = Mathf.RoundToInt(12f * scale);
            GUI.enabled = canAccelerate;
            if (GUI.Button(nearRect, "C → ~60s"))
                missions.AccelerateMissionCNear();
            if (GUI.Button(finishRect, "Skip C"))
                missions.AccelerateMissionCFinish();
            GUI.enabled = true;
            GUI.skin.button.fontSize = prevBtn;
            y += buttonRowH;

            return y;
        }

        private static float CalculateXeniteDepositTuningScrollHeight(
            float rowH,
            float sectionHeaderH,
            float buttonRowH,
            float pad)
        {
            return pad * 0.5f + sectionHeaderH + rowH * 3f + buttonRowH + pad;
        }

        private float DrawXeniteDepositTuningSection(
            Rect panelRect,
            float y,
            float rowH,
            float sectionHeaderH,
            float buttonRowH,
            float scale)
        {
            var sectionStyle = BrightLabel(
                Mathf.RoundToInt(14f * scale),
                new Color(0.88f, 0.94f, 1f),
                FontStyle.Bold);
            GUI.Label(
                new Rect(panelRect.x, y, panelRect.width, sectionHeaderH),
                "Xenite deposit",
                sectionStyle);
            y += sectionHeaderH;

            Vector3 previousEuler = xeniteDepositEulerOffset;

            float pitchX = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Deposit pitch X",
                xeniteDepositEulerOffset.x,
                -180f,
                180f,
                "0",
                scale);
            y += rowH;

            float yawY = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Deposit yaw Y",
                xeniteDepositEulerOffset.y,
                -180f,
                180f,
                "0",
                scale);
            y += rowH;

            float rollZ = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Deposit roll Z",
                xeniteDepositEulerOffset.z,
                -180f,
                180f,
                "0",
                scale);
            y += rowH;

            float buttonW = (panelRect.width - 8f * scale) * 0.5f;
            var saveRect = new Rect(panelRect.x, y, buttonW, buttonRowH - 8f * scale);
            var reloadRect = new Rect(saveRect.xMax + 8f * scale, y, buttonW, buttonRowH - 8f * scale);
            RegisterGameUiRect(saveRect);
            RegisterGameUiRect(reloadRect);

            var prevBtn = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = Mathf.RoundToInt(14f * scale);
            if (GUI.Button(saveRect, "Save tuning"))
                SaveXeniteDepositTuning();
            if (GUI.Button(reloadRect, "Reload"))
                ReloadXeniteDepositTuning();
            GUI.skin.button.fontSize = prevBtn;
            y += buttonRowH;

            Vector3 newEuler = new Vector3(pitchX, yawY, rollZ);
            if (newEuler != previousEuler)
            {
                xeniteDepositEulerOffset = newEuler;
                ApplyXeniteDepositTuning();
            }

            return y;
        }

        private static float CalculateHullTuningScrollHeight(
            float rowH,
            float sectionHeaderH,
            float buttonRowH,
            float scale,
            float pad)
        {
            float hull = sectionHeaderH + rowH * 5f + buttonRowH;
            float exhaust = sectionHeaderH + rowH * 8f + buttonRowH * 3f + 40f * scale;
            float engine = sectionHeaderH * 2f + 88f * scale + rowH * 9f + buttonRowH * 3f;
            return pad * 0.5f + hull + exhaust + engine + pad;
        }

        private float DrawExhaustColorSection(
            Rect panelRect,
            float y,
            float rowH,
            float sectionHeaderH,
            float buttonRowH,
            float scale)
        {
            EnsureExhaustColorStops();
            _exhaustColorStopEditIndex = Mathf.Clamp(
                _exhaustColorStopEditIndex,
                0,
                RtgExhaustColorProfile.StopCount - 1);
            _exhaustColorChannelIndex = Mathf.Clamp(_exhaustColorChannelIndex, 0, 3);

            if (_exhaustColorLastSyncedStopIndex != _exhaustColorStopEditIndex)
            {
                _exhaustColorLastSyncedStopIndex = _exhaustColorStopEditIndex;
                _exhaustColorPreviewMph = shipExhaustColorStops[_exhaustColorStopEditIndex].speedMph;
            }

            var sectionStyle = BrightLabel(Mathf.RoundToInt(14f * scale), new Color(0.88f, 0.94f, 1f), FontStyle.Bold);
            GUI.Label(
                new Rect(panelRect.x, y, panelRect.width, sectionHeaderH),
                "Exhaust colors",
                sectionStyle);
            y += sectionHeaderH;

            float previousMaxMph = shipExhaustColorMaxMph;
            float maxMph = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Color max mph",
                shipExhaustColorMaxMph,
                20f,
                120f,
                "0",
                scale);
            y += rowH;

            float btnW = (panelRect.width - 8f * scale) / RtgExhaustColorProfile.StopCount;
            var prevBtn = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = Mathf.RoundToInt(12f * scale);
            for (int i = 0; i < RtgExhaustColorProfile.StopCount; i++)
            {
                var btn = new Rect(panelRect.x + i * (btnW + 2f * scale), y, btnW, buttonRowH - 8f * scale);
                RegisterGameUiRect(btn);
                string label = _exhaustColorStopEditIndex == i
                    ? $"{shipExhaustColorStops[i].speedMph:0}*"
                    : $"{shipExhaustColorStops[i].speedMph:0}";
                if (GUI.Button(btn, label))
                {
                    GUIUtility.hotControl = 0;
                    _activeSettingSliderHint = -1;
                    _exhaustColorStopEditIndex = i;
                    _exhaustColorLastSyncedStopIndex = i;
                    _exhaustColorPreviewMph = shipExhaustColorStops[i].speedMph;
                    ApplyShipExhaustColors();
                }
            }

            GUI.skin.button.fontSize = prevBtn;
            y += buttonRowH;

            float previousPreviewMph = _exhaustColorPreviewMph;
            float previewMph = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Preview mph",
                _exhaustColorPreviewMph,
                0f,
                maxMph,
                "0",
                scale,
                controlName: "ExhaustPreviewMph");
            y += rowH;
            if (!Mathf.Approximately(previewMph, previousPreviewMph))
            {
                _exhaustColorPreviewMph = previewMph;
                ApplyShipExhaustColors();
            }

            var flameToggleBtn = new Rect(panelRect.x, y, panelRect.width, buttonRowH - 8f * scale);
            RegisterGameUiRect(flameToggleBtn);
            GUI.skin.button.fontSize = Mathf.RoundToInt(12f * scale);
            string flameToggleLabel = _exhaustFlamePreviewEnabled
                ? "Show plume (manual): ON"
                : "Show plume (manual): OFF";
            if (GUI.Button(flameToggleBtn, flameToggleLabel))
                _exhaustFlamePreviewEnabled = !_exhaustFlamePreviewEnabled;

            GUI.skin.button.fontSize = prevBtn;
            y += buttonRowH;

            string[] channelLabels = { "Cavity outer", "Cavity core", "Plume body", "Plume halo" };
            float channelBtnW = (panelRect.width - 6f * scale) / channelLabels.Length;
            GUI.skin.button.fontSize = Mathf.RoundToInt(11f * scale);
            for (int i = 0; i < channelLabels.Length; i++)
            {
                var btn = new Rect(panelRect.x + i * (channelBtnW + 2f * scale), y, channelBtnW, buttonRowH - 8f * scale);
                RegisterGameUiRect(btn);
                string label = _exhaustColorChannelIndex == i
                    ? $"{channelLabels[i]} *"
                    : channelLabels[i];
                if (GUI.Button(btn, label))
                {
                    GUIUtility.hotControl = 0;
                    _activeSettingSliderHint = -1;
                    _exhaustColorChannelIndex = i;
                }
            }

            GUI.skin.button.fontSize = prevBtn;
            y += buttonRowH;

            RtgExhaustColorStop storedStop = shipExhaustColorStops[_exhaustColorStopEditIndex];
            Color storedColor = GetExhaustColorChannel(storedStop, _exhaustColorChannelIndex);
            int stopHint = _exhaustColorStopEditIndex;
            int channelHint = _exhaustColorChannelIndex;

            float stopSpeed = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                $"Stop {_exhaustColorStopEditIndex + 1} speed (mph)",
                storedStop.speedMph,
                0f,
                maxMph,
                "0",
                scale,
                controlHint: ExhaustColorSliderHint(stopHint, channelHint, 0));
            y += rowH;

            float r = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Red",
                storedColor.r,
                0f,
                1f,
                "0.00",
                scale,
                controlHint: ExhaustColorSliderHint(stopHint, channelHint, 1));
            y += rowH;

            float g = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Green",
                storedColor.g,
                0f,
                1f,
                "0.00",
                scale,
                controlHint: ExhaustColorSliderHint(stopHint, channelHint, 2));
            y += rowH;

            float b = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Blue",
                storedColor.b,
                0f,
                1f,
                "0.00",
                scale,
                controlHint: ExhaustColorSliderHint(stopHint, channelHint, 3));
            y += rowH;

            float storedPlumeMax = RtgExhaustColorProfile.GetPlumeMaxLengthMeters(storedStop);
            float plumeMax = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Plume max length (m)",
                storedPlumeMax,
                0f,
                24f,
                "0.0",
                scale,
                controlHint: ExhaustColorSliderHint(stopHint, channelHint, 4));
            y += rowH;

            float storedPlumeScale = RtgExhaustColorProfile.GetPlumeLengthScale(storedStop);
            float plumeScale = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Plume length scale",
                storedPlumeScale,
                0.15f,
                2.5f,
                "0.00",
                scale,
                controlHint: ExhaustColorSliderHint(stopHint, channelHint, 5));
            y += rowH;

            var swatchRect = new Rect(panelRect.x, y + 6f * scale, panelRect.width, 28f * scale);
            RegisterGameUiRect(swatchRect);
            DrawColorSwatch(swatchRect, new Color(r, g, b, 1f));
            y += 36f * scale;

            Color editedColor = new Color(r, g, b, 1f);
            bool colorsChanged = !Mathf.Approximately(maxMph, shipExhaustColorMaxMph)
                || !Mathf.Approximately(stopSpeed, storedStop.speedMph)
                || !ColorsApproximately(storedColor, editedColor)
                || !Mathf.Approximately(plumeMax, storedPlumeMax)
                || !Mathf.Approximately(plumeScale, storedPlumeScale);

            if (colorsChanged)
            {
                _exhaustFlamePreviewEnabled = true;
                shipExhaustColorMaxMph = maxMph;
                RtgExhaustColorStop updatedStop = storedStop;
                updatedStop.speedMph = stopSpeed;
                updatedStop.plumeMaxLengthMeters = plumeMax;
                updatedStop.plumeLengthScale = plumeScale;
                SetExhaustColorChannel(ref updatedStop, _exhaustColorChannelIndex, editedColor);
                shipExhaustColorStops[_exhaustColorStopEditIndex] = updatedStop;
                shipExhaustColorStops = RtgExhaustColorProfile.NormalizeStops(
                    shipExhaustColorStops,
                    shipExhaustColorMaxMph);
                _exhaustColorPreviewMph = shipExhaustColorStops[_exhaustColorStopEditIndex].speedMph;
                ApplyShipExhaustColors();
            }

            return y;
        }

        private static bool ColorsApproximately(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r)
                && Mathf.Approximately(a.g, b.g)
                && Mathf.Approximately(a.b, b.b);
        }

        private static int ExhaustColorSliderHint(int stopIndex, int channelIndex, int componentIndex)
        {
            unchecked
            {
                return 0x52C00000 ^ (stopIndex << 16) ^ (channelIndex << 8) ^ componentIndex;
            }
        }

        private static Color GetExhaustColorChannel(RtgExhaustColorStop stop, int channelIndex)
        {
            return channelIndex switch
            {
                1 => stop.cavityCore,
                2 => stop.plumeOuter.r + stop.plumeOuter.g + stop.plumeOuter.b > 0.01f
                    ? stop.plumeOuter
                    : stop.flame,
                3 => stop.plumeCore.r + stop.plumeCore.g + stop.plumeCore.b > 0.01f
                    ? stop.plumeCore
                    : stop.glow,
                _ => stop.cavityOuter,
            };
        }

        private static void SetExhaustColorChannel(ref RtgExhaustColorStop stop, int channelIndex, Color color)
        {
            switch (channelIndex)
            {
                case 1:
                    stop.cavityCore = color;
                    break;
                case 2:
                    stop.plumeOuter = color;
                    stop.flame = color;
                    break;
                case 3:
                    stop.plumeCore = color;
                    stop.glow = color;
                    break;
                default:
                    stop.cavityOuter = color;
                    break;
            }
        }

        private static int ExhaustMountSliderHint(int engineIndex, int componentIndex)
        {
            unchecked
            {
                return 0x55B30000 ^ (engineIndex << 8) ^ componentIndex;
            }
        }

        private static int ExhaustCavitySliderHint(int engineIndex, int componentIndex)
        {
            unchecked
            {
                return 0x53C10000 ^ (engineIndex << 8) ^ componentIndex;
            }
        }

        private static void DrawColorSwatch(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.12f, 0.18f, 0.28f, 1f);
            GUI.Box(rect, GUIContent.none);
            var inset = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
            GUI.color = new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1f);
            GUI.Box(inset, GUIContent.none);
            GUI.color = previous;
        }

        private float DrawEnginePortTuningSection(
            Rect panelRect,
            float y,
            float rowH,
            float sectionHeaderH,
            float buttonRowH,
            float scale)
        {
            var sectionStyle = BrightLabel(Mathf.RoundToInt(14f * scale), new Color(0.88f, 0.94f, 1f), FontStyle.Bold);
            GUI.Label(
                new Rect(panelRect.x, y, panelRect.width, sectionHeaderH),
                "Exhaust position",
                sectionStyle);
            y += sectionHeaderH;

            var hintStyle = BrightLabel(Mathf.RoundToInt(11f * scale), new Color(0.72f, 0.82f, 0.95f));
            GUI.Label(
                new Rect(panelRect.x, y, panelRect.width, 88f * scale),
                "Socket position (Hull/Attachments local, meters).\n"
                + "+X = span/wings · +Y = height · +Z = exhaust direction.\n"
                + "VFX parent to socket at zero offset. Save tuning when done.",
                hintStyle);
            y += 90f * scale;

            float btnW = (panelRect.width - 8f * scale) / 3f;
            var mainBtn = new Rect(panelRect.x, y, btnW, buttonRowH - 8f * scale);
            var leftBtn = new Rect(mainBtn.xMax + 4f * scale, y, btnW, buttonRowH - 8f * scale);
            var rightBtn = new Rect(leftBtn.xMax + 4f * scale, y, btnW, buttonRowH - 8f * scale);
            RegisterGameUiRect(mainBtn);
            RegisterGameUiRect(leftBtn);
            RegisterGameUiRect(rightBtn);

            var prevBtn = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = Mathf.RoundToInt(13f * scale);
            if (GUI.Button(mainBtn, _exhaustEngineIndex == 0 ? "Main *" : "Main"))
                _exhaustEngineIndex = 0;
            if (GUI.Button(leftBtn, _exhaustEngineIndex == 1 ? "Left *" : "Left"))
                _exhaustEngineIndex = 1;
            if (GUI.Button(rightBtn, _exhaustEngineIndex == 2 ? "Right *" : "Right"))
                _exhaustEngineIndex = 2;
            GUI.skin.button.fontSize = prevBtn;
            y += buttonRowH;

            if (_shipVisual != null
                && !RtgGliderEngineMounts.HasSavedPositions(new RtgGliderEngineMounts(
                    shipMainEngineLocal,
                    shipLeftEngineLocal,
                    shipRightEngineLocal)))
            {
                SyncExhaustPositionsFromShipVisual();
            }

            Vector3 selectedMount = GetSelectedEngineMount();
            if (_shipVisual != null && _shipVisual.TryGetSocketLocalPosition(_exhaustEngineIndex, out Vector3 liveMount))
                selectedMount = liveMount;

            float previousX = selectedMount.x;
            float previousY = selectedMount.y;
            float previousZ = selectedMount.z;

            float mountX = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Socket X — span (m)",
                selectedMount.x,
                RtgPlayerShipVisual.SocketSpanMinMeters,
                RtgPlayerShipVisual.SocketSpanMaxMeters,
                "0.00",
                scale,
                controlHint: ExhaustMountSliderHint(_exhaustEngineIndex, 0));
            y += rowH;

            float mountY = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Socket Y — height (m)",
                selectedMount.y,
                RtgPlayerShipVisual.SocketHeightMinMeters,
                RtgPlayerShipVisual.SocketHeightMaxMeters,
                "0.00",
                scale,
                controlHint: ExhaustMountSliderHint(_exhaustEngineIndex, 1));
            y += rowH;

            float mountZ = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Socket Z — depth (m)",
                selectedMount.z,
                RtgPlayerShipVisual.SocketDepthMinMeters,
                RtgPlayerShipVisual.SocketDepthMaxMeters,
                "0.00",
                scale,
                controlHint: ExhaustMountSliderHint(_exhaustEngineIndex, 2));
            y += rowH;

            if (!Mathf.Approximately(mountX, previousX)
                || !Mathf.Approximately(mountY, previousY)
                || !Mathf.Approximately(mountZ, previousZ))
            {
                ApplySelectedEngineMount(new Vector3(mountX, mountY, mountZ));
                selectedMount = GetSelectedEngineMount();
            }

            if (_shipVisual != null && _settingsOpen)
                _shipVisual.SetCavityPreview(true, Mathf.Max(_exhaustColorPreviewMph, 14f), _exhaustColorStopEditIndex);

            var readoutStyle = BrightLabel(Mathf.RoundToInt(12f * scale), new Color(0.9f, 0.95f, 1f));
            GUI.Label(
                new Rect(panelRect.x, y, panelRect.width, rowH - 8f * scale),
                $"Socket X {selectedMount.x:0.00} · Y {selectedMount.y:0.00} · Z {selectedMount.z:0.00}",
                readoutStyle);
            y += rowH - 4f * scale;

            y += 8f * scale;
            GUI.Label(
                new Rect(panelRect.x, y, panelRect.width, sectionHeaderH),
                $"Cavity fill ({(_exhaustEngineIndex == 1 ? "Left" : _exhaustEngineIndex == 2 ? "Right" : "Main")})",
                sectionStyle);
            y += sectionHeaderH;

            float previousCavitySize = GetSelectedCavityTuning().sizeMeters;
            float cavitySize = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Cavity size (m)",
                GetSelectedCavityTuning().sizeMeters,
                0.08f,
                2f,
                "0.00",
                scale,
                controlHint: ExhaustCavitySliderHint(_exhaustEngineIndex, 0));
            y += rowH;

            float previousCavityOffsetX = GetSelectedCavityTuning().offsetXMeters;
            float cavityOffsetX = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Cavity nudge X (m)",
                GetSelectedCavityTuning().offsetXMeters,
                -0.35f,
                0.35f,
                "0.00",
                scale,
                controlHint: ExhaustCavitySliderHint(_exhaustEngineIndex, 1));
            y += rowH;

            float previousCavityOffsetY = GetSelectedCavityTuning().offsetYMeters;
            float cavityOffsetY = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Cavity nudge Y (m)",
                GetSelectedCavityTuning().offsetYMeters,
                -0.35f,
                0.35f,
                "0.00",
                scale,
                controlHint: ExhaustCavitySliderHint(_exhaustEngineIndex, 2));
            y += rowH;

            if (_cavityDepthDraftEngineIndex != _exhaustEngineIndex)
            {
                _cavityDepthDraftEngineIndex = _exhaustEngineIndex;
                _cavityDepthDraft = GetSelectedCavityTuning().depthOffsetMeters.ToString("0.0", CultureInfo.InvariantCulture);
            }

            float previousCavityDepth = GetSelectedCavityTuning().depthOffsetMeters;
            float cavityDepth = DrawSettingSliderWithInput(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Cavity depth (m)",
                GetSelectedCavityTuning().depthOffsetMeters,
                -0.5f,
                0.5f,
                "0.0",
                scale,
                $"CavityDepth_{_exhaustEngineIndex}",
                ref _cavityDepthDraft);
            y += rowH;

            float previousCavityIntensity = GetSelectedCavityTuning().intensity;
            float cavityIntensity = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Cavity intensity",
                GetSelectedCavityTuning().intensity,
                0.2f,
                5f,
                "0.00",
                scale,
                controlHint: ExhaustCavitySliderHint(_exhaustEngineIndex, 4));
            y += rowH;

            float previousCavityCore = GetSelectedCavityTuning().coreRatio;
            float cavityCore = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Cavity core",
                GetSelectedCavityTuning().coreRatio,
                0.15f,
                0.95f,
                "0.00",
                scale,
                controlHint: ExhaustCavitySliderHint(_exhaustEngineIndex, 5));
            y += rowH;

            RtgEngineCavityTuning selectedCavity = GetSelectedCavityTuning();
            RtgEngineCavityTuning updatedCavity = selectedCavity;
            updatedCavity.sizeMeters = cavitySize;
            updatedCavity.offsetXMeters = cavityOffsetX;
            updatedCavity.offsetYMeters = cavityOffsetY;
            updatedCavity.depthOffsetMeters = cavityDepth;
            updatedCavity.intensity = cavityIntensity;
            updatedCavity.coreRatio = cavityCore;
            updatedCavity.plumeOffsetXMeters = 0f;
            updatedCavity.plumeOffsetYMeters = 0f;
            updatedCavity.plumeOffsetZMeters = 0f;
            updatedCavity = updatedCavity.Clamped();
            if (!Mathf.Approximately(cavitySize, previousCavitySize)
                || !Mathf.Approximately(cavityOffsetX, previousCavityOffsetX)
                || !Mathf.Approximately(cavityOffsetY, previousCavityOffsetY)
                || !Mathf.Approximately(cavityDepth, previousCavityDepth)
                || !Mathf.Approximately(cavityIntensity, previousCavityIntensity)
                || !Mathf.Approximately(cavityCore, previousCavityCore))
            {
                SetSelectedCavityTuning(updatedCavity);
                ApplyShipCavityTuning();
            }

            float estimateW = (panelRect.width - 8f * scale) * 0.5f;
            var estimateRect = new Rect(panelRect.x, y, estimateW, buttonRowH - 8f * scale);
            var resetRect = new Rect(estimateRect.xMax + 8f * scale, y, estimateW, buttonRowH - 8f * scale);
            RegisterGameUiRect(estimateRect);
            RegisterGameUiRect(resetRect);
            GUI.skin.button.fontSize = Mathf.RoundToInt(13f * scale);
            if (GUI.Button(estimateRect, "Reset sockets"))
            {
                if (_shipVisual != null && _shipVisual.TryResetSocketsToDefaults(out RtgGliderEngineMounts mounts))
                {
                    shipMainEngineLocal = mounts.Main;
                    shipLeftEngineLocal = mounts.Left;
                    shipRightEngineLocal = mounts.Right;
                    shipUseCustomEnginePorts = true;
                    Debug.Log(
                        $"[RTG] Reset engine sockets to blockout defaults — " +
                        $"main={mounts.Main} left={mounts.Left} right={mounts.Right}");
                }
                else
                {
                    Debug.LogWarning("[RTG] Socket reset failed.");
                }
            }

            if (GUI.Button(resetRect, "Defaults"))
            {
                shipMainExhaustAnchor = RtgGliderExhaustAnchors.DefaultMain;
                shipLeftExhaustAnchor = RtgGliderExhaustAnchors.DefaultLeft;
                shipRightExhaustAnchor = RtgGliderExhaustAnchors.DefaultRight;
                shipMainEngineLocal = Vector3.zero;
                shipLeftEngineLocal = Vector3.zero;
                shipRightEngineLocal = Vector3.zero;
                if (_shipVisual != null)
                    _shipVisual.TryResetSocketsToDefaults(out RtgGliderEngineMounts mounts);
                else
                    ApplyShipExhaustAnchors();
            }

            GUI.skin.button.fontSize = prevBtn;
            y += buttonRowH;
            return y;
        }

        private float DrawHullTuningSection(
            Rect panelRect,
            float y,
            float rowH,
            float sectionHeaderH,
            float buttonRowH,
            float scale)
        {
            var sectionStyle = BrightLabel(Mathf.RoundToInt(14f * scale), new Color(0.88f, 0.94f, 1f), FontStyle.Bold);
            GUI.Label(
                new Rect(panelRect.x, y, panelRect.width, sectionHeaderH),
                "Hull orientation",
                sectionStyle);
            y += sectionHeaderH;

            Vector3 previousEuler = shipHullEulerOffset;
            float previousHeading = shipHeadingOffsetDegrees;

            float pitchX = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Hull pitch X",
                shipHullEulerOffset.x,
                -90f,
                90f,
                "0",
                scale);
            y += rowH;

            float yawY = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Hull yaw Y",
                shipHullEulerOffset.y,
                -180f,
                180f,
                "0",
                scale);
            y += rowH;

            float rollZ = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Hull roll Z",
                shipHullEulerOffset.z,
                -90f,
                90f,
                "0",
                scale);
            y += rowH;

            float heading = DrawSettingSlider(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Heading offset",
                shipHeadingOffsetDegrees,
                -180f,
                180f,
                "0",
                scale);
            y += rowH;

            bool autoOrient = DrawSettingToggle(
                new Rect(panelRect.x, y, panelRect.width, rowH),
                "Auto-orient hull",
                shipAutoOrientImportedHull,
                scale);
            y += rowH;

            float buttonW = (panelRect.width - 8f * scale) * 0.5f;
            var saveRect = new Rect(panelRect.x, y, buttonW, buttonRowH - 8f * scale);
            var reloadRect = new Rect(saveRect.xMax + 8f * scale, y, buttonW, buttonRowH - 8f * scale);
            RegisterGameUiRect(saveRect);
            RegisterGameUiRect(reloadRect);

            var prevBtn = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = Mathf.RoundToInt(14f * scale);
            if (GUI.Button(saveRect, "Save tuning"))
                SaveShipHullTuning();
            if (GUI.Button(reloadRect, "Reload"))
                ReloadShipHullTuning();
            GUI.skin.button.fontSize = prevBtn;
            y += buttonRowH;

            Vector3 newEuler = new Vector3(pitchX, yawY, rollZ);
            if (autoOrient != shipAutoOrientImportedHull)
            {
                shipAutoOrientImportedHull = autoOrient;
                shipHullEulerOffset = newEuler;
                shipHeadingOffsetDegrees = heading;
                RefreshMarkerVisual();
                return y;
            }

            if (newEuler != previousEuler || !Mathf.Approximately(heading, previousHeading))
            {
                shipHullEulerOffset = newEuler;
                shipHeadingOffsetDegrees = heading;
                ApplyShipHullTuning();
            }

            return y;
        }

        private static Texture2D GetSettingsGearIcon()
        {
            if (_settingsGearIcon != null)
                return _settingsGearIcon;

            const int size = 128;
            const int teeth = 10;
            const float outerR = 0.94f;
            const float innerR = 0.72f;
            const float holeR = 0.26f;
            var fill = new Color(0.95f, 0.98f, 1f, 1f);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            float center = (size - 1) * 0.5f;
            float radiusScale = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - center) / radiusScale;
                    float ny = (y - center) / radiusScale;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float angle = Mathf.Atan2(ny, nx);
                    float profileR = innerR + (outerR - innerR) * 0.5f * (1f + Mathf.Cos(teeth * angle));
                    bool inside = r <= profileR && r >= holeR;
                    tex.SetPixel(x, y, inside ? fill : Color.clear);
                }
            }

            tex.Apply();
            _settingsGearIcon = tex;
            return _settingsGearIcon;
        }

        private static void DrawGearGlyph(Rect rect)
        {
            Texture2D tex = GetSettingsGearIcon();
            Color prev = GUI.color;
            GUI.color = Color.white;
            float inset = rect.width * 0.16f;
            var iconRect = new Rect(
                rect.x + inset,
                rect.y + inset,
                rect.width - inset * 2f,
                rect.height - inset * 2f);
            GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
            GUI.color = prev;
        }

        private static void DrawCloseGlyph(Rect rect)
        {
            var style = BrightLabel(Mathf.RoundToInt(rect.height * 0.42f), new Color(0.95f, 0.98f, 1f), FontStyle.Bold);
            GUI.Label(rect, "X", style);
        }

        private static Texture2D GetLeverKnobGlowTexture()
        {
            if (_leverKnobGlowTexture != null)
                return _leverKnobGlowTexture;

            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - center) / radius;
                    float ny = (y - center) / radius;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float alpha = Mathf.Clamp01(1f - r);
                    alpha = alpha * alpha * alpha;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * 0.9f));
                }
            }

            tex.Apply();
            _leverKnobGlowTexture = tex;
            return _leverKnobGlowTexture;
        }

        private static Texture2D GetLeverKnobCoreTexture()
        {
            if (_leverKnobCoreTexture != null)
                return _leverKnobCoreTexture;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - center) / radius;
                    float ny = (y - center) / radius;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float alpha = r <= 0.34f
                        ? 1f
                        : Mathf.Clamp01(1f - (r - 0.34f) / 0.22f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            _leverKnobCoreTexture = tex;
            return _leverKnobCoreTexture;
        }

        private static void DrawFillGlow(Rect fillRect, Color glowColor)
        {
            if (fillRect.height <= 1f)
                return;

            Color prev = GUI.color;
            for (int i = 3; i >= 1; i--)
            {
                float expand = i * 4f;
                float alpha = 0.1f + 0.08f * (4 - i);
                GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, alpha);
                GUI.Box(
                    new Rect(
                        fillRect.x - expand,
                        fillRect.y - expand * 0.5f,
                        fillRect.width + expand * 2f,
                        fillRect.height + expand),
                    GUIContent.none);
            }

            GUI.color = prev;
        }

        private static void DrawLeverKnob(Rect trackRect, float normalized, Color glowColor, float knobScale = 1.6f)
        {
            float thumbY = Mathf.Lerp(trackRect.yMax, trackRect.y, normalized);
            float knobSize = Mathf.Max(trackRect.width * knobScale, 52f);
            float glowSize = knobSize * 1.85f;
            float cx = trackRect.x + trackRect.width * 0.5f;

            var glowRect = new Rect(cx - glowSize * 0.5f, thumbY - glowSize * 0.5f, glowSize, glowSize);
            var coreRect = new Rect(cx - knobSize * 0.5f, thumbY - knobSize * 0.5f, knobSize, knobSize);

            Color prev = GUI.color;
            GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0.72f);
            GUI.DrawTexture(glowRect, GetLeverKnobGlowTexture(), ScaleMode.ScaleToFit, true);

            GUI.color = Color.Lerp(glowColor, Color.white, 0.62f);
            GUI.DrawTexture(coreRect, GetLeverKnobCoreTexture(), ScaleMode.ScaleToFit, true);

            float capW = knobSize * 0.34f;
            float capH = knobSize * 0.16f;
            GUI.color = new Color(0.95f, 0.18f, 0.2f, 0.95f);
            GUI.Box(
                new Rect(cx - capW * 0.5f, thumbY - knobSize * 0.34f, capW, capH),
                GUIContent.none);

            GUI.color = prev;
        }

        private static Rect UnionRects(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Rect BuildVerticalLeverHitRect(Rect trackRect, float normalized, float knobScale)
        {
            float thumbY = Mathf.Lerp(trackRect.yMax, trackRect.y, normalized);
            float glowSize = Mathf.Max(trackRect.width * knobScale, 52f) * 1.85f;
            float cx = trackRect.x + trackRect.width * 0.5f;
            var knobRect = new Rect(cx - glowSize * 0.5f, thumbY - glowSize * 0.5f, glowSize, glowSize);

            const float pad = 16f;
            var paddedTrack = new Rect(
                trackRect.x - pad,
                trackRect.y - pad,
                trackRect.width + pad * 2f,
                trackRect.height + pad * 2f);

            return UnionRects(paddedTrack, knobRect);
        }

        private float VerticalLeverInput(
            Rect trackRect,
            float normalized,
            float value,
            float min,
            float max,
            int controlHint,
            float knobScale = 1.6f)
        {
            int id = GUIUtility.GetControlID(controlHint, FocusType.Passive);
            Event e = Event.current;
            Rect hitRect = BuildVerticalLeverHitRect(trackRect, normalized, knobScale);
            Rect screenHitRect = ToScreenGuiRect(hitRect);
            RegisterGameUiRect(hitRect);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0)
                        break;

                    if (screenHitRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        _activeLeverHint = controlHint;
                        _activeLeverTrack = ToScreenGuiRect(trackRect);
                        value = ValueFromVerticalTrack(e.mousePosition.y, _activeLeverTrack, min, max);
                        e.Use();
                    }

                    break;
                case EventType.MouseDrag:
                    if (_activeLeverHint == controlHint)
                    {
                        GUIUtility.hotControl = id;
                        value = ValueFromVerticalTrack(e.mousePosition.y, _activeLeverTrack, min, max);
                        e.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (_activeLeverHint == controlHint && e.button == 0)
                    {
                        _activeLeverHint = -1;
                        if (GUIUtility.hotControl == id)
                            GUIUtility.hotControl = 0;
                        e.Use();
                    }

                    break;
            }

            return Mathf.Clamp(value, min, max);
        }

        private static float ValueFromVerticalTrack(float mouseY, Rect trackRect, float min, float max)
        {
            float t = Mathf.InverseLerp(trackRect.yMax, trackRect.y, mouseY);
            return Mathf.Lerp(min, max, t);
        }

        private static float ValueFromHorizontalTrack(float mouseX, Rect trackRect, float min, float max)
        {
            float t = Mathf.InverseLerp(trackRect.xMin, trackRect.xMax, mouseX);
            return Mathf.Lerp(min, max, t);
        }

        private static void DrawHorizontalSliderVisual(Rect trackRect, float value, float min, float max)
        {
            GUIStyle sliderStyle = GUI.skin.horizontalSlider;
            GUIStyle thumbStyle = GUI.skin.horizontalSliderThumb;
            sliderStyle.Draw(trackRect, GUIContent.none, false, false, false, false);

            float t = Mathf.InverseLerp(min, max, value);
            float thumbW = thumbStyle.fixedWidth > 0f ? thumbStyle.fixedWidth : 13f;
            float thumbH = thumbStyle.fixedHeight > 0f ? thumbStyle.fixedHeight : trackRect.height;
            float thumbX = Mathf.Lerp(trackRect.x, trackRect.xMax - thumbW, t);
            float thumbY = trackRect.y + (trackRect.height - thumbH) * 0.5f;
            thumbStyle.Draw(
                new Rect(thumbX, thumbY, thumbW, thumbH),
                GUIContent.none,
                false,
                false,
                false,
                false);
        }

        private static float HorizontalSettingSlider(
            Rect trackRect,
            float value,
            float min,
            float max,
            int controlHint)
        {
            int id = GUIUtility.GetControlID(controlHint, FocusType.Passive);
            Event e = Event.current;
            Rect hitRect = trackRect;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !hitRect.Contains(e.mousePosition))
                        break;

                    GUIUtility.hotControl = id;
                    _activeSettingSliderHint = controlHint;
                    value = ValueFromHorizontalTrack(e.mousePosition.x, hitRect, min, max);
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id)
                        break;

                    value = ValueFromHorizontalTrack(e.mousePosition.x, hitRect, min, max);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id || e.button != 0)
                        break;

                    GUIUtility.hotControl = 0;
                    _activeSettingSliderHint = -1;
                    e.Use();
                    break;
                case EventType.Repaint:
                    DrawHorizontalSliderVisual(trackRect, value, min, max);
                    break;
            }

            return Mathf.Clamp(value, min, max);
        }

        private float DrawSettingSlider(
            Rect rect,
            string label,
            float value,
            float min,
            float max,
            string format = "0.0",
            float scale = 1f,
            float step = 0f,
            string controlName = null,
            int controlHint = 0)
        {
            RegisterGameUiRect(rect);

            int fontSize = Mathf.RoundToInt(14f * scale);
            var labelStyle = BrightLabel(fontSize, new Color(0.93f, 0.97f, 1f), FontStyle.Bold);
            var valueStyle = BrightLabel(fontSize, new Color(0.98f, 1f, 1f), FontStyle.Bold);

            GUI.Label(new Rect(rect.x, rect.y, rect.width, 20f * scale), label, labelStyle);

            var sliderRect = new Rect(
                rect.x,
                rect.y + 24f * scale,
                rect.width - 58f * scale,
                22f * scale);
            var valueRect = new Rect(
                rect.xMax - 54f * scale,
                rect.y + 22f * scale,
                54f * scale,
                22f * scale);
            RegisterGameUiRect(sliderRect);

            float sliderValue = controlHint != 0
                ? HorizontalSettingSlider(sliderRect, value, min, max, controlHint)
                : GUI.HorizontalSlider(sliderRect, value, min, max);

            float newValue = step > 0f
                ? SnapSettingValue(sliderValue, min, max, step)
                : sliderValue;
            GUI.Label(valueRect, newValue.ToString(format), valueStyle);
            return newValue;
        }

        private float DrawSettingSliderWithInput(
            Rect rect,
            string label,
            float value,
            float min,
            float max,
            string format,
            float scale,
            string controlName,
            ref string draft)
        {
            RegisterGameUiRect(rect);

            int fontSize = Mathf.RoundToInt(14f * scale);
            var labelStyle = BrightLabel(fontSize, new Color(0.93f, 0.97f, 1f), FontStyle.Bold);

            GUI.Label(new Rect(rect.x, rect.y, rect.width, 20f * scale), label, labelStyle);

            const float valueFieldW = 76f;
            var sliderRect = new Rect(
                rect.x,
                rect.y + 24f * scale,
                rect.width - (valueFieldW + 6f) * scale,
                22f * scale);
            var valueRect = new Rect(
                rect.xMax - valueFieldW * scale,
                rect.y + 22f * scale,
                valueFieldW * scale,
                22f * scale);
            RegisterGameUiRect(sliderRect);
            RegisterGameUiRect(valueRect);

            int sliderHint = $"{controlName}_Slider".GetHashCode();
            float sliderValue = HorizontalSettingSlider(sliderRect, value, min, max, sliderHint);

            var prevFieldFont = GUI.skin.textField.fontSize;
            GUI.skin.textField.fontSize = Mathf.RoundToInt(14f * scale);
            GUI.SetNextControlName(controlName);
            string nextDraft = GUI.TextField(valueRect, draft ?? string.Empty);
            GUI.skin.textField.fontSize = prevFieldFont;

            bool editing = GUI.GetNameOfFocusedControl() == controlName;
            draft = nextDraft;

            if (editing)
            {
                if (TryParseSettingFloat(nextDraft, out float typedValue))
                    return Mathf.Clamp(typedValue, min, max);

                return value;
            }

            float clampedSlider = Mathf.Clamp(sliderValue, min, max);
            bool sliderMoved = !Mathf.Approximately(sliderValue, value);
            if (sliderMoved)
            {
                draft = clampedSlider.ToString(format, CultureInfo.InvariantCulture);
                return clampedSlider;
            }

            if (TryParseSettingFloat(draft, out float fromDraft))
                return Mathf.Clamp(fromDraft, min, max);

            draft = clampedSlider.ToString(format, CultureInfo.InvariantCulture);
            return clampedSlider;
        }

        private static bool TryParseSettingFloat(string text, out float value)
        {
            return float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static float SnapSettingValue(float value, float min, float max, float step)
        {
            if (step <= 0f)
                return Mathf.Clamp(value, min, max);

            value = Mathf.Round(value / step) * step;
            return Mathf.Clamp(value, min, max);
        }

        private bool DrawSettingToggle(Rect rect, string label, bool value, float scale = 1f)
        {
            RegisterGameUiRect(rect);

            int fontSize = Mathf.RoundToInt(14f * scale);
            var labelStyle = BrightLabel(fontSize, new Color(0.93f, 0.97f, 1f), FontStyle.Bold);

            float toggleW = 84f * scale;
            float toggleH = 34f * scale;
            var labelRect = new Rect(rect.x, rect.y, rect.width - toggleW - 8f * scale, rect.height);
            var toggleRect = new Rect(
                rect.xMax - toggleW,
                rect.y + (rect.height - toggleH) * 0.5f,
                toggleW,
                toggleH);
            RegisterGameUiRect(toggleRect);

            GUI.Label(labelRect, label, labelStyle);

            var prevFont = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = Mathf.RoundToInt(14f * scale);
            if (GUI.Button(toggleRect, value ? "ON" : "OFF"))
                value = !value;
            GUI.skin.button.fontSize = prevFont;
            return value;
        }

        private string DrawSettingTextField(Rect rect, string label, string value, float scale = 1f)
        {
            RegisterGameUiRect(rect);

            int fontSize = Mathf.RoundToInt(13f * scale);
            var labelStyle = BrightLabel(fontSize, new Color(0.93f, 0.97f, 1f), FontStyle.Bold);
            var fieldRect = new Rect(rect.x, rect.y + 18f * scale, rect.width, rect.height - 18f * scale);
            RegisterGameUiRect(fieldRect);

            GUI.Label(new Rect(rect.x, rect.y, rect.width, 16f * scale), label, labelStyle);

            var prevFont = GUI.skin.textField.fontSize;
            GUI.skin.textField.fontSize = Mathf.RoundToInt(15f * scale);
            string next = GUI.TextField(fieldRect, value ?? string.Empty);
            GUI.skin.textField.fontSize = prevFont;
            return next;
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

            string sourceLabel = ActiveModeButtonLabel(_activeSource);
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

        private static void DrawOutlinedLabel(Rect rect, string text, GUIStyle style, Color fill, Color outline)
        {
            var outlineStyle = new GUIStyle(style) { normal = { textColor = outline } };
            const int spread = 2;
            for (int ox = -spread; ox <= spread; ox++)
            {
                for (int oy = -spread; oy <= spread; oy++)
                {
                    if (ox == 0 && oy == 0) continue;
                    GUI.Label(new Rect(rect.x + ox, rect.y + oy, rect.width, rect.height), text, outlineStyle);
                }
            }

            var fillStyle = new GUIStyle(style) { normal = { textColor = fill } };
            GUI.Label(rect, text, fillStyle);
        }

        private void DrawThrottleLever(Rect anchorRect, bool cockpitAnchored)
        {
            float minThrottle = Mathf.Max(0.01f, minSpeedThrottle);
            float maxThrottle = Mathf.Max(minThrottle + 0.01f, maxSpeedThrottle);
            const float desiredPanelH = 520f;
            const float margin = 24f;

            Rect panelRect;
            if (cockpitAnchored)
            {
                float width = Mathf.Clamp(anchorRect.width, 96f, 220f);
                float height = Mathf.Clamp(anchorRect.height, 160f, Screen.height * 0.55f);
                panelRect = new Rect(
                    anchorRect.x + (anchorRect.width - width) * 0.5f,
                    anchorRect.y + (anchorRect.height - height) * 0.5f,
                    width,
                    height);
            }
            else
            {
                float panelW = anchorRect.width;
                float width = Mathf.Max(panelW, 168f);
                float availableH = Screen.height - margin - anchorRect.y;
                float panelH = Mathf.Min(desiredPanelH, availableH);
                panelRect = new Rect(anchorRect.x - (width - panelW), anchorRect.y, width, panelH);
            }

            _gameUiRects.Add(panelRect);

            float currentMph = EffectiveSimulatedSpeedMph();
            float baseMph = BaseSimulatedSpeedMph();
            float minMph = baseMph * minThrottle;
            float maxMph = baseMph * maxThrottle;

            float speedometerH = cockpitAnchored ? panelRect.height / 3f : 0f;
            float leverTop = cockpitAnchored ? panelRect.y + speedometerH : panelRect.y;
            float leverH = cockpitAnchored ? panelRect.height - speedometerH : panelRect.height;

            float headerH = cockpitAnchored ? 0f : 80f;
            float footerH = cockpitAnchored
                ? Mathf.Clamp(leverH * 0.14f, 24f, 36f)
                : Mathf.Max(96f, panelRect.height * 0.22f);

            Color prevColor = GUI.color;
            var prevLabel = GUI.skin.label.fontSize;

            float bgAlpha = cockpitAnchored ? 0.42f : 0.94f;
            GUI.color = new Color(0.06f, 0.09f, 0.18f, bgAlpha);
            GUI.Box(panelRect, GUIContent.none);

            Color titleColor = new Color(0.97f, 0.99f, 1f);
            Color subColor = new Color(0.93f, 0.97f, 1f);
            Color tierColor = Color.Lerp(new Color(0.98f, 0.99f, 1f), ThrottleGlowColor(currentMph), 0.4f);
            Color mphColor = new Color(1f, 1f, 1f);
            Color mphOutline = new Color(0.02f, 0.06f, 0.14f, 0.95f);

            int titleSize = cockpitAnchored ? 14 : 20;
            int tierSize = cockpitAnchored ? 15 : 22;
            int speedSize = cockpitAnchored ? 18 : 28;
            int mphRangeSize = cockpitAnchored ? 12 : 16;
            int cruiseSize = cockpitAnchored ? 12 : 17;

            var titleStyle = BrightLabel(titleSize, titleColor, FontStyle.Bold);
            var tierStyle = BrightLabel(tierSize, tierColor, FontStyle.Bold);
            var speedStyle = BrightLabel(speedSize, mphColor, FontStyle.Bold);
            var mphRangeStyle = BrightLabel(mphRangeSize, subColor);
            var cruiseStyle = BrightLabel(cruiseSize, subColor);

            float footerTop = panelRect.yMax - footerH;

            if (cockpitAnchored)
            {
                var speedZone = new Rect(panelRect.x + 4f, panelRect.y + 4f, panelRect.width - 8f, speedometerH - 8f);
                GUI.color = new Color(0.02f, 0.05f, 0.12f, 0.82f);
                GUI.Box(speedZone, GUIContent.none);

                int speedFont = Mathf.RoundToInt(Mathf.Clamp(speedZone.height * 0.52f, 28f, 52f));
                int mphFont = Mathf.RoundToInt(Mathf.Clamp(speedFont * 0.34f, 16f, 24f));
                var speedometerStyle = BrightLabel(speedFont, mphColor, FontStyle.Bold);
                var mphUnitStyle = BrightLabel(mphFont, new Color(0.88f, 0.96f, 1f), FontStyle.Bold);

                float numberH = speedZone.height * 0.62f;
                var numberRect = new Rect(speedZone.x, speedZone.y + 2f, speedZone.width, numberH);
                DrawOutlinedLabel(numberRect, $"{currentMph:0}", speedometerStyle, mphColor, mphOutline);

                var mphRect = new Rect(speedZone.x, speedZone.yMax - speedZone.height * 0.34f, speedZone.width, speedZone.height * 0.3f);
                DrawOutlinedLabel(mphRect, "MPH", mphUnitStyle, new Color(0.92f, 0.98f, 1f), mphOutline);
            }
            else
            {
                GUI.Label(new Rect(panelRect.x, panelRect.y + 4f, panelRect.width, 18f), "THROTTLE", titleStyle);
                GUI.Label(
                    new Rect(panelRect.x, panelRect.y + 26f, panelRect.width, 34f),
                    $"{currentMph:0} MPH",
                    speedStyle);
            }

            float trackW = cockpitAnchored ? Mathf.Clamp(panelRect.width * 0.34f, 36f, 52f) : 48f;
            float trackX = cockpitAnchored
                ? panelRect.x + (panelRect.width - trackW) * 0.5f
                : panelRect.x + 28f;
            float trackTop = cockpitAnchored ? leverTop + 4f : panelRect.y + headerH;
            float trackBottom = cockpitAnchored ? panelRect.yMax - footerH - 4f : footerTop;
            var trackRect = new Rect(
                trackX,
                trackTop,
                trackW,
                Mathf.Max(48f, trackBottom - trackTop));

            if (!cockpitAnchored)
            {
                GUI.Label(
                    new Rect(panelRect.x + 82f, trackRect.y - 2f, 56f, 24f),
                    $"{maxMph:0}",
                    mphRangeStyle);
                GUI.Label(
                    new Rect(panelRect.x + 82f, trackRect.yMax - 22f, 56f, 24f),
                    $"{minMph:0}",
                    mphRangeStyle);
            }

            float fillT = Mathf.InverseLerp(minThrottle, maxThrottle, speedThrottle);
            float fillH = trackRect.height * fillT;
            var fillRect = new Rect(trackRect.x + 4f, trackRect.yMax - fillH - 4f, trackRect.width - 8f, fillH);
            Color glowColor = ThrottleGlowColor(currentMph);

            GUI.color = new Color(0.12f, 0.18f, 0.28f, cockpitAnchored ? 0.72f : 1f);
            GUI.Box(trackRect, GUIContent.none);
            GUI.color = glowColor;
            GUI.Box(fillRect, GUIContent.none);
            DrawFillGlow(fillRect, glowColor);

            if (fillH > 6f)
            {
                GUI.color = Color.Lerp(glowColor, Color.white, 0.55f);
                GUI.Box(new Rect(fillRect.x, fillRect.y - 4f, fillRect.width, 8f), GUIContent.none);
            }

            DrawLeverKnob(trackRect, fillT, glowColor, cockpitAnchored ? 1.75f : 1.55f);

            float knobScale = cockpitAnchored ? 1.75f : 1.55f;
            float newThrottle = VerticalLeverInput(
                trackRect,
                fillT,
                speedThrottle,
                minThrottle,
                maxThrottle,
                ThrottleLeverHint,
                knobScale);
            if (!Mathf.Approximately(newThrottle, speedThrottle))
            {
                speedThrottle = newThrottle;
                ApplyThrottleToProvider();
            }

            GUI.Label(
                new Rect(panelRect.x, footerTop + 4f, panelRect.width, 22f),
                ThrottleTierLabel(currentMph),
                tierStyle);
            if (!cockpitAnchored)
            {
                GUI.Label(
                    new Rect(panelRect.x, footerTop + 38f, panelRect.width, 24f),
                    $"cruise {baseMph:0} · {speedThrottle:0.0}×",
                    cruiseStyle);
            }

            GUI.color = prevColor;
            GUI.skin.label.fontSize = prevLabel;
        }

        private void DrawPitchLever(
            float panelX,
            float panelY,
            float panelW,
            float? panelHeightOverride = null,
            float uiScale = 1f)
        {
            float minPitch = cockpitPitchMinDegrees;
            float maxPitch = Mathf.Max(minPitch + 1f, cockpitPitchMaxDegrees);
            const float margin = 24f;

            float width = Mathf.Max(panelW, 140f * uiScale);
            float panelH = panelHeightOverride ?? Mathf.Min(320f * uiScale, Screen.height - margin - panelY);
            float headerH = 64f * uiScale;
            var panelRect = new Rect(panelX, panelY, width, panelH);
            RegisterGameUiRect(panelRect);

            bool portrait = Screen.height > Screen.width;
            float pitch = portrait ? cockpitPortraitPitchDegrees : cockpitLandscapePitchDegrees;

            Color prevColor = GUI.color;
            var prevLabel = GUI.skin.label.fontSize;

            GUI.color = new Color(0.06f, 0.09f, 0.18f, panelHeightOverride.HasValue ? 0.72f : 0.94f);
            GUI.Box(panelRect, GUIContent.none);

            var labelStyle = BrightLabel(Mathf.RoundToInt(18f * uiScale), new Color(0.97f, 0.99f, 1f), FontStyle.Bold);
            var valueStyle = BrightLabel(Mathf.RoundToInt(24f * uiScale), new Color(0.98f, 1f, 1f), FontStyle.Bold);

            GUI.Label(new Rect(panelRect.x, panelRect.y + 8f * uiScale, panelRect.width, 22f * uiScale), "Pitch", labelStyle);
            GUI.Label(
                new Rect(panelRect.x, panelRect.y + 30f * uiScale, panelRect.width, 30f * uiScale),
                $"{pitch:0.#}°",
                valueStyle);

            float trackW = 48f * uiScale;
            var trackRect = new Rect(
                panelRect.x + 28f * uiScale,
                panelRect.y + headerH,
                trackW,
                panelRect.height - headerH - 16f * uiScale);

            float fillT = Mathf.InverseLerp(minPitch, maxPitch, pitch);
            float fillH = trackRect.height * fillT;
            var fillRect = new Rect(trackRect.x + 4f, trackRect.yMax - fillH - 4f, trackRect.width - 8f, fillH);
            Color pitchGlow = new Color(0.35f, 0.75f, 0.95f, 1f);

            GUI.color = new Color(0.12f, 0.18f, 0.28f, 1f);
            GUI.Box(trackRect, GUIContent.none);
            GUI.color = pitchGlow;
            GUI.Box(fillRect, GUIContent.none);
            DrawFillGlow(fillRect, pitchGlow);
            DrawLeverKnob(trackRect, fillT, pitchGlow, 1.35f);

            float newPitch = VerticalLeverInput(
                trackRect,
                fillT,
                pitch,
                minPitch,
                maxPitch,
                PitchLeverHint,
                1.35f);
            if (!Mathf.Approximately(newPitch, pitch))
            {
                newPitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
                if (portrait)
                    cockpitPortraitPitchDegrees = newPitch;
                else
                    cockpitLandscapePitchDegrees = newPitch;
            }

            GUI.color = prevColor;
            GUI.skin.label.fontSize = prevLabel;
        }

        private string ThrottleTierLabel(float mph)
        {
            EnsureExhaustColorStops();
            if (mph >= shipExhaustColorStops[3].speedMph - 0.5f) return "WARP";
            if (mph >= shipExhaustColorStops[2].speedMph) return "BURN";
            if (mph >= shipExhaustColorStops[1].speedMph) return "FAST";
            return "CRUISE";
        }

        private Color ThrottleGlowColor(float mph)
        {
            EnsureExhaustColorStops();
            return RtgExhaustColorProfile.Sample(mph, shipExhaustColorStops, shipExhaustColorMaxMph, s => s.glow);
        }

        private string FormatSpeedLabel()
        {
            if (_activeSource == LocationSource.Manual)
                return _provider != null ? _provider.Status : "Manual";

            return $"{EffectiveSimulatedSpeedMph():0} MPH";
        }

        private void RecenterOnPlayer()
        {
            _panned = false; // focus glides back to the pin via smoothing
        }

        private void TogglePerspective()
        {
            ExitCockpit(immediate: true);

            perspective = perspective == CameraPerspective.Map
                ? CameraPerspective.LowAngle
                : CameraPerspective.Map;

            if (_followActive && _cameraManager != null)
                _cameraManager.ApplyChaseLookAt(DesiredCameraPosition(_focus), _focus);
        }

        private void RequestCockpit(bool fastZoom)
        {
            if (_cockpitView == null) return;

            if (_cockpitView.IsActive)
            {
                ExitCockpit();
                return;
            }

            _panned = false;
            _cockpitEntryPending = true;
            _cockpitFastZoom = fastZoom;
            _zoomTarget = minZoom;
        }

        private void EnterCockpit()
        {
            if (_cockpitView == null) return;

            _cockpitEntryPending = false;
            _cockpitFastZoom = false;
            _zoom = minZoom;
            _zoomTarget = minZoom;
            ResetCockpitLook();
            _savedCameraFov = _camera != null ? _camera.fieldOfView : 60f;
            _cameraManager?.SetCockpitFieldOfView(cockpitFieldOfView);

            _cameraManager?.SetMode(RtgCameraManager.CameraMode.Cockpit);
            _cameraManager?.SetGameplayCameraOwnership(true);
            Debug.Log("[RTG] Cockpit mode — CockpitCamera renders; fly camera stack suppressed.");
            _cockpitView.useGlassCanopyOverlay = true;
            _cockpitView.SetActive(true, immediate: false);
            SetShipVisible(false);
            ApplyCockpitCamera();
        }

        private void ExitCockpit(bool immediate = false)
        {
            if (_cockpitView == null) return;

            _cockpitEntryPending = false;
            _cockpitFastZoom = false;
            ResetCockpitLook();
            _cameraManager?.RestoreChaseFieldOfView(_savedCameraFov);

            _cameraManager?.SetMode(RtgCameraManager.CameraMode.Chase);
            _cameraManager?.SetGameplayCameraOwnership(_followActive);
            _cockpitView.SetActive(false, immediate);
            _cockpitRearCamera?.SetActive(false);
            SetShipVisible(true);

            if (!immediate)
                _zoomTarget = Mathf.Clamp(minZoom * 1.75f, minZoom, maxZoom);
        }

        private void SetShipVisible(bool visible)
        {
            if (_shipVisual != null)
                _shipVisual.gameObject.SetActive(visible);
            Transform beacon = _marker != null ? _marker.Find("Beacon") : null;
            if (beacon != null)
                beacon.gameObject.SetActive(visible);
        }

        private void HandleCockpitLookInput()
        {
            if (!IsCockpitCameraActive() || _cockpitView == null)
                return;

            if (IsMultiTouchActive())
            {
                _cockpitLookPointerDown = false;
                return;
            }

            if (!ReadPointer(out Vector2 pos, out bool isDown))
            {
                _cockpitLookPointerDown = false;
                return;
            }

            if (IsOverGameUi(pos))
            {
                _cockpitLookPointerDown = false;
                return;
            }

            if (!_cockpitView.IsPointerOverGlassViewport(pos))
            {
                _cockpitLookPointerDown = false;
                return;
            }

            if (isDown && !_cockpitLookPointerDown)
            {
                _cockpitLookLastPointer = pos;
                _cockpitLookPointerDown = true;
                return;
            }

            if (!isDown || !_cockpitLookPointerDown)
            {
                _cockpitLookPointerDown = false;
                return;
            }

            Vector2 delta = pos - _cockpitLookLastPointer;
            _cockpitLookLastPointer = pos;

            const float minDragPixels = 6f;
            if (delta.sqrMagnitude < minDragPixels * minDragPixels)
                return;

            _cockpitLookYawTargetDeg = Mathf.Clamp(
                _cockpitLookYawTargetDeg + delta.x * cockpitLookYawSensitivity,
                -cockpitLookYawMaxDegrees,
                cockpitLookYawMaxDegrees);
            _cockpitLookPitchTargetDeg = Mathf.Clamp(
                _cockpitLookPitchTargetDeg - delta.y * cockpitLookPitchSensitivity,
                cockpitLookPitchMinDegrees,
                cockpitLookPitchMaxDegrees);
        }

        private void SmoothCockpitLook()
        {
            float t = cockpitLookSmoothing > 0f
                ? 1f - Mathf.Exp(-cockpitLookSmoothing * Time.deltaTime)
                : 1f;
            _cockpitLookYawDeg = Mathf.Lerp(_cockpitLookYawDeg, _cockpitLookYawTargetDeg, t);
            _cockpitLookPitchDeg = Mathf.Lerp(_cockpitLookPitchDeg, _cockpitLookPitchTargetDeg, t);
        }

        private void DrawCockpitLookDebugHud()
        {
            Vector3 markerPos = _marker.position;
            float markerDelta = 0f;
            if (_cockpitDebugHasMarkerPos)
                markerDelta = (markerPos - _cockpitDebugLastMarkerPos).magnitude;
            _cockpitDebugLastMarkerPos = markerPos;
            _cockpitDebugHasMarkerPos = true;

            Camera cam = _cameraManager != null
                ? _cameraManager.ActiveGameplayCamera
                : _camera;
            string camName = cam != null ? cam.name : "none";

            float camDelta = 0f;
            if (cam != null)
            {
                Vector3 camPos = cam.transform.position;
                if (_cockpitDebugHasCamPos)
                    camDelta = (camPos - _cockpitDebugLastCamPos).magnitude;
                _cockpitDebugLastCamPos = camPos;
                _cockpitDebugHasCamPos = true;
            }

            string modeHint = camDelta < 0.001f && markerDelta < 0.001f
                ? "head-rotate (world should spin, HUD fixed)"
                : "slide-check";

            var rect = new Rect(12f, 12f, 520f, 118f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f),
                $"COCKPIT DBG  cam={camName}\n" +
                $"lookYaw={_cockpitLookYawDeg:F1}  lookPitch={_cockpitLookPitchDeg:F1}\n" +
                $"markerΔ={markerDelta * 100f:F2}cm  camΔ={camDelta * 100f:F2}cm  panned={_panned}\n" +
                modeHint);
        }

        private void ResetCockpitLook()
        {
            _cockpitLookYawDeg = 0f;
            _cockpitLookPitchDeg = 0f;
            _cockpitLookYawTargetDeg = 0f;
            _cockpitLookPitchTargetDeg = 0f;
            _cockpitLookPointerDown = false;
            _cockpitDebugHasMarkerPos = false;
            _cockpitDebugHasCamPos = false;
        }

        private void TickCockpitRearCamera()
        {
            if (_cockpitRearCamera == null || _marker == null || _camera == null)
                return;

            bool showRear = _cockpitView != null && _cockpitView.Blend > 0.35f;
            _cockpitRearCamera.SetActive(showRear);
            if (!showRear)
                return;

            _cockpitRearCamera.SyncFromMainCamera(_camera);
            _cockpitRearCamera.Render(
                _marker,
                _cameraManager != null ? _cameraManager.CameraRig : null,
                TravelDirectionXZ());
        }

        private void ApplyCockpitCamera()
        {
            if (_cameraManager == null || _marker == null)
                return;

            Vector3 forward = TravelDirectionXZ();
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;

            if (Mathf.Abs(cockpitHeadingOffsetDegrees) > 0.01f)
                forward = Quaternion.Euler(0f, cockpitHeadingOffsetDegrees, 0f) * forward;

            bool portrait = Screen.height > Screen.width;
            float basePitch = portrait ? cockpitPortraitPitchDegrees : cockpitLandscapePitchDegrees;

            _cameraManager.ApplyCockpitPose(
                forward,
                cockpitEyeHeightMeters,
                basePitch,
                _cockpitLookYawDeg,
                _cockpitLookPitchDeg);
        }

        private void TryCompleteCockpitEntry()
        {
            if (!_cockpitEntryPending || _cockpitView == null) return;
            if (_zoom > minZoom * 1.08f) return;
            EnterCockpit();
        }

        private void ApplyZoomInButton()
        {
            if (_cockpitView != null && _cockpitView.IsActive) return;

            if (_zoomTarget <= minZoom * 1.02f)
            {
                RequestCockpit(fastZoom: false);
                return;
            }

            _zoomTarget = Mathf.Clamp(_zoomTarget / 1.35f, minZoom, maxZoom);
            if (_zoomTarget <= minZoom * 1.02f)
                _cockpitEntryPending = true;
        }

        private void ApplyZoomOutButton()
        {
            if (_cockpitView != null && _cockpitView.IsActive)
            {
                ExitCockpit();
                return;
            }

            _cockpitEntryPending = false;
            _cockpitFastZoom = false;
            _zoomTarget = Mathf.Clamp(_zoomTarget * 1.35f, minZoom, maxZoom);
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

            float smoothing = _cockpitFastZoom ? cockpitZoomSmoothing : zoomSmoothing;
            float t = smoothing > 0f
                ? 1f - Mathf.Exp(-smoothing * Time.deltaTime)
                : 1f;
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, t);

            if (_cockpitFastZoom && Mathf.Abs(_zoom - _zoomTarget) < 0.01f)
                _cockpitFastZoom = false;
        }

        private void ApplyZoomStep(float step)
        {
            if (_cockpitView != null && _cockpitView.IsActive)
            {
                if (step > 0f)
                    ExitCockpit();
                return;
            }

            float clamped = Mathf.Clamp(step, -1f, 1f) * (invertZoom ? -1f : 1f);

            // Zooming in at max zoom enters the cockpit.
            if (clamped < 0f && _zoomTarget <= minZoom * 1.02f)
            {
                RequestCockpit(fastZoom: false);
                return;
            }

            _zoomTarget = Mathf.Clamp(
                _zoomTarget * Mathf.Exp(-clamped * zoomSensitivity), minZoom, maxZoom);

            if (clamped < 0f && _zoomTarget <= minZoom * 1.02f)
                _cockpitEntryPending = true;
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
            if (_cameraManager != null)
                _cameraManager.SetGameplayCameraOwnership(active);

            if (active && _marker != null && _cameraManager != null)
            {
                _panned = false;
                _focusTarget = _marker.position;
                _focus = _focusTarget;
                _hasHeadingSample = false;
                _cameraManager.SetMode(RtgCameraManager.CameraMode.Chase);
                _cameraManager.ApplyChaseLookAt(DesiredCameraPosition(_focus), _focus);
            }
        }

        private static void DestroyImmediateSafe(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        /// <summary>
        /// Home testing: abandon the open leg, clear the live trail, and restart the
        /// simulated route from its first waypoint without switching Manual/Auto Pilot.
        /// </summary>
        public void ResetAutopilotTestDrive()
        {
            _routeSession?.AbandonActiveLeg("test reset");

            if (_lightRoad != null)
            {
                _lightRoad.ClearRoad();
                _roadStarted = false;
            }

            if (_activeSource == LocationSource.AutoPilot && routeMode == RouteMode.HomeToCasper)
            {
                _cachedDriveRoute = null;
                _provider?.End();
                _provider = null;
                if (_routeBuildCoroutine != null)
                    StopCoroutine(_routeBuildCoroutine);
                _routeBuildCoroutine = StartCoroutine(BeginDriveRouteWhenReady());
                _panned = false;
                _hasHeadingSample = false;
                ResetDisplayPositionSmoothing();
                return;
            }

            ClearLightRoadAndRestartSim();
        }

        /// <summary>Dev helper: jump back to route start and clear the live Light Road.</summary>
        public void RestartSimulatedRoute()
        {
            ResetAutopilotTestDrive();
        }

        private void ClearLightRoadAndRestartSim()
        {
            if (_provider is RtgSimulatedLocationProvider sim)
            {
                if (TryGetPlayerLatLng(out double lat, out double lng))
                    sim.RestartAt(lat, lng);
                else
                    sim.Restart();
            }

            _panned = false;
            _hasHeadingSample = false;
            _hasMotionSample = false;
            _groundSpeedMps = 0f;
            _prevTravelHeadingRad = _travelHeadingRad;
            ResetDisplayPositionSmoothing();
            if (_marker != null)
            {
                _focusTarget = _marker.position;
                _focus = _focusTarget;
            }

            Debug.Log("[RTG] Auto Pilot test drive reset — continuing from current pin.");
        }

        private void DrawAutopilotTestControls()
        {
            const float margin = 24f;
            const float gap = 12f;
            const float wideW = 280f;
            const float wideH = 92f;
            float wideRight = Screen.width - wideW - margin;
            float resetY = Screen.height - margin - wideH;
            float clearY = resetY - wideH - gap;

            var prev = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = 28;

            bool confirmClear = Time.time < _clearRoutesConfirmUntil;
            string clearLabel = confirmClear ? "Confirm Clear?" : "Clear Routes";
            var clearRect = new Rect(wideRight, clearY, wideW, wideH);
            if (GUI.Button(clearRect, clearLabel))
            {
                if (confirmClear)
                    RequestClearWorldRoutes();
                else
                    _clearRoutesConfirmUntil = Time.time + 5f;
            }
            _gameUiRects.Add(clearRect);

            var resetRect = new Rect(wideRight, resetY, wideW, wideH);
            if (GUI.Button(resetRect, "Reset Drive"))
                ResetAutopilotTestDrive();
            _gameUiRects.Add(resetRect);

            GUI.skin.button.fontSize = prev;
        }

        private void RequestClearWorldRoutes()
        {
            _clearRoutesConfirmUntil = 0f;
            if (_routeSession == null) return;

            _routeSession.ResetWorldProgress((ok, message) =>
            {
                if (ok)
                {
                    if (_lightRoad != null)
                    {
                        _lightRoad.ClearRoad();
                        _roadStarted = false;
                    }

#if UNITY_2023_1_OR_NEWER
                    RtgEchoSiteLoader loader = Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
                    RtgEchoSiteLoader loader = Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
                    loader?.ReloadMarkersAfterReset();
                    RefreshAfterWorldReset();
                }

                Debug.Log(ok
                    ? $"[RTG] {message}"
                    : $"[RTG] World route clear failed: {message}");
            });
        }
    }
}
