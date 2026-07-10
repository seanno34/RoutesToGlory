using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Tap a map marker to connect it to the active Light Road when the item is
    /// within range of the route corridor (not the player's GPS pin). Uses the
    /// existing POST /api/worlds/:worldId/claim endpoint; the server anchors the
    /// connector at the nearest point on the submitted route path.
    /// </summary>
    public class RtgTapToConnect : MonoBehaviour
    {
        [Tooltip("Max distance from route path to allow tap-connect (m). Loaded from /config/public when possible.")]
        public float maxConnectDistanceM = 1000f;

        [Tooltip("Pointer movement under this (px) counts as a tap, not a map pan.")]
        public float tapSlopPixels = 18f;

        private RtgRouteSession _session;
        private Camera _camera;
        private Vector2? _pressStart;
        private string _toast;
        private float _toastUntil;

        private void Start()
        {
#if UNITY_2023_1_OR_NEWER
            _session = Object.FindFirstObjectByType<RtgRouteSession>();
#else
            _session = Object.FindObjectOfType<RtgRouteSession>();
#endif
            _camera = Camera.main;
            StartCoroutine(LoadConnectRadius());
        }

        private void Update()
        {
            if (ReadPressDown(out Vector2 down))
                _pressStart = down;

            if (ReadPressUp(out Vector2 up) && _pressStart.HasValue)
            {
                if ((up - _pressStart.Value).sqrMagnitude <= tapSlopPixels * tapSlopPixels)
                    TryTapAt(up);
                _pressStart = null;
            }
        }

        private IEnumerator LoadConnectRadius()
        {
            if (_session == null) yield break;

            string url = _session.apiBaseUrl.TrimEnd('/') + "/config/public";
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            var cfg = JsonUtility.FromJson<PublicConfigRoutes>(req.downloadHandler.text);
            if (cfg?.routes != null && cfg.routes.minConnectDistanceM > 0)
                maxConnectDistanceM = cfg.routes.minConnectDistanceM;
        }

        private void TryTapAt(Vector2 screenPos)
        {
            if (_camera == null || _session == null) return;

            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500_000f)) return;

            RtgMapMarker marker = hit.collider.GetComponentInParent<RtgMapMarker>();
            if (marker == null) return;

            if (!_session.TryGetRoutePath(out var path) || path.Count < 1)
            {
                ShowToast("Lay a route first — keep moving.");
                return;
            }

            double dist = RtgRouteGeometry.DistancePointToPathM(marker.lat, marker.lng, path);
            if (dist > maxConnectDistanceM)
            {
                ShowToast($"Too far from your route ({dist:0} m — need ≤{maxConnectDistanceM:0} m).");
                return;
            }

            StartCoroutine(_session.ClaimNearRoute(marker, OnClaimDone));
        }

        private void OnClaimDone(RtgRouteSession.ClaimResult result)
        {
            ShowToast(result.message);

            if (result.ok && result.hasConnector)
            {
                RtgConnectorLineDrawer drawer = RtgConnectorLineDrawer.FindOrCreate();
                drawer?.DrawConnector(
                    result.anchorLat, result.anchorLng,
                    result.targetLat, result.targetLng);
            }
        }

        private void ShowToast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.time + 4f;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_toast) || Time.time > _toastUntil) return;

            const float margin = 16f, h = 56f;
            var rect = new Rect(margin, Screen.height - h - margin - 80f, Screen.width - margin * 2, h);
            var prev = GUI.skin.box.fontSize;
            GUI.skin.box.fontSize = 16;
            GUI.Box(rect, _toast);
            GUI.skin.box.fontSize = prev;
        }

        private static bool ReadPressDown(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                return true;
            }
#else
            if (Input.GetMouseButtonDown(0))
            {
                position = Input.mousePosition;
                return true;
            }
#endif
            position = default;
            return false;
        }

        private static bool ReadPressUp(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                return true;
            }
#else
            if (Input.GetMouseButtonUp(0))
            {
                position = Input.mousePosition;
                return true;
            }
#endif
            position = default;
            return false;
        }

        [System.Serializable]
        private class PublicConfigRoutes
        {
            public RoutesBlock routes;
        }

        [System.Serializable]
        private class RoutesBlock
        {
            public float minConnectDistanceM;
        }
    }
}
