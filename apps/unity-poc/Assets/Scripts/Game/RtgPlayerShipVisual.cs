using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Player glider presentation: Tripo imported hull (preferred) or procedural blockout,
    /// blob shadow, and particle exhaust.
    ///
    /// TRIPO HULL GUARDRAILS (Jul 2026 — do not regress):
    /// • Editor Play uses <c>Assets/TripoModels/.../futuristic_fighter_3d_model.fbx</c> (embedded
    ///   Tripo PBR textures). Device builds MUST NOT rely on that path — it is not in the player.
    /// • Device loads <see cref="ResourcesTripoHullPrefabPath"/> or <see cref="ResourcesTripoHullLoadPath"/>
    ///   from Resources. Mesh + material refs must stay under <c>Assets/Resources/RTG_PlayerShip/TripoGlider/</c>.
    /// • <see cref="IsDeviceReadyHullPrefab"/> requires non-null materials on the baked prefab.
    ///   A Renderer alone is not enough — null materials or missing mesh refs look “fine” in logs but
    ///   render nothing on device.
    /// • Textured hull on device requires an albedo in Resources (<see cref="ResourcesTripoAlbedoPath"/>
    ///   or PNGs in <see cref="ResourcesTripoTextureFolder"/>). URP/Lit without _BaseMap is NOT usable;
    ///   see <see cref="IsUsableTripoMaterial"/>.
    /// • Always run <see cref="FitImportedHullScale"/> after instantiate (device prefab scale is reset to
    ///   one on the Model root before fit). Do not skip for “baked” prefabs.
    /// • Hull orientation comes from <c>rtg-ship-tuning.json</c> — do not force zero euler or auto-orient
    ///   over saved tuning in <see cref="RtgPlayerLocation.BuildShipVisual"/>.
    /// • Re-bake via Routes to Glory → Regenerate Playable World or the mobile build preprocessor after
    ///   changing Tripo assets. See <c>RtgPlayerShipAssetSync</c>.
    /// </summary>
    public class RtgPlayerShipVisual : MonoBehaviour
    {
        private const string DefaultTripoHullAssetPath =
            "Assets/TripoModels/futuristic_fighter_3d_model/futuristic_fighter_3d_model.fbx";

        public const string ResourcesTripoHullLoadPath =
            "RTG_PlayerShip/TripoGlider/futuristic_fighter_3d_model";
        public const string ResourcesTripoHullPrefabPath =
            "RTG_PlayerShip/TripoGlider/TripoGlider";
        public const string ResourcesTripoTextureFolder =
            "RTG_PlayerShip/TripoGlider";
        public const string ResourcesTripoAlbedoPath =
            "RTG_PlayerShip/TripoGlider/TripoHull_Albedo";

        public const string ResourcesGliderTexturePath = "RTG_PlayerShip/glider_01";
        public const string ResourcesCockpitTexturePath = "RTG_PlayerShip/glider_cockpit_01";
        public const string ResourcesCockpitPortraitTexturePath =
            "RTG_PlayerShip/glider_cockpit_portrait_01";

        /// <summary>True when the device-safe Tripo hull is present in Resources.</summary>
        public static bool HasResourcesHull()
        {
            return IsValidHullPrefab(LoadResourcesHullPrefab());
        }

        /// <summary>
        /// Device runtime load order: baked prefab first, then Resources FBX fallback.
        /// REGRESSION: preferring raw FBX over prefab in editor is OK; on device only Resources paths work.
        /// </summary>
        public static GameObject LoadResourcesHullPrefab()
        {
            GameObject hull = Resources.Load<GameObject>(ResourcesTripoHullPrefabPath);
            if (IsDeviceReadyHullPrefab(hull))
                return hull;

            hull = Resources.Load<GameObject>(ResourcesTripoHullLoadPath);
            if (IsValidHullPrefab(hull))
                return hull;

            return null;
        }

        public static bool IsValidHullPrefab(GameObject prefab)
        {
            if (prefab == null)
                return false;

            Renderer renderer = prefab.GetComponentInChildren<Renderer>(true);
            if (renderer == null)
                return false;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>()
                ?? renderer.GetComponentInChildren<MeshFilter>(true);
            return meshFilter == null || meshFilter.sharedMesh != null;
        }

        /// <summary>
        /// Baked <c>TripoGlider.prefab</c> gate for builds. REGRESSION: do not weaken to IsValidHullPrefab only —
        /// we repeatedly shipped prefabs with fileID:0 materials or TripoModels mesh guids outside Resources.
        /// </summary>
        public static bool IsDeviceReadyHullPrefab(GameObject prefab)
        {
            if (!IsValidHullPrefab(prefab))
                return false;

            Renderer renderer = prefab.GetComponentInChildren<Renderer>(true);
            if (renderer == null)
                return false;

            Material[] materials = renderer.sharedMaterials;
            if (materials.Length == 0)
                return false;

            foreach (Material material in materials)
            {
                if (material == null)
                    return false;
            }

            return true;
        }

        [Tooltip("Optional concept-art texture (legacy; not used for imported Tripo hull).")]
        public Texture2D texture;

        [Tooltip("Wingspan in meters.")]
        public float sizeMeters = 24f;

        [Tooltip("Heading offset if the nose points backward (180 = flip).")]
        public float headingOffsetDegrees;

        [Tooltip("Lift above the marker anchor to avoid z-fighting with terrain.")]
        public float groundClearanceMeters = 1.2f;

        [Header("Imported hull (Tripo)")]
        [Tooltip("Tripo FBX/prefab. Auto-loads the default Tripo import path in the editor when unset.")]
        public GameObject importedHullPrefab;

        [Tooltip("Extra local rotation after import (fine-tune only; auto-orient handles the base pose).")]
        public Vector3 hullLocalEulerOffset;

        [Tooltip("Infer nose/wings/up from mesh bounds instead of hard-coded Tripo euler guesses.")]
        public bool autoOrientImportedHull = true;

        [Tooltip("Extra uniform scale multiplier after wingspan fit.")]
        public float hullScaleMultiplier = 1f;

        [Header("Motion")]
        [Tooltip("Max bank angle (degrees) when turning hard.")]
        public float maxBankDegrees = 22f;

        [Tooltip("Nose-up pitch (degrees) at full thrust.")]
        public float maxThrustPitchDegrees = 5f;

        private Transform _hullRoot;
        private Transform _attachmentsRoot;
        private RtgGliderExhaustSockets.SocketSet _exhaustSockets;
        private Renderer _renderer;
        private Material _hullMaterial;
        private RtgGliderBlobShadow _blobShadow;
        private RtgGliderAfterburner _afterburner;
        private Mesh _hullMesh;
        private bool _usingImportedHull;

        private float _bankDegrees;
        private float _pitchDegrees;
        private Transform _meshTransform;
        private Quaternion _baseMeshRotation = Quaternion.identity;
        private RtgGliderEngineMounts _engineMounts;
        private bool _useCustomEnginePorts;
        private RtgExhaustAnchor _mainAnchor = RtgGliderExhaustAnchors.DefaultMain;
        private RtgExhaustAnchor _leftAnchor = RtgGliderExhaustAnchors.DefaultLeft;
        private RtgExhaustAnchor _rightAnchor = RtgGliderExhaustAnchors.DefaultRight;
        private RtgEngineCavityTuning _cachedMainCavity = RtgEngineCavityTuning.Default;
        private RtgEngineCavityTuning _cachedLeftCavity = RtgEngineCavityTuning.Default;
        private RtgEngineCavityTuning _cachedRightCavity = RtgEngineCavityTuning.Default;
        private bool _hasCachedCavityTuning;

        public const float SocketSpanMinMeters = -8f;
        public const float SocketSpanMaxMeters = 8f;
        public const float SocketHeightMinMeters = -4f;
        public const float SocketHeightMaxMeters = 4f;
        public const float SocketDepthMinMeters = -8f;
        public const float SocketDepthMaxMeters = 4f;

        public bool IsReady => _hullRoot != null && (_usingImportedHull
            ? _renderer != null
            : _renderer != null && _hullMaterial != null && _hullMesh != null);

        public void Configure(
            Texture2D tex,
            float sizeM,
            float headingOffsetDeg = 0f,
            GameObject hullPrefab = null,
            Vector3 hullEulerOffset = default,
            bool autoOrientHull = true,
            RtgGliderEngineMounts engineMounts = default,
            bool useCustomEnginePorts = false,
            RtgExhaustAnchor mainAnchor = default,
            RtgExhaustAnchor leftAnchor = default,
            RtgExhaustAnchor rightAnchor = default)
        {
            texture = tex;
            sizeMeters = sizeM;
            headingOffsetDegrees = headingOffsetDeg;
            if (RtgPlayerShipVisual.IsValidHullPrefab(hullPrefab))
                importedHullPrefab = hullPrefab;
            else if (hullPrefab != null)
            {
                importedHullPrefab = null;
                Debug.LogWarning(
                    "[RTG] Ignoring invalid shipHullPrefab — no Renderer found. " +
                    "Falling back to Resources/RTG_PlayerShip/TripoGlider.");
            }
            hullLocalEulerOffset = hullEulerOffset;
            autoOrientImportedHull = autoOrientHull;
            if (RtgGliderEngineMounts.HasSavedPositions(engineMounts))
                _engineMounts = engineMounts;
            _useCustomEnginePorts = useCustomEnginePorts;
            if (useCustomEnginePorts)
            {
                _mainAnchor = mainAnchor.Clamped();
                _leftAnchor = leftAnchor.Clamped();
                _rightAnchor = rightAnchor.Clamped();
            }

            Rebuild();
        }

        public void ApplySingleExhaustAnchor(int engineIndex, RtgExhaustAnchor anchor)
        {
            anchor = anchor.Clamped();
            switch (engineIndex)
            {
                case 1:
                    _leftAnchor = anchor;
                    break;
                case 2:
                    _rightAnchor = anchor;
                    break;
                default:
                    _mainAnchor = anchor;
                    break;
            }

            _useCustomEnginePorts = true;
        }

        public bool TryGetExhaustSocketWorldPosition(int engineIndex, out Vector3 world)
        {
            world = Vector3.zero;
            Transform socket = engineIndex switch
            {
                1 => _exhaustSockets.Left,
                2 => _exhaustSockets.Right,
                _ => _exhaustSockets.Main,
            };

            if (socket == null)
                return false;

            world = socket.position;
            return true;
        }

        public void ApplyEngineMounts(RtgGliderEngineMounts mounts)
        {
            _engineMounts = mounts;
            _useCustomEnginePorts = true;
            ApplyMountsToSockets();
            ReconfigureAfterburner();
        }

        public bool TryGetSocketLocalPosition(int engineIndex, out Vector3 localPosition)
        {
            localPosition = engineIndex switch
            {
                1 => _engineMounts.Left,
                2 => _engineMounts.Right,
                _ => _engineMounts.Main,
            };

            Transform socket = engineIndex switch
            {
                1 => _exhaustSockets.Left,
                2 => _exhaustSockets.Right,
                _ => _exhaustSockets.Main,
            };

            if (socket != null)
            {
                localPosition = socket.localPosition;
                return true;
            }

            return _exhaustSockets.Attachments != null;
        }

        public void ApplySingleEngineMount(int engineIndex, Vector3 socketLocal)
        {
            switch (engineIndex)
            {
                case 1:
                    _engineMounts.Left = socketLocal;
                    break;
                case 2:
                    _engineMounts.Right = socketLocal;
                    break;
                default:
                    _engineMounts.Main = socketLocal;
                    break;
            }

            _useCustomEnginePorts = true;
            ApplyMountsToSockets();
            _afterburner?.RefreshPresentation();
        }

        public bool TryGetEngineMounts(out RtgGliderEngineMounts mounts)
        {
            if (_exhaustSockets.Main != null || _exhaustSockets.Left != null || _exhaustSockets.Right != null)
            {
                mounts = RtgGliderExhaustSockets.CaptureLocalPositions(_exhaustSockets);
                _engineMounts = mounts;
                return true;
            }

            mounts = _engineMounts;
            return _exhaustSockets.Attachments != null;
        }

        public void ApplyExhaustAnchors(
            RtgExhaustAnchor main,
            RtgExhaustAnchor left,
            RtgExhaustAnchor right)
        {
            // Legacy anchor fields kept for JSON migration only — sockets are authoritative.
            _mainAnchor = main.Clamped();
            _leftAnchor = left.Clamped();
            _rightAnchor = right.Clamped();
            _useCustomEnginePorts = true;
        }

        public bool TryGetExhaustAnchors(
            out RtgExhaustAnchor main,
            out RtgExhaustAnchor left,
            out RtgExhaustAnchor right)
        {
            main = _mainAnchor;
            left = _leftAnchor;
            right = _rightAnchor;
            return true;
        }

        private void ReconfigureAfterburner()
        {
            if (_afterburner == null)
                return;

            if (_exhaustSockets.Main == null && _exhaustSockets.Left == null && _exhaustSockets.Right == null)
            {
                Debug.LogWarning("[RTG] Exhaust sockets missing — afterburner VFX not created.");
                return;
            }

            _afterburner.Configure(
                _exhaustSockets.Main,
                _exhaustSockets.Left,
                _exhaustSockets.Right,
                sizeMeters);

            if (_hasCachedCavityTuning)
            {
                _afterburner.SetEngineCavityTunings(
                    _cachedMainCavity,
                    _cachedLeftCavity,
                    _cachedRightCavity);
            }

            _afterburner.RefreshPresentation();
        }

        private void ApplyMountsToSockets()
        {
            if (_exhaustSockets.Attachments == null)
                return;

            RtgGliderExhaustSockets.ApplyLocalPositions(_exhaustSockets, _engineMounts);
        }

        private static bool HasAnyMountPosition(RtgGliderEngineMounts mounts)
        {
            return mounts.Main != Vector3.zero
                || mounts.Left != Vector3.zero
                || mounts.Right != Vector3.zero;
        }

        public void ApplyCavityTuning(
            RtgEngineCavityTuning main,
            RtgEngineCavityTuning left,
            RtgEngineCavityTuning right)
        {
            _cachedMainCavity = main.Clamped();
            _cachedLeftCavity = left.Clamped();
            _cachedRightCavity = right.Clamped();
            _hasCachedCavityTuning = true;

            if (_afterburner != null)
                _afterburner.SetEngineCavityTunings(_cachedMainCavity, _cachedLeftCavity, _cachedRightCavity);
        }

        public void ApplyExhaustColorProfile(RtgExhaustColorStop[] stops, float maxMph)
        {
            if (_afterburner != null)
                _afterburner.SetExhaustColorProfile(stops, maxMph);
        }

        public void SetCavityPreview(bool enabled, float previewMph = 0f, int tuneStopIndex = -1)
        {
            if (_afterburner != null)
                _afterburner.SetCavityPreview(enabled, previewMph, tuneStopIndex);
        }

        public void SetFlamePreview(bool enabled, float previewMph = 0f)
        {
            if (_afterburner != null)
                _afterburner.SetFlamePreview(enabled, previewMph);
        }

        public void ApplyExhaustLengthScale(float scale)
        {
            if (_afterburner != null)
                _afterburner.SetFlameLengthScale(scale);
        }

        public bool TryResetSocketsToDefaults(out RtgGliderEngineMounts mounts)
        {
            RtgGliderBlockoutMesh.BuildResult blockout = RtgGliderBlockoutMesh.Build(sizeMeters);
            mounts = new RtgGliderEngineMounts(
                blockout.MainEngineLocal,
                blockout.LeftEngineLocal,
                blockout.RightEngineLocal);
            ApplyEngineMounts(mounts);
            return true;
        }

        public bool TryGetEstimatedEnginePorts(out RtgGliderEngineMounts mounts)
        {
            if (_exhaustSockets.Main != null || _exhaustSockets.Left != null || _exhaustSockets.Right != null)
            {
                mounts = RtgGliderExhaustSockets.CaptureLocalPositions(_exhaustSockets);
                return true;
            }

            mounts = RtgGliderEngineMounts.BlockoutDefaults(sizeMeters);
            return true;
        }

        public void ApplyHullTuning(Vector3 hullEuler, float headingOffsetDeg, bool? autoOrient = null)
        {
            hullLocalEulerOffset = hullEuler;
            headingOffsetDegrees = headingOffsetDeg;

            if (autoOrient.HasValue && autoOrient.Value != autoOrientImportedHull)
            {
                autoOrientImportedHull = autoOrient.Value;
                Rebuild();
                return;
            }

            ApplyHullTilt();
        }

        public void SetHeadingRadians(float headingRad)
        {
            float yaw = headingRad + headingOffsetDegrees * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            if (forward.sqrMagnitude < 1e-6f) return;

            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            ApplyHullTilt();
        }

        public void SetPresentation(
            Camera camera,
            float zoom,
            float minZoom,
            float maxZoom,
            bool lowAngleView,
            float mobilePlumeVisibilityScale = 1f)
        {
            _ = camera;
            _ = zoom;
            _ = minZoom;
            _ = maxZoom;
            _ = lowAngleView;
            _ = mobilePlumeVisibilityScale;

            if (_afterburner == null)
                return;

            // VFX are mesh-anchored in world meters — no device-specific scale compensation.
            _afterburner.SetPlumeVisibilityScale(1f);
        }

        public void SetMotionState(float thrust01, float turnRateRadPerSec, float colorMph = -1f)
        {
            float turnScale = Mathf.Clamp(turnRateRadPerSec * 14f, -1f, 1f);
            float targetBank = turnScale * maxBankDegrees;
            float targetPitch = -Mathf.Clamp01(thrust01) * maxThrustPitchDegrees;

            float t = 1f - Mathf.Exp(-12f * Time.deltaTime);
            _bankDegrees = Mathf.Lerp(_bankDegrees, targetBank, t);
            _pitchDegrees = Mathf.Lerp(_pitchDegrees, targetPitch, t);
            ApplyHullTilt();

            if (_afterburner != null)
                _afterburner.SetThrust(thrust01, colorMph);
        }

        private void ApplyHullTilt()
        {
            if (_hullRoot == null) return;

            transform.localPosition = new Vector3(0f, groundClearanceMeters, 0f);

            Quaternion bankRoll = Quaternion.AngleAxis(_bankDegrees, Vector3.forward);
            Quaternion thrustPitch = Quaternion.Euler(_pitchDegrees, 0f, 0f);
            _hullRoot.localRotation = thrustPitch * bankRoll;

            if (_meshTransform == null)
                return;

            Quaternion staticPose = _baseMeshRotation * Quaternion.Euler(hullLocalEulerOffset);
            _meshTransform.localRotation = staticPose;
        }

        private void Rebuild()
        {
            EnsureHierarchy();
            ApplyHullMaterial();
            ApplyHullTilt();
        }

        private void EnsureHierarchy()
        {
            DestroyChild("BlobShadow");
            DestroyChild("Hull");

            _renderer = null;
            _hullMesh = null;
            _hullMaterial = null;
            _usingImportedHull = false;
            _meshTransform = null;
            _baseMeshRotation = Quaternion.identity;

            var shadowGo = new GameObject("BlobShadow");
            shadowGo.transform.SetParent(transform, false);
            _blobShadow = shadowGo.AddComponent<RtgGliderBlobShadow>();
            _blobShadow.Configure(sizeMeters);

            var hullGo = new GameObject("Hull");
            hullGo.transform.SetParent(transform, false);
            _hullRoot = hullGo.transform;

            RtgGliderBlockoutMesh.BuildResult blockout = RtgGliderBlockoutMesh.Build(sizeMeters);
            if (TryBuildImportedHull(hullGo.transform))
            {
                _usingImportedHull = true;
                Debug.Log("[RTG] Player ship using Tripo imported hull.");
            }
            else
            {
                BuildBlockoutHull(hullGo.transform, blockout);
                Debug.LogWarning(
                    "[RTG] Tripo hull unavailable — using procedural blockout. " +
                    "Assign shipHullPrefab on RtgPlayerLocation or add the model under Resources/RTG_PlayerShip/TripoGlider/.");
            }

            RtgGliderEngineMounts fallbackMounts = new RtgGliderEngineMounts(
                blockout.MainEngineLocal,
                blockout.LeftEngineLocal,
                blockout.RightEngineLocal);
            if (!RtgGliderEngineMounts.HasSavedPositions(_engineMounts))
                _engineMounts = fallbackMounts;

            _attachmentsRoot = null;
            _exhaustSockets = RtgGliderExhaustSockets.Resolve(_hullRoot, _engineMounts);
            _attachmentsRoot = _exhaustSockets.Attachments;

            _afterburner = hullGo.GetComponent<RtgGliderAfterburner>();
            if (_afterburner == null)
                _afterburner = hullGo.AddComponent<RtgGliderAfterburner>();

            ReconfigureAfterburner();
        }

        /// <summary>
        /// Instantiates Tripo hull under the Hull root. REGRESSION notes: (1) localScale reset to one is
        /// intentional — <see cref="FitImportedHullScale"/> always runs after; (2) log “using Tripo imported hull”
        /// only means a Renderer was found, not that textures are visible — check albedo= in the follow-up log.
        /// </summary>
        private bool TryBuildImportedHull(Transform hullParent)
        {
            GameObject source = ResolveImportedHullPrefab();
            if (source == null) return false;

            var meshGo = Instantiate(source, hullParent);
            meshGo.name = "Model";
            meshGo.transform.localPosition = Vector3.zero;
            meshGo.transform.localRotation = Quaternion.identity;
            meshGo.transform.localScale = Vector3.one;
            meshGo.SetActive(true);
            PrepareImportedHullInstance(meshGo);

            Quaternion baseRotation = autoOrientImportedHull
                ? ComputeImportedHullRotation(meshGo.transform)
                : Quaternion.identity;
            _meshTransform = meshGo.transform;
            _baseMeshRotation = baseRotation;
            ApplyHullTilt();

            FitImportedHullScale(meshGo.transform);
            _renderer = meshGo.GetComponentInChildren<Renderer>(true);

            if (_renderer != null)
            {
                Material hullMaterial = _renderer.sharedMaterial;
                string albedoName = hullMaterial != null && hullMaterial.mainTexture != null
                    ? hullMaterial.mainTexture.name
                    : "none";
                Debug.Log(
                    $"[RTG] Tripo hull rotation auto={baseRotation.eulerAngles} " +
                    $"fine-tune={hullLocalEulerOffset} renderer={_renderer.GetType().Name} " +
                    $"albedo={albedoName}");
            }
            else
            {
                Debug.LogError(
                    "[RTG] Tripo hull instantiated but no Renderer found on children. " +
                    "Check Resources/RTG_PlayerShip/TripoGlider import.");
            }

            return _renderer != null;
        }

        /// <summary>
        /// Map Tripo's arbitrary export axes to POC contract: +Z nose, +Y up, wings along X.
        /// Tries every axis/sign combination and picks the pose with a level nose on +Z.
        /// </summary>
        private static Quaternion ComputeImportedHullRotation(Transform root)
        {
            Bounds bounds = CalculateLocalBounds(root);
            Quaternion bestRotation = Quaternion.identity;
            float bestScore = float.NegativeInfinity;
            int bestLengthAxis = -1;
            int bestSpanAxis = -1;

            for (int lengthAxis = 0; lengthAxis < 3; lengthAxis++)
            {
                for (int spanAxis = 0; spanAxis < 3; spanAxis++)
                {
                    if (lengthAxis == spanAxis) continue;

                    for (int lengthSign = -1; lengthSign <= 1; lengthSign += 2)
                    {
                        for (int spanSign = -1; spanSign <= 1; spanSign += 2)
                        {
                            Vector3 lengthDir = AxisVector(lengthAxis) * lengthSign;
                            Vector3 spanDir = AxisVector(spanAxis) * spanSign;

                            Quaternion rotation = BuildCandidateRotation(lengthDir, spanDir);
                            float score = ScoreHullRotation(root, rotation, lengthDir, spanDir, bounds);
                            if (score <= bestScore) continue;

                            bestScore = score;
                            bestRotation = rotation;
                            bestLengthAxis = lengthAxis;
                            bestSpanAxis = spanAxis;
                        }
                    }
                }
            }

            bestRotation = LevelHullAttitude(root, bounds, bestRotation);

            Vector3 fuselageDir = GetFuselageDirection(root, bounds, bestRotation);
            Debug.Log(
                $"[RTG] Tripo hull auto-orient length={AxisLabel(bestLengthAxis)} " +
                $"span={AxisLabel(bestSpanAxis)} score={bestScore:F2} " +
                $"fuselagePitch={Mathf.Asin(Mathf.Clamp(fuselageDir.y, -1f, 1f)) * Mathf.Rad2Deg:F1} " +
                $"euler={bestRotation.eulerAngles}");

            return bestRotation;
        }

        /// <summary>
        /// Level using actual mesh bounds geometry (nose-tail / wing tips), not abstract box axes.
        /// </summary>
        private static Quaternion LevelHullAttitude(Transform root, Bounds bounds, Quaternion rotation)
        {
            Vector3 fuselageDir = GetFuselageDirection(root, bounds, rotation);
            rotation = FlattenDirection(rotation, fuselageDir);

            Vector3 wingDir = GetWingDirection(root, bounds, rotation);
            rotation = FlattenDirection(rotation, wingDir);

            return rotation;
        }

        private static Quaternion FlattenDirection(Quaternion rotation, Vector3 direction)
        {
            if (direction.sqrMagnitude < 1e-6f)
                return rotation;

            direction.Normalize();
            Vector3 flat = direction;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
                return rotation;

            flat.Normalize();
            return Quaternion.FromToRotation(direction, flat) * rotation;
        }

        private static Vector3 GetFuselageDirection(Transform root, Bounds bounds, Quaternion rotation)
        {
            if (TryGetAxisDirectionFromVertices(root, bounds, rotation, forwardAxis: true, out Vector3 direction))
                return direction;

            return GetFuselageDirectionFromBounds(bounds, rotation);
        }

        private static Vector3 GetWingDirection(Transform root, Bounds bounds, Quaternion rotation)
        {
            if (TryGetAxisDirectionFromVertices(root, bounds, rotation, forwardAxis: false, out Vector3 direction))
                return direction;

            return GetWingDirectionFromBounds(bounds, rotation);
        }

        private static bool TryGetAxisDirectionFromVertices(
            Transform root,
            Bounds bounds,
            Quaternion rotation,
            bool forwardAxis,
            out Vector3 direction)
        {
            direction = forwardAxis ? Vector3.forward : Vector3.right;
            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0)
                return false;

            Vector3 center = bounds.center;
            Vector3 positiveOffset = Vector3.zero;
            Vector3 negativeOffset = Vector3.zero;
            float bestPositiveScore = float.NegativeInfinity;
            float bestNegativeScore = float.PositiveInfinity;
            bool foundReadableMesh = false;

            foreach (MeshFilter meshFilter in meshFilters)
            {
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                    continue;

                foundReadableMesh = true;
                Transform meshTransform = meshFilter.transform;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 localPoint = root.InverseTransformPoint(meshTransform.TransformPoint(vertex));
                    Vector3 rotated = rotation * (localPoint - center);
                    float centerlineWeight = forwardAxis
                        ? 1f / (1f + Mathf.Abs(rotated.x) * 3f)
                        : 1f / (1f + Mathf.Abs(rotated.z) * 3f);

                    float axisValue = forwardAxis ? rotated.z : rotated.x;
                    float positiveScore = axisValue * centerlineWeight;
                    if (positiveScore > bestPositiveScore)
                    {
                        bestPositiveScore = positiveScore;
                        positiveOffset = rotated;
                    }

                    float negativeScore = axisValue * centerlineWeight;
                    if (negativeScore < bestNegativeScore)
                    {
                        bestNegativeScore = negativeScore;
                        negativeOffset = rotated;
                    }
                }
            }

            if (!foundReadableMesh)
                return false;

            Vector3 dir = positiveOffset - negativeOffset;
            if (dir.sqrMagnitude < 1e-6f)
                return false;

            direction = dir.normalized;
            return true;
        }

        private static Vector3 GetFuselageDirectionFromBounds(Bounds bounds, Quaternion rotation)
        {
            Vector3 center = bounds.center;
            Vector3 forwardOffset = Vector3.zero;
            Vector3 rearOffset = Vector3.zero;
            float bestForwardZ = float.NegativeInfinity;
            float bestRearZ = float.PositiveInfinity;

            foreach (Vector3 point in EnumerateOrientationShellPoints(bounds))
            {
                Vector3 rotated = rotation * (point - center);
                if (rotated.z > bestForwardZ)
                {
                    bestForwardZ = rotated.z;
                    forwardOffset = rotated;
                }

                if (rotated.z < bestRearZ)
                {
                    bestRearZ = rotated.z;
                    rearOffset = rotated;
                }
            }

            Vector3 dir = forwardOffset - rearOffset;
            if (dir.sqrMagnitude < 1e-6f)
                return Vector3.forward;

            return dir.normalized;
        }

        private static Vector3 GetWingDirectionFromBounds(Bounds bounds, Quaternion rotation)
        {
            Vector3 center = bounds.center;
            Vector3 rightOffset = Vector3.zero;
            Vector3 leftOffset = Vector3.zero;
            float bestRightX = float.NegativeInfinity;
            float bestLeftX = float.PositiveInfinity;

            foreach (Vector3 point in EnumerateOrientationShellPoints(bounds))
            {
                Vector3 rotated = rotation * (point - center);
                if (rotated.x > bestRightX)
                {
                    bestRightX = rotated.x;
                    rightOffset = rotated;
                }

                if (rotated.x < bestLeftX)
                {
                    bestLeftX = rotated.x;
                    leftOffset = rotated;
                }
            }

            Vector3 dir = rightOffset - leftOffset;
            if (dir.sqrMagnitude < 1e-6f)
                return Vector3.right;

            return dir.normalized;
        }

        private static System.Collections.Generic.IEnumerable<Vector3> EnumerateOrientationShellPoints(Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            for (int xi = -1; xi <= 1; xi += 2)
            {
                for (int yi = -1; yi <= 1; yi += 2)
                {
                    for (int zi = -1; zi <= 1; zi += 2)
                    {
                        yield return center + Vector3.Scale(extents, new Vector3(xi, yi, zi));
                    }
                }
            }

            yield return center + new Vector3(0f, 0f, extents.z);
            yield return center + new Vector3(0f, 0f, -extents.z);
            yield return center + new Vector3(extents.x, 0f, 0f);
            yield return center + new Vector3(-extents.x, 0f, 0f);
            yield return center + new Vector3(0f, extents.y, 0f);
            yield return center + new Vector3(0f, -extents.y, 0f);
            yield return center + new Vector3(0f, 0f, extents.z * 0.5f);
            yield return center + new Vector3(0f, -extents.y * 0.5f, extents.z);
        }

        private static Quaternion BuildCandidateRotation(Vector3 lengthDir, Vector3 spanDir)
        {
            Vector3 upDir = Vector3.Cross(lengthDir, spanDir).normalized;
            if (upDir.sqrMagnitude < 0.01f)
                return Quaternion.identity;
            if (Vector3.Dot(upDir, Vector3.up) < 0f)
                upDir = -upDir;

            Quaternion rotation = Quaternion.Inverse(Quaternion.LookRotation(lengthDir, upDir));

            Vector3 spanWorld = (rotation * spanDir).normalized;
            if (Vector3.Dot(spanWorld, Vector3.right) < 0f)
                rotation = Quaternion.Euler(0f, 180f, 0f) * rotation;

            Vector3 noseWorld = (rotation * lengthDir).normalized;
            spanWorld = (rotation * spanDir).normalized;
            Vector3 planeUp = Vector3.Cross(noseWorld, spanWorld).normalized;
            if (Vector3.Dot(planeUp, Vector3.up) < 0f)
                rotation = Quaternion.Euler(180f, 0f, 0f) * rotation;

            return rotation;
        }

        private static float ScoreHullRotation(
            Transform root,
            Quaternion rotation,
            Vector3 lengthDir,
            Vector3 spanDir,
            Bounds bounds)
        {
            Vector3 fuselageDir = GetFuselageDirection(root, bounds, rotation);
            Vector3 wingDir = GetWingDirection(root, bounds, rotation);
            Vector3 planeUp = Vector3.Cross(fuselageDir, wingDir).normalized;

            float score = Vector3.Dot(fuselageDir, Vector3.forward) * 4f;
            score += Vector3.Dot(wingDir, Vector3.right);
            score += Vector3.Dot(planeUp, Vector3.up);

            // Strongly prefer a level flight attitude: nose and wings in the horizontal plane.
            score -= Mathf.Abs(fuselageDir.y) * 12f;
            score -= Mathf.Abs(wingDir.y) * 6f;
            score -= Mathf.Abs(planeUp.y - 1f) * 2f;

            int lengthAxis = DominantAxis(lengthDir);
            score += Vector3.Dot(lengthDir, SignedAxisDirection(lengthAxis, bounds)) * 0.5f;

            return score;
        }

        private static int DominantAxis(Vector3 dir)
        {
            float ax = Mathf.Abs(dir.x);
            float ay = Mathf.Abs(dir.y);
            float az = Mathf.Abs(dir.z);
            if (ax >= ay && ax >= az) return 0;
            if (ay >= ax && ay >= az) return 1;
            return 2;
        }

        private static Vector3 AxisVector(int axis)
        {
            return axis switch
            {
                0 => Vector3.right,
                1 => Vector3.up,
                _ => Vector3.forward,
            };
        }

        private static string AxisLabel(int axis)
        {
            return axis switch
            {
                0 => "+X",
                1 => "+Y",
                _ => "+Z",
            };
        }

        private static Vector3 SignedAxisDirection(int axis, Bounds bounds)
        {
            float posExtent = axis switch
            {
                0 => bounds.max.x - bounds.center.x,
                1 => bounds.max.y - bounds.center.y,
                _ => bounds.max.z - bounds.center.z,
            };
            float negExtent = axis switch
            {
                0 => bounds.center.x - bounds.min.x,
                1 => bounds.center.y - bounds.min.y,
                _ => bounds.center.z - bounds.min.z,
            };

            float sign = posExtent >= negExtent ? 1f : -1f;
            return axis switch
            {
                0 => Vector3.right * sign,
                1 => Vector3.up * sign,
                _ => Vector3.forward * sign,
            };
        }

        private void BuildBlockoutHull(Transform hullParent, RtgGliderBlockoutMesh.BuildResult blockout)
        {
            _hullMesh = blockout.Mesh;

            var meshGo = new GameObject("Model");
            meshGo.transform.SetParent(hullParent, false);
            var meshFilter = meshGo.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = _hullMesh;
            _renderer = meshGo.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _meshTransform = meshGo.transform;
            _baseMeshRotation = Quaternion.identity;
            ApplyHullTilt();
        }

        /// <summary>
        /// Normalizes a Tripo hull instance for rendering (flatten nested mesh, enable renderers).
        /// Preserves native Tripo PBR/URP materials when already valid.
        /// </summary>
        public static void PrepareImportedHullInstance(GameObject hullRoot)
        {
            if (hullRoot == null)
                return;

            hullRoot.SetActive(true);
            FlattenImportedHierarchy(hullRoot.transform);
            ConfigureImportedRenderers(hullRoot);
        }

        /// <summary>
        /// One-time editor bake: fit wingspan before saving TripoGlider.prefab for device builds.
        /// </summary>
        public static void BakeHullScaleForDevice(Transform hullTransform, float wingspanMeters)
        {
            if (hullTransform == null)
                return;

            Bounds bounds = CalculateLocalBounds(hullTransform);
            float span = Mathf.Max(
                bounds.extents.x * 2f,
                bounds.extents.z * 2f,
                0.001f);
            float uniformScale = wingspanMeters / span;
            hullTransform.localScale = Vector3.one * uniformScale;
        }

        private GameObject ResolveImportedHullPrefab()
        {
            if (IsValidHullPrefab(importedHullPrefab))
                return importedHullPrefab;

            importedHullPrefab = null;

#if UNITY_EDITOR
            GameObject editorHull = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultTripoHullAssetPath);
            if (IsValidHullPrefab(editorHull))
            {
                importedHullPrefab = editorHull;
                return editorHull;
            }
