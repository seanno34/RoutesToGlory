using CesiumForUnity;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Owns chase vs cockpit camera modes. In cockpit the fly-camera GameObject is
    /// deactivated entirely; <see cref="CockpitCamera"/> renders with a direct
    /// world-space eye pose so drag-look never translates the view.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class RtgCameraManager : MonoBehaviour
    {
        public enum CameraMode
        {
            Chase = 0,
            Cockpit = 1,
        }

        public Camera CesiumCamera { get; private set; }
        public Camera ChaseCamera { get; private set; }
        public Camera CockpitCamera { get; private set; }
        public Camera ActiveGameplayCamera =>
            ActiveMode == CameraMode.Cockpit ? CockpitCamera : ChaseCamera;

        public Transform CameraRig { get; private set; }
        public Transform ChasePivot { get; private set; }
        public Transform CockpitPivot { get; private set; }
        public CameraMode ActiveMode { get; private set; } = CameraMode.Chase;

        private Transform _marker;
        private Transform _rigParent;
        private CesiumCameraController _cameraController;
        private CesiumOriginShift _cameraOriginShift;
        private CesiumGlobeAnchor _cameraGlobeAnchor;
        private CharacterController _characterController;
        private CesiumCameraManager _cesiumCameraManager;
        private bool _cockpitCameraRegistered;

        private bool _poseValid;
        private bool _cockpitRenderingActive;
        private string _savedMainCameraTag = "MainCamera";
        private float _eyeHeightMeters;
        private float _basePitchDeg;
        private float _lookYawDeg;
        private float _lookPitchDeg;
        private Vector3 _travelForward = Vector3.forward;
        private bool _preCullHooked;
        private bool _beforeRenderHooked;

        public Vector3 CockpitCameraWorldPosition =>
            CockpitCamera != null ? CockpitCamera.transform.position : Vector3.zero;

        public void EnsureInitialized(Transform marker)
        {
            _marker = marker;
            _rigParent = marker != null ? marker.parent : transform;

            CesiumCamera = Camera.main;
            if (CesiumCamera == null)
            {
                Debug.LogWarning("[RTG] CameraManager: Camera.main not found.");
                return;
            }

            if (CesiumCamera.transform.parent != null)
                CesiumCamera.transform.SetParent(null, worldPositionStays: true);

            RtgAudioSession.SetActiveListener(CesiumCamera);
            _cameraController = CesiumCamera.GetComponent<CesiumCameraController>();
            _cameraOriginShift = CesiumCamera.GetComponent<CesiumOriginShift>();
            _cameraGlobeAnchor = CesiumCamera.GetComponent<CesiumGlobeAnchor>();
            _characterController = CesiumCamera.GetComponent<CharacterController>();

            var georeference = marker != null
                ? marker.GetComponentInParent<CesiumGeoreference>()
                : null;
            if (georeference != null)
                _cesiumCameraManager = georeference.GetComponent<CesiumCameraManager>();

            EnsureRigHierarchy();
            HookCameraPreCull();
            HookBeforeRender();

            if (_cameraOriginShift != null && _cameraOriginShift.distance < 1.0)
                _cameraOriginShift.distance = 500.0;
        }

        private void OnDestroy()
        {
            if (_preCullHooked)
            {
                Camera.onPreCull -= OnCameraPreCull;
                _preCullHooked = false;
            }

            if (_beforeRenderHooked)
            {
                Application.onBeforeRender -= OnBeforeRender;
                _beforeRenderHooked = false;
            }
        }

        private void FixedUpdate()
        {
            if (ActiveMode == CameraMode.Cockpit)
                SuppressCesiumFlyCamera();
        }

        private void HookCameraPreCull()
        {
            if (_preCullHooked || CesiumCamera == null)
                return;

            Camera.onPreCull += OnCameraPreCull;
            _preCullHooked = true;
        }

        private void HookBeforeRender()
        {
            if (_beforeRenderHooked)
                return;

            Application.onBeforeRender += OnBeforeRender;
            _beforeRenderHooked = true;
        }

        private void OnCameraPreCull(Camera cam)
        {
            if (!_poseValid || ActiveMode != CameraMode.Chase || cam != CesiumCamera)
                return;

            MirrorChaseCameraToCesium();
        }

        private void OnBeforeRender()
        {
            if (!_poseValid || ActiveMode != CameraMode.Chase)
                return;

            MirrorChaseCameraToCesium();
        }

        public void SetMode(CameraMode mode)
        {
            bool wantCockpitRender = mode == CameraMode.Cockpit;
            if (ActiveMode == mode && _cockpitRenderingActive == wantCockpitRender)
                return;

            ActiveMode = mode;
            if (wantCockpitRender)
            {
                SetGameplayCameraOwnership(true);
                SetCockpitRenderingActive(true);
            }
            else
            {
                SetCockpitRenderingActive(false);
            }
        }

        public void SetGameplayCameraOwnership(bool gameplayOwnsPose)
        {
            if (gameplayOwnsPose)
                SuppressCesiumFlyCamera();
            else if (!_cockpitRenderingActive)
                RestoreCesiumFlyCamera(allowFly: true);
        }

        public void SetCesiumFlyEnabled(bool flyEnabled)
        {
            SetGameplayCameraOwnership(!flyEnabled);
        }

        private void SuppressCesiumFlyCamera()
        {
            if (_cameraController != null)
            {
                _cameraController.enabled = false;
                _cameraController.enableMovement = false;
                _cameraController.enableRotation = false;
            }

            if (_cameraOriginShift != null)
                _cameraOriginShift.enabled = false;

            if (_cameraGlobeAnchor != null)
                _cameraGlobeAnchor.enabled = false;

            if (_characterController != null)
                _characterController.enabled = false;
        }

        private void RestoreCesiumFlyCamera(bool allowFly)
        {
            if (!allowFly)
            {
                SuppressCesiumFlyCamera();
                return;
            }

            if (_cameraController != null)
            {
                _cameraController.enabled = true;
                _cameraController.enableMovement = true;
                _cameraController.enableRotation = true;
            }

            if (_cameraOriginShift != null)
                _cameraOriginShift.enabled = true;

            if (_cameraGlobeAnchor != null)
                _cameraGlobeAnchor.enabled = true;

            if (_characterController != null)
                _characterController.enabled = true;
        }

        public void SetCockpitFieldOfView(float fieldOfView)
        {
            if (CockpitCamera != null)
                CockpitCamera.fieldOfView = fieldOfView;
        }

        public void RestoreChaseFieldOfView(float fieldOfView)
        {
            if (ChaseCamera != null)
                ChaseCamera.fieldOfView = fieldOfView;
            if (CesiumCamera != null && ActiveMode == CameraMode.Chase)
                CesiumCamera.fieldOfView = fieldOfView;
        }

        public void ApplyChaseLookAt(Vector3 cameraPosition, Vector3 lookTarget)
        {
            if (ActiveMode != CameraMode.Chase || ChaseCamera == null || CesiumCamera == null)
                return;

            _poseValid = false;

            Vector3 lookDir = lookTarget - cameraPosition;
            if (lookDir.sqrMagnitude < 1e-8f)
                lookDir = Vector3.forward;

            ChaseCamera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation(lookDir.normalized, Vector3.up));

            _poseValid = true;
            MirrorChaseCameraToCesium();
        }

        public void ApplyCockpitPose(
            Vector3 travelForward,
            float eyeHeightMeters,
            float basePitchDeg,
            float lookYawDeg,
            float lookPitchDeg)
        {
            if (ActiveMode != CameraMode.Cockpit
                || CockpitCamera == null
                || _marker == null
                || CameraRig == null
                || CockpitPivot == null)
            {
                return;
            }

            _travelForward = travelForward;
            _eyeHeightMeters = eyeHeightMeters;
            _basePitchDeg = basePitchDeg;
            _lookYawDeg = lookYawDeg;
            _lookPitchDeg = lookPitchDeg;
            _poseValid = true;

            ApplyCockpitPoseInternal();
        }

        private void ApplyCockpitPoseInternal()
        {
            Vector3 forward = _travelForward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            Quaternion travelRot = Quaternion.LookRotation(forward, Vector3.up);
            float pitch = _basePitchDeg + _lookPitchDeg;
            Quaternion lookLocal =
                Quaternion.AngleAxis(_lookYawDeg, Vector3.up)
                * Quaternion.AngleAxis(pitch, Vector3.right);

            CameraRig.SetPositionAndRotation(_marker.position, travelRot);

            EnsureCockpitEyeOnMarker();
            CockpitPivot.localPosition = new Vector3(0f, _eyeHeightMeters, 0f);
            CockpitPivot.localRotation = Quaternion.Inverse(_marker.rotation) * travelRot * lookLocal;

            ReparentCockpitCamera();
            CockpitCamera.transform.localPosition = Vector3.zero;
            CockpitCamera.transform.localRotation = Quaternion.identity;
        }

        private void EnsureCockpitEyeOnMarker()
        {
            if (_marker == null || CockpitPivot == null)
                return;

            if (CockpitPivot.parent != _marker)
                CockpitPivot.SetParent(_marker, false);
        }

        private void ReparentCockpitCamera()
        {
            if (CockpitCamera == null || CockpitPivot == null)
                return;

            if (CockpitCamera.transform.parent != CockpitPivot)
                CockpitCamera.transform.SetParent(CockpitPivot, false);

            CockpitCamera.transform.localPosition = Vector3.zero;
            CockpitCamera.transform.localRotation = Quaternion.identity;
        }

        private void MirrorChaseCameraToCesium()
        {
            if (ChaseCamera == null || CesiumCamera == null || !CesiumCamera.gameObject.activeInHierarchy)
                return;

            Transform src = ChaseCamera.transform;
            CesiumCamera.transform.SetPositionAndRotation(src.position, src.rotation);
            CesiumCamera.fieldOfView = ChaseCamera.fieldOfView;
            CesiumCamera.nearClipPlane = ChaseCamera.nearClipPlane;
            CesiumCamera.farClipPlane = ChaseCamera.farClipPlane;
        }

        private void SetCockpitRenderingActive(bool active)
        {
            if (_cockpitRenderingActive == active)
                return;

            _cockpitRenderingActive = active;

            if (CockpitCamera == null || CesiumCamera == null)
                return;

            if (active)
            {
                SuppressCesiumFlyCamera();
                RegisterCockpitCameraForCesium();
                SyncGameplayCameraFromCesium(CockpitCamera);

                _savedMainCameraTag = CesiumCamera.tag;
                CesiumCamera.tag = "Untagged";
                CockpitCamera.tag = "MainCamera";
                CockpitCamera.depth = CesiumCamera.depth;
                EnsureCockpitEyeOnMarker();
                ReparentCockpitCamera();
                CockpitCamera.enabled = true;

                CesiumCamera.enabled = false;
                CesiumCamera.gameObject.SetActive(false);

                RtgAudioSession.SetActiveListener(CockpitCamera);
            }
            else
            {
                ReparentCockpitCamera();
                CockpitCamera.enabled = false;
                CockpitCamera.tag = "Untagged";

                CesiumCamera.gameObject.SetActive(true);
                CesiumCamera.enabled = true;
                CesiumCamera.tag = string.IsNullOrEmpty(_savedMainCameraTag)
                    ? "MainCamera"
                    : _savedMainCameraTag;

                RtgAudioSession.SetActiveListener(CesiumCamera);
            }
        }

        private static void SyncGameplayCameraFromCesium(Camera gameplayCamera, Camera sourceCamera)
        {
            if (gameplayCamera == null || sourceCamera == null || gameplayCamera == sourceCamera)
                return;

            gameplayCamera.clearFlags = sourceCamera.clearFlags;
            gameplayCamera.cullingMask = sourceCamera.cullingMask;
            gameplayCamera.nearClipPlane = sourceCamera.nearClipPlane;
            gameplayCamera.farClipPlane = sourceCamera.farClipPlane;
            gameplayCamera.fieldOfView = sourceCamera.fieldOfView;
            gameplayCamera.allowHDR = sourceCamera.allowHDR;
            gameplayCamera.allowMSAA = sourceCamera.allowMSAA;
        }

        private void SyncGameplayCameraFromCesium(Camera gameplayCamera)
        {
            SyncGameplayCameraFromCesium(gameplayCamera, CesiumCamera);
        }

        private void RegisterCockpitCameraForCesium()
        {
            if (_cockpitCameraRegistered || _cesiumCameraManager == null || CockpitCamera == null)
                return;

            if (!_cesiumCameraManager.additionalCameras.Contains(CockpitCamera))
                _cesiumCameraManager.additionalCameras.Add(CockpitCamera);

            _cockpitCameraRegistered = true;
        }

        private void EnsureRigHierarchy()
        {
            if (_rigParent == null)
                _rigParent = transform;

            CameraRig = _rigParent.Find("CameraRig");
            if (CameraRig == null && _marker != null)
            {
                Transform legacyRig = _marker.Find("CameraRig");
                if (legacyRig != null)
                {
                    legacyRig.SetParent(_rigParent, false);
                    CameraRig = legacyRig;
                }
            }

            if (CameraRig == null)
            {
                var rigGo = new GameObject("CameraRig");
                rigGo.transform.SetParent(_rigParent, false);
                CameraRig = rigGo.transform;
            }

            ChasePivot = CameraRig.Find("ChasePivot");
            if (ChasePivot == null)
            {
                var chaseGo = new GameObject("ChasePivot");
                chaseGo.transform.SetParent(CameraRig, false);
                ChasePivot = chaseGo.transform;
            }

            CockpitPivot = CameraRig.Find("CockpitPivot");
            if (CockpitPivot == null)
            {
                var cockpitGo = new GameObject("CockpitPivot");
                cockpitGo.transform.SetParent(CameraRig, false);
                CockpitPivot = cockpitGo.transform;
            }

            ChaseCamera = EnsureGameplayCamera(ChasePivot, "ChaseCamera");
            CockpitCamera = EnsureGameplayCamera(CockpitPivot, "CockpitCamera");
        }

        private Camera EnsureGameplayCamera(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            Camera cam;
            if (existing != null)
            {
                cam = existing.GetComponent<Camera>();
                if (cam != null)
                {
                    StripAudioListener(cam.gameObject);
                    return cam;
                }
            }

            var go = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null)
                go.transform.SetParent(parent, false);

            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            StripAudioListener(go);

            cam = go.GetComponent<Camera>();
            if (cam == null)
                cam = go.AddComponent<Camera>();

            cam.enabled = false;
            cam.depth = -100;
            cam.tag = "Untagged";
            cam.clearFlags = CesiumCamera != null
                ? CesiumCamera.clearFlags
                : CameraClearFlags.Skybox;
            if (CesiumCamera != null)
            {
                cam.cullingMask = CesiumCamera.cullingMask;
                cam.fieldOfView = CesiumCamera.fieldOfView;
                cam.nearClipPlane = CesiumCamera.nearClipPlane;
                cam.farClipPlane = CesiumCamera.farClipPlane;
            }

            return cam;
        }

        private static void StripAudioListener(GameObject go)
        {
            if (go == null) return;

            AudioListener listener = go.GetComponent<AudioListener>();
            if (listener != null)
                Destroy(listener);
        }
    }
}
