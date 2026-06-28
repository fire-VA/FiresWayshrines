using FiresCore.Logging;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Orchestrates the wayshrine teleport user flow. Holds the "armed" source
    /// shrine while the player has the map open; resolves a destination from
    /// a map click; validates teleport rules; charges coins; teleports.
    ///
    /// Destination resolution goes through <see cref="WayshrineLookup"/> so a
    /// player can map-click any shrine they've ever discovered, even if its
    /// zone isn't currently loaded. Live shrines provide accurate position +
    /// rotation; unloaded shrines have y resolved from the height map and
    /// land facing world-forward.
    /// </summary>
    public static class WayshrineTeleportFlow
    {
        public const string ITEM_COINS = "$item_coins";

        private static Wayshrine _armedSource;
        public static bool IsArmed => _armedSource != null;
        public static Wayshrine ArmedSource => _armedSource;

        // ---------------- Entry points ----------------

        public static void BeginFromShrine(Wayshrine source)
        {
            if (source == null) return;
            if (Minimap.instance == null) return;

            _armedSource = source;
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);

            FiresLogger.LogInfo($"[Wayshrine] Armed source='{source.DisplayName}' id='{source.ShrineId}'. Map opened, waiting for pin click.");

            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                $"Choose destination — click a wayshrine pin (escape to cancel).");
        }

        /// <summary>
        /// User clicked the map at <paramref name="worldClickPos"/> while the
        /// map is open. Returns true if the click matched any discovered
        /// shrine (loaded or not), false to let vanilla handle it.
        /// </summary>
        public static bool TryConsumeMapClick(Vector3 worldClickPos, float pickRadius)
        {
            if (_armedSource == null) return false;
            var player = Player.m_localPlayer;
            if (player == null) return false;

            var dest = WayshrineLookup.FindClosestDiscovered(player, worldClickPos, pickRadius);
            if (!dest.HasValue) return false;

            if (dest.Value.Id == _armedSource.ShrineId)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    "You're already at this wayshrine.");
                return true;
            }

            ShowConfirmPopup(_armedSource, dest.Value);
            return true;
        }

        public static void Disarm() => _armedSource = null;

        // ---------------- Confirm popup ----------------

        private static void ShowConfirmPopup(Wayshrine source, DiscoveredShrine dest)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            float distance = Vector3.Distance(
                new Vector3(source.transform.position.x, 0, source.transform.position.z),
                new Vector3(dest.Position.x,             0, dest.Position.z));

            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;
            int recent = WayshrineDiscovery.ReadAndPruneCooldownTimestamps(player, now).Count;
            float multiplier = WayshrineCostCalculator.CalculateMultiplier(recent);
            bool waived = IsCostWaived();
            int cost = waived ? 0 : WayshrineCostCalculator.CalculateCost(distance, recent);

            string body = BuildBodyText(dest.DisplayName, distance, multiplier, cost, waived);

            // Capture by value so the callback works even if zone state changes.
            var capturedDest = dest;
            var popup = new WayshrineTeleportPopup(
                header: "Wayshrine",
                body: body,
                yesLabel: $"Teleport ({cost} coins)",
                noLabel: "Cancel",
                yesCallback: () =>
                {
                    UnifiedPopup.Pop();
                    ExecuteTeleport(source, capturedDest, cost);
                    Disarm();
                },
                noCallback: () =>
                {
                    UnifiedPopup.Pop();
                    Disarm();
                },
                localizeText: false);

            UnifiedPopup.Push(popup);
        }

        private static string BuildBodyText(string destName, float distance, float multiplier, int cost, bool waived)
        {
            float km = distance / 1000f;
            string costLine = waived
                ? "<color=#88ff88>Free (creative mode)</color>"
                : $"Cost: <color=#FFAA33>{cost} coins</color>";
            string mult = multiplier > 1.01f
                ? $"<size=80%><color=#888>{km:F2} km · cooldown x{multiplier:F1}</color></size>"
                : $"<size=80%><color=#888>{km:F2} km</color></size>";
            return $"Travel to <color=#FFD966>{destName}</color>?\n\n{costLine}\n{mult}";
        }

        // ---------------- Validation + execution ----------------

        public static bool IsCostWaived()
        {
            if (!(WayshrineConfig.WaiveCostInCreative?.Value ?? true)) return false;
            return ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost);
        }

        public static bool ValidateCanTeleport(Player player, out string reason)
        {
            reason = null;
            if (player == null) { reason = "No local player."; return false; }
            if (player.IsTeleporting()) { reason = "Already teleporting."; return false; }
            if (player.InCutscene()) { reason = "In cutscene."; return false; }

            if (WayshrineConfig.RespectNoTeleportItems?.Value ?? true)
            {
                if (!player.IsTeleportable())
                {
                    reason = "$msg_noteleport";
                    return false;
                }
            }

            if (ZoneSystem.instance != null)
            {
                if ((WayshrineConfig.RespectNoPortalsKey?.Value ?? true)
                    && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoPortals))
                {
                    reason = "$msg_blocked";
                    return false;
                }

                if (WayshrineConfig.RespectBossLockout?.Value ?? true)
                {
                    bool noBossPortals = ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBossPortals);
                    bool activeBoss = RandEventSystem.instance != null && RandEventSystem.instance.GetBossEvent() != null;
                    if (noBossPortals && activeBoss)
                    {
                        reason = "$msg_blockedbyboss";
                        return false;
                    }
                }
            }
            return true;
        }

        private static void ExecuteTeleport(Wayshrine source, DiscoveredShrine dest, int cost)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            // Lazy validation against the (server-authoritative on dedicated,
            // ZDOMan-authoritative on host) world registry. Catches the rare
            // race where a shrine was destroyed between the moment the player
            // clicked its pin and the moment they hit "Teleport" on the
            // confirmation popup — also catches stale ids that survived a
            // session because no other validation path noticed.
            //
            // Removing the discovery here completes the lazy-cleanup contract:
            // server-side pruning during sync handles the bulk case at login,
            // and this branch handles the late-breaking case mid-session.
            if (!WayshrineWorldRegistry.Contains(dest.Id))
            {
                WayshrineDiscovery.RemoveDiscovered(player, dest.Id);
                WayshrineMapPins.RemovePinForShrine(dest.Id);
                player.Message(MessageHud.MessageType.Center,
                    $"That wayshrine no longer exists.", 0, null);
                FiresLogger.LogInfo($"[Wayshrine] Lazy-validation removed stale discovery '{dest.DisplayName}' (id={dest.Id}) at teleport time.");
                return;
            }

            if (!ValidateCanTeleport(player, out var reason))
            {
                player.Message(MessageHud.MessageType.Center, reason ?? "Cannot teleport.", 0, null);
                return;
            }

            if (cost > 0)
            {
                int have = player.GetInventory()?.CountItems(ITEM_COINS) ?? 0;
                if (have < cost)
                {
                    player.Message(MessageHud.MessageType.Center, $"Insufficient coins ({have}/{cost})", 0, null);
                    return;
                }
                player.GetInventory().RemoveItem(ITEM_COINS, cost);
            }

            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;
            WayshrineDiscovery.RecordTeleport(player, now);

            // Land slightly above ground, in front of the shrine if it's
            // loaded (so we don't clip the shrine geometry); just on the
            // height-mapped point if we're warping into an unloaded zone.
            Vector3 exit;
            Quaternion rot;
            if (dest.IsLive && dest.LiveInstance != null)
            {
                rot = dest.LiveInstance.transform.rotation;
                exit = dest.LiveInstance.transform.position
                     + dest.LiveInstance.transform.forward * 1.5f
                     + Vector3.up * 1.0f;
            }
            else
            {
                rot = Quaternion.identity;
                exit = dest.Position + Vector3.up * 1.0f;
            }

            player.TeleportTo(exit, rot, distantTeleport: true);

            FiresLogger.LogInfo($"[Wayshrine] Teleport: {source.DisplayName} -> {dest.DisplayName} ({cost} coins, isLive={dest.IsLive}, recent={WayshrineDiscovery.ReadAndPruneCooldownTimestamps(player, now).Count})");
        }

        /// <summary>
        /// Direct-teleport entry point used by Town Portal Scrolls — same
        /// validation + cooldown record, but no coin cost.
        /// </summary>
        public static bool ExecuteFreeTeleport(Vector3 destPos, Quaternion destRot, string label)
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;
            if (!ValidateCanTeleport(player, out var reason))
            {
                player.Message(MessageHud.MessageType.Center, reason ?? "Cannot teleport.", 0, null);
                return false;
            }
            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;
            WayshrineDiscovery.RecordTeleport(player, now);
            player.TeleportTo(destPos + Vector3.up * 1.0f, destRot, distantTeleport: true);
            FiresLogger.LogInfo($"[Wayshrine] Free teleport via {label}");
            return true;
        }
    }
}
