using System.Collections.Generic;
using System.Globalization;
using FiresCore.UI.ContextMenu;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Admin right-click options for wayshrines, contributed to Core's hold-Alt context menu
    /// (<see cref="FiresContextMenu"/>). Only shown to admins, only when hovering a wayshrine.
    ///
    /// All settings are per-shrine and ZDO-backed (see <see cref="Wayshrine"/>'s setters), so they
    /// replicate to every client via vanilla ZDO networking and ride the shrine-list sync for
    /// unloaded shrines on dedicated clients. The map-marker wipe is server-authoritative
    /// (<see cref="WayshrineSyncRPC.RequestWipe"/>).
    /// </summary>
    public sealed class WayshrineAdminContextProvider : IContextMenuProvider
    {
        public string TitleFor(ContextTarget target)
        {
            var shrine = target?.Find<Wayshrine>();
            if (shrine == null || !Wayshrine.IsLocalPlayerAdmin()) return null;
            return $"Wayshrine — {shrine.DisplayName}";
        }

        public IEnumerable<ContextMenuItem> GetItems(ContextTarget target)
        {
            var shrine = target?.Find<Wayshrine>();
            if (shrine == null) return null;
            if (!Wayshrine.IsLocalPlayerAdmin()) return null;

            string costNow = shrine.TryGetCostOverride(out int c) ? $"{c} coins" : "auto";
            int cdNow = Mathf.RoundToInt(shrine.GetCooldownSeconds());

            var items = new List<ContextMenuItem>
            {
                ContextMenuItem.Row("Rename…",                            () => Rename(shrine)),
                ContextMenuItem.Row($"Set teleport cost…  (now: {costNow})", () => PromptCost(shrine)),
                ContextMenuItem.Row($"Adjust cooldown…  (now: {cdNow}s)",  () => PromptCooldown(shrine)),
            };

            // Trigger-box wayshrines: resize the invisible volume (Right Shift + scroll also works while placing).
            if (shrine.GetComponent<WayshrineTriggerWire>() != null)
            {
                Vector3 sc = shrine.transform.localScale;
                items.Add(ContextMenuItem.Row($"Set trigger size…  ({sc.x:0.#}×{sc.y:0.#}×{sc.z:0.#} m)", () => PromptSize(shrine)));
            }

            items.Add(ContextMenuItem.Sep());
            items.Add(ContextMenuItem.Row($"Auto-discover:  {OnOff(shrine.GetAutoDiscover())}", () => ToggleAutoDiscover(shrine)));
            items.Add(ContextMenuItem.Row($"Admin-only:  {OnOff(shrine.GetAdminOnly())}",       () => ToggleAdminOnly(shrine)));
            items.Add(ContextMenuItem.Sep());
            items.Add(ContextMenuItem.Row($"Network group…  (now: {WayshrineGroups.Describe(shrine.GetGroup(), shrine.IsGroupParent())})",
                () => PromptGroup(shrine)));
            items.Add(ContextMenuItem.Row($"Group parent (way in and out):  {OnOff(shrine.IsGroupParent())}",
                () => ToggleGroupParent(shrine), enabled: shrine.GetGroup().Length > 0));
            items.Add(ContextMenuItem.Sep());
            items.Add(ContextMenuItem.Row("Wipe map marker (all players)",      () => WayshrineSyncRPC.RequestWipe(shrine)));
            return items;
        }

        private static void PromptSize(Wayshrine shrine)
        {
            if (TextInput.instance == null) { Toast("TextInput unavailable."); return; }
            Vector3 cur = shrine.transform.localScale;
            string init = $"{cur.x:0.#} {cur.y:0.#} {cur.z:0.#}";
            TextInput.instance.RequestText(new PromptReceiver(init, s =>
            {
                var p = (s ?? "").Split(new[] { ' ', ',', 'x', 'X', '*' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 3
                    && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    WayshrineTriggerScaling.SetSize(shrine, new Vector3(x, y, z));
                    Toast($"Trigger size: {x:0.#} × {y:0.#} × {z:0.#} m");
                }
                else Toast("Enter three numbers: X Y Z (metres).");
            }), "Trigger box size — X Y Z in metres", 24);
        }

        // ---------------- actions ----------------

        private static void Rename(Wayshrine shrine)
        {
            if (TextInput.instance == null) { Toast("TextInput unavailable."); return; }
            // Wayshrine is itself a TextReceiver; SetText routes to RenameByAdmin (admin-guarded).
            TextInput.instance.RequestText(shrine, $"Rename Wayshrine ({shrine.DisplayName})", 40);
        }

        private static void PromptCost(Wayshrine shrine)
        {
            if (TextInput.instance == null) { Toast("TextInput unavailable."); return; }
            string init = shrine.TryGetCostOverride(out int c) ? c.ToString(CultureInfo.InvariantCulture) : string.Empty;
            TextInput.instance.RequestText(new PromptReceiver(init, s =>
            {
                s = s?.Trim();
                if (string.IsNullOrEmpty(s)) { shrine.ClearCostOverride(); Toast("Teleport cost: auto (distance-based)"); return; }
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int coins) && coins >= 0)
                {
                    shrine.SetCostOverride(coins);
                    Toast(coins == 0 ? "Teleport cost: free" : $"Teleport cost: {coins} coins");
                }
                else Toast("Enter a whole number ≥ 0 (blank = auto, 0 = free).");
            }), "Teleport cost in coins  (blank = auto, 0 = free)", 8);
        }

        private static void PromptCooldown(Wayshrine shrine)
        {
            if (TextInput.instance == null) { Toast("TextInput unavailable."); return; }
            string init = Mathf.RoundToInt(shrine.GetCooldownSeconds()).ToString(CultureInfo.InvariantCulture);
            TextInput.instance.RequestText(new PromptReceiver(init, s =>
            {
                s = s?.Trim();
                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float sec) && sec >= 0f)
                {
                    shrine.SetCooldownSeconds(sec);
                    WayshrineSyncRPC.AnnounceSettingsChanged();
                    Toast(sec <= 0f ? "Cooldown: none" : $"Cooldown: {Mathf.RoundToInt(sec)}s per player");
                }
                else Toast("Enter seconds ≥ 0 (0 = none).");
            }), "Per-player cooldown in seconds  (0 = none)", 8);
        }

        private static void ToggleAutoDiscover(Wayshrine shrine)
        {
            bool on = !shrine.GetAutoDiscover();
            shrine.SetAutoDiscover(on);
            if (on && Player.m_localPlayer != null)
                WayshrineDiscovery.MarkDiscovered(Player.m_localPlayer, shrine.ShrineId, shrine.GetEpoch());
            WayshrineMapPins.RemovePinForShrine(shrine.ShrineId);
            WayshrineMapPins.AddPinForShrineRespectingDiscovery(shrine);
            WayshrineSyncRPC.AnnounceSettingsChanged();
            Toast($"Auto-discover {OnOff(on)}");
        }

        private static void ToggleAdminOnly(Wayshrine shrine)
        {
            bool on = !shrine.GetAdminOnly();
            shrine.SetAdminOnly(on);
            // Re-evaluate the local pin immediately (hidden for non-admins; the admin keeps seeing it).
            WayshrineMapPins.RemovePinForShrine(shrine.ShrineId);
            WayshrineMapPins.AddPinForShrineRespectingDiscovery(shrine);
            WayshrineSyncRPC.AnnounceSettingsChanged();
            Toast($"Admin-only {OnOff(on)}");
        }

        private static void PromptGroup(Wayshrine shrine)
        {
            if (TextInput.instance == null) { Toast("TextInput unavailable."); return; }
            TextInput.instance.RequestText(new PromptReceiver(shrine.GetGroup(), s =>
            {
                string group = WayshrineGroups.Normalize(s);
                shrine.SetGroup(group);
                if (group.Length == 0 && shrine.IsGroupParent())
                    shrine.SetGroupParent(false);
                AfterNetworkChange(shrine, group, group.Length > 0 && shrine.IsGroupParent());
            }), "Network group  (blank = world network; same name = same dungeon network)", WayshrineGroups.MaxGroupNameLength);
        }

        private static void ToggleGroupParent(Wayshrine shrine)
        {
            string group = shrine.GetGroup();
            if (group.Length == 0) { Toast("Set a network group first."); return; }
            bool on = !shrine.IsGroupParent();
            shrine.SetGroupParent(on);
            AfterNetworkChange(shrine, group, on);
        }

        private static void AfterNetworkChange(Wayshrine shrine, string group, bool isParent)
        {
            WayshrineMapPins.RebuildPinsForLocalPlayer();
            WayshrineSyncRPC.AnnounceSettingsChanged();
            Toast($"{shrine.DisplayName}: {WayshrineGroups.Describe(group, isParent)}");
        }

        private static string OnOff(bool b) => b ? "ON" : "OFF";

        private static void Toast(string msg)
            => Player.m_localPlayer?.Message(MessageHud.MessageType.Center, msg, 0, null);

        /// <summary>Throwaway <see cref="TextReceiver"/> for one-shot numeric prompts via vanilla TextInput.</summary>
        private sealed class PromptReceiver : TextReceiver
        {
            private readonly string _initial;
            private readonly System.Action<string> _onSubmit;
            public PromptReceiver(string initial, System.Action<string> onSubmit) { _initial = initial; _onSubmit = onSubmit; }
            public string GetText() => _initial;
            public void SetText(string text) => _onSubmit?.Invoke(text);
        }
    }
}
