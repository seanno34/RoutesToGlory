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
    /// Full-screen first-person cockpit overlay. The 3D world renders through the
    /// keyed-out windshield while frame/HUD art sits on top.
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

            Texture2D tex = ResolveActiveTexture();
            if (tex == null) return;

            var screen = new Rect(0f, 0f, Screen.width, Screen.height);

            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Blend);
            GUI.DrawTexture(screen, tex, ScaleMode.ScaleAndCrop, true);
            GUI.color = prev;
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
