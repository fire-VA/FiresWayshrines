using HarmonyLib;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines.TownPortalScroll
{
    /// <summary>
    /// Detects when the player consumes a Town Portal Scroll
    /// (<see cref="TownPortalScrollPrefabName"/>) and replaces vanilla's
    /// potion-consume flow with a Bed/Wayshrine/Cancel choice popup.
    /// On confirmed choice we manually decrement the inventory by one and
    /// route to <see cref="WayshrineTeleportFlow.ExecuteFreeTeleport"/>.
    ///
    /// Wayshrine resolution goes through <see cref="WayshrineLookup"/> so a
    /// player far from any loaded shrine can still scroll-home to one they
    /// discovered earlier.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "UseItem")]
    internal static class TownPortalScrollHandler
    {
        public const string TownPortalScrollPrefabName = "ScrollTownPortal";

        [HarmonyPrefix]
        private static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item, bool fromInventoryGui)
        {
            if (item == null) return true;
            if (__instance == null || __instance != Player.m_localPlayer) return true;
            if (!IsTownPortalScroll(item)) return true;

            var player = (Player)__instance;
            ShowChoicePopup(player, inventory ?? player.GetInventory(), item);
            return false;
        }

        private static bool IsTownPortalScroll(ItemDrop.ItemData item)
        {
            if (item?.m_dropPrefab != null && item.m_dropPrefab.name == TownPortalScrollPrefabName)
                return true;
            if (item?.m_shared != null && !string.IsNullOrEmpty(item.m_shared.m_name)
                && (item.m_shared.m_name.IndexOf("TownPortal", System.StringComparison.OrdinalIgnoreCase) >= 0
                 || item.m_shared.m_name.IndexOf("ScrollTownPortal", System.StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            return false;
        }

        private static void ShowChoicePopup(Player player, Inventory inventory, ItemDrop.ItemData item)
        {
            var spawn = TryGetCustomSpawnPoint(out bool hasSpawn);
            var nearest = WayshrineLookup.FindClosestOnWorldNetwork(player, player.transform.position);

            if (!hasSpawn && !nearest.HasValue)
            {
                player.Message(MessageHud.MessageType.Center, "No bed or discovered wayshrine to travel to.", 0, null);
                return;
            }

            string body = BuildBodyText(player, hasSpawn, spawn, nearest);

            var popup = new WayshrineTeleportPopup(
                header: "Town Portal Scroll",
                body: body,
                yesLabel: hasSpawn ? "Bed" : "Bed (none)",
                noLabel: "Cancel",
                centerLabel: nearest.HasValue ? "Wayshrine" : "Wayshrine (none)",
                yesCallback: () =>
                {
                    UnifiedPopup.Pop();
                    if (!hasSpawn) return;
                    if (WayshrineTeleportFlow.ExecuteFreeTeleport(spawn, Quaternion.identity, "scroll->bed"))
                        ConsumeOne(inventory, item);
                },
                noCallback: () => UnifiedPopup.Pop(),
                centerCallback: () =>
                {
                    UnifiedPopup.Pop();
                    if (!nearest.HasValue) return;
                    var n = nearest.Value;
                    var rot = n.Rotation;
                    // Pop in front of the shrine if it's loaded; warp in place
                    // if the zone isn't loaded (height already resolved).
                    var pos = n.IsLive ? n.Position + rot * Vector3.forward * 1.5f : n.Position;
                    if (WayshrineTeleportFlow.ExecuteFreeTeleport(pos, rot, $"scroll->{n.DisplayName}"))
                        ConsumeOne(inventory, item);
                },
                localizeText: false);

            UnifiedPopup.Push(popup);
        }

        private static string BuildBodyText(Player player, bool hasSpawn, Vector3 spawn, DiscoveredShrine? nearest)
        {
            string spawnLine = hasSpawn
                ? $"<color=#FFD966>Bed</color> ({Vector3.Distance(player.transform.position, spawn) / 1000f:F2} km)"
                : "<color=#888>No bed set</color>";

            string shrineLine = nearest.HasValue
                ? $"<color=#FFD966>{nearest.Value.DisplayName}</color> ({Vector3.Distance(player.transform.position, nearest.Value.Position) / 1000f:F2} km)"
                : "<color=#888>No discovered wayshrine</color>";

            return $"Choose a destination:\n\n{spawnLine}\n{shrineLine}\n\n<size=80%><color=#888>Consumes the scroll.</color></size>";
        }

        private static Vector3 TryGetCustomSpawnPoint(out bool hasSpawn)
        {
            hasSpawn = false;
            if (Game.instance == null) return Vector3.zero;
            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) return Vector3.zero;
            if (!profile.HaveCustomSpawnPoint()) return Vector3.zero;
            hasSpawn = true;
            return profile.GetCustomSpawnPoint();
        }

        private static void ConsumeOne(Inventory inv, ItemDrop.ItemData item)
        {
            if (inv == null || item == null) return;
            inv.RemoveOneItem(item);
        }
    }
}