#endif

            GameObject resourcesHull = LoadResourcesHullPrefab();
            if (IsValidHullPrefab(resourcesHull))
            {
                importedHullPrefab = resourcesHull;
                return resourcesHull;
            }

#if UNITY_EDITOR
            editorHull = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/RTG_PlayerShip/TripoGlider/TripoGlider.prefab");
            if (IsValidHullPrefab(editorHull))
                return editorHull;

            editorHull = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/RTG_PlayerShip/TripoGlider/futuristic_fighter_3d_model.fbx");
            if (IsValidHullPrefab(editorHull))
                return editorHull;
#endif
            return null;
        }

        private void FitImportedHullScale(Transform hullTransform)
        {
            Bounds bounds = CalculateLocalBounds(hullTransform);
            float span = Mathf.Max(
                bounds.extents.x * 2f,
                bounds.extents.z * 2f,
                0.001f);
            float uniformScale = sizeMeters / span * hullScaleMultiplier;
            hullTransform.localScale = Vector3.one * uniformScale;
        }

        private static Bounds CalculateLocalBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Bounds bounds = new Bounds(root.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                bounds.Encapsulate(root.InverseTransformPoint(world.min));
                bounds.Encapsulate(root.InverseTransformPoint(world.max));
            }

            return bounds;
        }

        private static void FlattenImportedHierarchy(Transform root)
        {
            // Tripo FBX imports may include cameras/lights that clutter the hierarchy.
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                DestroyImmediate(camera.gameObject);
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                DestroyImmediate(light.gameObject);

            if (root.GetComponent<Renderer>() != null)
                return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != 1)
                return;

            Transform meshNode = renderers[0].transform;
            if (meshNode == root)
                return;

            MeshFilter meshFilter = meshNode.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = meshNode.GetComponent<MeshRenderer>();
            if (meshFilter == null || meshRenderer == null || meshFilter.sharedMesh == null)
                return;

            MeshFilter rootFilter = root.GetComponent<MeshFilter>();
            if (rootFilter == null)
                rootFilter = root.gameObject.AddComponent<MeshFilter>();
            rootFilter.sharedMesh = meshFilter.sharedMesh;

            MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();
            if (rootRenderer == null)
                rootRenderer = root.gameObject.AddComponent<MeshRenderer>();
            rootRenderer.sharedMaterials = meshRenderer.sharedMaterials;

            root.localRotation = root.localRotation * meshNode.localRotation;
            root.localPosition = root.localPosition + root.localRotation * meshNode.localPosition;
            root.localScale = Vector3.Scale(root.localScale, meshNode.localScale);

            DestroyImmediate(meshNode.gameObject);
        }

        private static void ConfigureImportedRenderers(GameObject hullRoot)
        {
            foreach (Renderer renderer in hullRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                renderer.gameObject.SetActive(true);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Material[] sourceMaterials = renderer.sharedMaterials;
                Material[] runtimeMaterials = new Material[sourceMaterials.Length];
                for (int i = 0; i < sourceMaterials.Length; i++)
                    runtimeMaterials[i] = CreateVisibleHullMaterial(sourceMaterials[i]);
                renderer.materials = runtimeMaterials;
            }
        }

        private static Material CreateVisibleHullMaterial(Material source)
        {
            Texture albedo = ExtractAlbedoTexture(source);
            if (albedo == null)
                albedo = TryLoadHullAlbedoFromResources();

            if (IsUsableTripoMaterial(source))
                return new Material(source);

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
                return source != null ? new Material(source) : null;

            Material runtime = source != null
                ? new Material(source)
                : new Material(urpLit);
            runtime.name = "RTG_TripoHull_Runtime";
            runtime.shader = urpLit;

            ApplyAlbedoToMaterial(runtime, albedo);

            Color baseColor = Color.white;
            if (source != null)
            {
                if (source.HasProperty("_BaseColor"))
                    baseColor = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color"))
                    baseColor = source.GetColor("_Color");
            }

            baseColor.a = 1f;
            if (runtime.HasProperty("_BaseColor"))
                runtime.SetColor("_BaseColor", baseColor);

            if (runtime.HasProperty("_Surface"))
                runtime.SetFloat("_Surface", 0f);

            return runtime;
        }

        /// <summary>
        /// Device fallback when TripoHull.mat has no embedded albedo (common after prefab bake).
        /// REGRESSION: IsUsableTripoMaterial requires a real albedo — do not treat bare URP/Lit as usable.
        /// </summary>
        public static Texture2D TryLoadHullAlbedoFromResources()
        {
            Texture2D named = Resources.Load<Texture2D>(ResourcesTripoAlbedoPath);
            if (named != null)
                return named;

            Texture2D[] textures = Resources.LoadAll<Texture2D>(ResourcesTripoTextureFolder);
            Texture2D fallback = null;
            foreach (Texture2D texture in textures)
            {
                if (texture == null)
                    continue;

                string lower = texture.name.ToLowerInvariant();
                if (lower.Contains("normal")
                    || lower.Contains("rough")
                    || lower.Contains("metallic")
                    || lower.Contains("ao")
                    || lower.Contains("height")
                    || lower.Contains("mask"))
                {
                    continue;
                }

                fallback = texture;
                if (lower.Contains("base")
                    || lower.Contains("color")
                    || lower.Contains("albedo")
                    || lower.Contains("diffuse"))
                {
                    return texture;
                }
            }

            return fallback;
        }

        public static void ApplyAlbedoToMaterial(Material material, Texture albedo)
        {
            if (material == null || albedo == null)
                return;

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", albedo);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", albedo);
        }

        private static bool IsUsableTripoMaterial(Material source)
        {
            if (source == null || source.shader == null)
                return false;
            if (!source.shader.isSupported)
                return false;
            if (source.shader.name.Contains("Hidden/InternalErrorShader"))
                return false;
            if (source.shader.name == "Standard")
                return false;
            // REGRESSION: URP/Lit with empty _BaseMap passed here and shipped a gray/invisible hull on device.
            if (ExtractAlbedoTexture(source) == null)
                return false;

            return source.shader.name.Contains("Universal Render Pipeline");
        }

        public static Texture ExtractAlbedoTexture(Material source)
        {
            if (source == null)
                return null;

            if (source.mainTexture != null)
                return source.mainTexture;

            string[] texturePropertyNames =
            {
                "_BaseMap",
                "_MainTex",
                "_BaseColorMap",
                "_DiffuseMap",
            };

            foreach (string propertyName in texturePropertyNames)
            {
                if (!source.HasProperty(propertyName))
                    continue;

                Texture texture = source.GetTexture(propertyName);
                if (texture != null)
                    return texture;
            }

            return null;
        }

        private void ApplyHullMaterial()
        {
            if (_usingImportedHull || _renderer == null) return;

            Material template = Resources.Load<Material>("RTG_PlayerShip/GliderBlockout");
            if (template != null && template.shader != null && template.shader.isSupported)
            {
                _hullMaterial = new Material(template) { name = "RTG_GliderBlockout_Runtime" };
                _renderer.sharedMaterial = _hullMaterial;
                return;
            }

            Shader shader = Shader.Find("RTG/GliderVertexColor");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;

            _hullMaterial = new Material(shader) { name = "RTG_GliderBlockout_Runtime" };
            if (_hullMaterial.HasProperty("_Smoothness"))
                _hullMaterial.SetFloat("_Smoothness", 0.42f);
            _renderer.sharedMaterial = _hullMaterial;
        }

        private void DestroyChild(string childName)
        {
            Transform existing = transform.Find(childName);
            if (existing == null) return;
            DestroyImmediate(existing.gameObject);
        }

        private void OnDestroy()
        {
            if (_hullMaterial != null)
            {
                if (Application.isPlaying) Destroy(_hullMaterial);
                else DestroyImmediate(_hullMaterial);
            }

            if (_hullMesh != null)
            {
                if (Application.isPlaying) Destroy(_hullMesh);
                else DestroyImmediate(_hullMesh);
            }
        }
    }
}
