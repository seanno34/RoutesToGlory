using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Full-screen IMGUI join overlay: user PIN + game session ID + saved-sessions dropdown.
    /// Gates world load and play interaction until the user explicitly Joins or starts New Game
    /// (PlayerPrefs may prefill PIN/session; silent auto-resume is disabled).
    /// Sibling of the Echo Site loader — not part of <see cref="RtgPlayerLocation"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class RtgGameSessionLogin : MonoBehaviour
    {
        private const string PrefApiBaseUrl = "rtg.apiBaseUrl";
        private const string PrefWorldId = "rtg.worldId";
        private const string PrefEmpireId = "rtg.empireId";
        private const string PrefAccessCode = "rtg.accessCode";
        private const string PrefSlug = "rtg.slug";
        private const string PrefUserPin = "rtg.userPin";

        [Tooltip("When true, show the join overlay until a session is applied.")]
        public bool requireLoginOverlay = true;

        private RtgEchoSiteLoader _loader;
        private bool _sessionReady;
        private bool _overlayVisible;
        private bool _busy;
        private string _userPinDraft = "";
        private string _accessCodeDraft = "";
        private string _rememberedAccessCode = "";
        private string _status = "";
        private string _error = "";
        private Vector2 _savedScroll;
        private readonly List<SavedWorldSummary> _savedWorlds = new();
        private int _selectedSavedIndex = -1;
        private string _lastListedPin = "";

        /// <summary>True after Join / New Game (or Editor sample) confirms a playable session.</summary>
        public bool HasSession => _sessionReady;

        /// <summary>True while the join overlay is blocking play.</summary>
        public bool IsLoginOverlayOpen => requireLoginOverlay && _overlayVisible && !_sessionReady;

        /// <summary>Blocks world load / ship play until <see cref="HasSession"/>.</summary>
        public bool BlocksPlay => requireLoginOverlay && !_sessionReady;

        [Serializable]
        private class SavedWorldSummary
        {
            public string accessCode;
            public string id;
            public string slug;
            public string name;
            public string empireId;
            public string userId;
            public string playerName;
            public int settlementCount;
            public string createdAt;
        }

        [Serializable]
        private class SavedWorldsResponse
        {
            public SavedWorldSummary[] worlds;
        }

        [Serializable]
        private class BootstrapWorldResponse
        {
            public string id;
            public string slug;
            public string accessCode;
            public string empireId;
            public string userId;
            public int settlementCount;
            public string storage;
            public string error;
        }

        [Serializable]
        private class CreateWorldRequest
        {
            public string name;
            public string playerName;
            public string pin;
            public double spawnLat;
            public double spawnLng;
        }

        /// <summary>
        /// POC play-area default (Douglas / Orin Junction). Must match API
        /// <c>POC_DEFAULT_SPAWN_*</c> and <see cref="RtgEchoSiteLoader"/> scatter center.
        /// </summary>
        private const double PocDefaultSpawnLat = 42.7597;
        private const double PocDefaultSpawnLng = -105.3819;

        /// <summary>Ensure a login component exists on the Echo Sites GameObject.</summary>
        public static RtgGameSessionLogin EnsureOn(RtgEchoSiteLoader loader)
        {
            if (loader == null) return null;
            var existing = loader.GetComponent<RtgGameSessionLogin>();
            if (existing != null) return existing;
            return loader.gameObject.AddComponent<RtgGameSessionLogin>();
        }

        /// <summary>True when any login component is blocking world load for this loader.</summary>
        public static bool IsBlockingWorldLoad(RtgEchoSiteLoader loader)
        {
            if (loader == null) return false;
            var login = loader.GetComponent<RtgGameSessionLogin>();
            if (login == null)
            {
#if UNITY_2023_1_OR_NEWER
                login = FindFirstObjectByType<RtgGameSessionLogin>();
#else
                login = FindObjectOfType<RtgGameSessionLogin>();
#endif
            }
            return login != null && login.BlocksPlay;
        }

        private static RtgGameSessionLogin _playBlockCache;
        private static int _playBlockCacheFrame = -1;

        /// <summary>True when play interaction should be disabled (overlay / no session).</summary>
        public static bool IsPlayBlocked()
        {
            int frame = Time.frameCount;
            if (_playBlockCacheFrame != frame)
            {
                _playBlockCacheFrame = frame;
#if UNITY_2023_1_OR_NEWER
                _playBlockCache = FindFirstObjectByType<RtgGameSessionLogin>();
#else
                _playBlockCache = FindObjectOfType<RtgGameSessionLogin>();
#endif
            }
            return _playBlockCache != null && _playBlockCache.BlocksPlay;
        }

        /// <summary>Find the active login component (if any).</summary>
        public static RtgGameSessionLogin FindActive()
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<RtgGameSessionLogin>();
#else
            return FindObjectOfType<RtgGameSessionLogin>();
#endif
        }

        private void Awake()
        {
            ResolveLoader();
            GateWorldLoadUntilSession();
        }

        private void Start()
        {
            ResolveLoader();
            GateWorldLoadUntilSession();

            if (!requireLoginOverlay)
            {
                _sessionReady = true;
                _overlayVisible = false;
                if (_loader != null)
                {
                    _loader.SetSessionLoadAllowed(true);
                    _loader.loadOnPlay = true;
                    _loader.ReloadFromConfiguredSource();
                }
                return;
            }

            // Never silent-auto-join: prefill remembered PIN/session and require Join.
            PrefillFromPlayerPrefs();
            ShowLoginOverlay();
        }

        private void ResolveLoader()
        {
            if (_loader != null) return;
            _loader = GetComponent<RtgEchoSiteLoader>();
            if (_loader != null) return;
#if UNITY_2023_1_OR_NEWER
            _loader = FindFirstObjectByType<RtgEchoSiteLoader>();
#else
            _loader = FindObjectOfType<RtgEchoSiteLoader>();
#endif
        }

        private void GateWorldLoadUntilSession()
        {
            if (_loader == null || _sessionReady) return;
            _loader.loadOnPlay = false;
            _loader.SetSessionLoadAllowed(false);
        }

        private void PrefillFromPlayerPrefs()
        {
            string pin = PlayerPrefs.GetString(PrefUserPin, "");
            if (!string.IsNullOrWhiteSpace(pin))
                _userPinDraft = RtgApiHttp.NormalizeUserPin(pin);

            string code = PlayerPrefs.GetString(PrefAccessCode, "");
            if (string.IsNullOrWhiteSpace(code)) return;

            _rememberedAccessCode = RtgApiHttp.NormalizeAccessCode(code);
            if (string.IsNullOrWhiteSpace(_accessCodeDraft))
                _accessCodeDraft = _rememberedAccessCode;
        }

        private void ShowLoginOverlay()
        {
            _sessionReady = false;
            _overlayVisible = true;
            GateWorldLoadUntilSession();

            string pin = RtgApiHttp.NormalizeUserPin(_userPinDraft);
            if (!string.IsNullOrEmpty(pin) && !string.IsNullOrEmpty(_rememberedAccessCode))
            {
                _status =
                    $"PIN {pin} remembered. Session {_rememberedAccessCode} prefills — " +
                    "confirm Join (or Exit / New Game). World will not load until then.";
            }
            else if (!string.IsNullOrEmpty(pin))
            {
                _status = $"PIN {pin} ready. Pick or enter a game session, then Join — or New Game.";
            }
            else
            {
                _status = "Enter your 4-digit user PIN, then pick a game session (or New Game).";
            }

            if (!string.IsNullOrEmpty(pin))
                StartCoroutine(RefreshSavedWorlds());
        }

        /// <summary>
        /// Leave the active session: clear play state / markers, keep PIN, clear game selection,
        /// and show the join overlay again (no silent resume).
        /// </summary>
        public void ExitSessionToLogin()
        {
            if (_busy) return;

            ResolveLoader();
            _sessionReady = false;
            _overlayVisible = true;
            _error = "";
            _accessCodeDraft = "";
            _rememberedAccessCode = "";
            _selectedSavedIndex = -1;
            _savedWorlds.Clear();
            _lastListedPin = "";

            // Prefer keeping the in-memory PIN; fall back to PlayerPrefs.
            if (string.IsNullOrWhiteSpace(_userPinDraft))
            {
                string prefsPin = PlayerPrefs.GetString(PrefUserPin, "");
                if (!string.IsNullOrWhiteSpace(prefsPin))
                    _userPinDraft = RtgApiHttp.NormalizeUserPin(prefsPin);
            }
            else
            {
                string normalized = RtgApiHttp.NormalizeUserPin(_userPinDraft);
                if (!string.IsNullOrEmpty(normalized))
                    _userPinDraft = normalized;
            }

            PlayerPrefs.DeleteKey(PrefWorldId);
            PlayerPrefs.DeleteKey(PrefEmpireId);
            PlayerPrefs.DeleteKey(PrefAccessCode);
            PlayerPrefs.DeleteKey(PrefSlug);
            PlayerPrefs.Save();

            if (_loader != null)
            {
                _loader.worldId = "";
                _loader.empireId = "";
                _loader.SetSessionLoadAllowed(false);
            }

            SyncRouteSession();
            EndActiveRouteSession();

            string pin = RtgApiHttp.NormalizeUserPin(_userPinDraft);
            _status = string.IsNullOrEmpty(pin)
                ? "Exited session. Enter your user PIN and pick a game session (or New Game)."
                : $"Exited session. PIN {pin} kept — pick a game session or New Game, then Join.";

            Debug.Log("[RTG] Exited game session — returned to login overlay.");

            if (!string.IsNullOrEmpty(pin))
                StartCoroutine(RefreshSavedWorlds());
        }

        private void ApplyLiveSessionAndLoad()
        {
            if (_loader == null) return;
            _loader.dataSource = RtgEchoSiteLoader.DataSource.LiveApi;
            _loader.loadOnPlay = false;
            _loader.SetSessionLoadAllowed(true);
            _loader.ReloadFromConfiguredSource();
            SyncRouteSession();
        }

        /// <summary>
        /// Seed New Game resources under the player (or POC Douglas center before GPS starts).
        /// Omitting spawn made the API default to Denver — xenite existed but ~330 km from the camera.
        /// </summary>
        private void ResolveNewGameSpawn(out double spawnLat, out double spawnLng)
        {
            spawnLat = PocDefaultSpawnLat;
            spawnLng = PocDefaultSpawnLng;

#if UNITY_2023_1_OR_NEWER
            var player = FindFirstObjectByType<RtgPlayerLocation>();
#else
            var player = FindObjectOfType<RtgPlayerLocation>();
#endif
            if (player != null)
            {
                if (player.TryGetPlayerLatLng(out double lat, out double lng)
                    && IsPlausibleSpawn(lat, lng))
                {
                    spawnLat = lat;
                    spawnLng = lng;
                    return;
                }

                if (IsPlausibleSpawn(player.tourCenterLatitude, player.tourCenterLongitude))
                {
                    spawnLat = player.tourCenterLatitude;
                    spawnLng = player.tourCenterLongitude;
                    return;
                }
            }

            if (_loader != null && IsPlausibleSpawn(_loader.scatterCenterLat, _loader.scatterCenterLng))
            {
                spawnLat = _loader.scatterCenterLat;
                spawnLng = _loader.scatterCenterLng;
            }
        }

        private static bool IsPlausibleSpawn(double lat, double lng)
        {
            if (double.IsNaN(lat) || double.IsNaN(lng) || double.IsInfinity(lat) || double.IsInfinity(lng))
                return false;
            if (lat < -90.0 || lat > 90.0 || lng < -180.0 || lng > 180.0)
                return false;
            // Reject uninitialized globe anchors still at 0,0.
            return Math.Abs(lat) > 0.01 || Math.Abs(lng) > 0.01;
        }

        private void SyncRouteSession()
        {
#if UNITY_2023_1_OR_NEWER
            var routes = FindFirstObjectByType<RtgRouteSession>();
#else
            var routes = FindObjectOfType<RtgRouteSession>();
#endif
            routes?.SyncConfigFromEchoLoader();
        }

        private void EndActiveRouteSession()
        {
#if UNITY_2023_1_OR_NEWER
            var routes = FindFirstObjectByType<RtgRouteSession>();
#else
            var routes = FindObjectOfType<RtgRouteSession>();
#endif
            if (routes != null && routes.IsActive)
                routes.End();
        }

        private void PersistBootstrap(string apiBaseUrl, string worldId, string empireId, string accessCode, string slug, string pin)
        {
            if (!string.IsNullOrWhiteSpace(apiBaseUrl))
                PlayerPrefs.SetString(PrefApiBaseUrl, apiBaseUrl.Trim());
            PlayerPrefs.SetString(PrefWorldId, worldId ?? "");
            PlayerPrefs.SetString(PrefEmpireId, empireId ?? "");
            PlayerPrefs.SetString(PrefAccessCode, accessCode ?? "");
            PlayerPrefs.SetString(PrefSlug, slug ?? "");
            if (!string.IsNullOrWhiteSpace(pin))
                PlayerPrefs.SetString(PrefUserPin, pin);
            PlayerPrefs.Save();
        }

        private void ClearPersistedSessionKeepPin()
        {
            PlayerPrefs.DeleteKey(PrefWorldId);
            PlayerPrefs.DeleteKey(PrefEmpireId);
            PlayerPrefs.DeleteKey(PrefAccessCode);
            PlayerPrefs.DeleteKey(PrefSlug);
            PlayerPrefs.Save();
            _rememberedAccessCode = "";
            _accessCodeDraft = "";
            _selectedSavedIndex = -1;
            _error = "";
            string pin = RtgApiHttp.NormalizeUserPin(_userPinDraft);
            _status = string.IsNullOrEmpty(pin)
                ? "Signed out of session. Enter PIN and pick a game session, then Join."
                : $"Session cleared. PIN {pin} kept — pick a game session or New Game.";
            GateWorldLoadUntilSession();
            Debug.Log("[RTG] Session signed out — world prefs cleared, PIN kept.");
        }

        private string ResolveApiBaseUrl()
        {
            if (_loader != null && !string.IsNullOrWhiteSpace(_loader.apiBaseUrl))
                return _loader.apiBaseUrl.TrimEnd('/');
            string prefs = PlayerPrefs.GetString(PrefApiBaseUrl, "");
            if (!string.IsNullOrWhiteSpace(prefs))
                return prefs.TrimEnd('/');
            return "http://localhost:3001/api";
        }

        private bool CanJoin()
        {
            if (_busy) return false;
            string pin = RtgApiHttp.NormalizeUserPin(_userPinDraft);
            string code = RtgApiHttp.NormalizeAccessCode(_accessCodeDraft);
            return !string.IsNullOrEmpty(pin) && !string.IsNullOrEmpty(code);
        }

        private bool CanCreateNewGame()
        {
            if (_busy) return false;
            return !string.IsNullOrEmpty(RtgApiHttp.NormalizeUserPin(_userPinDraft));
        }

        private void TryJoinFromUi()
        {
            string pin = RtgApiHttp.NormalizeUserPin(_userPinDraft);
            string code = RtgApiHttp.NormalizeAccessCode(_accessCodeDraft);
            if (string.IsNullOrEmpty(pin))
            {
                _error = "Enter your 4-digit user PIN before joining.";
                _status = "";
                return;
            }
            if (string.IsNullOrEmpty(code))
            {
                _error = "Select or enter a game session ID before joining.";
                _status = "";
                return;
            }

            StartCoroutine(JoinByAccessCode(code, pin));
        }

        private void TryNewGameFromUi()
        {
            string pin = RtgApiHttp.NormalizeUserPin(_userPinDraft);
            if (string.IsNullOrEmpty(pin))
            {
                _error = "Enter your 4-digit user PIN before starting a new game.";
                _status = "";
                return;
            }

            StartCoroutine(CreateNewGame(pin));
        }

        private IEnumerator RefreshSavedWorlds()
        {
            string pin = RtgApiHttp.NormalizeUserPin(_userPinDraft);
            if (string.IsNullOrEmpty(pin))
            {
                _savedWorlds.Clear();
                _selectedSavedIndex = -1;
                _lastListedPin = "";
                _status = "Enter your 4-digit user PIN to load your game sessions.";
                yield break;
            }

            string url = RtgApiHttp.JoinUrl(ResolveApiBaseUrl(), $"worlds/saved?pin={UnityEngine.Networking.UnityWebRequest.EscapeURL(pin)}");
            string body = null;
            string error = null;
            yield return RtgApiHttp.Get(url, (b, e) =>
            {
                body = b;
                error = e;
            });

            // Ignore stale responses if the PIN changed while the request was in flight.
            if (RtgApiHttp.NormalizeUserPin(_userPinDraft) != pin)
                yield break;

            _savedWorlds.Clear();
            _selectedSavedIndex = -1;
            _lastListedPin = pin;

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(body))
            {
                if (string.IsNullOrEmpty(_error))
                    _status = "Could not load your sessions (is the API running?). You can still join by session ID.";
                yield break;
            }

            try
            {
                var resp = JsonUtility.FromJson<SavedWorldsResponse>(body);
                if (resp?.worlds != null)
                {
                    foreach (var w in resp.worlds)
                    {
                        if (w != null && !string.IsNullOrWhiteSpace(w.accessCode))
                            _savedWorlds.Add(w);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RTG] Failed to parse /worlds/saved: {ex.Message}");
                _status = "Game sessions list unavailable.";
                yield break;
            }

            _status = _savedWorlds.Count > 0
                ? $"PIN {pin}: {_savedWorlds.Count} session(s). Select one to fill the game ID, then Join — or New Game."
                : $"PIN {pin}: no sessions yet — press New Game to create a world.";
        }

        private IEnumerator JoinByAccessCode(string rawCode, string pin)
        {
            string code = RtgApiHttp.NormalizeAccessCode(rawCode);
            string normalizedPin = RtgApiHttp.NormalizeUserPin(pin);
            if (string.IsNullOrEmpty(normalizedPin))
            {
                _error = "Enter your 4-digit user PIN before joining.";
                _status = "";
                yield break;
            }
            if (string.IsNullOrEmpty(code))
            {
                _error = "Select or enter a game session ID before joining.";
                _status = "";
                yield break;
            }

            _busy = true;
            _error = "";
            _status = "Joining…";

            string url = RtgApiHttp.JoinUrl(
                ResolveApiBaseUrl(),
                $"worlds/by-code/{UnityEngine.Networking.UnityWebRequest.EscapeURL(code)}?pin={UnityEngine.Networking.UnityWebRequest.EscapeURL(normalizedPin)}");
            string body = null;
            string error = null;
            yield return RtgApiHttp.Get(url, (b, e) =>
            {
                body = b;
                error = e;
            });

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(body))
            {
                _busy = false;
                string apiError = TryParseApiError(body);
                _error = !string.IsNullOrEmpty(apiError)
                    ? apiError
                    : (string.IsNullOrEmpty(error) ? "Join failed." : $"Join failed: {error}");
                _status = "";
                yield break;
            }

            BootstrapWorldResponse bootstrap = null;
            try
            {
                bootstrap = JsonUtility.FromJson<BootstrapWorldResponse>(body);
            }
            catch (Exception ex)
            {
                _busy = false;
                _error = $"Bad response: {ex.Message}";
                yield break;
            }

            if (bootstrap == null || string.IsNullOrWhiteSpace(bootstrap.id) || string.IsNullOrWhiteSpace(bootstrap.empireId))
            {
                _busy = false;
                _error = !string.IsNullOrWhiteSpace(bootstrap?.error)
                    ? bootstrap.error
                    : "Game not found for that session ID / PIN.";
                _status = "";
                yield break;
            }

            ApplyBootstrapAndLoad(bootstrap, code, normalizedPin);
        }

        private IEnumerator CreateNewGame(string pin)
        {
            string normalizedPin = RtgApiHttp.NormalizeUserPin(pin);
            if (string.IsNullOrEmpty(normalizedPin))
            {
                _error = "Enter your 4-digit user PIN before starting a new game.";
                _status = "";
                yield break;
            }

            if (_loader == null)
            {
                _error = "Echo Site loader missing from scene.";
                yield break;
            }

            _busy = true;
            _error = "";
            _status = "Creating new world…";

            ResolveNewGameSpawn(out double spawnLat, out double spawnLng);
            var reqBody = new CreateWorldRequest
            {
                name = $"World {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                playerName = $"PIN {normalizedPin}",
                pin = normalizedPin,
                spawnLat = spawnLat,
                spawnLng = spawnLng,
            };
            string json = JsonUtility.ToJson(reqBody);
            Debug.Log($"[RTG] New Game seeding play area at {spawnLat:F4}, {spawnLng:F4}");
            string url = RtgApiHttp.JoinUrl(ResolveApiBaseUrl(), "worlds");
            string body = null;
            string error = null;
            yield return RtgApiHttp.PostJson(
                url,
                json,
                (b, e) =>
                {
                    body = b;
                    error = e;
                },
                RtgApiHttp.CreateWorldTimeoutSeconds);

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(body))
            {
                _busy = false;
                string apiError = TryParseApiError(body);
                _error = !string.IsNullOrEmpty(apiError)
                    ? apiError
                    : (string.IsNullOrEmpty(error) ? "New Game failed." : $"New Game failed: {error}");
                _status = "";
                yield break;
            }

            BootstrapWorldResponse bootstrap = null;
            try
            {
                bootstrap = JsonUtility.FromJson<BootstrapWorldResponse>(body);
            }
            catch (Exception ex)
            {
                _busy = false;
                _error = $"Bad response: {ex.Message}";
                yield break;
            }

            if (bootstrap == null || string.IsNullOrWhiteSpace(bootstrap.id) || string.IsNullOrWhiteSpace(bootstrap.empireId))
            {
                _busy = false;
                _error = !string.IsNullOrWhiteSpace(bootstrap?.error)
                    ? bootstrap.error
                    : "New Game did not return a playable world.";
                _status = "";
                yield break;
            }

            string code = string.IsNullOrWhiteSpace(bootstrap.accessCode)
                ? ""
                : RtgApiHttp.NormalizeAccessCode(bootstrap.accessCode);
            ApplyBootstrapAndLoad(bootstrap, code, normalizedPin);
            Debug.Log($"[RTG] New Game created — session {code}, world {bootstrap.id}, pin {normalizedPin}");
        }

        private void ApplyBootstrapAndLoad(BootstrapWorldResponse bootstrap, string fallbackCode, string pin)
        {
            if (_loader == null)
            {
                _busy = false;
                _error = "Echo Site loader missing from scene.";
                return;
            }

            string apiBase = ResolveApiBaseUrl();
            _loader.apiBaseUrl = apiBase;
            _loader.worldId = bootstrap.id.Trim();
            _loader.empireId = bootstrap.empireId.Trim();
            _loader.dataSource = RtgEchoSiteLoader.DataSource.LiveApi;

            string accessCode = string.IsNullOrWhiteSpace(bootstrap.accessCode)
                ? RtgApiHttp.NormalizeAccessCode(fallbackCode)
                : RtgApiHttp.NormalizeAccessCode(bootstrap.accessCode);

            PersistBootstrap(apiBase, _loader.worldId, _loader.empireId, accessCode, bootstrap.slug ?? "", pin);
            _userPinDraft = pin;
            _accessCodeDraft = accessCode;
            _rememberedAccessCode = accessCode;
            _sessionReady = true;
            _overlayVisible = false;
            _busy = false;
            _status = $"Joined session {accessCode}";
            Debug.Log($"[RTG] Session ready — pin {pin}, code {accessCode}, world {_loader.worldId}");

            ApplyLiveSessionAndLoad();
        }

        private static string TryParseApiError(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            try
            {
                var errResp = JsonUtility.FromJson<BootstrapWorldResponse>(body);
                if (!string.IsNullOrWhiteSpace(errResp?.error))
                    return errResp.error;
            }
            catch { /* ignore */ }
            return null;
        }

        private void UseSampleWorldDev()
        {
#if UNITY_EDITOR
            if (_loader == null)
            {
                _error = "Echo Site loader missing from scene.";
                return;
            }

            _loader.dataSource = RtgEchoSiteLoader.DataSource.SampleFile;
            _loader.loadOnPlay = false;
            _loader.SetSessionLoadAllowed(true);
            _sessionReady = true;
            _overlayVisible = false;
            _error = "";
            _status = "Using sample world (Editor-only).";
            _loader.ReloadFromConfiguredSource();
            SyncRouteSession();
            Debug.Log("[RTG] Session login: Editor sample-world escape hatch.");
#else
            _error = "Sample world is Editor-only.";
#endif
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !_overlayVisible || _sessionReady) return;

            float scale = Mathf.Clamp(Screen.height / 900f, 0.85f, 1.6f);
            float pad = 20f * scale;
            float panelW = Mathf.Min(540f * scale, Screen.width - pad * 2f);
            float panelH = Mathf.Min(680f * scale, Screen.height - pad * 2f);
            var panel = new Rect((Screen.width - panelW) * 0.5f, (Screen.height - panelH) * 0.5f, panelW, panelH);

            Color prev = GUI.color;
            GUI.color = new Color(0.02f, 0.04f, 0.1f, 0.88f);
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);
            GUI.color = new Color(0.06f, 0.09f, 0.18f, 0.96f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = prev;

            float x = panel.x + pad;
            float y = panel.y + pad;
            float innerW = panel.width - pad * 2f;
            float rowH = 28f * scale;

            var titleStyle = BrightLabel(Mathf.RoundToInt(22f * scale), new Color(0.97f, 0.99f, 1f), FontStyle.Bold);
            GUI.Label(new Rect(x, y, innerW, 32f * scale), "Join game", titleStyle);
            y += 36f * scale;

            var bodyStyle = BrightLabel(Mathf.RoundToInt(13f * scale), new Color(0.82f, 0.9f, 0.98f));
            var codeHintStyle = BrightLabel(Mathf.RoundToInt(14f * scale), new Color(1f, 0.88f, 0.45f), FontStyle.Bold);

            string rememberedPin = RtgApiHttp.NormalizeUserPin(_userPinDraft);
            if (!string.IsNullOrEmpty(rememberedPin) && !string.IsNullOrEmpty(_rememberedAccessCode))
            {
                GUI.Label(
                    new Rect(x, y, innerW, rowH),
                    $"Remembered: PIN {rememberedPin} · session {_rememberedAccessCode}",
                    codeHintStyle);
                y += rowH + 4f * scale;
            }

            GUI.Label(new Rect(x, y, innerW, rowH), "User PIN (4 digits)", bodyStyle);
            y += rowH;

            var prevFont = GUI.skin.textField.fontSize;
            GUI.skin.textField.fontSize = Mathf.RoundToInt(16f * scale);
            GUI.enabled = !_busy;
            string nextPin = GUI.TextField(new Rect(x, y, innerW, 36f * scale), _userPinDraft ?? "");
            if (nextPin != _userPinDraft)
            {
                _userPinDraft = nextPin;
                _selectedSavedIndex = -1;
                if (!string.IsNullOrEmpty(_error) &&
                    _error.IndexOf("PIN", StringComparison.OrdinalIgnoreCase) >= 0)
                    _error = "";

                string normalized = RtgApiHttp.NormalizeUserPin(_userPinDraft);
                if (!string.IsNullOrEmpty(normalized) && normalized != _lastListedPin)
                    StartCoroutine(RefreshSavedWorlds());
                else if (string.IsNullOrEmpty(normalized))
                {
                    _savedWorlds.Clear();
                    _lastListedPin = "";
                }
            }
            GUI.skin.textField.fontSize = prevFont;
            y += 44f * scale;

            GUI.Label(new Rect(x, y, innerW, rowH), "Game session ID (access code)", bodyStyle);
            y += rowH;

            GUI.skin.textField.fontSize = Mathf.RoundToInt(16f * scale);
            GUI.enabled = !_busy;
            string nextCode = GUI.TextField(new Rect(x, y, innerW, 36f * scale), _accessCodeDraft ?? "");
            if (nextCode != _accessCodeDraft)
            {
                _accessCodeDraft = nextCode;
                _selectedSavedIndex = -1;
                if (!string.IsNullOrEmpty(_error) &&
                    (_error.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     _error.IndexOf("access code", StringComparison.OrdinalIgnoreCase) >= 0))
                    _error = "";
            }
            GUI.skin.textField.fontSize = prevFont;
            y += 44f * scale;

            GUI.Label(new Rect(x, y, innerW, rowH), "Your sessions (select fills ID — then press Join)", bodyStyle);
            y += rowH;

            float listH = Mathf.Max(90f * scale, panel.yMax - y - 230f * scale);
            var listOuter = new Rect(x, y, innerW, listH);
            float contentH = Mathf.Max(listH, (_savedWorlds.Count + 1) * (rowH + 4f * scale) + 8f);
            _savedScroll = GUI.BeginScrollView(listOuter, _savedScroll, new Rect(0, 0, innerW - 16f * scale, contentH));
            float ly = 4f * scale;
            float itemW = innerW - 24f * scale;

            if (_savedWorlds.Count == 0)
            {
                GUI.Label(new Rect(4f * scale, ly, itemW, rowH), "(none for this PIN)", bodyStyle);
            }
            else
            {
                for (int i = 0; i < _savedWorlds.Count; i++)
                {
                    var w = _savedWorlds[i];
                    string label = FormatSavedGameLabel(w);
                    bool selected = i == _selectedSavedIndex;
                    var prevBtn = GUI.backgroundColor;
                    if (selected)
                        GUI.backgroundColor = new Color(0.25f, 0.55f, 0.85f, 1f);
                    if (GUI.Button(new Rect(4f * scale, ly, itemW, rowH + 2f * scale), label))
                    {
                        // Select only — never join from the list alone.
                        _selectedSavedIndex = i;
                        _accessCodeDraft = w.accessCode ?? "";
                        _error = "";
                        _status = $"Selected {FormatSavedGameLabel(w)}. Press Join to confirm.";
                    }
                    GUI.backgroundColor = prevBtn;
                    ly += rowH + 6f * scale;
                }
            }

            GUI.EndScrollView();
            y += listH + 12f * scale;

            float btnH = 40f * scale;
            float btnW = (innerW - 10f * scale) * 0.5f;
            var prevBtnFont = GUI.skin.button.fontSize;
            GUI.skin.button.fontSize = Mathf.RoundToInt(15f * scale);

            bool canJoin = CanJoin();
            GUI.enabled = canJoin;
            if (GUI.Button(new Rect(x, y, btnW, btnH), _busy ? "Joining…" : "Join"))
                TryJoinFromUi();

            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(x + btnW + 10f * scale, y, btnW, btnH), "Refresh list"))
                StartCoroutine(RefreshSavedWorlds());

            y += btnH + 10f * scale;

            // New Game is the primary create action on mobile and Editor.
            GUI.enabled = CanCreateNewGame();
            if (GUI.Button(new Rect(x, y, innerW, btnH), _busy ? "Creating…" : "New Game"))
                TryNewGameFromUi();

            y += btnH + 10f * scale;

            float thirdW = (innerW - 10f * scale) * 0.5f;
            GUI.enabled = !_busy;
            if (GUI.Button(new Rect(x, y, thirdW, 32f * scale), "Clear session") && !_busy)
                ClearPersistedSessionKeepPin();

