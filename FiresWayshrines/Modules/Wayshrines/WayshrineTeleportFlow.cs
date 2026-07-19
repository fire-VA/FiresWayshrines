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
            int cost = waived ? 0 : EffectiveCost(source, dest, distance, recent);

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
                    // Disarm ONLY on a successful teleport — a failed one (no coins / cooldown / blocked) must
                    // leave the flow armed so the player can pick another destination without reopening the map.
                    if (ExecuteTeleport(source, capturedDest, cost)) Disarm();
                },
                noCallback: () =>
                {
                    // Keep the flow armed so the player can pick a DIFFERENT destination while the map stays
                    // open. Disarm happens on an actual teleport (yesCallback) or when the map closes
                    // (MinimapSetMapModePatch) — cancelling one destination must not end the whole flow.
                    UnifiedPopup.Pop();
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

        private static bool ExecuteTeleport(Wayshrine source, DiscoveredShrine dest, int cost)
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;

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
                return false;
            }

            if (!ValidateCanTeleport(player, out var reason))
            {
                player.Message(MessageHud.MessageType.Center, reason ?? "Cannot teleport.", 0, null);
                return false;
            }

            // Source-side admin-only re-check (belt-and-braces): catches a shrine toggled admin-only after the
            // map opened. The destination side is already filtered by WayshrineLookup.EnumerateDiscovered.
            if (source != null && source.GetAdminOnly() && !Wayshrine.IsLocalPlayerAdmin())
            {
                player.Message(MessageHud.MessageType.Center, "This wayshrine is restricted to admins.", 0, null);
                return false;
            }

            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

            // Admin per-shrine cooldown — gates using THIS shrine, as the source you leave from OR the
            // destination you arrive at (per-player, persisted in customData).
            if (OnCooldown(player, source.ShrineId, source.GetCooldownSeconds(), now, out int srcWait))
            {
                player.Message(MessageHud.MessageType.Center, $"This wayshrine is on cooldown ({srcWait}s).", 0, null);
                return false;
            }
            if (OnCooldown(player, dest.Id, GetDestCooldown(dest), now, out int dstWait))
            {
                player.Message(MessageHud.MessageType.Center, $"Destination wayshrine is on cooldown ({dstWait}s).", 0, null);
                return false;
            }

            if (cost > 0)
            {
                int have = player.GetInventory()?.CountItems(ITEM_COINS) ?? 0;
                if (have < cost)
                {
                    player.Message(MessageHud.MessageType.Center, $"Insufficient coins ({have}/{cost})", 0, null);
                    return false;
                }
                player.GetInventory().RemoveItem(ITEM_COINS, cost);
            }

            WayshrineDiscovery.RecordTeleport(player, now);
            WayshrineDiscovery.SetLastUse(player, source.ShrineId, now);   // cooldown stamp: both endpoints
            WayshrineDiscovery.SetLastUse(player, dest.Id, now);

            // Land in a ring around the shrine centre (never on top of it / the landmark a trigger box wraps),
            // preferring a spot with clear headroom so we don't rematerialize inside geometry.
            Vector3 exit = ChooseLandingPosition(dest, out Quaternion rot);
            player.TeleportTo(exit, rot, distantTeleport: true);

            FiresLogger.LogInfo($"[Wayshrine] Teleport: {source.DisplayName} -> {dest.DisplayName} ({cost} coins, isLive={dest.IsLive}, recent={WayshrineDiscovery.ReadAndPruneCooldownTimestamps(player, now).Count})");
            return true;
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

        // ---------------- Per-shrine cooldown helpers ----------------

        private static bool OnCooldown(Player player, string shrineId, float cooldownSeconds, double now, out int waitSeconds)
        {
            waitSeconds = 0;
            if (cooldownSeconds <= 0f || string.IsNullOrEmpty(shrineId)) return false;
            double last = WayshrineDiscovery.GetLastUse(player, shrineId);
            if (last <= 0d) return false;
            double remaining = cooldownSeconds - (now - last);
            if (remaining <= 0d) return false;
            waitSeconds = Mathf.CeilToInt((float)remaining);
            return true;
        }

        private static float GetDestCooldown(DiscoveredShrine dest)
        {
            if (dest.IsLive && dest.LiveInstance != null) return dest.LiveInstance.GetCooldownSeconds();
            return WayshrineWorldRegistry.TryGet(dest.Id, out var e) ? e.Cooldown : 0f;
        }

        // Effective coin cost for a Source->Dest trip. An admin per-shrine override beats the distance calc and
        // applies to EITHER endpoint: if the source OR the destination is set free, the trip is free. The source
        // is always loaded; a destination override is read live when its zone is loaded.
        private static int EffectiveCost(Wayshrine source, DiscoveredShrine dest, float distance, int recent)
        {
            bool srcHas = source.TryGetCostOverride(out int srcCost);
            bool dstHas = TryGetDestCostOverride(dest, out int dstCost);

            if ((srcHas && srcCost == 0) || (dstHas && dstCost == 0)) return 0;  // either endpoint free -> free trip
            if (srcHas) return srcCost;                                          // source fixed price
            if (dstHas) return dstCost;                                          // destination fixed price
            return WayshrineCostCalculator.CalculateCost(distance, recent);
        }

        private static bool TryGetDestCostOverride(DiscoveredShrine dest, out int coins)
        {
            coins = 0;
            if (dest.IsLive && dest.LiveInstance != null) return dest.LiveInstance.TryGetCostOverride(out coins);
            return false;  // unloaded destination: per-shrine override not carried in the sync yet
        }

        // ---------------- Landing placement ----------------

        private const float LandMinRadius = 2f;   // never land ON the shrine / the landmark it wraps
        private const float LandMaxRadius = 12f;

        /// <summary>
        /// Picks a landing point in a 2–12 m ring around the destination centre. Samples several angles/radii,
        /// resolves the top-of-solids height (loaded) or heightmap (unloaded), and takes the first spot with
        /// clear headroom so the player doesn't teleport inside geometry. Faces outward from the centre.
        /// Vanilla's own post-teleport FindFloor (Player.cs) then refines the exact Y.
        /// </summary>
        private static Vector3 ChooseLandingPosition(DiscoveredShrine dest, out Quaternion rot)
        {
            Vector3 center = (dest.IsLive && dest.LiveInstance != null) ? dest.LiveInstance.transform.position : dest.Position;
            bool loaded = dest.IsLive && dest.LiveInstance != null;
            var zs = ZoneSystem.instance;

            Vector3 chosen = center;
            for (int i = 0; i < 16; i++)
            {
                float ang = UnityEngine.Random.value * Mathf.PI * 2f;
                // area-uniform radius so points aren't bunched near the inner edge
                float rr = Mathf.Lerp(LandMinRadius * LandMinRadius, LandMaxRadius * LandMaxRadius, UnityEngine.Random.value);
                float r = Mathf.Sqrt(rr);
                Vector3 xz = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;

                float y = center.y;
                if (zs != null)
                {
                    if (loaded && zs.GetSolidHeight(xz, out float sh)) y = sh;
                    else if (zs.GetGroundHeight(xz, out float gh)) y = gh;
                }
                Vector3 cand = new Vector3(xz.x, y, xz.z);

                if (!loaded || IsClearSpot(cand)) { chosen = cand; break; }
                if (i == 15) chosen = cand;   // give up after N tries — still off-centre, vanilla FindFloor will nudge
            }

            Vector3 outward = chosen - center; outward.y = 0f;
            rot = outward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(outward.normalized) : Quaternion.identity;
            return chosen + Vector3.up * 0.5f;
        }

        private static bool IsClearSpot(Vector3 ground)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "piece");
            if (mask == 0) return true;
            // No solid geometry where the player's capsule would stand.
            return !Physics.CheckCapsule(ground + Vector3.up * 0.4f, ground + Vector3.up * 1.7f, 0.4f, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
