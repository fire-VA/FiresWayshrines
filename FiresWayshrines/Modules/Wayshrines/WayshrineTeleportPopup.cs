namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Subclass of <see cref="YesNoPopup"/> that carries custom button labels
    /// and an optional third (center) button. The patch
    /// <see cref="Patches.UnifiedPopupButtonLabelPatch"/> postfixes
    /// <c>UnifiedPopup.ShowYesNo</c> to apply the labels and wire the third
    /// button when <see cref="CenterLabel"/> is set.
    /// </summary>
    public class WayshrineTeleportPopup : YesNoPopup
    {
        public readonly string YesLabel;
        public readonly string NoLabel;

        // Optional 3rd button (UnifiedPopup.buttonCenter slot). When set, the
        // popup renders three buttons. Used by the town-portal-scroll choice
        // popup (Bed / Wayshrine / Cancel).
        public readonly string CenterLabel;
        public readonly PopupButtonCallback CenterCallback;

        public WayshrineTeleportPopup(
            string header,
            string body,
            string yesLabel,
            string noLabel,
            PopupButtonCallback yesCallback,
            PopupButtonCallback noCallback,
            string centerLabel = null,
            PopupButtonCallback centerCallback = null,
            bool localizeText = false)
            : base(header, body, yesCallback, noCallback, localizeText)
        {
            YesLabel = yesLabel;
            NoLabel = noLabel;
            CenterLabel = centerLabel;
            CenterCallback = centerCallback;
        }
    }
}