#if UNITY_EDITOR
            if (GUI.Button(new Rect(x + thirdW + 10f * scale, y, thirdW, 32f * scale), "Sample (Editor)") && !_busy)
                UseSampleWorldDev();
#endif
            y += 40f * scale;

            GUI.skin.button.fontSize = prevBtnFont;
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_error))
            {
                var errStyle = BrightLabel(Mathf.RoundToInt(13f * scale), new Color(1f, 0.55f, 0.5f));
                GUI.Label(new Rect(x, y, innerW, rowH * 2f), _error, errStyle);
            }
            else if (!string.IsNullOrEmpty(_status))
            {
                GUI.Label(new Rect(x, y, innerW, rowH * 2.5f), _status, bodyStyle);
            }
        }

        private static string FormatSavedGameLabel(SavedWorldSummary world)
        {
            string date = FormatCreatedAt(world?.createdAt);
            string code = world?.accessCode ?? "???";
            string name = string.IsNullOrWhiteSpace(world?.name) ? "World" : world.name;
            return $"{code} — {name} ({date})";
        }

        private static string FormatCreatedAt(string createdAt)
        {
            if (string.IsNullOrWhiteSpace(createdAt))
                return "—";

            if (DateTime.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dt) ||
                DateTime.TryParse(createdAt, out dt))
            {
                return dt.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);
            }

            return createdAt;
        }

        private static GUIStyle BrightLabel(int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                wordWrap = true,
                richText = false,
            };
            style.normal.textColor = color;
            return style;
        }
    }
}
