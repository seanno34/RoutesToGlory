using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Tap a map marker to connect it to the active Light Road when the item is
    /// within range of the route corridor (not the player's GPS pin). Goodie huts
    /// open a choice modal (found town vs claim reward) before calling the claim API.
    /// </summary>
    public class RtgTapToConnect : MonoBehaviour
    {
        [Tooltip("Max distance from route path to allow tap-connect (m). Loaded from /config/public when possible.")]
        public float maxConnectDistanceM = 1000f;

        [Tooltip("Pointer movement under this (px) counts as a tap, not a map pan.")]
        public float tapSlopPixels = 18f;

        [Tooltip("Screen-space radius (px) for tapping a marker without physics colliders.")]
        public float tapHitRadiusPixels = 56f;

        private RtgRouteSession _session;
        private RtgEchoSiteLoader _echoLoader;
        private Camera _camera;
        private Vector2? _pressStart;
        private RtgMapMarker _pendingGoodie;
        private List<RtgRouteGeometry.LatLng> _pendingGoodiePath;
        private string _toast;
        private float _toastUntil;

        private void Start()
        {
#if UNITY_2023_1_OR_NEWER
            _session = Object.FindFirstObjectByType<RtgRouteSession>();
            _echoLoader = Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
            _session = Object.FindObjectOfType<RtgRouteSession>();
            _echoLoader = Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
            _camera = Camera.main;
            RtgMapMarkerRegistry.Refresh();
            StartCoroutine(LoadConnectRadius());
        }

        private void Update()
        {
            if (_pendingGoodie != null) return;

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

            RtgMapMarker marker = FindMarkerNearScreenPoint(screenPos);
            if (marker == null) return;

            if (marker.IsConnected) return;

            _session.TryGetRoutePath(out List<RtgRouteGeometry.LatLng> activeLeg);
            RtgRoute[] persisted = _echoLoader != null ? _echoLoader.LastMap?.routes : null;

            if (!RtgRouteCorridor.IsWithinNetwork(
                    marker.lat,
                    marker.lng,
                    activeLeg,
                    persisted,
                    _session.empireId,
                    maxConnectDistanceM,
                    out double dist))
            {
                bool hasNetwork = (activeLeg != null && activeLeg.Count > 0) ||
                                  (persisted != null && persisted.Length > 0);
                if (!hasNetwork)
                    ShowToast("Lay a route first — keep moving.");
                else
                    ShowToast($"Too far from your route ({dist:0} m — need ≤{maxConnectDistanceM:0} m).");
                return;
            }

            if (marker.IsGoodieHut)
            {
                _pendingGoodie = marker;
                _pendingGoodiePath = activeLeg != null
                    ? new List<RtgRouteGeometry.LatLng>(activeLeg)
                    : null;
                return;
            }

            StartCoroutine(_session.ClaimNearRoute(marker, null, OnClaimDone));
        }

        private RtgMapMarker FindMarkerNearScreenPoint(Vector2 screenPos)
        {
            RtgMapMarker best = null;
            float bestDist = tapHitRadiusPixels;

            foreach (RtgMapMarker marker in RtgMapMarkerRegistry.All)
            {
                if (marker == null || !marker.gameObject.activeInHierarchy) continue;

                Vector3 world = marker.transform.position;
                Vector3 screen = _camera.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;

                float dist = Vector2.Distance(screenPos, new Vector2(screen.x, screen.y));
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    best = marker;
                }
            }

            return best;
        }

        private void SubmitGoodieChoice(string choice)
        {
            RtgMapMarker marker = _pendingGoodie;
            List<RtgRouteGeometry.LatLng> path = _pendingGoodiePath;
            _pendingGoodie = null;
            _pendingGoodiePath = null;
            if (marker == null || _session == null) return;
            StartCoroutine(_session.ClaimNearRoute(marker, choice, OnClaimDone, path));
        }

        private void OnClaimDone(RtgRouteSession.ClaimResult result)
        {
            if (result.alreadyConnected)
            {
                RefreshAfterClaim(result);
                return;
            }

            ShowToast(result.message);

            if (!result.ok) return;

            MarkConnected(result.connectedTargetId);
            RefreshAfterClaim(result);
        }

        private static void MarkConnected(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            RtgMapMarker marker = RtgMapMarkerRegistry.FindByTargetId(targetId);
            if (marker != null)
                marker.SetConnected(true);
        }

        private void RefreshAfterClaim(RtgRouteSession.ClaimResult result)
        {
            if (_session == null) return;

            if (result.reloadMap && _echoLoader != null)
            {
                StartCoroutine(_echoLoader.ReloadFromApi());
                return;
            }

            if (result.hasConnector)
            {
                RtgPersistedRouteDrawer drawer = RtgPersistedRouteDrawer.FindOrCreate();
                drawer?.AppendConnector(
                    result.connectorRouteId,
                    result.anchorLat,
                    result.anchorLng,
                    result.targetLat,
                    result.targetLng);
                return;
            }

            RefreshPersistedRoutes();
        }

        private void RefreshPersistedRoutes()
        {
            if (_session == null) return;
            RtgPersistedRouteDrawer drawer = RtgPersistedRouteDrawer.FindOrCreate();
            if (drawer == null) return;
            drawer.StartCoroutine(drawer.RefreshFromApi(_session.apiBaseUrl, _session.worldId, _session.empireId));
        }

        private void ShowToast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.time + 4f;
        }

        private void OnGUI()
        {
            DrawGoodieChoiceModal();
            DrawToast();
        }

        private void DrawGoodieChoiceModal()
        {
            if (_pendingGoodie == null) return;

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            const float panelW = 380f;
            const float panelH = 250f;
            float x = (Screen.width - panelW) * 0.5f;
            float y = (Screen.height - panelH) * 0.5f;
            var panel = new Rect(x, y, panelW, panelH);

            var prevBtn = GUI.skin.button.fontSize;
            var prevLabel = GUI.skin.label.fontSize;
            var prevBox = GUI.skin.box.fontSize;
            GUI.skin.button.fontSize = 17;
            GUI.skin.label.fontSize = 17;
            GUI.skin.box.fontSize = 17;

            GUI.Box(panel, GUIContent.none);

            GUILayout.BeginArea(new Rect(x + 16f, y + 12f, panelW - 32f, panelH - 24f));
            GUILayout.Label(_pendingGoodie.displayName);
            GUILayout.Label("Goodie Hut — choose your reward");
            GUILayout.Space(10f);

            if (GUILayout.Button("Found Town\nInstant town + population; settlement modifiers queued.", GUILayout.Height(52f)))
                SubmitGoodieChoice("found_town");

            if (GUILayout.Button("Claim Reward\nOne-time gold, tech, or alien unit.", GUILayout.Height(52f)))
                SubmitGoodieChoice("claim_reward");

            GUILayout.Space(4f);
            if (GUILayout.Button("Cancel", GUILayout.Height(34f)))
            {
                _pendingGoodie = null;
                _pendingGoodiePath = null;
            }

            GUILayout.EndArea();

            GUI.skin.button.fontSize = prevBtn;
            GUI.skin.label.fontSize = prevLabel;
            GUI.skin.box.fontSize = prevBox;
        }

        private void DrawToast()
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
