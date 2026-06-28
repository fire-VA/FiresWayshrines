using FiresCore.Logging;
using HarmonyLib;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines.Patches
{
    /// <summary>
    /// Map-click handlers for wayshrine flows.
    ///
    /// Left-click  : if a teleport flow is armed (player interacted with a
    ///               shrine and the map opened), resolve the click to a
    ///               discovered wayshrine — loaded OR unloaded — via
    ///               <see cref="WayshrineLookup.FindClosestDiscovered"/> and
    ///               open the confirm popup.
    /// Right-click : on a wayshrine pin, prevent vanilla pin-removal so the
    ///               player doesn't accidentally lose the pin. Admins +
    ///               alt-modifier opens the rename TextInput targeting the
    ///               clicked shrine.
    ///
    /// Pick radius matches vanilla's own
    /// (<c>m_removeRadius * (m_largeZoom * 2)</c>) so the click feel is
    /// identical to vanilla pin handling.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    internal static class MinimapLeftClickPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Minimap __instance)
        {
            if (!WayshrineTeleportFlow.IsArmed) return true;

            var worldPos = MinimapPatchUtil.ScreenToWorld(__instance, ZInput.mousePosition);
            float pickRadius = MinimapPatchUtil.PickRadius(__instance);

            if (WayshrineTeleportFlow.TryConsumeMapClick(worldPos, pickRadius))
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnMapRightClick")]
    internal static class MinimapRightClickPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Minimap __instance)
        {
            if (Player.m_localPlayer == null) return true;

            var worldPos = MinimapPatchUtil.ScreenToWorld(__instance, ZInput.mousePosition);
            float pickRadius = MinimapPatchUtil.PickRadius(__instance);

            // Find the nearest LOADED wayshrine to the click. Rename only
            // works on shrines whose zone is currently loaded (we mutate the
            // live MonoBehaviour). Rename via map can't reach unloaded shrines
            // — admin must travel near them first.
            Wayshrine shrine = WayshrineRegistry.FindClosest(worldPos, pickRadius);
            if (shrine == null) return true; // not on a wayshrine — vanilla can handle the click

            // Wayshrine.IsLocalPlayerAdmin uses the mod's own admin-sync RPC, not
            // ZNet.LocalPlayerIsAdminOrHost — vanilla's check returns false on
            // dedicated clients because m_adminList is server-only.
            bool admin = Wayshrine.IsLocalPlayerAdmin();
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            if (altHeld && admin)
            {
                if (TextInput.instance != null)
                {
                    TextInput.instance.RequestText(shrine, $"Rename Wayshrine ({shrine.DisplayName})", 40);
                    FiresLogger.LogInfo($"[Wayshrine] Opened admin rename dialog for '{shrine.DisplayName}' (id={shrine.ShrineId})");
                }
                else
                {
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "TextInput unavailable.", 0, null);
                }
                return false; // consumed
            }

            // Plain RMB on a wayshrine pin — block vanilla pin-removal so the
            // player doesn't accidentally drop the pin. Pins are re-added on
            // discovery rebuild anyway, but the explicit block keeps UX clean.
            return false;
        }
    }

    /// <summary>
    /// When the player closes the map (Escape, M etc.) while a teleport
    /// flow is armed, disarm it so a later click somewhere else doesn't
    /// trigger a stale popup.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), "SetMapMode")]
    internal static class MinimapSetMapModePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap.MapMode mode)
        {
            if (mode != Minimap.MapMode.Large && WayshrineTeleportFlow.IsArmed)
                WayshrineTeleportFlow.Disarm();
        }
    }

    internal static class MinimapPatchUtil
    {
        public static Vector3 ScreenToWorld(Minimap mm, Vector3 mousePos)
        {
            return Traverse.Create(mm).Method("ScreenToWorldPoint", new object[] { mousePos })
                                       .GetValue<Vector3>();
        }

        public static float PickRadius(Minimap mm)
        {
            float largeZoom = Traverse.Create(mm).Field("m_largeZoom").GetValue<float>();
            return mm.m_removeRadius * (largeZoom * 2f);
        }
    }
}
