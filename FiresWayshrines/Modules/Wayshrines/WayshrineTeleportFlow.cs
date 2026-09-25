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
            WayshrineMapPins.RebuildPinsForLocalPlayer();
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);

            FiresLogger.LogInfo($"[Wayshrine] Armed source='{source.DisplayName}' id='{source.ShrineId}' ({WayshrineGroups.Describe(source.GetGroup(), source.IsGroupParent())}). Map opened, waiting for pin click.");

            string scope = WayshrineGroups.IsOnWorldNetwork(source.GetGroup(), source.IsGroupParent())
                ? "click a wayshrine pin"
                : "click a wayshrine pin in this network";
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                $"Choose destination — {scope} (escape to cancel).");
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

            var dest = WayshrineLookup.FindClosestReachable(player, _armedSource, worldClickPos, pickRadius);
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

        public static void Disarm()
        {
            if (_armedSource == null) return;
            _armedSource = null;
            WayshrineMapPins.RebuildPinsForLocalPlayer();
        }

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
                if (!player.IsTeleportable(allowAllItems: false))
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

            // Network re-check: a group may have been reassigned while the map was open.
            if (source != null && !WayshrineGroups.CanTravel(source, dest))
            {
                player.Message(MessageHud.MessageType.Center, "That wayshrine isn't on this network.", 0, null);
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
            if (!dest.IsLive)
                player.StartCoroutine(SettleOntoRingAfterArrival(player, dest.Id));

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
        private const int LandAttempts = 24;
        private const float LandProbeAbove = 1.6f;
        private const float LandProbeBelow = 6f;
        private const float EyeHeight = 1.2f;
        private const float WadeDepth = 0.4f;
        private const float OnTerrainTolerance = 1f;
        private const float ArrivalSettleTimeoutSeconds = 30f;

        /// <summary>
        /// Where the player lands. Loaded destination: a safe spot in the 2-12 m ring (see FindSafeRingSpot).
        /// Unloaded destination: there are no colliders yet, so aim at the spot in front of the shrine at the shrine's
        /// own height; vanilla's post-teleport FindFloor casts down from 1 m above that and settles on the floor the
        /// shrine stands on (a dungeon floor, not the terrain far below), then SettleOntoRingAfterArrival moves the
        /// player into the ring once the area has loaded.
        /// </summary>
        private static Vector3 ChooseLandingPosition(DiscoveredShrine dest, out Quaternion rot)
        {
            bool loaded = dest.IsLive && dest.LiveInstance != null;
            Vector3 center = loaded ? dest.LiveInstance.transform.position : dest.Position;
            Quaternion facing = loaded ? dest.LiveInstance.transform.rotation : dest.Rotation;
            Vector3 front = FrontOfShrine(center, facing);
            Vector3 chosen = loaded && TryFindSafeRingSpot(center, out Vector3 ring) ? ring : front;
            rot = FaceAway(center, chosen, facing);
            return chosen + Vector3.up * 0.5f;
        }

        private static System.Collections.IEnumerator SettleOntoRingAfterArrival(Player player, string shrineId)
        {
            float deadline = Time.time + ArrivalSettleTimeoutSeconds;
            while (player != null && player.IsTeleporting() && Time.time < deadline)
                yield return null;
            yield return null;
            if (player == null || player.IsTeleporting()) yield break;

            Wayshrine arrived = null;
            foreach (Wayshrine shrine in WayshrineRegistry.All)
                if (shrine != null && shrine.ShrineId == shrineId) { arrived = shrine; break; }
            if (arrived == null) yield break;

            Vector3 center = arrived.transform.position;
            if (!TryFindSafeRingSpot(center, out Vector3 ring)) yield break;
            player.transform.position = ring + Vector3.up * 0.1f;
            player.transform.rotation = FaceAway(center, ring, arrived.transform.rotation);
        }

        /// <summary>
        /// A random spot 2-12 m from the shrine that is safe to stand on. The floor comes from a SHORT downward probe
        /// that starts just above the shrine's own base: vanilla's GetSolidHeight casts from 1000 m up, returned the
        /// top of any roof, and so put players on top of dungeons, caves and buildings. A candidate must also be in
        /// plain sight of the shrine (same room, not behind a wall or above a ceiling), have headroom, and not be in
        /// water deeper than knee height (freezing in the Deep North, burning in the Ashlands sea) or on lava.
        /// </summary>
        private static bool TryFindSafeRingSpot(Vector3 center, out Vector3 spot)
        {
            int floorMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            int wallMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            Vector3 shrineEye = center + Vector3.up * EyeHeight;
            for (int attempt = 0; attempt < LandAttempts; attempt++)
            {
                Vector3 ring = center + RandomRingOffset();
                Vector3 probe = new Vector3(ring.x, center.y + LandProbeAbove, ring.z);
                if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, LandProbeAbove + LandProbeBelow, floorMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (hit.collider.attachedRigidbody != null)
                    continue;
                Vector3 ground = hit.point;
                if (Physics.Linecast(shrineEye, ground + Vector3.up * EyeHeight, wallMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (IsHazardous(ground) || !HasHeadroom(ground))
                    continue;
                spot = ground;
                return true;
            }
            spot = center;
            return false;
        }

        private static Vector3 RandomRingOffset()
        {
            float angle = UnityEngine.Random.value * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Mathf.Lerp(LandMinRadius * LandMinRadius, LandMaxRadius * LandMaxRadius, UnityEngine.Random.value));
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        }

        private static Vector3 FrontOfShrine(Vector3 center, Quaternion facing)
        {
            Vector3 forward = facing * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            return center + forward.normalized * LandMinRadius;
        }

        private static Quaternion FaceAway(Vector3 center, Vector3 spot, Quaternion fallback)
        {
            Vector3 outward = spot - center;
            outward.y = 0f;
            return outward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(outward.normalized) : fallback;
        }

        private static bool IsHazardous(Vector3 ground)
        {
            if (Floating.GetLiquidLevel(ground + Vector3.up * 0.1f) > ground.y + WadeDepth)
                return true;
            var zones = ZoneSystem.instance;
            if (zones == null || !zones.GetGroundHeight(ground, out float terrain) || Mathf.Abs(ground.y - terrain) > OnTerrainTolerance)
                return false;
            return zones.IsLava(ground);
        }

        private static bool HasHeadroom(Vector3 ground)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "piece");
            return !Physics.CheckCapsule(ground + Vector3.up * 0.4f, ground + Vector3.up * 1.7f, 0.4f, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
