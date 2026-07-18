using UnityEngine;
using UnityEngine.Rendering;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Places the Tripo horizon planets on a fixed celestial sphere around the
    /// play-area / Cesium georeference origin (Douglas / Orin). No camera follow,
    /// parallax, physics, or shadows — environmental art only.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class RtgCelestialBodies : MonoBehaviour
    {
        public const string RootName = "CelestialBodies";
        public const string RingedPlanetName = "RingedPlanet";
        public const string RinglessPlanetName = "RinglessPlanet";
        public const string RingedModelName = "green_ringed_planet_3d_model";
        public const string RinglessModelName = "earth_planet_3d_model";

        private const string CelestialShaderName = "RoutesToGlory/CelestialBody";

        [System.Serializable]
        public class PlanetTuning
        {
            [Tooltip("Wrapper transform (RingedPlanet / RinglessPlanet).")]
            public Transform root;

            [Tooltip("Tripo model instance under the wrapper.")]
            public Transform model;

            [Tooltip("Uniform scale multiplier on top of apparent-diameter sizing.")]
            public float scale = 1f;

            [Tooltip("Angular diameter in degrees (Earth moon ≈ 0.5°).")]
            public float apparentDiameterDegrees = 5f;

            [Tooltip("Degrees above the local horizon (ENU +Y).")]
            public float elevationDegrees = 1f;

            [Tooltip("Azimuth degrees: 0 = North (+Z), 90 = East (+X).")]
            public float azimuthDegrees = 40f;

            [Tooltip("Spin around the view axis (degrees).")]
            public float rotationDegrees = 0f;

            [Tooltip("Ring tilt for the ringed planet (degrees). Ignored when hasRings is false.")]
            public float ringAngleDegrees = 28f;

            [Tooltip("Whether ringAngleDegrees is applied.")]
            public bool hasRings = false;

            [Range(0.05f, 4f)]
            public float brightness = 1.15f;

            public Color tint = Color.white;
        }

        [Header("Celestial sphere")]
        [Tooltip("Distance from play-area origin (meters). Keep within camera far clip.")]
        public float distanceMeters = 88000f;

        [Header("Planets")]
        public PlanetTuning ringedPlanet = new PlanetTuning
        {
            scale = 1f,
            apparentDiameterDegrees = 5f, // ~10× moon
            elevationDegrees = 1f,        // ~60% above horizon at this diameter
            azimuthDegrees = 40f,
            rotationDegrees = 12f,
            ringAngleDegrees = 28f,
            hasRings = true,
            brightness = 1.2f,
            tint = new Color(0.92f, 1f, 0.88f, 1f)
        };

        public PlanetTuning ringlessPlanet = new PlanetTuning
        {
            scale = 1f,
            apparentDiameterDegrees = 2.1f, // ~42% of Planet A
            elevationDegrees = 1.5f,
            azimuthDegrees = 215f, // ~175° opposite Planet A
            rotationDegrees = -8f,
            ringAngleDegrees = 0f,
            hasRings = false,
            brightness = 1.05f,
            tint = new Color(0.85f, 0.92f, 1f, 1f)
        };

        [Header("Atmosphere fade")]
        [Tooltip("Softens planet contrast near the horizon (0 = none, 1 = strong).")]
        [Range(0f, 1f)]
        public float horizonHaze = 0.35f;

        [Tooltip("Faint rim / bloom-like edge lift.")]
        [Range(0f, 1f)]
        public float rimGlow = 0.22f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");
        private static readonly int HazeId = Shader.PropertyToID("_HorizonHaze");
        private static readonly int ElevationId = Shader.PropertyToID("_ElevationDegrees");

        private bool _stripDone;

        private void OnEnable()
        {
            EnsureHierarchy();
            StripNonVisualComponents();
            ApplyPlacement();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            EnsureHierarchy();
            ApplyPlacement();
        }

        /// <summary>
        /// Reparents the Tripo PrefabInstances under CelestialBodies and applies
        /// azimuth / elevation / distance / scale from serialized fields.
        /// </summary>
        public void ApplyPlacement()
        {
            EnsureHierarchy();
            PlacePlanet(ringedPlanet);
            PlacePlanet(ringlessPlanet);
            ApplyPlanetMaterials(ringedPlanet);
            ApplyPlanetMaterials(ringlessPlanet);
            EnsureCamerasCanSeePlanets();
        }

        public void EnsureHierarchy()
        {
            if (transform.name != RootName)
                transform.name = RootName;

            Transform ringedRoot = EnsureChild(transform, RingedPlanetName);
            Transform ringlessRoot = EnsureChild(transform, RinglessPlanetName);

            ringedPlanet.root = ringedRoot;
            ringlessPlanet.root = ringlessRoot;

            ringedPlanet.model = EnsureModelUnder(ringedRoot, RingedModelName, ringedPlanet.model);
            ringlessPlanet.model = EnsureModelUnder(ringlessRoot, RinglessModelName, ringlessPlanet.model);
        }

        private static Transform EnsureChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null) return existing;

            var go = new GameObject(childName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static Transform EnsureModelUnder(Transform wrapper, string modelName, Transform current)
        {
            if (current != null && current.name == modelName)
            {
                if (current.parent != wrapper)
                    current.SetParent(wrapper, false);
                return current;
            }

            Transform existing = wrapper.Find(modelName);
            if (existing != null) return existing;

            // Already nested deeper (rare).
            foreach (Transform child in wrapper.GetComponentsInChildren<Transform>(true))
            {
                if (child != wrapper && child.name == modelName)
                {
                    if (child.parent != wrapper)
                        child.SetParent(wrapper, true);
                    return child;
                }
            }

            Transform found = FindSceneTransformByName(modelName);
            if (found == null) return current;

            found.SetParent(wrapper, false);
            found.localPosition = Vector3.zero;
            found.localRotation = Quaternion.identity;
            found.localScale = Vector3.one;
            return found;
        }

        private static Transform FindSceneTransformByName(string objectName)
        {
#if UNITY_2023_1_OR_NEWER
            Transform[] all = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            Transform[] all = Object.FindObjectsOfType<Transform>(true);
#endif
            foreach (Transform t in all)
            {
                if (t.name == objectName) return t;
            }

            return null;
        }

        private void PlacePlanet(PlanetTuning planet)
        {
            if (planet == null || planet.root == null) return;

            Vector3 direction = DirectionFromAzimuthElevation(
                planet.azimuthDegrees, planet.elevationDegrees);
            planet.root.localPosition = direction * distanceMeters;
            planet.root.localRotation = Quaternion.identity;
            planet.root.localScale = Vector3.one;

            if (planet.model == null) return;

            float meshRadius = EstimateLocalMeshRadius(planet.model);
            if (meshRadius < 1e-4f) meshRadius = 0.5f;

            float halfAngleRad = Mathf.Max(0.01f, planet.apparentDiameterDegrees) * 0.5f
                * Mathf.Deg2Rad;
            float targetRadius = distanceMeters * Mathf.Tan(halfAngleRad);
            float uniform = (targetRadius / meshRadius) * Mathf.Max(0.01f, planet.scale);
            planet.model.localScale = Vector3.one * uniform;
            planet.model.localPosition = Vector3.zero;

            // Face the play origin so the disc reads against the sky; ring tilt + spin on top.
            Quaternion faceOrigin = Quaternion.LookRotation(-direction, Vector3.up);
            Quaternion art = Quaternion.Euler(
                planet.hasRings ? planet.ringAngleDegrees : 0f,
                planet.rotationDegrees,
                0f);
            planet.model.localRotation = faceOrigin * art;
        }

        /// <summary>
        /// Cesium georeference local ENU: +X east, +Y up, +Z north.
        /// Azimuth 0 = north, 90 = east.
        /// </summary>
        public static Vector3 DirectionFromAzimuthElevation(float azimuthDegrees, float elevationDegrees)
        {
            float az = azimuthDegrees * Mathf.Deg2Rad;
            float el = elevationDegrees * Mathf.Deg2Rad;
            float cosEl = Mathf.Cos(el);
            return new Vector3(
                Mathf.Sin(az) * cosEl,
                Mathf.Sin(el),
                Mathf.Cos(az) * cosEl).normalized;
        }

        private static float EstimateLocalMeshRadius(Transform model)
        {
            Vector3 savedScale = model.localScale;
            Quaternion savedRot = model.localRotation;
            Vector3 savedPos = model.localPosition;
            model.localScale = Vector3.one;
            model.localRotation = Quaternion.identity;
            model.localPosition = Vector3.zero;

            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool has = false;

            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                Bounds mb = filter.sharedMesh.bounds;
                Matrix4x4 toModel = model.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                EncapsulateBounds(ref localBounds, ref has, mb, toModel);
            }

#if UNITY_EDITOR
            if (!has)
            {
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    Bounds wb = r.bounds;
                    Matrix4x4 toModel = model.worldToLocalMatrix;
                    EncapsulateWorldBounds(ref localBounds, ref has, wb, toModel);
                }
            }
