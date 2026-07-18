using UnityEngine;
using UnityEngine.Rendering;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Xenite vent vapor: ground-hugging orange mist + ember sparks + soft point light.
    /// Attached on every xenite deposit (claimed and unclaimed). Claim only intensifies the light.
    /// Not a force-field sphere — terrain stays visible through translucent billboard mist.
    /// </summary>
    public static class RtgXeniteClaimedVentVfx
    {
        public const string RootObjectName = "XeniteVentVfx";
        private const string LegacyRootObjectName = "ClaimedVentVfx";

        /// <summary>Bump when fixing invisible / broken builds so live sessions rebuild stale VFX.</summary>
        private const int VfxBuildVersion = 3;

        private const float BaseLightIntensity = 2.8f;
        private const float ClaimedLightIntensity = 3.6f;
        private const float BaseLightRange = 12f;
        private const float ClaimedLightRange = 14f;

        private static Material _mistMaterial;
        private static Material _emberMaterial;
        private static Texture2D _softCircleTexture;
        private static bool _loggedAttach;

        /// <summary>
        /// Spawns vent vapor under <paramref name="depositRoot"/> if missing or stale.
        /// Idempotent for current build version — never reconfigures playing ParticleSystems.
        /// Does not touch Tripo crystal materials.
        /// </summary>
        public static void EnsureBuilt(Transform depositRoot)
        {
            if (depositRoot == null)
                return;

            Transform existing = FindVentRoot(depositRoot);
            if (existing != null)
            {
                var tag = existing.GetComponent<RtgXeniteClaimedVentVfxTag>();
                if (tag != null && tag.BuildVersion >= VfxBuildVersion)
                    return;
                // DestroyImmediate so a same-frame Find/parent won't see a zombie duplicate.
                Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject(RootObjectName);
            root.transform.SetParent(depositRoot, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            // Keep world scale ~1 even if a Cesium / marker parent is non-unit.
            root.transform.localScale = InverseLossyScale(depositRoot.lossyScale);
            root.AddComponent<RtgXeniteClaimedVentVfxTag>().BuildVersion = VfxBuildVersion;

            ParticleSystem mist = CreateGroundMist(root.transform);
            ParticleSystem embers = CreateFloatingEmbers(root.transform);
            CreateVentLight(root.transform, claimed: false);

            if (!_loggedAttach)
            {
                _loggedAttach = true;
                int mistMax = mist != null ? mist.main.maxParticles : 0;
                int emberMax = embers != null ? embers.main.maxParticles : 0;
                Debug.Log(
                    $"[RTG] Xenite vent VFX attached — mist max={mistMax} embers max={emberMax} " +
                    $"(parent lossyScale={depositRoot.lossyScale})");
            }
        }

        /// <summary>
        /// Slightly brighten the vent light after a live claim. Does not recreate particles.
        /// </summary>
        public static void IntensifyForClaim(Transform depositRoot)
        {
            if (depositRoot == null)
                return;

            EnsureBuilt(depositRoot);
            Transform ventRoot = FindVentRoot(depositRoot);
            if (ventRoot == null)
                return;

            Transform lightTf = ventRoot.Find("VentLight");
            if (lightTf == null)
                return;

            Light light = lightTf.GetComponent<Light>();
            var pulse = lightTf.GetComponent<RtgXeniteClaimedVentPulse>();
            if (light == null)
                return;

            if (pulse != null)
                pulse.Bind(light, ClaimedLightIntensity, ClaimedLightRange);
            else
            {
                light.intensity = ClaimedLightIntensity;
                light.range = ClaimedLightRange;
            }
        }

        public static bool HasVentVfx(Transform depositRoot)
        {
            if (depositRoot == null)
                return false;
            Transform existing = FindVentRoot(depositRoot);
            if (existing == null)
                return false;
            var tag = existing.GetComponent<RtgXeniteClaimedVentVfxTag>();
            return tag != null && tag.BuildVersion >= VfxBuildVersion;
        }

        private static Transform FindVentRoot(Transform depositRoot)
        {
            Transform existing = depositRoot.Find(RootObjectName);
            if (existing != null)
                return existing;
            return depositRoot.Find(LegacyRootObjectName);
        }

        private static Vector3 InverseLossyScale(Vector3 lossy)
        {
            return new Vector3(
                SafeInv(lossy.x),
                SafeInv(lossy.y),
                SafeInv(lossy.z));
        }

        private static float SafeInv(float v) =>
            Mathf.Abs(v) < 1e-5f ? 1f : 1f / v;

        /// <summary>
        /// AddComponent&lt;ParticleSystem&gt; starts playing immediately (playOnAwake default).
        /// Must StopEmittingAndClear before mutating main.duration / other main-module props.
        /// </summary>
        private static ParticleSystem BeginParticleSetup(GameObject go)
        {
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            return ps;
        }

        private static ParticleSystem CreateGroundMist(Transform parent)
        {
            var go = new GameObject("GroundMist");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            ParticleSystem ps = BeginParticleSetup(go);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 22f;
            emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // Local (not Hierarchy): sizes stay in meters even if an ancestor scales later.
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.gravityModifier = 0f;
            main.maxParticles = 140;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.38f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Visible start alpha; colorOverLifetime alphas are multipliers (~1→0).
            main.startColor = new ParticleSystem.MinMaxGradient(
                HdrOrange(Mathf.Max(0.72f, RtgTerrainDepositGuards.MaxClaimedHaloAlpha)));
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 1.35f;
            shape.radiusThickness = 0.9f;
            shape.arc = 360f;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.45f));

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var mistGrad = new Gradient();
            mistGrad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.2f, 0.5f, 0.12f), 0f),
                    new GradientColorKey(new Color(1f, 0.38f, 0.08f), 0.5f),
                    new GradientColorKey(new Color(0.65f, 0.2f, 0.05f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.25f),
                    new GradientAlphaKey(0.55f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(mistGrad);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.05f, 0.22f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.22f;
            noise.frequency = 0.2f;
            noise.scrollSpeed = 0.1f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = true;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = GetMistMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = -2;
            renderer.sortingFudge = 4f;
            renderer.minParticleSize = 0.02f;
            renderer.maxParticleSize = 4f;
            renderer.allowRoll = true;

            ps.Play(true);
            return ps;
        }

        private static ParticleSystem CreateFloatingEmbers(Transform parent)
        {
            var go = new GameObject("FloatingEmbers");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            ParticleSystem ps = BeginParticleSetup(go);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 6f;
            emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.gravityModifier = 0f;
            main.maxParticles = 40;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(HdrOrange(0.95f));
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.7f;
            shape.radiusThickness = 1f;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var emberGrad = new Gradient();
            emberGrad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.35f, 0.55f, 0.1f), 0f),
                    new GradientColorKey(new Color(1.1f, 0.7f, 0.2f), 0.4f),
                    new GradientColorKey(new Color(0.9f, 0.4f, 0.1f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.45f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(emberGrad);

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;
            noise.damping = true;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = true;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GetEmberMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = -1;
            renderer.minParticleSize = 0.01f;
            renderer.maxParticleSize = 0.5f;

            ps.Play(true);
            return ps;
        }

        private static void CreateVentLight(Transform parent, bool claimed)
        {
            var go = new GameObject("VentLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            go.transform.localScale = Vector3.one;

            float intensity = claimed ? ClaimedLightIntensity : BaseLightIntensity;
            float range = claimed ? ClaimedLightRange : BaseLightRange;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = RtgTerrainDepositGuards.XeniteCanonicalColor;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = true;

            TryAddUniversalAdditionalLightData(go);

            var pulse = go.AddComponent<RtgXeniteClaimedVentPulse>();
            pulse.Bind(light, intensity, range);
        }

        private static void TryAddUniversalAdditionalLightData(GameObject go)
        {
            // URP attaches this automatically in recent versions; add if missing so the
            // point light is registered as an additional light.
            System.Type type = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime");
            if (type == null || go.GetComponent(type) != null)
                return;
            go.AddComponent(type);
        }

        private static Color HdrOrange(float alpha)
        {
            Color c = RtgTerrainDepositGuards.XeniteCanonicalColor;
            return new Color(c.r * 1.2f, c.g * 1.08f, c.b, Mathf.Clamp01(alpha));
        }

        private static Material GetMistMaterial()
        {
            if (_mistMaterial != null)
                return _mistMaterial;

            Texture2D soft = GetSoftCircleTexture();
            _mistMaterial = CreateParticleMaterial("RTG_XeniteVentMist", soft, additive: false);
            return _mistMaterial;
        }

        private static Material GetEmberMaterial()
        {
            if (_emberMaterial != null)
                return _emberMaterial;

            Texture2D glow = RtgGliderExhaustTextures.SoftGlow;
            if (glow == null)
                glow = GetSoftCircleTexture();
            _emberMaterial = CreateParticleMaterial("RTG_XeniteVentEmber", glow, additive: true);
            return _emberMaterial;
        }

        private static Material CreateParticleMaterial(string name, Texture2D texture, bool additive)
        {
            // Prefer Resources exhaust template (known-good URP particle setup), then Shader.Find.
            Material mat = TryCloneResourcesParticleMaterial(name, additive);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                    shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null)
                    shader = Shader.Find("Mobile/Particles/Alpha Blended");
                if (shader == null)
                {
                    Debug.LogError($"[RTG] Xenite vent VFX: no particle shader for '{name}'.");
                    return new Material(Shader.Find("Hidden/InternalErrorShader"));
                }

                mat = new Material(shader) { name = name };
                ConfigureBlend(mat, additive);
            }

            if (texture != null)
            {
                mat.mainTexture = texture;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", texture);
            }

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_TintColor"))
                mat.SetColor("_TintColor", Color.white);

            // Soft particles fade ground mist to invisible against Cesium depth — keep OFF.
            if (mat.HasProperty("_SoftParticlesEnabled"))
                mat.SetFloat("_SoftParticlesEnabled", 0f);
            if (mat.HasProperty("_CameraFadingEnabled"))
                mat.SetFloat("_CameraFadingEnabled", 0f);
            mat.DisableKeyword("_SOFTPARTICLES_ON");
            mat.DisableKeyword("_FADING_ON");

            mat.renderQueue = 3000;
            return mat;
        }

        private static Material TryCloneResourcesParticleMaterial(string name, bool additive)
        {
            // Exhaust flipbook/streak mats are proven URP Particles/Unlit assets in Resources.
            string path = additive ? "RTG_PlayerShip/RTG_ExhaustStreak" : "RTG_PlayerShip/RTG_ExhaustFlipbook";
            Material template = Resources.Load<Material>(path);
            if (template == null || template.shader == null || !template.shader.isSupported)
                return null;

            var mat = new Material(template) { name = name };
            ConfigureBlend(mat, additive);
            return mat;
        }

        private static void ConfigureBlend(Material mat, bool additive)
        {
            if (mat == null)
                return;

            if (additive)
            {
                if (mat.HasProperty("_Surface"))
                {
                    mat.SetFloat("_Surface", 1f);
                    mat.SetFloat("_Blend", 2f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_BLENDMODE_ADDITIVE");
                    mat.DisableKeyword("_BLENDMODE_ALPHA");
                }

                mat.SetInt("_SrcBlend", (int)BlendMode.One);
                mat.SetInt("_DstBlend", (int)BlendMode.One);
            }
            else
            {
                if (mat.HasProperty("_Surface"))
                {
                    mat.SetFloat("_Surface", 1f);
                    mat.SetFloat("_Blend", 0f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_BLENDMODE_ALPHA");
                    mat.DisableKeyword("_BLENDMODE_ADDITIVE");
                }

                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }

            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.SetOverrideTag("RenderType", "Transparent");
        }

        private static Texture2D GetSoftCircleTexture()
        {
            if (_softCircleTexture != null)
                return _softCircleTexture;

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RTG_XeniteVentSoftCircle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float radius = size * 0.5f;
            var center = new Vector2(radius, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center) / radius;
                    float radial = Mathf.Clamp01(1f - dist);
                    // Soft feathered disc — denser core so mist reads outdoors.
                    float alpha = Mathf.Pow(radial, 1.2f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, true);
            _softCircleTexture = texture;
            return _softCircleTexture;
        }
    }

    /// <summary>Marks a vent VFX instance with a build version for stale rebuilds.</summary>
    public sealed class RtgXeniteClaimedVentVfxTag : MonoBehaviour
    {
        public int BuildVersion;
    }

    /// <summary>
    /// Lazy Perlin pulse on the vent point light — never flashes.
    /// </summary>
    public sealed class RtgXeniteClaimedVentPulse : MonoBehaviour
    {
        private Light _light;
        private float _baseIntensity = 2.8f;
        private float _baseRange = 12f;
        private float _seed;

        public void Bind(Light light, float baseIntensity, float baseRange)
        {
            _light = light;
            _baseIntensity = Mathf.Clamp(baseIntensity, 1.5f, 5f);
            _baseRange = Mathf.Clamp(baseRange, 8f, 18f);
            if (_seed <= 0f)
                _seed = Random.Range(0f, 1000f);
        }

        private void LateUpdate()
        {
            if (_light == null)
                return;

            float t = Time.time * 0.18f + _seed;
            float intensityNoise = Mathf.PerlinNoise(t, _seed * 0.13f);
            float rangeNoise = Mathf.PerlinNoise(_seed * 0.17f, t * 0.85f);

            // ±15% intensity, ±10% range — slow volcanic breathe, no strobe.
            _light.intensity = _baseIntensity * (1f + (intensityNoise - 0.5f) * 0.3f);
            _light.range = _baseRange * (1f + (rangeNoise - 0.5f) * 0.2f);
        }
    }
}
