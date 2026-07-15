using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>Normalized UV rectangle on cockpit art (origin top-left, 0–1).</summary>
    public readonly struct CockpitTextureAnchor
    {
        public readonly float CenterU;
        public readonly float CenterV;
        public readonly float WidthU;
        public readonly float HeightV;

        public CockpitTextureAnchor(float centerU, float centerV, float widthU, float heightV)
        {
            CenterU = centerU;
            CenterV = centerV;
            WidthU = widthU;
            HeightV = heightV;
        }
    }

    /// <summary>
    /// First-person cockpit overlay. Open glass canopy: world renders through the top and
    /// sides; only the lower dashboard and thin frame rails are drawn from cockpit art.
    /// </summary>
    public class RtgCockpitView : MonoBehaviour
    {
        private const string LandscapeTexturePath = "RTG_PlayerShip/glider_cockpit_01";
        private const string PortraitTexturePath = "RTG_PlayerShip/glider_cockpit_portrait_01";

        [Tooltip("Optional landscape override (16:9).")]
        public Texture2D cockpitTexture;

        [Tooltip("Optional portrait override (9:16).")]
        public Texture2D cockpitPortraitTexture;

        [Tooltip("Seconds to fade the cockpit overlay in/out.")]
        public float fadeSeconds = 0.22f;

        [Tooltip("When true, only dashboard + frame rails are drawn (open glass canopy).")]
        public bool useGlassCanopyOverlay = true;

        public bool IsActive { get; private set; }
        public float Blend { get; private set; }

        private Texture2D _landscapeTexture;
        private Texture2D _portraitTexture;

        private void Awake()
        {
            ResolveTextures();
        }

        public void SetActive(bool active, bool immediate = false)
        {
            IsActive = active;
            if (immediate)
                Blend = active ? 1f : 0f;
        }

        private void Update()
        {
            float target = IsActive ? 1f : 0f;
            if (Mathf.Approximately(Blend, target)) return;

            float speed = fadeSeconds > 0.01f ? 1f / fadeSeconds : 100f;
            Blend = Mathf.MoveTowards(Blend, target, speed * Time.deltaTime);
        }

        public void DrawOverlay()
        {
            if (Blend <= 0.001f) return;

            if (useGlassCanopyOverlay)
                DrawOpenGlassCanopyOverlay();
            else
                DrawLegacyOverlay();
        }

        private void DrawLegacyOverlay()
        {
            Texture2D tex = ResolveActiveTexture();
            if (tex == null) return;

            var screen = new Rect(0f, 0f, Screen.width, Screen.height);

            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Blend);
            GUI.DrawTexture(screen, tex, ScaleMode.ScaleAndCrop, true);
            GUI.color = prev;
        }

        /// <summary>
        /// Open 270° glass canopy: no roof or side tint bars — only art frame rails and dashboard.
        /// </summary>
        private void DrawOpenGlassCanopyOverlay()
        {
            Texture2D tex = ResolveActiveTexture();
            if (tex == null)
                return;

            bool portrait = Screen.height > Screen.width;
            float blend = Blend;

            DrawTexturedBand(tex, LeftFrameRailAnchor(portrait), blend);
            DrawTexturedBand(tex, RightFrameRailAnchor(portrait), blend);
            DrawTexturedBand(tex, DashboardStripAnchor(portrait), blend);
        }

        private static void DrawTexturedBand(Texture2D tex, CockpitTextureAnchor anchor, float alpha)
        {
            if (tex == null || alpha <= 0.001f) return;

            Rect screen = MapNormalizedAnchorToScreen(tex, anchor);
            float u0 = Mathf.Clamp01(anchor.CenterU - anchor.WidthU * 0.5f);
            float u1 = Mathf.Clamp01(u0 + anchor.WidthU);
            float vTop = Mathf.Clamp01(anchor.CenterV - anchor.HeightV * 0.5f);
            float vBottom = Mathf.Clamp01(vTop + anchor.HeightV);
            float ty0 = 1f - vBottom;
            var texCoords = new Rect(u0, ty0, u1 - u0, vBottom - vTop);

            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(screen, tex, texCoords, true);
            GUI.color = prev;
        }

        /// <summary>Lower instrument panel and joystick surround.</summary>
        public static CockpitTextureAnchor DashboardStripAnchor(bool portrait)
        {
            return portrait
                ? new CockpitTextureAnchor(0.5f, 0.84f, 1f, 0.32f)
                : new CockpitTextureAnchor(0.5f, 0.86f, 1f, 0.28f);
        }

        /// <summary>Left A-pillar / frame rail from cockpit art (not a flat tint bar).</summary>
        public static CockpitTextureAnchor LeftFrameRailAnchor(bool portrait)
        {
            return portrait
                ? new CockpitTextureAnchor(0.11f, 0.44f, 0.22f, 0.62f)
                : new CockpitTextureAnchor(0.10f, 0.42f, 0.20f, 0.58f);
        }

        /// <summary>Right A-pillar / frame rail from cockpit art (not a flat tint bar).</summary>
        public static CockpitTextureAnchor RightFrameRailAnchor(bool portrait)
        {
            return portrait
                ? new CockpitTextureAnchor(0.89f, 0.44f, 0.22f, 0.62f)
                : new CockpitTextureAnchor(0.90f, 0.42f, 0.20f, 0.58f);
        }

        /// <summary>
        /// Primary drag-look region: open glass (forward, sides, and sky). Excludes dashboard.
        /// </summary>
        public static CockpitTextureAnchor GlassDragViewportAnchor(bool portrait)
        {
            return portrait
                ? new CockpitTextureAnchor(0.5f, 0.30f, 0.88f, 0.58f)
                : new CockpitTextureAnchor(0.5f, 0.28f, 0.86f, 0.56f);
        }

        public bool IsPointerOverGlassViewport(Vector2 screenPosBottomLeft)
        {
            bool portrait = Screen.height > Screen.width;
            float dashboardCutoff = Screen.height * (portrait ? 0.38f : 0.42f);
            if (screenPosBottomLeft.y <= dashboardCutoff)
                return false;

            return screenPosBottomLeft.x >= 0f && screenPosBottomLeft.x <= Screen.width;
        }

        /// <summary>
        /// Center joystick column on cockpit art (measured from glider_cockpit_* PNGs).
        /// </summary>
        public static CockpitTextureAnchor JoystickAnchor(bool portrait)
        {
            return portrait
                ? new CockpitTextureAnchor(0.498f, 0.624f, 0.19f, 0.34f)
                : new CockpitTextureAnchor(0.498f, 0.736f, 0.20f, 0.45f);
        }

        /// <summary>
        /// Backup-camera screen on the cockpit dashboard (measured from glider_cockpit_* PNGs).
        /// </summary>
        public static CockpitTextureAnchor RearCameraAnchor(bool portrait)
        {
            return portrait
                ? new CockpitTextureAnchor(0.5f, 0.78f, 0.34f, 0.14f)
                : new CockpitTextureAnchor(0.5f, 0.88f, 0.24f, 0.13f);
        }

        /// <summary>
        /// Maps a normalized cockpit-art anchor to screen pixels using the same
        /// ScaleAndCrop math as <see cref="DrawOverlay"/>.
        /// </summary>
        public bool TryMapAnchorToScreen(CockpitTextureAnchor anchor, out Rect screenRect)
        {
            Texture2D tex = ResolveActiveTexture();
            if (tex == null)
            {
                screenRect = default;
                return false;
            }

            screenRect = MapNormalizedAnchorToScreen(tex, anchor);
            return true;
        }

        public static Rect MapNormalizedAnchorToScreen(Texture2D tex, CockpitTextureAnchor anchor)
        {
            float tw = tex.width;
            float th = tex.height;
            float sw = Screen.width;
            float sh = Screen.height;
            float scale = Mathf.Max(sw / tw, sh / th);
            float cropX = (tw * scale - sw) * 0.5f;
            float cropY = (th * scale - sh) * 0.5f;

            float width = anchor.WidthU * tw * scale;
            float height = anchor.HeightV * th * scale;
            float left = anchor.CenterU * tw * scale - cropX - width * 0.5f;
            float top = anchor.CenterV * th * scale - cropY - height * 0.5f;
            return new Rect(left, top, width, height);
        }

        private Texture2D ResolveActiveTexture()
        {
            bool portrait = Screen.height > Screen.width;
            if (portrait)
            {
                if (cockpitPortraitTexture != null) return cockpitPortraitTexture;
                if (_portraitTexture == null)
                    _portraitTexture = Resources.Load<Texture2D>(PortraitTexturePath);
                if (_portraitTexture != null) return _portraitTexture;
            }

            if (cockpitTexture != null) return cockpitTexture;
            if (_landscapeTexture == null)
                _landscapeTexture = Resources.Load<Texture2D>(LandscapeTexturePath);
            if (_landscapeTexture == null)
                Debug.LogWarning("[RTG] Cockpit texture missing. Run Routes to Glory → 8b. Sync Player Ship Art.");
            return _landscapeTexture;
        }

        private void ResolveTextures()
        {
            if (_landscapeTexture == null && cockpitTexture == null)
                _landscapeTexture = Resources.Load<Texture2D>(LandscapeTexturePath);
            if (_portraitTexture == null && cockpitPortraitTexture == null)
                _portraitTexture = Resources.Load<Texture2D>(PortraitTexturePath);
        }
    }
}
