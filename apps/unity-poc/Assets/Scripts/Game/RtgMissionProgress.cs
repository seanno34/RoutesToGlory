using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// POC sequential missions A→B→C HUD + actions.
    /// Polls <c>GET /worlds/:id/missions</c>; founds Base Camp; Settings accelerate for Mission C.
    /// </summary>
    public class RtgMissionProgress : MonoBehaviour
    {
        public const int XeniteRequired = 5;

        [Tooltip("Seconds between mission status polls while in play.")]
        public float pollIntervalSeconds = 8f;

        private RtgRouteSession _session;
        private RtgEchoSiteLoader _echoLoader;
        private RtgPlayerLocation _player;

        private MissionDto _progress;
        private string _error;
        private string _toast;
        private float _toastUntil;
        private bool _pollInFlight;
        private bool _actionInFlight;
        private bool _victoryBannerDismissed;
        private Coroutine _pollLoop;
        private Vector2 _missionScroll;
        private Vector2 _missionExpandedScroll;
        private bool _missionExpanded;
        private readonly System.Collections.Generic.List<Rect> _hudRects =
            new System.Collections.Generic.List<Rect>(4);

        [Serializable]
        private class MissionADto
        {
            public string status;
            public int xeniteConnected;
            public int xeniteRequired;
        }

        [Serializable]
        private class MissionBDto
        {
            public string status;
            public string baseCampSettlementId;
        }

        [Serializable]
        private class MissionCDto
        {
            public string status;
            public string startedAt;
            public string completesAt;
            public int remainingSeconds;
            public int reserveCurrent;
            public int reserveTarget;
            public int reserveFilled;
            public float fillPercent;
            public int connectedXeniteCount;
            public float fillRatePercentPerHour;
        }

        [Serializable]
        private class MissionDto
        {
            public string worldId;
            public string empireId;
            public string currentMission;
            public bool victory;
            public string title;
            public string objective;
            public string progressLabel;
            public MissionADto missionA;
            public MissionBDto missionB;
            public MissionCDto missionC;
        }

        [Serializable]
        private class FoundBaseCampResp
        {
            public bool ok;
            public string settlementId;
            public MissionDto progress;
        }

        [Serializable]
        private class ErrResp
        {
            public string error;
            public string message;
        }

        public static RtgMissionProgress FindOrCreate()
        {
#if UNITY_2023_1_OR_NEWER
            var existing = FindFirstObjectByType<RtgMissionProgress>();
#else
            var existing = FindObjectOfType<RtgMissionProgress>();
#endif
            if (existing != null) return existing;

#if UNITY_2023_1_OR_NEWER
            var player = FindFirstObjectByType<RtgPlayerLocation>();
#else
            var player = FindObjectOfType<RtgPlayerLocation>();
#endif
            if (player != null)
                return player.gameObject.AddComponent<RtgMissionProgress>();

            var go = new GameObject("RtgMissionProgress");
            return go.AddComponent<RtgMissionProgress>();
        }

        private void Start()
        {
            ResolveDeps();
            if (_pollLoop == null)
                _pollLoop = StartCoroutine(PollLoop());
        }

        private void OnEnable()
        {
            ResolveDeps();
        }

        private void ResolveDeps()
        {
#if UNITY_2023_1_OR_NEWER
            if (_session == null) _session = FindFirstObjectByType<RtgRouteSession>();
            if (_echoLoader == null) _echoLoader = FindFirstObjectByType<RtgEchoSiteLoader>();
            if (_player == null) _player = FindFirstObjectByType<RtgPlayerLocation>();
#else
            if (_session == null) _session = FindObjectOfType<RtgRouteSession>();
            if (_echoLoader == null) _echoLoader = FindObjectOfType<RtgEchoSiteLoader>();
            if (_player == null) _player = FindObjectOfType<RtgPlayerLocation>();
#endif
        }

        private IEnumerator PollLoop()
        {
            while (enabled)
            {
                if (!RtgGameSessionLogin.IsPlayBlocked())
                    yield return RefreshOnce();
                yield return new WaitForSeconds(Mathf.Max(2f, pollIntervalSeconds));
            }
            _pollLoop = null;
        }

        /// <summary>Call after a successful xenite claim so Mission A updates immediately.</summary>
        public void NotifyClaimSucceeded()
        {
            if (!isActiveAndEnabled) return;
            StartCoroutine(RefreshOnce());
        }

        public IEnumerator RefreshOnce()
        {
            if (_pollInFlight) yield break;
            ResolveDeps();
            if (_session == null
                || string.IsNullOrWhiteSpace(_session.apiBaseUrl)
                || string.IsNullOrWhiteSpace(_session.worldId)
                || string.IsNullOrWhiteSpace(_session.empireId))
            {
                yield break;
            }

            _pollInFlight = true;
            string url =
                $"{_session.apiBaseUrl.TrimEnd('/')}/worlds/{_session.worldId}/missions?empireId={Uri.EscapeDataString(_session.empireId)}";

            yield return RtgApiHttp.Get(url, (body, err) =>
            {
                if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(body))
                {
                    _error = FormatRequestError(body, err, "Mission status unavailable");
                    return;
                }

                try
                {
                    var dto = JsonUtility.FromJson<MissionDto>(body);
                    if (dto != null && !string.IsNullOrEmpty(dto.currentMission))
                    {
                        bool wasVictory = _progress != null && _progress.victory;
                        _progress = dto;
                        _error = null;
                        PersistLocalCache(dto);
                        if (dto.victory && !wasVictory)
                            _victoryBannerDismissed = false;
                    }
                }
                catch (Exception e)
                {
                    _error = e.Message;
                }
            });

            _pollInFlight = false;
        }

        public bool IsMissionCActive =>
            _progress != null
            && string.Equals(_progress.currentMission, "C", StringComparison.OrdinalIgnoreCase);

        /// <summary>IMGUI y-from-top point over mission HUD / victory / toast.</summary>
        public bool IsGuiPointOverHud(Vector2 guiPos)
        {
            foreach (Rect rect in _hudRects)
            {
                if (rect.Contains(guiPos))
                    return true;
            }
            return false;
        }

        public void AccelerateMissionCNear()
        {
            if (_actionInFlight) return;
            StartCoroutine(AccelerateRoutine("near"));
        }

        public void AccelerateMissionCFinish()
        {
            if (_actionInFlight) return;
            StartCoroutine(AccelerateRoutine("finish"));
        }

        private IEnumerator AccelerateRoutine(string mode)
        {
            ResolveDeps();
            if (_session == null) yield break;
            _actionInFlight = true;

            string url =
                $"{_session.apiBaseUrl.TrimEnd('/')}/worlds/{_session.worldId}/missions/accelerate";
            string json =
                $"{{\"empireId\":\"{_session.empireId}\",\"mode\":\"{mode}\"}}";

            yield return RtgApiHttp.PostJson(url, json, (body, err) =>
            {
                if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(body))
                {
                    ShowToast(FormatRequestError(body, err, "Accelerate failed"));
                    return;
                }

                try
                {
                    var dto = JsonUtility.FromJson<MissionDto>(body);
                    if (dto != null)
                    {
                        _progress = dto;
                        PersistLocalCache(dto);
                        ShowToast(mode == "finish"
                            ? "Mission C completed (dev accelerate)."
                            : "Mission C timer set to ~60s.");
                    }
                }
                catch (Exception e)
                {
                    ShowToast(e.Message);
                }
            });

            _actionInFlight = false;
        }

        private IEnumerator FoundBaseCampRoutine()
        {
            ResolveDeps();
            if (_session == null || _player == null) yield break;
            if (!_player.TryGetPlayerLatLng(out double lat, out double lng))
            {
                ShowToast("No player position for Base Camp.");
                yield break;
            }

            _actionInFlight = true;

            var sb = new StringBuilder();
            sb.Append("{\"empireId\":\"").Append(_session.empireId).Append("\",");
            sb.Append("\"lat\":").Append(D(lat)).Append(',');
            sb.Append("\"lng\":").Append(D(lng));

            if (_session.TryGetClaimPathHint(out var path) && path != null && path.Count > 0)
            {
                sb.Append(",\"routePath\":[");
                for (int i = 0; i < path.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"lat\":").Append(D(path[i].lat))
                        .Append(",\"lng\":").Append(D(path[i].lng)).Append('}');
                }
                sb.Append(']');
            }
            sb.Append('}');

            string url =
                $"{_session.apiBaseUrl.TrimEnd('/')}/worlds/{_session.worldId}/missions/base-camp";

            yield return RtgApiHttp.PostJson(url, sb.ToString(), (body, err) =>
            {
                if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(body))
                {
                    ShowToast(FormatRequestError(body, err, "Base Camp failed"));
                    return;
                }

                try
                {
                    var resp = JsonUtility.FromJson<FoundBaseCampResp>(body);
                    if (resp?.progress != null)
                    {
                        _progress = resp.progress;
                        PersistLocalCache(resp.progress);
                    }
                    ShowToast("Base Camp founded — Mission C started (1%/hr per xenite).");
                    if (_echoLoader != null)
                        StartCoroutine(_echoLoader.ReloadFromApi());
                }
                catch (Exception e)
                {
                    ShowToast(e.Message);
                }
            }, timeoutSeconds: 20);

            _actionInFlight = false;
        }

        private void PersistLocalCache(MissionDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.worldId) || string.IsNullOrEmpty(dto.empireId))
                return;
            string key = PrefKey(dto.worldId, dto.empireId);
            PlayerPrefs.SetString(key, JsonUtility.ToJson(dto));
            PlayerPrefs.Save();
        }

        private static string PrefKey(string worldId, string empireId) =>
            $"rtg.missions.{worldId}.{empireId}";

        private static string D(double v) =>
            v.ToString("0.#######", CultureInfo.InvariantCulture);

        private static string TryParseError(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var e = JsonUtility.FromJson<ErrResp>(json);
                if (!string.IsNullOrEmpty(e?.message) && IsHumanReadableApiMessage(e.message))
                    return e.message;
                if (!string.IsNullOrEmpty(e?.error) && IsHumanReadableApiMessage(e.error))
                    return e.error;
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reject Fastify / JSON.parse leftovers like
        /// <c>"[object Object]" is not valid JSON</c> so the HUD can fall back to HTTP status.
        /// </summary>
        private static bool IsHumanReadableApiMessage(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return false;
            if (msg.IndexOf("[object Object]", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (msg.IndexOf("is not valid JSON", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            // Fastify default 404: "Route GET:/api/worlds/.../missions?… not found"
            if (msg.StartsWith("Route GET:", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("Route POST:", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("Route PUT:", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("Route PATCH:", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("Route DELETE:", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        /// <summary>True when the body is Fastify's default missing-route 404 JSON.</summary>
        private static bool LooksLikeFastifyRouteNotFound(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            return body.IndexOf("Route GET:", StringComparison.Ordinal) >= 0
                || body.IndexOf("Route POST:", StringComparison.Ordinal) >= 0
                || body.IndexOf("Route PUT:", StringComparison.Ordinal) >= 0
                || body.IndexOf("Route PATCH:", StringComparison.Ordinal) >= 0
                || body.IndexOf("Route DELETE:", StringComparison.Ordinal) >= 0;
        }

        private static string FormatRequestError(string body, string transportErr, string fallback)
        {
            if (LooksLikeFastifyRouteNotFound(body))
            {
                return string.IsNullOrEmpty(fallback)
                    ? "API route missing on server — redeploy rtg_api."
                    : $"{fallback} — API route missing on server (redeploy rtg_api).";
            }

            string apiMsg = TryParseError(body);
            if (!string.IsNullOrEmpty(apiMsg)) return apiMsg;

            // Prefer fallback over raw "404 HTTP/1.1 404 Not Found" when status is clear.
            if (!string.IsNullOrEmpty(transportErr)
                && transportErr.StartsWith("404", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(fallback))
                return fallback;

            if (!string.IsNullOrEmpty(transportErr)) return transportErr;
            if (!string.IsNullOrEmpty(body) && body.Length < 160 && body[0] != '{' && body[0] != '[')
                return body;
            return fallback;
        }

        private void ShowToast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.time + 4.5f;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (RtgGameSessionLogin.IsPlayBlocked())
            {
                _hudRects.Clear();
                _missionExpanded = false;
                return;
            }

            _hudRects.Clear();
            DrawMissionHud();
            DrawVictoryBanner();
            DrawToast();
        }

        private void TrackHudRect(Rect rect)
        {
            _hudRects.Add(rect);
        }

        /// <summary>
        /// Screen.safeArea is bottom-left origin; OnGUI is top-left. Returns safe rect in GUI space.
        /// </summary>
        private static Rect GuiSafeArea()
        {
            Rect safe = Screen.safeArea;
            return new Rect(
                safe.x,
                Screen.height - safe.yMax,
                safe.width,
                safe.height);
        }

        private void DrawMissionHud()
        {
            if (_progress == null && string.IsNullOrEmpty(_error)) return;

            DrawCompactMissionPanel();
            if (_missionExpanded)
                DrawExpandedMissionModal();
        }

        private void DrawCompactMissionPanel()
        {
            Rect safe = GuiSafeArea();
            const float margin = 12f;
            const float pad = 12f;
            const float gap = 8f;
            const float btnH = 34f;

            // Top-right compact panel — leave left mid free for Exit/Gear.
            float panelW = Mathf.Clamp(Mathf.Min(340f, safe.width * 0.52f), 220f, 380f);
            float maxPanelH = Mathf.Clamp(safe.height * 0.36f, 120f, 260f);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            titleStyle.normal.textColor = new Color(0.95f, 0.98f, 1f);

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            bodyStyle.normal.textColor = new Color(0.82f, 0.9f, 0.98f);

            var progressStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            progressStyle.normal.textColor = new Color(0.55f, 0.95f, 0.75f);

            GetMissionCopy(out string title, out string objective, out string progressLabel);
            bool showFound = CanShowFoundBaseCamp();
            bool showVictoryStub = _progress != null && _progress.victory;

            float innerW = panelW - pad * 2f;
            float measureW = innerW;
            MeasureMissionContent(
                title, objective, progressLabel, showFound, showVictoryStub,
                titleStyle, bodyStyle, progressStyle,
                pad, gap, btnH, 28f, measureW,
                out float titleH, out float objectiveH, out float progressH,
                out float victoryStubH, out float contentH);

            float panelH = Mathf.Min(contentH, maxPanelH);
            bool needsScroll = contentH > panelH + 0.5f;

            // Remeasure with scrollbar gutter so wrapped lines aren't clipped.
            if (needsScroll)
            {
                measureW = innerW - 16f;
                MeasureMissionContent(
                    title, objective, progressLabel, showFound, showVictoryStub,
                    titleStyle, bodyStyle, progressStyle,
                    pad, gap, btnH, 28f, measureW,
                    out titleH, out objectiveH, out progressH,
                    out victoryStubH, out contentH);
                panelH = Mathf.Min(contentH, maxPanelH);
            }

            float x = safe.xMax - panelW - margin;
            float y = safe.y + margin;
            x = Mathf.Max(safe.x + margin, x);
            y = Mathf.Min(y, safe.yMax - panelH - margin);

            var panel = new Rect(x, y, panelW, panelH);
            TrackHudRect(panel);

            // Only expand from compact when collapsed — expanded modal owns close taps.
            if (!_missionExpanded)
                HandleMissionPanelToggle(panel, expandOnly: true);

            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.08f, 0.14f, 0.88f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = prev;

            var viewRect = new Rect(x, y, panelW, panelH);
            var contentRect = new Rect(0f, 0f, panelW, contentH);
            _missionScroll = GUI.BeginScrollView(viewRect, _missionScroll, contentRect, false, needsScroll);

            DrawMissionContent(
                pad, gap, btnH, measureW,
                title, objective, progressLabel,
                titleH, objectiveH, progressH, victoryStubH,
                titleStyle, bodyStyle, progressStyle,
                showFound, showVictoryStub,
                foundBtnFontSize: 13,
                foundBtnMaxW: 148f,
                interactFoundButton: false,
                out _);

            GUI.EndScrollView();
        }

        private void DrawExpandedMissionModal()
        {
            Rect safe = GuiSafeArea();
            const float margin = 16f;
            const float pad = 20f;
            const float gap = 14f;
            const float btnH = 48f;
            const float hintH = 28f;

            // Dimmed full-screen backdrop — tap closes; blocks world/ship hits.
            var backdrop = new Rect(0f, 0f, Screen.width, Screen.height);
            TrackHudRect(backdrop);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(backdrop, Texture2D.whiteTexture);
            GUI.color = prev;

            float panelW = Mathf.Clamp(safe.width - margin * 2f, 280f, Mathf.Min(560f, safe.width - margin * 2f));
            float maxPanelH = Mathf.Clamp(safe.height - margin * 2f, 220f, safe.height - margin * 2f);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            titleStyle.normal.textColor = new Color(0.95f, 0.98f, 1f);

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            bodyStyle.normal.textColor = new Color(0.82f, 0.9f, 0.98f);

            var progressStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            progressStyle.normal.textColor = new Color(0.55f, 0.95f, 0.75f);

            GetMissionCopy(out string title, out string objective, out string progressLabel);
            bool showFound = CanShowFoundBaseCamp();
            bool showVictoryStub = _progress != null && _progress.victory;

            float innerW = panelW - pad * 2f;
            float measureW = innerW;
            MeasureMissionContent(
                title, objective, progressLabel, showFound, showVictoryStub,
                titleStyle, bodyStyle, progressStyle,
                pad, gap, btnH, 44f, measureW,
                out float titleH, out float objectiveH, out float progressH,
                out float victoryStubH, out float contentH);
            contentH += gap + hintH;

            float panelH = Mathf.Min(contentH, maxPanelH);
            bool needsScroll = contentH > panelH + 0.5f;
            if (needsScroll)
            {
                measureW = innerW - 16f;
                MeasureMissionContent(
                    title, objective, progressLabel, showFound, showVictoryStub,
                    titleStyle, bodyStyle, progressStyle,
                    pad, gap, btnH, 44f, measureW,
                    out titleH, out objectiveH, out progressH,
                    out victoryStubH, out contentH);
                contentH += gap + hintH;
                panelH = Mathf.Min(contentH, maxPanelH);
            }

            float x = safe.x + (safe.width - panelW) * 0.5f;
            float y = safe.y + (safe.height - panelH) * 0.5f;
            x = Mathf.Clamp(x, safe.x + margin, safe.xMax - panelW - margin);
            y = Mathf.Clamp(y, safe.y + margin, safe.yMax - panelH - margin);

            var panel = new Rect(x, y, panelW, panelH);
            TrackHudRect(panel);

            prev = GUI.color;
            GUI.color = new Color(0.05f, 0.08f, 0.14f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = prev;

            var viewRect = new Rect(x, y, panelW, panelH);
            var contentRect = new Rect(0f, 0f, panelW, contentH);
            _missionExpandedScroll = GUI.BeginScrollView(
                viewRect, _missionExpandedScroll, contentRect, false, needsScroll);

            float cy = DrawMissionContent(
                pad, gap, btnH, measureW,
                title, objective, progressLabel,
                titleH, objectiveH, progressH, victoryStubH,
                titleStyle, bodyStyle, progressStyle,
                showFound, showVictoryStub,
                foundBtnFontSize: 20,
                foundBtnMaxW: 240f,
                interactFoundButton: true,
                out Rect foundBtnLocal);

            var hintStyle = new GUIStyle(bodyStyle)
            {
                fontSize = 18,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.UpperLeft,
            };
            hintStyle.normal.textColor = new Color(0.65f, 0.72f, 0.82f, 0.9f);
            GUI.Label(
                new Rect(pad, cy + gap, measureW, hintH),
                "Tap anywhere to close",
                hintStyle);

            GUI.EndScrollView();

            // Map Found button into screen space (scroll-aware) so it doesn't collapse the modal.
            bool overFound = false;
            if (showFound && foundBtnLocal.width > 0f)
            {
                var foundScreen = new Rect(
                    x + foundBtnLocal.x - _missionExpandedScroll.x,
                    y + foundBtnLocal.y - _missionExpandedScroll.y,
                    foundBtnLocal.width,
                    foundBtnLocal.height);
                TrackHudRect(foundScreen);
                overFound = foundScreen.Contains(Event.current.mousePosition);
            }

            // Close after Found Base Camp so GUI.Button can Use() the click first.
            Event e = Event.current;
            if (e.type == EventType.MouseDown
                && e.button == 0
                && !overFound
                && backdrop.Contains(e.mousePosition))
            {
                _missionExpanded = false;
                e.Use();
            }
        }

        private void GetMissionCopy(out string title, out string objective, out string progressLabel)
        {
            title = _progress != null ? _progress.title : "Missions";
            objective = _progress != null ? _progress.objective : (_error ?? "");
            progressLabel = _progress != null ? (_progress.progressLabel ?? "") : "";
        }

        private bool CanShowFoundBaseCamp() =>
            _progress != null
            && string.Equals(_progress.currentMission, "B", StringComparison.OrdinalIgnoreCase)
            && !_actionInFlight;

        private void HandleMissionPanelToggle(Rect panel, bool expandOnly)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;
            if (!panel.Contains(e.mousePosition)) return;

            if (expandOnly)
                _missionExpanded = true;
            else
                _missionExpanded = !_missionExpanded;
            e.Use();
        }

        private static void MeasureMissionContent(
            string title,
            string objective,
            string progressLabel,
            bool showFound,
            bool showVictoryStub,
            GUIStyle titleStyle,
            GUIStyle bodyStyle,
            GUIStyle progressStyle,
            float pad,
            float gap,
            float btnH,
            float victoryStubHFixed,
            float measureW,
            out float titleH,
            out float objectiveH,
            out float progressH,
            out float victoryStubH,
            out float contentH)
        {
            titleH = Mathf.Max(22f, titleStyle.CalcHeight(new GUIContent(title), measureW));
            objectiveH = Mathf.Max(18f, bodyStyle.CalcHeight(new GUIContent(objective), measureW));
            progressH = string.IsNullOrEmpty(progressLabel)
                ? 0f
                : Mathf.Max(20f, progressStyle.CalcHeight(new GUIContent(progressLabel), measureW));
            victoryStubH = showVictoryStub ? victoryStubHFixed : 0f;

            contentH = pad + titleH + gap + objectiveH;
            if (progressH > 0f) contentH += gap + progressH;
            if (showVictoryStub) contentH += gap + victoryStubH;
            if (showFound) contentH += gap + btnH;
            contentH += pad;
        }

        /// <returns>Y cursor after last drawn content block (before trailing pad).</returns>
        private float DrawMissionContent(
            float pad,
            float gap,
            float btnH,
            float labelW,
            string title,
            string objective,
            string progressLabel,
            float titleH,
            float objectiveH,
            float progressH,
            float victoryStubH,
            GUIStyle titleStyle,
            GUIStyle bodyStyle,
            GUIStyle progressStyle,
            bool showFound,
            bool showVictoryStub,
            int foundBtnFontSize,
            float foundBtnMaxW,
            bool interactFoundButton,
            out Rect foundBtnLocal)
        {
            foundBtnLocal = default;
            float cy = pad;
            GUI.Label(new Rect(pad, cy, labelW, titleH), title, titleStyle);
            cy += titleH + gap;

            GUI.Label(new Rect(pad, cy, labelW, objectiveH), objective, bodyStyle);
            cy += objectiveH;

            if (progressH > 0f)
            {
                cy += gap;
                GUI.Label(new Rect(pad, cy, labelW, progressH), progressLabel, progressStyle);
                cy += progressH;
            }

            if (showVictoryStub)
            {
                cy += gap;
                var stubStyle = new GUIStyle(bodyStyle)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };
                stubStyle.normal.textColor = new Color(0.95f, 0.9f, 0.45f);
                GUI.Label(
                    new Rect(pad, cy, labelW, victoryStubH),
                    "Victory — reserves full (stats UI soon).",
                    stubStyle);
                cy += victoryStubH;
            }

            if (showFound)
            {
                cy += gap;
                float btnW = Mathf.Min(foundBtnMaxW, labelW);
                foundBtnLocal = new Rect(pad, cy, btnW, btnH);
                int prevFont = GUI.skin.button.fontSize;
                GUI.skin.button.fontSize = foundBtnFontSize;
                if (interactFoundButton)
                {
                    if (GUI.Button(foundBtnLocal, "Found Base Camp"))
                        StartCoroutine(FoundBaseCampRoutine());
                }
                else
                {
                    // Visual stub only — compact panel tap expands the readable modal.
                    GUI.Box(foundBtnLocal, "Found Base Camp");
                }
                GUI.skin.button.fontSize = prevFont;
                cy += btnH;
            }

            return cy;
        }

        private void DrawVictoryBanner()
        {
            if (_progress == null || !_progress.victory || _victoryBannerDismissed)
                return;

            Rect safe = GuiSafeArea();
            const float panelW = 360f;
            const float panelH = 160f;
            float x = safe.x + (safe.width - panelW) * 0.5f;
            float y = safe.y + safe.height * 0.28f;
            x = Mathf.Clamp(x, safe.x + 12f, safe.xMax - panelW - 12f);
            y = Mathf.Clamp(y, safe.y + 12f, safe.yMax - panelH - 12f);
            var panel = new Rect(x, y, panelW, panelH);
            TrackHudRect(panel);

            Color prev = GUI.color;
            GUI.color = new Color(0.08f, 0.12f, 0.08f, 0.94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = prev;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = new Color(0.95f, 0.9f, 0.45f);

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            bodyStyle.normal.textColor = Color.white;

            GUI.Label(new Rect(x, y + 18f, panelW, 40f), "Victory!", titleStyle);
            GUI.Label(
                new Rect(x + 20f, y + 62f, panelW - 40f, 48f),
                "Missions complete. Xenite reserves are full.\n(Victory Stats UI — criterion 3 — coming soon.)",
                bodyStyle);

            var dismiss = new Rect(x + (panelW - 120f) * 0.5f, y + panelH - 44f, 120f, 32f);
            TrackHudRect(dismiss);
            if (GUI.Button(dismiss, "Continue"))
                _victoryBannerDismissed = true;
        }

        private void DrawToast()
        {
            if (string.IsNullOrEmpty(_toast) || Time.time > _toastUntil) return;

            Rect safe = GuiSafeArea();
            const float w = 440f;
            const float h = 48f;
            float toastW = Mathf.Min(w, safe.width - 24f);
            float x = safe.x + (safe.width - toastW) * 0.5f;
            float y = safe.yMax - 72f - h;
            var rect = new Rect(x, y, toastW, h);
            TrackHudRect(rect);

            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.9f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            style.normal.textColor = Color.white;
            GUI.Label(rect, _toast, style);
        }
    }
}
