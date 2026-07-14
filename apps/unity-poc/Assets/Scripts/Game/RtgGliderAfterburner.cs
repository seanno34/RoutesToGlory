using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Layered sci-fi exhaust per nozzle: nozzle bloom + stretched flame core + animated flipbook halo.
    /// Warm yellow at low thrust, electric blue at full burn. Uses additive particles + soft sprites.
    /// </summary>
    public class RtgGliderAfterburner : MonoBehaviour
    {
        [Range(0f, 0.5f)]
        public float minVisibleThrust = 0.06f;

        public int maxFlameEmissionRate = 22;
        public int maxGlowEmissionRate = 12;
        public float colorIntensity = 2.2f;

        private const float BaseStreakLengthScale = 1.35f;
        private const float BaseStreakLifetimeMin = 0.08f;
        private const float BaseStreakLifetimeMax = 0.16f;
        private const float BaseStreakVelocityMin = -10f;
        private const float BaseStreakVelocityMax = -18f;
        private const float BaseGlowLifetimeMin = 0.07f;
        private const float BaseGlowLifetimeMax = 0.13f;
        private const float BaseGlowVelocityMin = -6f;
        private const float BaseGlowVelocityMax = -12f;

        private float _flameLengthScale = 1f;
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
        private MaterialPropertyBlock _cavityOrbPropertyBlock;

        private struct EngineVfx
        {
            public ParticleSystem FlameStreak;
            public ParticleSystem FlipbookGlow;
            public Transform NozzleCavityRoot;
            public Transform NozzleCavityOuter;
            public Transform NozzleCavityCore;
            public MeshRenderer CavityOuterRenderer;
            public MeshRenderer CavityCoreRenderer;
            public Transform NozzleBloom;
            public Material NozzleMaterial;
            public Material CavityOuterMaterial;
            public Material CavityCoreMaterial;
            public Material StreakMaterial;
            public Material GlowMaterial;
            public RtgEngineCavityTuning CavityTuning;
            public float Weight;
            public float SizeScale;
        }

        public void SetEngineCavityTunings(
            RtgEngineCavityTuning main,
            RtgEngineCavityTuning left,
            RtgEngineCavityTuning right)
        {
            ApplyCavityTuningToEngine(0, main.Clamped());
            ApplyCavityTuningToEngine(1, left.Clamped());
            ApplyCavityTuningToEngine(2, right.Clamped());

            if (_forceCavityPreview)
                RefreshCavityPreview();
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
            SetThrust(_lastThrust01, _lastColorMph);
        }

        public void SetFlamePreview(bool enabled, float previewMph = 0f)
        {
            _forceFlamePreview = enabled;
            _flamePreviewMph = Mathf.Max(0f, previewMph);
            SetThrust(_lastThrust01, _lastColorMph);
        }

        private void RefreshCavityPreview()
        {
            ThrustPalette palette = BuildTunePalette(_cavityPreviewMph, _cavityTuneStopIndex);
            float heat = Mathf.Clamp01(_cavityPreviewMph / Mathf.Max(1f, _colorMaxMph));
            foreach (EngineVfx engine in _engines)
            {
                if (engine.NozzleCavityRoot == null)
                    continue;

                engine.NozzleCavityRoot.gameObject.SetActive(true);
                ApplyCavityLayout(engine, heat, palette);
            }
        }

        private void ApplyCavityTuningToEngine(int index, RtgEngineCavityTuning tuning)
        {
            if (index < 0 || index >= _engines.Count)
                return;

            EngineVfx engine = _engines[index];
            engine.CavityTuning = tuning;
            _engines[index] = engine;
        }

        public void SetFlameLengthScale(float scale)
        {
            _flameLengthScale = Mathf.Clamp(scale, 0.15f, 2.5f);
            foreach (EngineVfx engine in _engines)
                ApplyFlameLength(engine);
        }

        private void ApplyFlameLength(EngineVfx engine)
        {
            float length = _flameLengthScale;
            float size = engine.SizeScale;

            if (engine.FlameStreak != null)
            {
                var streakMain = engine.FlameStreak.main;
                streakMain.startLifetime = new ParticleSystem.MinMaxCurve(
                    BaseStreakLifetimeMin * length,
                    BaseStreakLifetimeMax * length);

                var streakVelocity = engine.FlameStreak.velocityOverLifetime;
                streakVelocity.z = new ParticleSystem.MinMaxCurve(
                    BaseStreakVelocityMin * size * length,
                    BaseStreakVelocityMax * size * length);

                var streakRenderer = engine.FlameStreak.GetComponent<ParticleSystemRenderer>();
                streakRenderer.lengthScale = BaseStreakLengthScale * length;
            }

            if (engine.FlipbookGlow != null)
            {
                var glowMain = engine.FlipbookGlow.main;
                glowMain.startLifetime = new ParticleSystem.MinMaxCurve(
                    BaseGlowLifetimeMin * length,
                    BaseGlowLifetimeMax * length);

                var glowVelocity = engine.FlipbookGlow.velocityOverLifetime;
                glowVelocity.z = new ParticleSystem.MinMaxCurve(
                    BaseGlowVelocityMin * size * length,
                    BaseGlowVelocityMax * size * length);
            }
        }

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
            Debug.Log($"[RTG] Afterburner configured with {_engines.Count} engine VFX stacks.");
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
            bool showFlames = _forceCavityPreview
                ? _forceFlamePreview
                : exhaustOn;
            bool useFlamePreviewPalette = _forceFlamePreview && _forceCavityPreview;
            float flameMph = useFlamePreviewPalette ? _flamePreviewMph : _lastColorMph;
            float flameHeat = Mathf.Clamp01(flameMph / Mathf.Max(1f, _colorMaxMph));
            ThrustPalette flamePalette = useFlamePreviewPalette ? BuildPalette(_flamePreviewMph) : palette;

            float intensity = Mathf.Max(active, heat * 0.85f);
            if (useFlamePreviewPalette)
                intensity = Mathf.Max(0.45f, flameHeat * 0.85f);

            float flameRate = maxFlameEmissionRate * intensity * share;
            float glowRate = maxGlowEmissionRate * intensity * share;

            UpdateParticleSystem(engine.FlameStreak, flameRate, flamePalette.FlameStart, flamePalette.FlameEnd, showFlames);
            UpdateParticleSystem(engine.FlipbookGlow, glowRate, flamePalette.GlowStart, flamePalette.GlowEnd, showFlames);
            ApplyParticleTint(engine.FlameStreak, engine.StreakMaterial, flamePalette.FlameStart, flameHeat);
            ApplyParticleTint(engine.FlipbookGlow, engine.GlowMaterial, flamePalette.GlowStart, flameHeat);

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
                    ApplyCavityLayout(engine, cavityHeat, cavityPalette);
                }
            }

            if (engine.NozzleBloom != null)
                engine.NozzleBloom.gameObject.SetActive(false);
        }

        private void ApplyCavityLayout(EngineVfx engine, float heat, ThrustPalette palette)
        {
            RtgEngineCavityTuning tuning = engine.CavityTuning.sizeMeters > 0f
                ? engine.CavityTuning
                : RtgEngineCavityTuning.Default;

            float nozzleScale = engine.Weight < 0.9f ? 0.84f : 1f;
            float diameter = tuning.sizeMeters * nozzleScale * (0.92f + 0.08f * heat);

            engine.NozzleCavityRoot.localPosition = new Vector3(
                tuning.offsetXMeters,
                tuning.offsetYMeters,
                tuning.depthOffsetMeters);

            if (engine.NozzleCavityOuter != null)
            {
                engine.NozzleCavityOuter.localScale = Vector3.one * diameter;
                ApplyOrbTint(engine.CavityOuterRenderer, engine.CavityOuterMaterial, palette.CavityOuter * tuning.intensity);
            }

            if (engine.NozzleCavityCore != null)
            {
                engine.NozzleCavityCore.localScale = Vector3.one * diameter * tuning.coreRatio;
                ApplyOrbTint(engine.CavityCoreRenderer, engine.CavityCoreMaterial, palette.CavityCore * tuning.intensity);
            }
        }

        private void ApplyOrbTint(MeshRenderer renderer, Material material, Color hdrColor)
        {
            if (material == null)
                return;

            hdrColor.a = 1f;
            ApplyAdditiveTint(material, hdrColor);

            if (renderer == null)
                return;

            _cavityOrbPropertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_cavityOrbPropertyBlock);
            _cavityOrbPropertyBlock.SetColor("_BaseColor", hdrColor);
            if (material.HasProperty("_Color"))
                _cavityOrbPropertyBlock.SetColor("_Color", hdrColor);
            if (material.HasProperty("_TintColor"))
                _cavityOrbPropertyBlock.SetColor("_TintColor", hdrColor);
            renderer.SetPropertyBlock(_cavityOrbPropertyBlock);
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
            Color flameTailBase = tuneStopIndex >= 0 && tuneStopIndex < stops.Length
                ? Color.Lerp(stops[tuneStopIndex].flame, stops[tuneStopIndex].glow, 0.45f)
                : RtgExhaustColorProfile.Sample(
                    cappedMph * 0.85f,
                    stops,
                    maxMph,
                    s => Color.Lerp(s.flame, s.glow, 0.45f));

            Color flameStart = flameBase * Mathf.Lerp(2.4f, 4f, heat) * i;
            Color flameEnd = flameTailBase * Mathf.Lerp(1.6f, 3f, heat) * i;
            Color glowStart = glowBase * Mathf.Lerp(2f, 3.5f, heat) * i;
            Color glowEnd = flameTailBase * Mathf.Lerp(1.4f, 2.6f, heat) * i;

            float cavityOuterBold = Mathf.Lerp(5f, 9f, rawHeat) * i;
            float cavityCoreBold = Mathf.Lerp(6f, 11f, rawHeat) * i;
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
                CavityOuter = cavityOuter,
                CavityCore = cavityCore,
                Nozzle = nozzleRim,
            };
        }

        private struct ThrustPalette
        {
            public Color FlameStart;
            public Color FlameEnd;
            public Color GlowStart;
            public Color GlowEnd;
            public Color CavityOuter;
            public Color CavityCore;
            public Color Nozzle;
        }

        private void AddEngine(Transform nozzle, float weight, float sizeScale)
        {
            if (_streakMaterial == null) return;

            _engines.Add(new EngineVfx
            {
                FlameStreak = CreateFlameStreakSystem(nozzle, sizeScale, weight, out Material streakMat),
                FlipbookGlow = CreateFlipbookSystem(nozzle, sizeScale, weight, out Material glowMat),
                NozzleCavityRoot = CreateNozzleCavity(
                    nozzle,
                    out Transform outer,
                    out Transform core,
                    out Material outerMat,
                    out Material coreMat,
                    out MeshRenderer outerRenderer,
                    out MeshRenderer coreRenderer),
                NozzleCavityOuter = outer,
                NozzleCavityCore = core,
                CavityOuterRenderer = outerRenderer,
                CavityCoreRenderer = coreRenderer,
                NozzleBloom = CreateNozzleBloom(nozzle, sizeScale),
                NozzleMaterial = null,
                CavityOuterMaterial = outerMat,
                CavityCoreMaterial = coreMat,
                StreakMaterial = streakMat,
                GlowMaterial = glowMat,
                CavityTuning = RtgEngineCavityTuning.Default,
                Weight = weight,
                SizeScale = sizeScale,
            });

            EngineVfx last = _engines[_engines.Count - 1];
            if (last.NozzleBloom != null)
                last.NozzleMaterial = last.NozzleBloom.GetComponent<MeshRenderer>().material;
            _engines[_engines.Count - 1] = last;
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
            renderer.material = instanceMaterial;
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
            renderer.material = instanceMaterial;
            renderer.sortingOrder = 8;

            _ = weight;
            return ps;
        }

        private Transform CreateNozzleCavity(
            Transform nozzle,
            out Transform outer,
            out Transform core,
            out Material outerMaterial,
            out Material coreMaterial,
            out MeshRenderer outerRenderer,
            out MeshRenderer coreRenderer)
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

            return root.transform;
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
            meshRenderer.material = instanceMaterial;
            meshRenderer.sortingOrder = sortingOrder;
            ApplyOrbTint(meshRenderer, instanceMaterial, Color.black);
            return go.transform;
        }

        private Transform CreateNozzleBloom(Transform nozzle, float sizeScale)
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
            renderer.material = new Material(_nozzleMaterial) { name = "RTG_NozzleBloom_Runtime" };
            renderer.sortingOrder = 10;
            go.SetActive(false);
            return go.transform;
        }

        private void EnsureMaterials()
        {
            if (_streakMaterial == null)
            {
                _streakMaterial = CreateAdditiveMaterial("RTG_ExhaustStreak", RtgGliderExhaustTextures.FlameStreak);
                if (_streakMaterial != null)
                    _streakMaterial.mainTexture = RtgGliderExhaustTextures.FlameStreak;
            }

            if (_flipbookMaterial == null)
            {
                _flipbookMaterial = CreateAdditiveMaterial("RTG_ExhaustFlipbook", RtgGliderExhaustTextures.FlameFlipbook);
                if (_flipbookMaterial != null)
                    _flipbookMaterial.mainTexture = RtgGliderExhaustTextures.FlameFlipbook;
            }

            if (_nozzleMaterial == null)
            {
                _nozzleMaterial = CreateAdditiveMaterial("RTG_ExhaustNozzle", RtgGliderExhaustTextures.SoftGlow);
                if (_nozzleMaterial != null)
                    _nozzleMaterial.mainTexture = RtgGliderExhaustTextures.SoftGlow;
            }

            if (_cavityFillMaterial == null)
                _cavityFillMaterial = CreateCavityOrbMaterial("RTG_CavityFill");

            if (_cavityCoreMaterial == null)
                _cavityCoreMaterial = CreateCavityOrbMaterial("RTG_CavityCore");
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
                    DestroySafe(engine.NozzleCavityRoot.gameObject);
                }
            }
            _engines.Clear();
        }

        private static void DestroySafe(Object obj)
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
        }
    }
}
