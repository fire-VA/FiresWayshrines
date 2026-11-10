using HarmonyLib;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Built-in scaling for the trigger-box wayshrine: hold Right Shift + mouse scroll while placing to size the
    /// ghost, then the chosen scale is stamped onto the placed piece. Uses vanilla's native scale persistence
    /// (<see cref="ZNetView.m_syncInitialScale"/> + <see cref="ZNetView.SetLocalScale"/>), so the size round-trips
    /// through the ZDO on load and replicates to peers — the same mechanism Structure Tweaks toggles globally, so
    /// it's also scalable with those mods. Placement UI is client-only; the patches no-op on a headless server
    /// (no placement ghost, no local player).
    /// </summary>
    [HarmonyPatch]
    public static class WayshrineTriggerScaling
    {
        private const float MinScale = 0.5f, MaxScale = 64f;

        // Fraction the trigger box grows or shrinks per scroll notch.
        private const float ScaleStep = 0.1f;

        // Last-used size; kept across placements so consecutive boxes match. Default equals the prefab's scale.
        internal static Vector3 CurrentScale = new Vector3(2f, 3f, 2f);

        internal static bool IsTrigger(GameObject go)
            => go != null && go.GetComponent<WayshrineTriggerWire>() != null;

        internal static Vector3 Clamp(Vector3 s) => new Vector3(
            Mathf.Clamp(s.x, MinScale, MaxScale),
            Mathf.Clamp(s.y, MinScale, MaxScale),
            Mathf.Clamp(s.z, MinScale, MaxScale));

        // ---- ghost scaling: Right Shift + scroll (uniform). Scroll also rotates, so freeze rotation while held. ----
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        private static class UpdatePlacementPatch
        {
            private static void Prefix(Player __instance, out int __state) => __state = __instance.m_placeRotation;

            private static void Postfix(Player __instance, int __state)
            {
                var ghost = __instance.m_placementGhost;
                if (ghost == null || !IsTrigger(ghost)) return;

                if (Input.GetKey(KeyCode.RightShift))
                {
                    __instance.m_placeRotation = __state;   // undo the rotation vanilla derived from the scroll

                    float scroll = ZInput.GetMouseScrollWheel();
                    if (Mathf.Abs(scroll) > 0.001f)
                        CurrentScale = Clamp(CurrentScale * (1f + Mathf.Sign(scroll) * ScaleStep));
                }
                ghost.transform.localScale = CurrentScale;
            }
        }

        // ---- stamp the chosen scale onto the freshly-placed piece (PlacePiece drops the ghost scale) ----
        [HarmonyPatch(typeof(Piece), "SetCreator")]
        private static class SetCreatorPatch
        {
            private static void Postfix(Piece __instance)
            {
                if (__instance == null || !IsTrigger(__instance.gameObject)) return;
                var nv = __instance.GetComponent<ZNetView>();
                if (nv == null || nv.GetZDO() == null || !nv.IsOwner()) return;
                nv.m_syncInitialScale = true;
                nv.SetLocalScale(CurrentScale);   // writes the scale to the ZDO + applies transform.localScale
            }
        }

        /// <summary>Admin "Set trigger size": apply + persist an explicit X/Y/Z on a placed trigger box.</summary>
        public static void SetSize(Wayshrine shrine, Vector3 size)
        {
            if (shrine == null) return;
            var nv = shrine.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid()) return;
            nv.ClaimOwnership();
            nv.m_syncInitialScale = true;
            var s = Clamp(size);
            nv.SetLocalScale(s);
            CurrentScale = s;   // so the next placed box matches
        }
    }
}
