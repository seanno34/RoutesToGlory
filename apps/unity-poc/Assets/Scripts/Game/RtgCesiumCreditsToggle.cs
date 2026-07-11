using System.Collections;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Hides Cesium's default on-screen credits and shows them only when the
    /// player taps the About ("i") button. Satisfies mobile attribution access
    /// without cluttering the main map HUD.
    /// </summary>
    public class RtgCesiumCreditsToggle : MonoBehaviour
    {
        private const string OnScreenCreditsName = "OnScreenCredits";
        private const string PopupCreditsName = "PopupCredits";

        private VisualElement _onScreenCredits;
        private VisualElement _popupCredits;
        private bool _bound;
        private bool _visible;

        public bool IsVisible => _visible;

        private void Start()
        {
            StartCoroutine(BindWhenReady());
        }

        private void LateUpdate()
        {
            if (!_bound || _visible) return;
            EnforceHidden();
        }

        private IEnumerator BindWhenReady()
        {
            for (int i = 0; i < 180; i++)
            {
                if (TryBind())
                {
                    SetVisible(false);
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning("[RTG] Cesium credit system not found — About credits toggle inactive.");
        }

        public void Toggle()
        {
            if (!_bound && !TryBind()) return;
            SetVisible(!_visible);
        }

        private bool TryBind()
        {
            CesiumCreditSystem[] systems = Resources.FindObjectsOfTypeAll<CesiumCreditSystem>();
            foreach (CesiumCreditSystem system in systems)
            {
                if (system == null) continue;

                UIDocument doc = system.GetComponent<UIDocument>();
                if (doc?.rootVisualElement == null) continue;

                VisualElement onScreen = doc.rootVisualElement.Q(OnScreenCreditsName);
                if (onScreen == null) continue;

                _onScreenCredits = onScreen;
                _popupCredits = doc.rootVisualElement.Q(PopupCreditsName);
                ApplyLayoutStyles(_onScreenCredits, _popupCredits);
                _bound = true;
                return true;
            }

            return false;
        }

        private static void ApplyLayoutStyles(VisualElement onScreen, VisualElement popup)
        {
            onScreen.style.position = Position.Absolute;
            onScreen.style.left = 16;
            onScreen.style.right = 16;
            onScreen.style.bottom = 120;
            onScreen.style.flexWrap = Wrap.Wrap;
            onScreen.style.backgroundColor = new Color(0.06f, 0.09f, 0.16f, 0.92f);
            onScreen.style.paddingLeft = 12;
            onScreen.style.paddingRight = 12;
            onScreen.style.paddingTop = 8;
            onScreen.style.paddingBottom = 8;
            onScreen.style.borderTopLeftRadius = 8;
            onScreen.style.borderTopRightRadius = 8;
            onScreen.style.borderBottomLeftRadius = 8;
            onScreen.style.borderBottomRightRadius = 8;

            if (popup == null) return;

            popup.style.position = Position.Absolute;
            popup.style.left = 16;
            popup.style.right = 16;
            popup.style.bottom = 16;
            popup.style.maxHeight = Length.Percent(40);
            popup.style.backgroundColor = new Color(0.06f, 0.09f, 0.16f, 0.96f);
            popup.style.paddingLeft = 12;
            popup.style.paddingRight = 12;
            popup.style.paddingTop = 8;
            popup.style.paddingBottom = 8;
            popup.style.borderTopLeftRadius = 8;
            popup.style.borderTopRightRadius = 8;
            popup.style.borderBottomLeftRadius = 8;
            popup.style.borderBottomRightRadius = 8;
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;

            if (_onScreenCredits != null)
                _onScreenCredits.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!visible && _popupCredits != null)
                _popupCredits.style.display = DisplayStyle.None;
        }

        private void EnforceHidden()
        {
            if (_onScreenCredits != null && _onScreenCredits.style.display == DisplayStyle.Flex)
                _onScreenCredits.style.display = DisplayStyle.None;

            if (_popupCredits != null && _popupCredits.style.display == DisplayStyle.Flex)
                _popupCredits.style.display = DisplayStyle.None;
        }
    }
}
