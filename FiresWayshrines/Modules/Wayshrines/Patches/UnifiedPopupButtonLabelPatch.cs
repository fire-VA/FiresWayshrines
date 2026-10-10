using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VerdantsAscent.Modules.Wayshrines.Patches
{
    /// <summary>
    /// Postfixes <see cref="UnifiedPopup"/>'s ShowYesNo to swap the button
    /// labels — and optionally enable a third (center) button — when the
    /// active popup is a <see cref="WayshrineTeleportPopup"/>. Runs after
    /// vanilla has set "$menu_yes" / "$menu_no" defaults.
    ///
    /// Vanilla's two-button layout positions <c>buttonLeft</c> and
    /// <c>buttonRight</c> with anchored offsets that overlap the
    /// <c>buttonCenter</c> slot when it's enabled. When we activate the
    /// third button we reposition all three to thirds of the popup width
    /// so the labels don't clip into each other.
    /// </summary>
    [HarmonyPatch(typeof(UnifiedPopup), "ShowYesNo")]
    internal static class UnifiedPopupButtonLabelPatch
    {
        // Cached snapshot of vanilla 2-button anchored positions so we can
        // restore them when a non-WayshrineTeleportPopup pops next. Without
        // this, the next vanilla Yes/No would inherit our 3-button spacing.
        private static Vector2? _origLeftPos;
        private static Vector2? _origRightPos;
        private static Vector2? _origCenterPos;
        private static bool _origCenterActive;

        [HarmonyPostfix]
        private static void Postfix(UnifiedPopup __instance, YesNoPopup popup)
        {
            var trv = Traverse.Create(__instance);
            var rightText = trv.Field("buttonRightText").GetValue<TMP_Text>();
            var leftText = trv.Field("buttonLeftText").GetValue<TMP_Text>();
            var centerText = trv.Field("buttonCenterText").GetValue<TMP_Text>();
            var rightBtn = trv.Field("buttonRight").GetValue<Button>();
            var leftBtn = trv.Field("buttonLeft").GetValue<Button>();
            var centerBtn = trv.Field("buttonCenter").GetValue<Button>();

            CacheOriginalLayoutOnce(leftBtn, rightBtn, centerBtn);

            if (!(popup is WayshrineTeleportPopup wp))
            {
                RestoreOriginalLayout(leftBtn, rightBtn, centerBtn);
                return;
            }

            if (rightText != null && !string.IsNullOrEmpty(wp.YesLabel))
                rightText.text = wp.YesLabel;
            if (leftText != null && !string.IsNullOrEmpty(wp.NoLabel))
                leftText.text = wp.NoLabel;

            if (centerBtn == null || centerText == null || string.IsNullOrEmpty(wp.CenterLabel))
            {
                RestoreOriginalLayout(leftBtn, rightBtn, centerBtn);
                return;
            }

            centerText.text = wp.CenterLabel;
            centerBtn.gameObject.SetActive(true);
            centerBtn.onClick.RemoveAllListeners();
            if (wp.CenterCallback != null)
            {
                var cb = wp.CenterCallback;
                centerBtn.onClick.AddListener(() => cb());
            }

            ApplyThreeButtonLayout(leftBtn, rightBtn, centerBtn);
        }

        private static void CacheOriginalLayoutOnce(Button left, Button right, Button center)
        {
            if (_origLeftPos.HasValue) return;
            if (left != null && left.transform is RectTransform lrt)
                _origLeftPos = lrt.anchoredPosition;
            if (right != null && right.transform is RectTransform rrt)
                _origRightPos = rrt.anchoredPosition;
            if (center != null)
            {
                _origCenterActive = center.gameObject.activeSelf;
                if (center.transform is RectTransform crt)
                    _origCenterPos = crt.anchoredPosition;
            }
        }

        private static void RestoreOriginalLayout(Button left, Button right, Button center)
        {
            if (left?.transform is RectTransform lrt && _origLeftPos.HasValue)
                lrt.anchoredPosition = _origLeftPos.Value;
            if (right?.transform is RectTransform rrt && _origRightPos.HasValue)
                rrt.anchoredPosition = _origRightPos.Value;
            if (center != null)
            {
                if (center.transform is RectTransform crt && _origCenterPos.HasValue)
                    crt.anchoredPosition = _origCenterPos.Value;
                center.gameObject.SetActive(_origCenterActive);
            }
        }

        // Slide left and right buttons outward from vanilla's two-button
        // positions and place center exactly between them, using vanilla's
        // own button width for the spacing so it scales with the original
        // anchors. No magic pixel values — derived from runtime layout.
        private static void ApplyThreeButtonLayout(Button left, Button right, Button center)
        {
            if (!(left?.transform is RectTransform lrt)) return;
            if (!(right?.transform is RectTransform rrt)) return;
            if (!(center?.transform is RectTransform crt)) return;
            if (!_origLeftPos.HasValue || !_origRightPos.HasValue) return;

            float btnWidth = lrt.rect.width;
            float gap = 16f;
            float halfShift = btnWidth * 0.5f + gap * 0.5f;

            var origLeft = _origLeftPos.Value;
            var origRight = _origRightPos.Value;

            lrt.anchoredPosition  = new Vector2(origLeft.x  - halfShift, origLeft.y);
            rrt.anchoredPosition  = new Vector2(origRight.x + halfShift, origRight.y);

            float centerX = (origLeft.x + origRight.x) * 0.5f;
            float centerY = (origLeft.y + origRight.y) * 0.5f;
            crt.anchoredPosition = new Vector2(centerX, centerY);
        }
    }
}
