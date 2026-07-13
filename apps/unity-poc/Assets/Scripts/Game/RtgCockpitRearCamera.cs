using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Renders a backward-looking backup camera into a small inset while cockpit mode is active.
    /// </summary>
    public class RtgCockpitRearCamera : MonoBehaviour
    {
        [Tooltip("Vertical offset (m) matching cockpit eye height.")]
        public float eyeHeightMeters = 3.5f;

        [Tooltip("Degrees below horizon for the rear view.")]
        public float lookDownDegrees = 18f;

        [Tooltip("Render texture width.")]
        public int textureWidth = 384;

        [Tooltip("Render texture height.")]
        public int textureHeight = 216;

        public RenderTexture Texture => _renderTexture;
        public bool IsReady => _renderTexture != null && _camera != null;
        public Rect LastScreenRect { get; private set; }

        private Camera _camera;
        private RenderTexture _renderTexture;
        private bool _active;

        private void Awake()
        {
            EnsureCamera();
        }

        public void SetActive(bool active)
        {
            _active = active;
            if (_camera != null)
                _camera.gameObject.SetActive(active);
        }

        public void SyncFromMainCamera(Camera main)
        {
            if (_camera == null || main == null) return;

            _camera.fieldOfView = main.fieldOfView * 0.85f;
            _camera.nearClipPlane = main.nearClipPlane;
            _camera.farClipPlane = main.farClipPlane;
            _camera.cullingMask = main.cullingMask;
        }

        public void Render(Transform marker, Vector3 travelForward)
        {
            if (!_active || marker == null) return;

            EnsureCamera();
            EnsureRenderTarget();

            Vector3 forward = travelForward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            Vector3 eye = marker.position + Vector3.up * eyeHeightMeters;
            Quaternion rot = Quaternion.LookRotation(-forward, Vector3.up)
                * Quaternion.Euler(-lookDownDegrees, 0f, 0f);
            _camera.transform.SetPositionAndRotation(eye, rot);
            _camera.Render();
        }

        public void DrawInset(RtgCockpitView cockpitView, float blend)
        {
            LastScreenRect = default;
            if (blend <= 0.001f || cockpitView == null || !IsReady) return;

            bool portrait = Screen.height > Screen.width;
            if (!cockpitView.TryMapAnchorToScreen(
                    RtgCockpitView.RearCameraAnchor(portrait),
                    out Rect rect))
            {
                return;
            }

            LastScreenRect = rect;
            const float border = 3f;
            var frame = new Rect(
                rect.x - border,
                rect.y - border,
                rect.width + border * 2f,
                rect.height + border * 2f);

            Color prev = GUI.color;
            GUI.color = new Color(0.08f, 0.12f, 0.18f, 0.92f * blend);
            GUI.DrawTexture(frame, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
            GUI.color = new Color(0.45f, 0.85f, 1f, 0.85f * blend);
            GUI.DrawTexture(
                new Rect(frame.x, frame.y, frame.width, 2f),
                Texture2D.whiteTexture,
                ScaleMode.StretchToFill,
                false);

            GUI.color = new Color(1f, 1f, 1f, blend);
            GUI.DrawTexture(rect, _renderTexture, ScaleMode.StretchToFill, false);

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = Mathf.Max(10, Mathf.RoundToInt(rect.height * 0.14f)),
                fontStyle = FontStyle.Bold,
            };
            labelStyle.normal.textColor = new Color(0.55f, 0.95f, 1f, 0.95f * blend);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 18f), "REAR", labelStyle);
            GUI.color = prev;
        }

        private void EnsureCamera()
        {
            if (_camera != null) return;

            var go = new GameObject("RTG_RearCamera");
            go.transform.SetParent(transform, false);
            _camera = go.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.depth = -10;
            go.SetActive(false);
        }

        private void EnsureRenderTarget()
        {
            if (_renderTexture != null
                && _renderTexture.width == textureWidth
                && _renderTexture.height == textureHeight)
            {
                return;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            _renderTexture = new RenderTexture(textureWidth, textureHeight, 16)
            {
                name = "RTG_CockpitRear",
            };
            _camera.targetTexture = _renderTexture;
        }

        private void OnDestroy()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            if (_camera != null)
                Destroy(_camera.gameObject);
        }
    }
}
