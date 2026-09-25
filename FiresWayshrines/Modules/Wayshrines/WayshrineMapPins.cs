using System.Collections.Generic;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Manages map pins for wayshrines. Discovered shrines get a labeled pin
    /// (Icon3), undiscovered shrines optionally get a faded "???" pin (Icon4)
    /// when <see cref="WayshrineConfig.ShowUndiscoveredAsLocked"/> is enabled.
    /// All wayshrine pins are <c>save: false</c> — we rebuild them every
    /// session from the player's discovery customData and the world ZDO scan.
    /// </summary>
    public static class WayshrineMapPins
    {
        // PinType for discovered wayshrines (full label, full color).
        public const Minimap.PinType DiscoveredPinType = Minimap.PinType.Icon3;
        // PinType for undiscovered "locked" wayshrines (no label / generic name).
        public const Minimap.PinType LockedPinType     = Minimap.PinType.Icon4;

        // Backwards-compat alias for callers that referenced the old name.
        public const Minimap.PinType WayshrinePinType  = DiscoveredPinType;

        private const string LockedPinName = "???";

        // shrine id -> pin. Same map covers both discovered and locked pins;
        // the pin's Type tells them apart.
        private static readonly Dictionary<string, Minimap.PinData> _pinByShrineId
            = new Dictionary<string, Minimap.PinData>();

        // World-entry summary box reads these: current pin total and how many
        // full rebuilds have completed (lets the plugin wait for the first one).
        public static int PinCount => _pinByShrineId.Count;
        public static int CompletedRebuilds { get; private set; }

        /// <summary>
        /// Adds (or upgrades) a pin for a discovered live shrine. If a locked
        /// pin already exists for this id, it's replaced with a discovered one.
        /// </summary>
        public static void AddPinForShrine(Wayshrine shrine)
        {
            if (shrine == null || Minimap.instance == null) return;
            if (shrine.GetAdminOnly() && !Wayshrine.IsLocalPlayerAdmin()) return; // admin-only: invisible to non-admins
            if (!WayshrineGroups.ShowsOnMap(shrine.ShrineId, shrine.GetGroup(), shrine.IsGroupParent(), WayshrineTeleportFlow.ArmedSource)) return;
            if (_pinByShrineId.TryGetValue(shrine.ShrineId, out var existing))
            {
                if (existing != null && existing.m_type == DiscoveredPinType) return;
                RemovePinForShrine(shrine.ShrineId);
            }

            var pin = Minimap.instance.AddPin(
                shrine.transform.position,
                DiscoveredPinType,
                shrine.DisplayName,
                save: false,
                isChecked: false);
            if (pin != null)
            {
                ApplyCustomIcon(pin, Utils.GetPrefabName(shrine.gameObject));
                _pinByShrineId[shrine.ShrineId] = pin;
            }
        }

        /// <summary>
        /// Overrides the vanilla type-based icon on a wayshrine pin with a
        /// runtime-baked render of the actual prefab. Vanilla
        /// <see cref="Minimap.AddPin"/> assigns <c>m_icon</c> from
        /// <see cref="Minimap.GetSprite"/> once at creation; nothing
        /// overwrites it after, so this swap sticks.
        /// </summary>
        private static void ApplyCustomIcon(Minimap.PinData pin, string prefabName)
        {
            if (pin == null || string.IsNullOrEmpty(prefabName)) return;
            var custom = WayshrineIconBaker.GetIcon(prefabName);
            if (custom == null) return;
            pin.m_icon = custom;
            if (pin.m_iconElement != null)
                pin.m_iconElement.sprite = custom;
        }

        /// <summary>
        /// Adds the appropriate pin for a freshly-loaded live shrine: a
        /// labeled discovered pin if the local player already knows it, or a
        /// locked "???" pin if they don't and
        /// <see cref="WayshrineConfig.ShowUndiscoveredAsLocked"/> is enabled.
        /// Does nothing when the local player isn't ready yet.
        /// </summary>
        public static void AddPinForShrineRespectingDiscovery(Wayshrine shrine)
        {
            if (shrine == null || Minimap.instance == null) return;
            var player = Player.m_localPlayer;
            if (player == null) return;

            if (shrine.GetAdminOnly() && !Wayshrine.IsLocalPlayerAdmin()) return; // admin-only: invisible to non-admins
            if (!WayshrineGroups.ShowsOnMap(shrine.ShrineId, shrine.GetGroup(), shrine.IsGroupParent(), WayshrineTeleportFlow.ArmedSource)) return;

            if (WayshrineDiscovery.HasDiscovered(player, shrine.ShrineId))
            {
                AddPinForShrine(shrine);
                return;
            }

            if (WayshrineConfig.ShowUndiscoveredAsLocked?.Value ?? false)
                AddLockedPin(shrine.ShrineId, shrine.transform.position);
        }

        public static void RemovePinForShrine(string shrineId)
        {
            if (string.IsNullOrEmpty(shrineId)) return;
            if (!_pinByShrineId.TryGetValue(shrineId, out var pin)) return;
            if (Minimap.instance != null && pin != null)
                Minimap.instance.RemovePin(pin);
            _pinByShrineId.Remove(shrineId);
        }

        /// <summary>
        /// Rebuild every wayshrine pin from scratch — discovered shrines get
        /// a labeled Icon3, and (if enabled) undiscovered shrines get an
        /// Icon4 "???" pin so the player can see something is there without
        /// learning its name. Stale ids in the player's discovery set get
        /// pruned in the same pass.
        /// </summary>
        public static void RebuildPinsForLocalPlayer()
        {
            // DIAG (login-freeze investigation): per-section ENTER/EXIT so a repro
            // log pinpoints which step is hung. Verbose-gated via FiresLogger.
            FiresLogger.LogVerbose($"[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: ENTER (existing pins={_pinByShrineId.Count})");

            // Clear all our previous pins from the map.
            if (Minimap.instance != null)
            {
                foreach (var kv in _pinByShrineId)
                    if (kv.Value != null) Minimap.instance.RemovePin(kv.Value);
            }
            _pinByShrineId.Clear();
            FiresLogger.LogVerbose("[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: cleared prior pins");

            var player = Player.m_localPlayer;
            if (player == null || Minimap.instance == null)
            {
                FiresLogger.LogVerbose("[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: bail — player or Minimap.instance null");
                return;
            }

            WayshrineWorldRegistry.PruneStaleDiscovery(player);
            FiresLogger.LogVerbose("[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: PruneStaleDiscovery returned");

            var discovered = WayshrineDiscovery.GetAllDiscovered(player);
            bool showLocked = WayshrineConfig.ShowUndiscoveredAsLocked?.Value ?? false;
            bool isAdmin = Wayshrine.IsLocalPlayerAdmin();
            Wayshrine armedSource = WayshrineTeleportFlow.ArmedSource;
            FiresLogger.LogVerbose($"[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: discovered={discovered.Count}, showLocked={showLocked}");

            // Live shrines first — authoritative position + name.
            // Snapshot the registry first: a freshly-spawning shrine's Awake can call
            // WayshrineRegistry.Register during this loop (e.g. when zone-stream
            // delivers a new shrine right as the player spawns), which would mutate
            // the underlying List<Wayshrine> mid-enumeration and throw
            // InvalidOperationException.
            var liveShrines = new List<Wayshrine>(WayshrineRegistry.All);
            FiresLogger.LogVerbose($"[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: live-loop ENTER ({liveShrines.Count} live shrines)");
            var seen = new HashSet<string>();
            foreach (var shrine in liveShrines)
            {
                if (shrine == null) continue;

                if (shrine.GetAdminOnly() && !isAdmin) { seen.Add(shrine.ShrineId); continue; } // hidden from non-admins
                if (!WayshrineGroups.ShowsOnMap(shrine.ShrineId, shrine.GetGroup(), shrine.IsGroupParent(), armedSource))
                {
                    if (shrine.GetAutoDiscover() && !discovered.Contains(shrine.ShrineId))
                        WayshrineDiscovery.MarkDiscovered(player, shrine.ShrineId, shrine.GetEpoch());
                    seen.Add(shrine.ShrineId);
                    continue;
                }

                // Auto-discover shrines become known (and usable) the moment they're seen.
                if (shrine.GetAutoDiscover() && !discovered.Contains(shrine.ShrineId))
                    WayshrineDiscovery.MarkDiscovered(player, shrine.ShrineId, shrine.GetEpoch());

                if (discovered.Contains(shrine.ShrineId) || shrine.GetAutoDiscover())
                {
                    AddPinForShrine(shrine);
                    seen.Add(shrine.ShrineId);
                }
                else if (showLocked)
                {
                    AddLockedPin(shrine.ShrineId, shrine.transform.position);
                    seen.Add(shrine.ShrineId);
                }
            }
            FiresLogger.LogVerbose($"[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: live-loop EXIT (pins so far={_pinByShrineId.Count})");

            // World-registry snapshot for shrines whose zone isn't loaded.
            var registryEntries = WayshrineWorldRegistry.GetAll();
            FiresLogger.LogVerbose($"[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: registry-loop ENTER ({registryEntries.Count} entries)");
            foreach (var entry in registryEntries)
            {
                if (!seen.Add(entry.ShrineId)) continue;
                if (entry.AdminOnly && !isAdmin) continue; // hidden from non-admins
                if (!WayshrineGroups.ShowsOnMap(entry.ShrineId, entry.Group ?? "", entry.IsGroupParent, armedSource))
                {
                    if (entry.AutoDiscover && !discovered.Contains(entry.ShrineId))
                        WayshrineDiscovery.MarkDiscovered(player, entry.ShrineId, entry.Epoch);
                    continue;
                }

                if (entry.AutoDiscover && !discovered.Contains(entry.ShrineId))
                    WayshrineDiscovery.MarkDiscovered(player, entry.ShrineId, entry.Epoch);

                if (discovered.Contains(entry.ShrineId) || entry.AutoDiscover)
                {
                    var pin = Minimap.instance.AddPin(
                        entry.Position, DiscoveredPinType, entry.DisplayName,
                        save: false, isChecked: false);
                    if (pin != null)
                    {
                        ApplyCustomIcon(pin, entry.PrefabName);
                        _pinByShrineId[entry.ShrineId] = pin;
                    }
                }
                else if (showLocked)
                {
                    AddLockedPin(entry.ShrineId, entry.Position);
                }
            }
            FiresLogger.LogVerbose($"[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: registry-loop EXIT (total pins={_pinByShrineId.Count})");
            CompletedRebuilds++;
            FiresLogger.LogVerbose("[WayshrineMapPins][DIAG] RebuildPinsForLocalPlayer: EXIT");
        }

        private static void AddLockedPin(string shrineId, Vector3 pos)
        {
            if (string.IsNullOrEmpty(shrineId) || Minimap.instance == null) return;
            if (_pinByShrineId.ContainsKey(shrineId)) return;
            var pin = Minimap.instance.AddPin(
                pos, LockedPinType, LockedPinName,
                save: false, isChecked: false);
            if (pin != null) _pinByShrineId[shrineId] = pin;
        }

        public static Wayshrine ResolveShrineForPin(Minimap.PinData pin)
        {
            if (pin == null) return null;
            // Snapshot the registry up front; matches the defensive copy in
            // RebuildPinsForLocalPlayer for the same concurrent-mutation reason.
            var liveShrines = new List<Wayshrine>(WayshrineRegistry.All);
            foreach (var kvp in _pinByShrineId)
            {
                if (kvp.Value == pin)
                {
                    foreach (var shrine in liveShrines)
                        if (shrine != null && shrine.ShrineId == kvp.Key)
                            return shrine;
                    return null;
                }
            }
            return null;
        }

        public static bool IsWayshrinePin(Minimap.PinData pin)
        {
            if (pin == null) return false;
            foreach (var kvp in _pinByShrineId)
                if (kvp.Value == pin) return true;
            return false;
        }
    }
}
