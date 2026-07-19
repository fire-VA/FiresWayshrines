using System.Collections.Generic;
using System.Linq;
using FiresCore.UI.ContextMenu;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Public seam for consumer mods (the FiresNPCs "Teleporter" NPC type via its
    /// Bridges/WayshrinesBridge) to drive the wayshrine network without a hard reference. Reflected
    /// by name, so signatures are load-bearing: keep them stable and grow by adding methods.
    ///
    /// Hub destinations come from <see cref="TeleporterHubConfig"/> (admin coordinate pins + MP
    /// drop-in); teleport execution reuses <see cref="WayshrineTeleportFlow"/> so hub jumps obey the
    /// same ore / NoPortals / boss rules as shrine jumps. The picker is Core's FDT context menu.
    /// </summary>
    public static class WayshrinesApi
    {
        public const int ApiMajorVersion = 1;

        /// <summary>All configured hub profile names.</summary>
        public static IEnumerable<string> GetProfileNames() => TeleporterHubConfig.ProfileNames.ToList();

        /// <summary>Open the destination chooser for one hub profile (the Teleporter NPC entry point).</summary>
        public static void OpenSelectionForProfile(Player player, string profile)
        {
            if (player == null) return;
            var pins = TeleporterHubConfig.GetPins(profile);
            ShowPicker(profile, pins);
        }

        /// <summary>Open a chooser listing every hub's destinations.</summary>
        public static void OpenSelection(Player player)
        {
            if (player == null) return;
            var all = new List<TeleporterPin>();
            foreach (var name in TeleporterHubConfig.ProfileNames) all.AddRange(TeleporterHubConfig.GetPins(name));
            ShowPicker(null, all);
        }

        /// <summary>Teleport straight to a named destination (no picker). Reason filled on failure.</summary>
        public static bool TeleportToNamed(Player player, string name, out string reason)
        {
            reason = null;
            if (player == null) { reason = "No player."; return false; }
            var pin = TeleporterHubConfig.FindPinByName(name);
            if (pin == null) { reason = $"No destination named '{name}'."; return false; }
            if (!WayshrineTeleportFlow.ValidateCanTeleport(player, out reason)) return false;
            return WayshrineTeleportFlow.ExecuteFreeTeleport(pin.Pos, player.transform.rotation, "hub:" + pin.Name);
        }

        private static void ShowPicker(string profile, List<TeleporterPin> pins)
        {
            var player = Player.m_localPlayer;
            if (pins == null || pins.Count == 0)
            {
                player?.Message(MessageHud.MessageType.Center, "No destinations available.");
                return;
            }

            var items = new List<ContextMenuItem>();
            foreach (var pin in pins)
            {
                var captured = pin;
                items.Add(ContextMenuItem.Row(pin.Name, () =>
                {
                    var p = Player.m_localPlayer;
                    if (p == null) return;
                    WayshrineTeleportFlow.ExecuteFreeTeleport(captured.Pos, p.transform.rotation, "hub:" + captured.Name);
                }));
            }

            string title = string.IsNullOrEmpty(profile) ? "Teleport" : "Teleport — " + profile;
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            FiresContextMenu.OpenCustom(title, items, center);
        }
    }
}
