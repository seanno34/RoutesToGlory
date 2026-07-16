using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Sci-fi exhaust per nozzle: cavity head spheres + thrust-driven cone plume.
    /// Cone replaces particle flames for a cleaner mobile-friendly look.
    /// </summary>
    public class RtgGliderAfterburner : MonoBehaviour
    {
        [Range(0f, 0.5f)]
        public float minVisibleThrust = 0.06f;

        [Tooltip("When true, cone mesh plumes replace particle flame streaks.")]
        public bool useConePlume = true;

        public float colorIntensity = 2.2f;

        private float _flameLengthScale = 1f;
        private float _plumeVisibilityScale = 1f;
        private float _lastThrust01;
        private float _lastColorMph;
        private float _colorMaxMph = 99f;
        private RtgExhaustColorStop[] _colorStops;
        private bool _forceCavityPreview;
        private float _cavityPreviewMph;
        private int _cavityTuneStopIndex = -1;
        private bool _forceFlamePreview;
        private float _flamePreviewMph;

        private readonly List<EngineVfx> _engines = new();
        private Material _streakMaterial;
        private Material _flipbookMaterial;
        private Material _nozzleMaterial;
        private Material _cavityFillMaterial;
        private Material _cavityCoreMaterial;
        private Material _plumeMaterial;

        private struct EngineVfx
        {
            public string SocketName;
            public ParticleSystem FlameStreak;
            public ParticleSystem FlipbookGlow;
            public Transform NozzleCavityRoot;
            public Transform NozzleCavityOuter;
            public Transform NozzleCavityCore;
            public Transform PlumeOuter;
            public Transform PlumeCore;
            public MeshRenderer CavityOuterRenderer;
            public MeshRenderer CavityCoreRenderer;
            public MeshRenderer PlumeOuterRenderer;
            public MeshRenderer PlumeCoreRenderer;
            public Transform NozzleBloom;
            public Material NozzleMaterial;
            public Material CavityOuterMaterial;
            public Material CavityCoreMaterial;
            public Material PlumeOuterMaterial;
            public Material PlumeCoreMaterial;
            public Material StreakMaterial;
            public Material GlowMaterial;
            public MaterialPropertyBlock CavityOuterBlock;
            public MaterialPropertyBlock CavityCoreBlock;
            public MaterialPropertyBlock PlumeOuterBlock;
            public MaterialPropertyBlock PlumeCoreBlock;
            public RtgEngineCavityTuning CavityTuning;
            public float Weight;
            public float SizeScale;
        }

        public void SetEngineCavityTunings(
            RtgEngineCavityTuning main,
            RtgEngineCavityTuning left,
            RtgEngineCavityTuning right)
        {
            for (int i = 0; i < _engines.Count; i++)
            {
                EngineVfx engine = _engines[i];
                engine.CavityTuning = ResolveCavityTuningForSocket(
                    engine.SocketName,
                    main,
                    left,
                    right);
                _engines[i] = engine;
            }

            RefreshPresentation();
        }

        public void RefreshPresentation()
        {
            SetThrust(_lastThrust01, _lastColorMph);
        }

        public void SetExhaustColorProfile(RtgExhaustColorStop[] stops, float maxMph)
        {
            _colorMaxMph = Mathf.Max(1f, maxMph);
            _colorStops = RtgExhaustColorProfile.NormalizeStops(stops, _colorMaxMph);
            SetThrust(_lastThrust01, _lastColorMph);

            if (_forceCavityPreview)
                RefreshCavityPreview();
        }

        public void SetCavityPreview(bool enabled, float previewMph = 0f, int tuneStopIndex = -1)
        {
            _forceCavityPreview = enabled;
            _cavityPreviewMph = Mathf.Max(0f, previewMph);
            _cavityTuneStopIndex = enabled ? tuneStopIndex : -1;
            RefreshPresentation();
        }

        public void SetFlamePreview(bool enabled, float previewMph = 0f)
        {
            _forceFlamePreview = enabled;
            _flamePreviewMph = Mathf.Max(0f, previewMph);
            RefreshPresentation();
        }

        private void RefreshCavityPreview()
        {
            ThrustPalette palette = BuildTunePalette(_cavityPreviewMph, _cavityTuneStopIndex);
            float heat = Mathf.Clamp01(_cavityPreviewMph / Mathf.Max(1f, _colorMaxMph));
            float plumeDrive = Mathf.Max(0.65f, heat * 0.9f);
            foreach (EngineVfx engine in _engines)
            {
                if (engine.NozzleCavityRoot == null)
                    continue;

                engine.NozzleCavityRoot.gameObject.SetActive(true);
                ApplyCavityLayout(
                    engine,
                    heat,
                    plumeDrive,
                    palette,
                    showPlume: useConePlume && _forceFlamePreview,
                    colorMph: _cavityPreviewMph,
                    tuneStopIndex: _cavityTuneStopIndex);
            }
        }

        private static RtgEngineCavityTuning ResolveCavityTuningForSocket(
            string socketName,
            RtgEngineCavityTuning main,
            RtgEngineCavityTuning left,
            RtgEngineCavityTuning right)
        {
            if (string.Equals(socketName, RtgGliderExhaustSockets.LeftSocketName, StringComparison.Ordinal))
                return left.Clamped();
            if (string.Equals(socketName, RtgGliderExhaustSockets.RightSocketName, StringComparison.Ordinal))
                return right.Clamped();
            return main.Clamped();
        }

        public void SetFlameLengthScale(float scale)
        {
            _flameLengthScale = Mathf.Clamp(scale, 0.15f, 2.5f);
            SetThrust(_lastThrust01, _lastColorMph);
        }

        /// <summary>
        /// Boosts plume length only (mobile zoom compensation). Cavity head size stays in tuned meters.
        /// </summary>
        public void SetPlumeVisibilityScale(float scale)
        {
            _plumeVisibilityScale = Mathf.Max(1f, scale);
            SetThrust(_lastThrust01, _lastColorMph);
        }

        public void SetVfxWorldScale(float scale) => SetPlumeVisibilityScale(scale);

        public void Configure(Transform mainEngine, Transform leftEngine, Transform rightEngine, float shipSizeMeters)
        {
            ClearEngines();
            EnsureMaterials();

            float s = Mathf.Max(6f, shipSizeMeters);
            if (mainEngine != null) AddEngine(mainEngine, 1f, s);
            if (leftEngine != null) AddEngine(leftEngine, 0.58f, s * 0.82f);
            if (rightEngine != null) AddEngine(rightEngine, 0.58f, s * 0.82f);

            SetFlameLengthScale(_flameLengthScale);
            SetThrust(0f, 0f);
            if (_engines.Count == 0)
            {
                Debug.LogError(
                    "[RTG] Afterburner has 0 engine VFX stacks — exhaust shaders/materials failed to load. " +
                    "Check Resources/RTG_PlayerShip/RTG_Exhaust*.mat are included in the build.");
            }
            else
            {
                Debug.Log($"[RTG] Afterburner configured with {_engines.Count} engine VFX stacks.");
            }
        }

        public void SetThrust(float thrust01, float colorMph = -1f)
        {
            _lastThrust01 = thrust01;
            if (colorMph >= 0f)
                _lastColorMph = colorMph;

            float heat = Mathf.Clamp01(_lastColorMph / Mathf.Max(1f, _colorMaxMph));
            float active = thrust01 <= minVisibleThrust
                ? 0f
                : Mathf.InverseLerp(minVisibleThrust, 1f, Mathf.Clamp01(thrust01));
            bool exhaustOn = active > 0.01f || _lastColorMph > 0.5f;

            ThrustPalette palette = BuildPalette(_lastColorMph);
            float weightSum = 0f;
            foreach (EngineVfx engine in _engines)
                weightSum += engine.Weight;

            foreach (EngineVfx engine in _engines)
            {
                float share = weightSum > 0f ? engine.Weight / weightSum : 0f;
                ApplyEngineThrust(engine, active, heat, share, palette, exhaustOn);
            }
        }

        private void ApplyEngineThrust(
            EngineVfx engine,
            float active,
            float heat,
            float share,
            ThrustPalette palette,
            bool exhaustOn)
        {
            bool showPlume = useConePlume && (_forceCavityPreview
                ? _forceFlamePreview
                : exhaustOn);

            if (!useConePlume)
            {
                bool showFlames = _forceCavityPreview ? _forceFlamePreview : exhaustOn;
                bool useFlamePreviewPalette = _forceFlamePreview && _forceCavityPreview;
                float flameMph = useFlamePreviewPalette ? _flamePreviewMph : _lastColorMph;
                float flameHeat = Mathf.Clamp01(flameMph / Mathf.Max(1f, _colorMaxMph));
                ThrustPalette flamePalette = useFlamePreviewPalette ? BuildPalette(_flamePreviewMph) : palette;
                float intensity = Mathf.Max(active, heat * 0.85f);
                if (useFlamePreviewPalette)
                    intensity = Mathf.Max(0.45f, flameHeat * 0.85f);

                float flameRate = 22 * intensity * share;
                float glowRate = 12 * intensity * share;
                UpdateParticleSystem(engine.FlameStreak, flameRate, flamePalette.FlameStart, flamePalette.FlameEnd, showFlames);
                UpdateParticleSystem(engine.FlipbookGlow, glowRate, flamePalette.GlowStart, flamePalette.GlowEnd, showFlames);
                ApplyParticleTint(engine.FlameStreak, engine.StreakMaterial, flamePalette.FlameStart, flameHeat);
                ApplyParticleTint(engine.FlipbookGlow, engine.GlowMaterial, flamePalette.GlowStart, flameHeat);
            }
            else
            {
                HideParticleSystems(engine);
            }

            if (engine.NozzleCavityRoot != null)
            {
                bool showCavity = exhaustOn || _forceCavityPreview;
                engine.NozzleCavityRoot.gameObject.SetActive(showCavity);
                if (showCavity)
                {
                    float cavityMph = _forceCavityPreview
                        ? _cavityPreviewMph
                        : (exhaustOn ? _lastColorMph : 0f);
                    float cavityHeat = Mathf.Clamp01(cavityMph / Mathf.Max(1f, _colorMaxMph));
                    int tuneStopIndex = _forceCavityPreview ? _cavityTuneStopIndex : -1;
                    ThrustPalette cavityPalette = BuildTunePalette(cavityMph, tuneStopIndex);
                    float plumeDrive = _forceCavityPreview
                        ? Mathf.Max(0.65f, active, cavityHeat * 0.9f)
                        : active;
                    ApplyCavityLayout(
                        engine,
                        cavityHeat,
                        plumeDrive,
                        cavityPalette,
                        showPlume,
                        colorMph: cavityMph,
                        tuneStopIndex: tuneStopIndex);
                }
            }

            if (engine.NozzleBloom != null)
                engine.NozzleBloom.gameObject.SetActive(false);
        }

        private static void HideParticleSystems(EngineVfx engine)
        {
            if (engine.FlameStreak != null && engine.FlameStreak.gameObject.activeSelf)
                engine.FlameStreak.gameObject.SetActive(false);
            if (engine.FlipbookGlow != null && engine.FlipbookGlow.gameObject.activeSelf)
                engine.FlipbookGlow.gameObject.SetActive(false);
        }

        private void ApplyCavityLayout(
            EngineVfx engine,
            float heat,
            float plumeDrive01,
            ThrustPalette palette,
            bool showPlume,
            float colorMph,
            int tuneStopIndex)
        {
            RtgEngineCavityTuning tuning = engine.CavityTuning.sizeMeters > 0f
                ? engine.CavityTuning
                : RtgEngineCavityTuning.Default;

            float meterToLocal = 1f;
            if (engine.NozzleCavityRoot != null && engine.NozzleCavityRoot.parent != null)
            {
                float parentScale = engine.NozzleCavityRoot.parent.lossyScale.x;
                meterToLocal = 1f / Mathf.Max(0.001f, parentScale);
            }

            float nozzleScale = engine.Weight < 0.9f ? 0.84f : 1f;
            float diameter = tuning.sizeMeters * meterToLocal * nozzleScale * (0.92f + 0.08f * heat);

            engine.NozzleCavityRoot.localPosition = new Vector3(
                tuning.offsetXMeters * meterToLocal,
                tuning.offsetYMeters * meterToLocal,
                tuning.depthOffsetMeters * meterToLocal);

            // During color-stop preview, skip per-engine intensity on cavity tints so RGB sliders
            // stay responsive (main often runs intensity 5x vs 0.2 on the wing nozzles).
            float cavityColorGain = tuneStopIndex >= 0 ? 1f : tuning.intensity;

            if (engine.NozzleCavityOuter != null)
            {
                engine.NozzleCavityOuter.localScale = Vector3.one * diameter;
                ApplyOrbTint(
                    engine.CavityOuterRenderer,
                    engine.CavityOuterMaterial,
                    engine.CavityOuterBlock,
                    palette.CavityOuter * cavityColorGain);
            }

            if (engine.NozzleCavityCore != null)
            {
                engine.NozzleCavityCore.localScale = Vector3.one * diameter * tuning.coreRatio;
                ApplyOrbTint(
                    engine.CavityCoreRenderer,
                    engine.CavityCoreMaterial,
                    engine.CavityCoreBlock,
                    palette.CavityCore * cavityColorGain);
            }

            float plumeActive = plumeDrive01 <= minVisibleThrust
                ? 0f
                : Mathf.InverseLerp(minVisibleThrust, 1f, Mathf.Clamp01(plumeDrive01));
            ResolvePlumeLength(
                colorMph,
                tuneStopIndex,
                out float plumeMaxMeters,
                out float plumeLengthScale);
            float plumeLength = plumeMaxMeters
                * meterToLocal
                * Mathf.Pow(plumeActive, tuning.plumeLengthPower)
                * plumeLengthScale
                * _plumeVisibilityScale;
            float baseWidth = diameter * tuning.plumeBaseWidthScale;
            float coreWidth = baseWidth * tuning.plumeCoreWidthRatio;
            float plumeAttachZ = diameter * 0.42f;
            Vector3 plumeLocalPos = new Vector3(
                tuning.plumeOffsetXMeters * meterToLocal,
                tuning.plumeOffsetYMeters * meterToLocal,
                plumeAttachZ + tuning.plumeOffsetZMeters * meterToLocal);

            Color plumeOuterColor = palette.PlumeOuter * tuning.intensity;
            Color plumeCoreColor = palette.PlumeCore * tuning.intensity;

            if (engine.PlumeOuter != null)
            {
                bool visible = showPlume && plumeLength > 0.02f;
                engine.PlumeOuter.gameObject.SetActive(visible);
                if (visible)
                {
                    engine.PlumeOuter.localPosition = plumeLocalPos;
                    engine.PlumeOuter.localScale = new Vector3(baseWidth, baseWidth, plumeLength);
                    ApplyOrbTint(
                        engine.PlumeOuterRenderer,
                        engine.PlumeOuterMaterial,
                        engine.PlumeOuterBlock,
                        plumeOuterColor);
                }
            }

            if (engine.PlumeCore != null)
            {
                bool visible = showPlume && plumeLength > 0.04f;
                engine.PlumeCore.gameObject.SetActive(visible);
                if (visible)
                {
                    float coreLength = plumeLength * 0.88f;
                    engine.PlumeCore.localPosition = plumeLocalPos;
                    engine.PlumeCore.localScale = new Vector3(coreWidth, coreWidth, coreLength);
                    ApplyOrbTint(
                        engine.PlumeCoreRenderer,
                        engine.PlumeCoreMaterial,
                        engine.PlumeCoreBlock,
                        plumeCoreColor);
                }
            }
        }

        private void ResolvePlumeLength(
            float colorMph,
            int tuneStopIndex,
            out float plumeMaxMeters,
            out float plumeLengthScale)
        {
            RtgExhaustColorStop[] stops = _colorStops ?? RtgExhaustColorProfile.CreateDefaultStops();
            float maxMph = Mathf.Max(1f, _colorMaxMph);

            if (tuneStopIndex >= 0 && tuneStopIndex < stops.Length)
            {
                RtgExhaustColorStop stop = stops[tuneStopIndex];
                plumeMaxMeters = RtgExhaustColorProfile.GetPlumeMaxLengthMeters(stop);
                plumeLengthScale = RtgExhaustColorProfile.GetPlumeLengthScale(stop);
                return;
            }

            plumeMaxMeters = RtgExhaustColorProfile.SamplePlumeMaxLengthMeters(colorMph, stops, maxMph);
            plumeLengthScale = RtgExhaustColorProfile.SamplePlumeLengthScale(colorMph, stops, maxMph);
        }

        private void ApplyOrbTint(
            MeshRenderer renderer,
            Material material,
            MaterialPropertyBlock propertyBlock,
            Color hdrColor)
        {
            if (renderer == null || material == null)
                return;

            hdrColor.a = 1f;
            ApplyAdditiveTint(material, hdrColor);
            renderer.sharedMaterial = material;

            if (propertyBlock == null)
                return;

            propertyBlock.SetColor("_BaseColor", hdrColor);
            if (material.HasProperty("_Color"))
                propertyBlock.SetColor("_Color", hdrColor);
            if (material.HasProperty("_TintColor"))
                propertyBlock.SetColor("_TintColor", hdrColor);
            if (material.HasProperty("_EmissionColor"))
                propertyBlock.SetColor("_EmissionColor", hdrColor);
            renderer.SetPropertyBlock(propertyBlock);
        }

        private static void ApplyParticleTint(
            ParticleSystem ps,
            Material material,
            Color tint,
            float heat)
        {
            if (ps == null) return;

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = false;

            if (material != null)
                ApplyAdditiveTint(material, tint);

            var main = ps.main;
            Color tail = Color.Lerp(new Color(1f, 0.25f, 0.02f), new Color(0.05f, 0.35f, 1f), heat);
            main.startColor = new ParticleSystem.MinMaxGradient(tint, tail);
        }

        private static void ApplyAdditiveTint(Material material, Color color)
        {
            if (material == null) return;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color);
            material.SetInt("_Cull", (int)CullMode.Off);
        }

        private static void UpdateParticleSystem(
            ParticleSystem ps,
            float rate,
            Color startColor,
            Color endColor,
            bool playing)
        {
            if (ps == null) return;

            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(startColor, endColor);

            var emission = ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);

            if (playing && !ps.isPlaying) ps.Play();
            else if (!playing && ps.isPlaying)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private ThrustPalette BuildPalette(float mph)
        {
            return BuildTunePalette(mph, -1);
        }

        private ThrustPalette BuildTunePalette(float mph, int tuneStopIndex)
        {
            RtgExhaustColorStop[] stops = _colorStops ?? RtgExhaustColorProfile.CreateDefaultStops();
            float maxMph = Mathf.Max(1f, _colorMaxMph);
            float cappedMph = Mathf.Clamp(mph, 0f, maxMph);
            float rawHeat = Mathf.Clamp01(cappedMph / maxMph);
            float heat = Mathf.Pow(rawHeat, 0.75f);
            float i = colorIntensity;

            Color SampleChannel(System.Func<RtgExhaustColorStop, Color> pickColor)
            {
                if (tuneStopIndex >= 0 && tuneStopIndex < stops.Length)
                    return pickColor(stops[tuneStopIndex]);

                return RtgExhaustColorProfile.Sample(cappedMph, stops, maxMph, pickColor);
            }

            Color cavityOuterBase = SampleChannel(s => s.cavityOuter);
            Color cavityCoreBase = SampleChannel(s => s.cavityCore);
            Color flameBase = SampleChannel(s => s.flame);
            Color glowBase = SampleChannel(s => s.glow);
            Color plumeOuterBase = SampleChannel(s => ResolvePlumeOuter(s));
            Color plumeCoreBase = SampleChannel(s => ResolvePlumeCore(s));
            Color flameTailBase = tuneStopIndex >= 0 && tuneStopIndex < stops.Length
                ? Color.Lerp(ResolvePlumeOuter(stops[tuneStopIndex]), ResolvePlumeCore(stops[tuneStopIndex]), 0.45f)
                : RtgExhaustColorProfile.Sample(
                    cappedMph * 0.85f,
                    stops,
                    maxMph,
                    s => Color.Lerp(ResolvePlumeOuter(s), ResolvePlumeCore(s), 0.45f));

            Color flameStart = flameBase * Mathf.Lerp(2.4f, 4f, heat) * i;
            Color flameEnd = flameTailBase * Mathf.Lerp(1.6f, 3f, heat) * i;
            Color glowStart = glowBase * Mathf.Lerp(2f, 3.5f, heat) * i;
            Color glowEnd = flameTailBase * Mathf.Lerp(1.4f, 2.6f, heat) * i;
            float plumeBold = Mathf.Lerp(2.4f, 4f, heat) * i;
            Color plumeOuter = plumeOuterBase * plumeBold;
            Color plumeCore = plumeCoreBase * Mathf.Lerp(2f, 3.5f, heat) * i;

            float cavityOuterBold = tuneStopIndex >= 0
                ? colorIntensity
                : Mathf.Lerp(5f, 9f, rawHeat) * i;
            float cavityCoreBold = tuneStopIndex >= 0
                ? colorIntensity
                : Mathf.Lerp(6f, 11f, rawHeat) * i;
            Color cavityOuter = cavityOuterBase * cavityOuterBold;
            Color cavityCore = cavityCoreBase * cavityCoreBold;

            Color nozzleRim = Color.Lerp(flameBase, glowBase, 0.35f)
                * Mathf.Lerp(2.2f, 4f, heat) * i;

            return new ThrustPalette
            {
                FlameStart = flameStart,
                FlameEnd = flameEnd,
                GlowStart = glowStart,
                GlowEnd = glowEnd,
                PlumeOuter = plumeOuter,
                PlumeCore = plumeCore,
                CavityOuter = cavityOuter,
                CavityCore = cavityCore,
                Nozzle = nozzleRim,
            };
        }

        private static Color ResolvePlumeOuter(RtgExhaustColorStop stop)
        {
            return stop.plumeOuter.r + stop.plumeOuter.g + stop.plumeOuter.b > 0.01f
                ? stop.plumeOuter
                : stop.flame;
        }

        private static Color ResolvePlumeCore(RtgExhaustColorStop stop)
        {
            return stop.plumeCore.r + stop.plumeCore.g + stop.plumeCore.b > 0.01f
                ? stop.plumeCore
                : stop.glow;
        }

        private struct ThrustPalette
        {
            public Color FlameStart;
            public Color FlameEnd;
            public Color GlowStart;
            public Color GlowEnd;
            public Color PlumeOuter;
            public Color PlumeCore;
            public Color CavityOuter;
            public Color CavityCore;
            public Color Nozzle;
        }

        private void AddEngine(Transform nozzle, float weight, float sizeScale)
        {
            if (_cavityFillMaterial == null || _cavityCoreMaterial == null)
                return;

            if (!useConePlume && (_streakMaterial == null || _flipbookMaterial == null))
                return;

            ClearNozzleVfxChildren(nozzle);

            ParticleSystem flameStreak = null;
            ParticleSystem flipbookGlow = null;
            Material streakMat = null;
            Material glowMat = null;

            if (!useConePlume)
            {
                flameStreak = CreateFlameStreakSystem(nozzle, sizeScale, weight, out streakMat);
                flipbookGlow = CreateFlipbookSystem(nozzle, sizeScale, weight, out glowMat);
            }

            _engines.Add(new EngineVfx
            {
                SocketName = nozzle.name,
                FlameStreak = flameStreak,
                FlipbookGlow = flipbookGlow,
                NozzleCavityRoot = CreateNozzleCavity(
                    nozzle,
                    out Transform outer,
                    out Transform core,
                    out Transform plumeOuter,
                    out Transform plumeCore,
                    out Material outerMat,
                    out Material coreMat,
                    out Material plumeOuterMat,
                    out Material plumeCoreMat,
                    out MeshRenderer outerRenderer,
                    out MeshRenderer coreRenderer,
                    out MeshRenderer plumeOuterRenderer,
                    out MeshRenderer plumeCoreRenderer),
                NozzleCavityOuter = outer,
                NozzleCavityCore = core,
                PlumeOuter = plumeOuter,
                PlumeCore = plumeCore,
                CavityOuterRenderer = outerRenderer,
                CavityCoreRenderer = coreRenderer,
                PlumeOuterRenderer = plumeOuterRenderer,
                PlumeCoreRenderer = plumeCoreRenderer,
                NozzleBloom = CreateNozzleBloom(nozzle, sizeScale, out Material nozzleMat),
                NozzleMaterial = nozzleMat,
                CavityOuterMaterial = outerMat,
                CavityCoreMaterial = coreMat,
                PlumeOuterMaterial = plumeOuterMat,
                PlumeCoreMaterial = plumeCoreMat,
                StreakMaterial = streakMat,
                GlowMaterial = glowMat,
                CavityOuterBlock = new MaterialPropertyBlock(),
                CavityCoreBlock = new MaterialPropertyBlock(),
                PlumeOuterBlock = new MaterialPropertyBlock(),
                PlumeCoreBlock = new MaterialPropertyBlock(),
                CavityTuning = RtgEngineCavityTuning.Default,
                Weight = weight,
                SizeScale = sizeScale,
            });
        }

        private ParticleSystem CreateFlameStreakSystem(
            Transform nozzle,
            float sizeScale,
            float weight,
            out Material instanceMaterial)
        {
            instanceMaterial = null;
            var go = new GameObject($"{nozzle.name}_Flame");
            go.transform.SetParent(nozzle, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;
            main.maxParticles = 40;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f * sizeScale, 0.18f * sizeScale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 3.5f;
            shape.radius = 0.006f * sizeScale * (0.75f + weight * 0.25f);
            shape.radiusThickness = 0.35f;
            shape.arc = 18f;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-10f * sizeScale, -18f * sizeScale);

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.85f, 1f, 0.15f));

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = false;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.65f;
            noise.scrollSpeed = 1.4f;
            noise.damping = true;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 1.35f;
            renderer.normalDirection = 1f;
            instanceMaterial = new Material(_streakMaterial) { name = "RTG_ExhaustStreak_Runtime" };
            ApplyAdditiveTint(instanceMaterial, new Color(1f, 0.4f, 0.05f) * colorIntensity * 2f);
            renderer.sharedMaterial = instanceMaterial;
            renderer.sortingOrder = 9;

            _ = weight;
            return ps;
        }

        private ParticleSystem CreateFlipbookSystem(
            Transform nozzle,
            float sizeScale,
            float weight,
            out Material instanceMaterial)
        {
            instanceMaterial = null;
            var go = new GameObject($"{nozzle.name}_Glow");
            go.transform.SetParent(nozzle, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;
            main.maxParticles = 20;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * sizeScale, 0.28f * sizeScale);

            var emission = ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 5f;
            shape.radius = 0.01f * sizeScale;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-6f * sizeScale, -12f * sizeScale);

            var texAnim = ps.textureSheetAnimation;
            texAnim.enabled = true;
            texAnim.mode = ParticleSystemAnimationMode.Grid;
            texAnim.numTilesX = 4;
            texAnim.numTilesY = 4;
            texAnim.animation = ParticleSystemAnimationType.SingleRow;
            texAnim.rowIndex = 0;
            texAnim.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 1f);
            texAnim.cycleCount = 1;

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            instanceMaterial = new Material(_flipbookMaterial) { name = "RTG_ExhaustFlipbook_Runtime" };
            ApplyAdditiveTint(instanceMaterial, new Color(1f, 0.35f, 0.03f) * colorIntensity * 1.8f);
            renderer.sharedMaterial = instanceMaterial;
            renderer.sortingOrder = 8;

            _ = weight;
            return ps;
        }

        private Transform CreateNozzleCavity(
            Transform nozzle,
            out Transform outer,
            out Transform core,
            out Transform plumeOuter,
            out Transform plumeCore,
            out Material outerMaterial,
            out Material coreMaterial,
            out Material plumeOuterMaterial,
            out Material plumeCoreMaterial,
            out MeshRenderer outerRenderer,
            out MeshRenderer coreRenderer,
            out MeshRenderer plumeOuterRenderer,
            out MeshRenderer plumeCoreRenderer)
        {
            var root = new GameObject($"{nozzle.name}_CavityRoot");
            root.transform.SetParent(nozzle, false);
            root.transform.localRotation = Quaternion.identity;
            root.SetActive(false);

            outer = CreateCavitySphere(
                root.transform,
                "Outer",
                _cavityFillMaterial,
                sortingOrder: 11,
                out outerMaterial,
                out outerRenderer);
            core = CreateCavitySphere(
                root.transform,
                "Core",
                _cavityCoreMaterial,
                sortingOrder: 12,
                out coreMaterial,
                out coreRenderer);
            plumeOuter = CreatePlumeCone(
                root.transform,
                "PlumeOuter",
                _plumeMaterial,
                sortingOrder: 9,
                out plumeOuterMaterial,
                out plumeOuterRenderer);
            plumeCore = CreatePlumeCone(
                root.transform,
                "PlumeCore",
                _plumeMaterial,
                sortingOrder: 10,
                out plumeCoreMaterial,
                out plumeCoreRenderer);

            plumeOuter.gameObject.SetActive(false);
            plumeCore.gameObject.SetActive(false);
            return root.transform;
        }

        private Transform CreatePlumeCone(
            Transform parent,
            string name,
            Material template,
            int sortingOrder,
            out Material instanceMaterial,
            out MeshRenderer meshRenderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = RtgMeshPrimitives.ExhaustCone;

            meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            instanceMaterial = new Material(template) { name = $"RTG_{name}_Runtime" };
            ApplyExhaustTexture(instanceMaterial, RtgGliderExhaustTextures.CavityFill);
            meshRenderer.sharedMaterial = instanceMaterial;
            meshRenderer.sortingOrder = sortingOrder;
            ApplyOrbTint(meshRenderer, instanceMaterial, null, Color.black);
            return go.transform;
        }

        private Transform CreateCavitySphere(
            Transform parent,
            string name,
            Material template,
            int sortingOrder,
            out Material instanceMaterial,
            out MeshRenderer meshRenderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = RtgMeshPrimitives.Sphere;

            meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            instanceMaterial = new Material(template) { name = $"RTG_{name}_Runtime" };
            meshRenderer.sharedMaterial = instanceMaterial;
            meshRenderer.sortingOrder = sortingOrder;
            ApplyOrbTint(meshRenderer, instanceMaterial, null, Color.black);
            return go.transform;
        }

        private Transform CreateNozzleBloom(Transform nozzle, float sizeScale, out Material instanceMaterial)
        {
            var go = new GameObject($"{nozzle.name}_Nozzle");
            go.transform.SetParent(nozzle, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.08f * sizeScale;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = RtgMeshPrimitives.VerticalQuad;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            instanceMaterial = new Material(_nozzleMaterial) { name = "RTG_NozzleBloom_Runtime" };
            renderer.sharedMaterial = instanceMaterial;
            renderer.sortingOrder = 10;
            go.SetActive(false);
            return go.transform;
        }

        private void EnsureMaterials()
        {
            if (_streakMaterial == null)
            {
                _streakMaterial = LoadExhaustMaterial(
                    "RTG_PlayerShip/RTG_ExhaustStreak",
                    "RTG_ExhaustStreak",
                    RtgGliderExhaustTextures.FlameStreak);
            }

            if (_flipbookMaterial == null)
            {
                _flipbookMaterial = LoadExhaustMaterial(
                    "RTG_PlayerShip/RTG_ExhaustFlipbook",
                    "RTG_ExhaustFlipbook",
                    RtgGliderExhaustTextures.FlameFlipbook);
            }

            if (_nozzleMaterial == null)
            {
                _nozzleMaterial = LoadExhaustMaterial(
                    "RTG_PlayerShip/RTG_ExhaustNozzle",
                    "RTG_ExhaustNozzle",
                    RtgGliderExhaustTextures.SoftGlow);
            }

            if (_cavityFillMaterial == null)
            {
                _cavityFillMaterial = LoadExhaustMaterial(
                    "RTG_PlayerShip/RTG_CavityFill",
                    "RTG_CavityFill",
                    texture: null);
            }

            if (_cavityCoreMaterial == null)
            {
                _cavityCoreMaterial = LoadExhaustMaterial(
                    "RTG_PlayerShip/RTG_CavityCore",
                    "RTG_CavityCore",
                    texture: null);
            }

            if (_plumeMaterial == null)
            {
                _plumeMaterial = LoadExhaustMaterial(
                    "RTG_PlayerShip/RTG_CavityFill",
                    "RTG_ExhaustPlume",
                    RtgGliderExhaustTextures.CavityFill);
            }

            if (_cavityFillMaterial == null)
            {
                Debug.LogError(
                    "[RTG] Cavity fill material missing — exhaust VFX cannot be created.");
            }
        }

        private static Material LoadExhaustMaterial(string resourcePath, string runtimeName, Texture2D texture)
        {
            Material template = Resources.Load<Material>(resourcePath);
            if (template != null && template.shader != null && template.shader.isSupported)
            {
                var material = new Material(template) { name = runtimeName };
                ApplyExhaustTexture(material, texture);
                return material;
            }

            Material fallback = texture != null
                ? CreateAdditiveMaterial(runtimeName, texture)
                : CreateCavityOrbMaterial(runtimeName);
            if (fallback != null)
            {
                Debug.LogWarning(
                    $"[RTG] Loaded exhaust material '{runtimeName}' via Shader.Find fallback. " +
                    $"Prefer Resources asset at {resourcePath}.");
                return fallback;
            }

            Debug.LogError(
                $"[RTG] Failed to load exhaust material '{runtimeName}' from Resources ({resourcePath}) " +
                "and Shader.Find fallback.");
            return null;
        }

        private static void ApplyExhaustTexture(Material material, Texture2D texture)
        {
            if (material == null || texture == null)
                return;

            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
        }

        private static Material CreateCavityOrbMaterial(string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Mobile/Particles/Additive");
            if (shader == null)
                return null;

            var material = new Material(shader)
            {
                name = name,
                renderQueue = 3000,
            };

            if (shader.name.Contains("Universal Render Pipeline/Unlit"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 2f);
                material.SetFloat("_AlphaClip", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.One);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.SetColor("_BaseColor", Color.black);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_BLENDMODE_ADDITIVE");
                material.SetOverrideTag("RenderType", "Transparent");
            }
            else if (shader.name.Contains("Particles"))
            {
                material.SetColor("_TintColor", Color.black);
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.One);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_Cull", (int)CullMode.Off);
            }
            else
            {
                material.color = Color.black;
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.One);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_Cull", (int)CullMode.Off);
            }

            return material;
        }

        private static Material CreateAdditiveMaterial(string name, Texture2D texture)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) return null;

            var material = new Material(shader) { name = name, mainTexture = texture };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.renderQueue = 3000;
            return material;
        }

        private static void ClearNozzleVfxChildren(Transform nozzle)
        {
            if (nozzle == null)
                return;

            for (int i = nozzle.childCount - 1; i >= 0; i--)
                DestroySafe(nozzle.GetChild(i).gameObject);
        }

        private void ClearEngines()
        {
            foreach (EngineVfx engine in _engines)
            {
                DestroySafe(engine.FlameStreak);
                DestroySafe(engine.FlipbookGlow);
                DestroySafe(engine.StreakMaterial);
                DestroySafe(engine.GlowMaterial);
                if (engine.NozzleBloom != null)
                {
                    if (engine.NozzleMaterial != null) DestroySafe(engine.NozzleMaterial);
                    DestroySafe(engine.NozzleBloom.gameObject);
                }

                if (engine.NozzleCavityRoot != null)
                {
                    if (engine.CavityOuterMaterial != null) DestroySafe(engine.CavityOuterMaterial);
                    if (engine.CavityCoreMaterial != null) DestroySafe(engine.CavityCoreMaterial);
                    if (engine.PlumeOuterMaterial != null) DestroySafe(engine.PlumeOuterMaterial);
                    if (engine.PlumeCoreMaterial != null) DestroySafe(engine.PlumeCoreMaterial);
                    DestroySafe(engine.NozzleCavityRoot.gameObject);
                }
            }
            _engines.Clear();
        }

        private static void DestroySafe(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private void OnDestroy()
        {
            ClearEngines();
            DestroySafe(_streakMaterial);
            DestroySafe(_flipbookMaterial);
            DestroySafe(_nozzleMaterial);
            DestroySafe(_cavityFillMaterial);
            DestroySafe(_cavityCoreMaterial);
            DestroySafe(_plumeMaterial);
        }
    }
}
