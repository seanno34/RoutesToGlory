using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Pathfinder corridor lance: activates when vaporizable scatter props enter
    /// detection range, then disintegrates them. Hills/mountains use terrain clearance only.
    /// </summary>
    public class RtgPathfinderBeam : MonoBehaviour
    {
        [Header("Activation")]
        [Tooltip("Beam arms when a vaporizable prop enters this forward distance (m).")]
        public float detectionRangeM = 115f;

        [Tooltip("While active, props within this forward distance (m) are disintegrated.")]
        public float vaporizeRangeM = 80f;

        [Tooltip("Corridor half-width near the glider (m).")]
        public float corridorHalfWidthNearM = 9f;

        [Tooltip("Corridor half-width at max range (m).")]
        public float corridorHalfWidthFarM = 24f;

        [Tooltip("Threat scan rate (Hz).")]
        public float scanHz = 8f;

        [Header("Beam visual (map / route view)")]
        public float beamOriginHeightM = 2.5f;
        public float beamFadeSpeed = 14f;
        public Color beamColor = new Color(0.35f, 0.95f, 1f, 1f);

        [Tooltip("Beam width at the glider (m).")]
        public float beamWidthStartM = 22f;

        [Tooltip("Beam width at the far end, as a fraction of start width.")]
        [Range(0.2f, 1f)]
        public float beamWidthEndRatio = 0.55f;

        [Tooltip("Outer glow ring width multiplier on top of the core beam.")]
        public float beamGlowWidthMultiplier = 2.6f;

        [Header("Cockpit beam (first-person)")]
        [Tooltip("Use a narrow camera-aligned beam in cockpit instead of the wide map wedge.")]
        public bool useCockpitBeam = true;

        [Tooltip("Core beam width at the cockpit camera (m).")]
        public float cockpitBeamWidthStartM = 5f;

        [Tooltip("Beam reach in cockpit view (m).")]
        public float cockpitBeamLengthM = 50f;

        [Tooltip("Glow multiplier for the cockpit beam.")]
        public float cockpitBeamGlowMultiplier = 1.6f;

        [Header("Beam audio")]
        [Tooltip("Play beam hum + vaporize zaps while the corridor lance is active.")]
        public bool enableBeamAudio = true;

        [Tooltip("Optional loop while the beam is armed. Uses procedural hum if empty.")]
        public AudioClip beamHumClip;

        [Tooltip("Optional one-shot when props disintegrate. Uses procedural zap if empty.")]
        public AudioClip vaporizeClip;

        [Tooltip("Hum volume at full beam intensity.")]
        [Range(0f, 1f)]
        public float beamHumVolume = 0.38f;

        [Tooltip("Vaporize zap volume.")]
        [Range(0f, 1f)]
        public float vaporizeVolume = 0.45f;

        [Tooltip("Minimum seconds between vaporize zaps (avoids machine-gun SFX).")]
        public float vaporizeMinInterval = 0.09f;

        [Header("Beam haptics")]
        [Tooltip("Light phone vibration when the beam arms and at intervals while active.")]
        public bool enableBeamHaptics = true;

        [Tooltip("Seconds between haptic pulses while the beam stays armed.")]
        public float beamHapticInterval = 0.34f;

        [Tooltip("Android pulse length (ms). iOS uses the system vibrate.")]
        public int beamHapticDurationMs = 18;

        public bool IsBeamActive { get; private set; }
        public float BeamIntensity { get; private set; }
        public float NearestThreatM { get; private set; }

        private LineRenderer _beamLine;
        private LineRenderer _beamGlowLine;
        private Transform _beamRoot;
        private Material _beamMaterial;
        private Material _beamGlowMaterial;
        private RtgTerrainScatter _scatter;
        private RtgTerrainHeight _terrainHeight;
        private float _nextScanTime;
        private bool _threatInRange;
        private bool _cockpitMode;
        private Camera _viewCamera;
        private AudioSource _humSource;
        private AudioSource _zapSource;
        private AudioClip _runtimeHumClip;
        private AudioClip _runtimeZapClip;
        private bool _ownsRuntimeHumClip;
        private bool _ownsRuntimeZapClip;
        private float _lastVaporizeSfxTime;
        private bool _humPlaying;
        private float _lastHapticTime;
        private bool _wasBeamArmed;

        public static RtgPathfinderBeam Find()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<RtgPathfinderBeam>();
#else
            return UnityEngine.Object.FindObjectOfType<RtgPathfinderBeam>();
#endif
        }

        public static RtgPathfinderBeam Ensure(RtgPlayerLocation player)
        {
            if (player == null) return null;
            RtgPathfinderBeam beam = player.GetComponent<RtgPathfinderBeam>();
            if (beam == null)
                beam = player.gameObject.AddComponent<RtgPathfinderBeam>();
            return beam;
        }

        private void Awake()
        {
            EnsureBeamLine();
        }

        public void Tick(
            double lat,
            double lng,
            float headingRad,
            Transform shipAnchor,
            RtgTerrainHeight terrainHeight,
            bool cockpitMode = false,
            Camera viewCamera = null)
        {
            _terrainHeight = terrainHeight;
            _cockpitMode = cockpitMode;
            _viewCamera = viewCamera;
            if (_scatter == null)
                _scatter = RtgTerrainScatter.Find();

            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + (scanHz > 0.01f ? 1f / scanHz : 0.125f);
                RunThreatScan(lat, lng, headingRad);
            }

            float targetIntensity = _threatInRange ? 1f : 0f;
            float fadeT = beamFadeSpeed > 0f
                ? 1f - Mathf.Exp(-beamFadeSpeed * Time.deltaTime)
                : 1f;
            BeamIntensity = Mathf.Lerp(BeamIntensity, targetIntensity, fadeT);
            IsBeamActive = BeamIntensity > 0.05f;

            if (BeamIntensity > 0.4f && _scatter != null)
            {
                int vaporized = _scatter.VaporizeInCorridor(
                    lat, lng, headingRad,
                    vaporizeRangeM,
                    corridorHalfWidthNearM,
                    corridorHalfWidthFarM);
                PlayVaporizeSfx(vaporized);
            }

            UpdateBeamVisual(shipAnchor, headingRad);
            UpdateBeamAudio();
            UpdateBeamHaptics();
        }

        private void RunThreatScan(double lat, double lng, float headingRad)
        {
            _threatInRange = false;
            NearestThreatM = float.MaxValue;

            if (_scatter == null) return;

            if (_scatter.TryFindNearestThreat(
                    lat, lng, headingRad,
                    detectionRangeM,
                    corridorHalfWidthNearM,
                    corridorHalfWidthFarM,
                    out RtgScatterObstacle nearest,
                    out float forwardM))
            {
                NearestThreatM = forwardM;
                _threatInRange = forwardM <= detectionRangeM && forwardM > 0.5f;
            }
        }

        private void UpdateBeamVisual(Transform shipAnchor, float headingRad)
        {
            EnsureBeamLine();
            if (_beamLine == null)
                return;

            bool show = BeamIntensity > 0.02f;
            _beamLine.enabled = show;
            if (_beamGlowLine != null)
                _beamGlowLine.enabled = show;
            if (!show) return;

            if (_cockpitMode && useCockpitBeam && _viewCamera != null)
                UpdateCockpitBeamVisual(_viewCamera);
            else
            {
                if (shipAnchor == null) return;
                UpdateMapBeamVisual(shipAnchor, headingRad);
            }
        }

        private void UpdateMapBeamVisual(Transform shipAnchor, float headingRad)
        {
            // Chase camera looks down at the route; billboard toward the camera so the
            // corridor reads as a wide ground wedge instead of edge-on caps.
            SetBeamAlignment(LineAlignment.View);
            if (_beamRoot != null)
                _beamRoot.rotation = Quaternion.identity;

            Vector3 origin = shipAnchor.position + Vector3.up * beamOriginHeightM;
            float sin = Mathf.Sin(headingRad);
            float cos = Mathf.Cos(headingRad);
            Vector3 forward = new Vector3(sin, 0f, cos);
            if (forward.sqrMagnitude < 1e-6f)
                forward = shipAnchor.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 1e-6f)
                forward.Normalize();

            float length = vaporizeRangeM * Mathf.Lerp(0.55f, 1f, BeamIntensity);
            Vector3 end = origin + forward * length;

            ApplyBeamGeometry(origin, end, beamWidthStartM, beamWidthEndRatio, beamGlowWidthMultiplier);
        }

        private void UpdateCockpitBeamVisual(Camera camera)
        {
            // First-person laser runs along bore sight; width must expand in the view plane,
            // not toward the camera (View alignment collapses to invisible on-axis).
            SetBeamAlignment(LineAlignment.TransformZ);
            if (_beamRoot != null)
                _beamRoot.rotation = camera.transform.rotation;

            Vector3 origin = camera.transform.position + camera.transform.forward * 3f;
            float length = cockpitBeamLengthM * Mathf.Lerp(0.5f, 1f, BeamIntensity);
            Vector3 end = origin + camera.transform.forward * length;

            ApplyBeamGeometry(
                origin,
                end,
                cockpitBeamWidthStartM,
                beamWidthEndRatio,
                cockpitBeamGlowMultiplier);
        }

        private void ApplyBeamGeometry(
            Vector3 origin,
            Vector3 end,
            float widthStartM,
            float widthEndRatio,
            float glowMultiplier)
        {
            _beamLine.SetPosition(0, origin);
            _beamLine.SetPosition(1, end);

            float startW = Mathf.Max(0.75f, widthStartM * BeamIntensity);
            float endW = Mathf.Max(0.35f, startW * widthEndRatio);
            _beamLine.startWidth = startW;
            _beamLine.endWidth = endW;

            float coreAlpha = Mathf.Clamp01(0.35f + BeamIntensity * 0.65f);
            float tailAlpha = coreAlpha * 0.25f;

            if (_beamGlowLine != null)
            {
                _beamGlowLine.SetPosition(0, origin);
                _beamGlowLine.SetPosition(1, end);
                _beamGlowLine.startWidth = startW * glowMultiplier;
                _beamGlowLine.endWidth = endW * glowMultiplier;

                float glowAlpha = _cockpitMode ? 0.35f : 0.45f;
                Color glow = beamColor;
                glow.a = BeamIntensity * glowAlpha;
                _beamGlowLine.startColor = glow;
                _beamGlowLine.endColor = new Color(glow.r, glow.g, glow.b, glow.a * 0.3f);
            }

            if (_beamMaterial != null)
            {
                Color c = beamColor * (1.5f + BeamIntensity * 2f);
                c.a = coreAlpha;
                ConfigureBeamMaterial(_beamMaterial, c);
            }

            if (_beamGlowMaterial != null)
            {
                Color glowMat = beamColor;
                glowMat.a = _cockpitMode ? BeamIntensity * 0.35f : BeamIntensity * 0.45f;
                ConfigureBeamMaterial(_beamGlowMaterial, glowMat);
            }

            _beamLine.startColor = new Color(beamColor.r, beamColor.g, beamColor.b, coreAlpha);
            _beamLine.endColor = new Color(beamColor.r, beamColor.g, beamColor.b, tailAlpha);
        }

        private void SetBeamAlignment(LineAlignment alignment)
        {
            if (_beamLine != null)
                _beamLine.alignment = alignment;
            if (_beamGlowLine != null)
                _beamGlowLine.alignment = alignment;
        }

        private void EnsureBeamLine()
        {
            if (_beamLine != null) return;

            var root = new GameObject("PathfinderBeam");
            root.transform.SetParent(transform, false);
            _beamRoot = root.transform;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null) return;

            _beamGlowLine = CreateBeamLine(root.transform, "Glow", shader, out _beamGlowMaterial);
            _beamLine = CreateBeamLine(root.transform, "Core", shader, out _beamMaterial);
        }

        private static LineRenderer CreateBeamLine(
            Transform parent,
            string name,
            Shader shader,
            out Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.sortingOrder = 6;

            material = new Material(shader) { name = $"RTG_PathfinderBeam_{name}" };
            ConfigureBeamMaterial(material, Color.white);
            line.material = material;
            return line;
        }

        private static void ConfigureBeamMaterial(Material mat, Color color)
        {
            if (mat == null) return;

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
        }

        private void EnsureBeamAudio()
        {
            if (!enableBeamAudio) return;

            if (_humSource == null)
            {
                var audioRoot = new GameObject("PathfinderBeamAudio");
                audioRoot.transform.SetParent(transform, false);

                _humSource = audioRoot.AddComponent<AudioSource>();
                _humSource.playOnAwake = false;
                _humSource.loop = true;
                _humSource.spatialBlend = 0f;
                _humSource.dopplerLevel = 0f;

                _zapSource = audioRoot.AddComponent<AudioSource>();
                _zapSource.playOnAwake = false;
                _zapSource.loop = false;
                _zapSource.spatialBlend = 0f;
                _zapSource.dopplerLevel = 0f;
            }

            if (_runtimeHumClip == null)
            {
                _runtimeHumClip = beamHumClip != null
                    ? beamHumClip
                    : RtgPathfinderBeamSfx.CreateBeamHumLoop();
                _ownsRuntimeHumClip = beamHumClip == null;
                _humSource.clip = _runtimeHumClip;
            }

            if (_runtimeZapClip == null)
            {
                _runtimeZapClip = vaporizeClip != null
                    ? vaporizeClip
                    : RtgPathfinderBeamSfx.CreateVaporizeZap();
                _ownsRuntimeZapClip = vaporizeClip == null;
            }
        }

        private void UpdateBeamAudio()
        {
            if (!enableBeamAudio)
            {
                StopBeamHum();
                return;
            }

            EnsureBeamAudio();
            if (_humSource == null) return;

            bool shouldHum = BeamIntensity > 0.04f;
            if (shouldHum)
            {
                _humSource.volume = beamHumVolume * BeamIntensity;
                _humSource.pitch = Mathf.Lerp(0.92f, 1.05f, BeamIntensity);
                if (!_humPlaying)
                {
                    _humSource.Play();
                    _humPlaying = true;
                }
            }
            else
            {
                StopBeamHum();
            }
        }

        private void StopBeamHum()
        {
            if (_humSource == null || !_humPlaying) return;
            _humSource.Stop();
            _humPlaying = false;
        }

        private void PlayVaporizeSfx(int vaporizedCount)
        {
            if (!enableBeamAudio || vaporizedCount <= 0) return;

            EnsureBeamAudio();
            if (_zapSource == null) return;
            if (Time.time - _lastVaporizeSfxTime < vaporizeMinInterval) return;

            _lastVaporizeSfxTime = Time.time;
            float burst = Mathf.Clamp01(vaporizedCount / 3f);
            _zapSource.pitch = Mathf.Lerp(0.95f, 1.15f, burst);
            _zapSource.PlayOneShot(_runtimeZapClip, vaporizeVolume * (0.65f + burst * 0.35f));
        }

        private void UpdateBeamHaptics()
        {
            if (!enableBeamHaptics)
            {
                _wasBeamArmed = false;
                return;
            }

#if !UNITY_EDITOR
            if (!Application.isMobilePlatform) return;
#endif

            bool armed = BeamIntensity > 0.12f;
            bool justArmed = armed && !_wasBeamArmed;
            bool intervalElapsed = armed && Time.time - _lastHapticTime >= beamHapticInterval;

            if (justArmed || intervalElapsed)
            {
                _lastHapticTime = Time.time;
                RtgDeviceHaptics.PulseLight(beamHapticDurationMs);
            }

            _wasBeamArmed = armed;
        }

        private static void DestroyRuntimeClip(AudioClip clip, bool ownsClip)
        {
            if (!ownsClip || clip == null) return;
            if (Application.isPlaying) Object.Destroy(clip);
            else Object.DestroyImmediate(clip);
        }

        private void OnDestroy()
        {
            DestroyMaterial(_beamMaterial);
            DestroyMaterial(_beamGlowMaterial);
            DestroyRuntimeClip(_runtimeHumClip, _ownsRuntimeHumClip);
            DestroyRuntimeClip(_runtimeZapClip, _ownsRuntimeZapClip);
        }

        private static void DestroyMaterial(Material mat)
        {
            if (mat == null) return;
            if (Application.isPlaying) Destroy(mat);
            else DestroyImmediate(mat);
        }
    }
}