#endif

            model.localScale = savedScale;
            model.localRotation = savedRot;
            model.localPosition = savedPos;

            if (!has) return 0.5f;
            return Mathf.Max(localBounds.extents.x, localBounds.extents.y, localBounds.extents.z);
        }

        private static void EncapsulateBounds(
            ref Bounds localBounds, ref bool has, Bounds meshBounds, Matrix4x4 toModel)
        {
            Vector3 c = meshBounds.center;
            Vector3 e = meshBounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 local = toModel.MultiplyPoint3x4(corner);
                if (!has)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    has = true;
                }
                else localBounds.Encapsulate(local);
            }
        }

        private static void EncapsulateWorldBounds(
            ref Bounds localBounds, ref bool has, Bounds worldBounds, Matrix4x4 worldToModel)
        {
            Vector3 c = worldBounds.center;
            Vector3 e = worldBounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 local = worldToModel.MultiplyPoint3x4(corner);
                if (!has)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    has = true;
                }
                else localBounds.Encapsulate(local);
            }
        }

        public void StripNonVisualComponents()
        {
            if (_stripDone && Application.isPlaying) return;

            StripOn(ringedPlanet != null ? ringedPlanet.model : null);
            StripOn(ringlessPlanet != null ? ringlessPlanet.model : null);
            _stripDone = true;
        }

        private static void StripOn(Transform model)
        {
            if (model == null) return;

            foreach (Collider col in model.GetComponentsInChildren<Collider>(true))
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }

            foreach (Rigidbody rb in model.GetComponentsInChildren<Rigidbody>(true))
            {
                if (Application.isPlaying) Object.Destroy(rb);
                else Object.DestroyImmediate(rb);
            }

            foreach (ParticleSystem ps in model.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (Application.isPlaying) Object.Destroy(ps.gameObject);
                else Object.DestroyImmediate(ps.gameObject);
            }

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.allowOcclusionWhenDynamic = false;
            }
        }

        private void ApplyPlanetMaterials(PlanetTuning planet)
        {
            if (planet == null || planet.model == null) return;

            Shader celestial = Shader.Find(CelestialShaderName);
            if (celestial == null) return;

            Renderer[] renderers = planet.model.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                Material[] shared = r.sharedMaterials;
                if (shared == null || shared.Length == 0) continue;

                Material[] next = new Material[shared.Length];
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    Material src = shared[i];
                    if (src == null)
                    {
                        next[i] = null;
                        continue;
                    }

                    Material mat = src;
                    if (src.shader != celestial)
                    {
                        Texture albedo = null;
                        if (src.HasProperty(BaseMapId))
                            albedo = src.GetTexture(BaseMapId);
                        if (albedo == null)
                            albedo = src.mainTexture;

                        mat = new Material(celestial)
                        {
                            name = src.name.Replace(" (Instance)", "") + "_Celestial"
                        };
                        if (albedo != null && mat.HasProperty(BaseMapId))
                            mat.SetTexture(BaseMapId, albedo);
                        changed = true;
                    }

                    if (mat.HasProperty(BaseColorId))
                        mat.SetColor(BaseColorId, planet.tint);
                    if (mat.HasProperty(BrightnessId))
                        mat.SetFloat(BrightnessId, planet.brightness);
                    if (mat.HasProperty(RimStrengthId))
                        mat.SetFloat(RimStrengthId, rimGlow);
                    if (mat.HasProperty(HazeId))
                        mat.SetFloat(HazeId, horizonHaze);
                    if (mat.HasProperty(ElevationId))
                        mat.SetFloat(ElevationId, planet.elevationDegrees);

                    next[i] = mat;
                }

                if (changed)
                    r.sharedMaterials = next;
                else
                {
                    // Properties only — avoid reallocating material slots.
                    for (int i = 0; i < next.Length; i++)
                    {
                        if (next[i] == null) continue;
                        if (next[i].HasProperty(BaseColorId))
                            next[i].SetColor(BaseColorId, planet.tint);
                        if (next[i].HasProperty(BrightnessId))
                            next[i].SetFloat(BrightnessId, planet.brightness);
                        if (next[i].HasProperty(RimStrengthId))
                            next[i].SetFloat(RimStrengthId, rimGlow);
                        if (next[i].HasProperty(HazeId))
                            next[i].SetFloat(HazeId, horizonHaze);
                        if (next[i].HasProperty(ElevationId))
                            next[i].SetFloat(ElevationId, planet.elevationDegrees);
                    }
                }
            }
        }

        private void EnsureCamerasCanSeePlanets()
        {
            float needFar = distanceMeters * 1.15f;
#if UNITY_2023_1_OR_NEWER
            Camera[] cameras = Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            Camera[] cameras = Object.FindObjectsOfType<Camera>(true);
#endif
            foreach (Camera cam in cameras)
            {
                if (cam == null) continue;
                if (cam.farClipPlane < needFar)
                    cam.farClipPlane = needFar;
            }
        }
    }
}
