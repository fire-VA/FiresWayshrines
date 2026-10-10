using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Per-player wayshrine discovery state and cooldown timestamp tracking.
    /// All state is stored in <c>Player.m_customData</c> so it auto-saves
    /// with the player profile and survives logout/login.
    /// </summary>
    public static class WayshrineDiscovery
    {
        private const string KEY_DISCOVERED = "WayshrinesDiscovered";
        private const string KEY_COOLDOWN_TIMESTAMPS = "WayshrineTeleports";

        // ---------------- Discovery ----------------

        /// <summary>
        /// Has this player discovered the given shrine?
        /// </summary>
        public static bool HasDiscovered(Player player, string shrineId)
        {
            if (player == null || string.IsNullOrEmpty(shrineId)) return false;
            var set = ReadDiscoveredSet(player);
            return set.Contains(shrineId);
        }

        /// <summary>
        /// Marks the shrine as discovered for this player. Returns true if it
        /// was newly added (false if already known).
        /// </summary>
        public static bool MarkDiscovered(Player player, string shrineId, int epoch = 0)
        {
            if (player == null || string.IsNullOrEmpty(shrineId)) return false;
            // Always stamp the epoch we discovered it at, so a later wipe (which bumps the shrine's epoch)
            // can tell this discovery is stale.
            SetDiscoveredEpoch(player, shrineId, epoch);
            var set = ReadDiscoveredSet(player);
            if (set.Contains(shrineId)) return false;
            set.Add(shrineId);
            WriteDiscoveredSet(player, set);
            return true;
        }

        /// <summary>
        /// Returns all shrine IDs the player has discovered.
        /// </summary>
        public static HashSet<string> GetAllDiscovered(Player player)
        {
            return ReadDiscoveredSet(player);
        }

        /// <summary>
        /// Removes a shrine id from the player's discovered set (e.g. when
        /// the shrine is destroyed). Returns true if it was present and
        /// removed, false otherwise.
        /// </summary>
        public static bool RemoveDiscovered(Player player, string shrineId)
        {
            if (player == null || string.IsNullOrEmpty(shrineId)) return false;
            var set = ReadDiscoveredSet(player);
            if (!set.Remove(shrineId)) return false;
            WriteDiscoveredSet(player, set);
            return true;
        }

        private static HashSet<string> ReadDiscoveredSet(Player player)
        {
            var set = new HashSet<string>();
            if (player?.m_customData == null) return set;
            if (!player.m_customData.TryGetValue(KEY_DISCOVERED, out var raw) || string.IsNullOrEmpty(raw))
                return set;
            foreach (var id in raw.Split(','))
            {
                var trimmed = id.Trim();
                if (trimmed.Length > 0) set.Add(trimmed);
            }
            return set;
        }

        private static void WriteDiscoveredSet(Player player, HashSet<string> set)
        {
            if (player?.m_customData == null) return;
            if (set.Count == 0)
            {
                player.m_customData.Remove(KEY_DISCOVERED);
                return;
            }
            var sb = new StringBuilder(set.Count * 24);
            bool first = true;
            foreach (var id in set)
            {
                if (!first) sb.Append(',');
                sb.Append(id);
                first = false;
            }
            player.m_customData[KEY_DISCOVERED] = sb.ToString();
        }

        // ---------------- Cooldown timestamps ----------------

        /// <summary>
        /// Reads recent teleport timestamps (unix-seconds, server time) and
        /// prunes any outside the cooldown window. The pruned list is also
        /// written back so we don't accumulate forever.
        /// </summary>
        public static List<double> ReadAndPruneCooldownTimestamps(Player player, double nowSeconds)
        {
            var list = new List<double>();
            if (player?.m_customData == null) return list;
            if (player.m_customData.TryGetValue(KEY_COOLDOWN_TIMESTAMPS, out var raw) && !string.IsNullOrEmpty(raw))
            {
                foreach (var token in raw.Split(','))
                {
                    if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var ts))
                        list.Add(ts);
                }
            }
            var pruned = WayshrineCostCalculator.PruneOutsideWindow(list, nowSeconds);
            // Persist if pruning removed anything
            if (pruned.Count != list.Count)
                WriteCooldownTimestamps(player, pruned);
            return pruned;
        }

        /// <summary>
        /// Records a new teleport-now timestamp.
        /// </summary>
        public static void RecordTeleport(Player player, double nowSeconds)
        {
            var current = ReadAndPruneCooldownTimestamps(player, nowSeconds);
            current.Add(nowSeconds);
            WriteCooldownTimestamps(player, current);
        }

        private static void WriteCooldownTimestamps(Player player, List<double> list)
        {
            if (player?.m_customData == null) return;
            if (list.Count == 0)
            {
                player.m_customData.Remove(KEY_COOLDOWN_TIMESTAMPS);
                return;
            }
            var sb = new StringBuilder(list.Count * 12);
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(list[i].ToString("F1", CultureInfo.InvariantCulture));
            }
            player.m_customData[KEY_COOLDOWN_TIMESTAMPS] = sb.ToString();
        }

        // ---------------- Per-shrine discovered-epoch (wipe support) ----------------

        private const string KEY_DISCOVERED_EPOCHS = "WayshrinesDiscoveredEpochs"; // "shrineId=epoch" pairs

        /// <summary>Epoch at which the player discovered this shrine; -1 if not recorded.</summary>
        public static int GetDiscoveredEpoch(Player player, string shrineId)
        {
            var map = ReadPairMap(player, KEY_DISCOVERED_EPOCHS);
            return map.TryGetValue(shrineId, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) ? e : -1;
        }

        public static void SetDiscoveredEpoch(Player player, string shrineId, int epoch)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(shrineId)) return;
            var map = ReadPairMap(player, KEY_DISCOVERED_EPOCHS);
            map[shrineId] = epoch.ToString(CultureInfo.InvariantCulture);
            WritePairMap(player, KEY_DISCOVERED_EPOCHS, map);
        }

        // ---------------- Per-shrine, per-player last-use (cooldown) ----------------

        private const string KEY_LAST_USE = "WayshrineLastUse"; // "shrineId=unixSeconds" pairs

        public static double GetLastUse(Player player, string shrineId)
        {
            var map = ReadPairMap(player, KEY_LAST_USE);
            return map.TryGetValue(shrineId, out var raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 0d;
        }

        public static void SetLastUse(Player player, string shrineId, double nowSeconds)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(shrineId)) return;
            var map = ReadPairMap(player, KEY_LAST_USE);
            map[shrineId] = nowSeconds.ToString("F0", CultureInfo.InvariantCulture);
            WritePairMap(player, KEY_LAST_USE, map);
        }

        // ---------------- shared "key=value;key=value" customData map helpers ----------------

        private static Dictionary<string, string> ReadPairMap(Player player, string customDataKey)
        {
            var map = new Dictionary<string, string>();
            if (player?.m_customData == null) return map;
            if (!player.m_customData.TryGetValue(customDataKey, out var raw) || string.IsNullOrEmpty(raw)) return map;
            foreach (var pair in raw.Split(';'))
            {
                int eq = pair.LastIndexOf('=');
                if (eq <= 0) continue;
                map[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }
            return map;
        }

        private static void WritePairMap(Player player, string customDataKey, Dictionary<string, string> map)
        {
            if (player?.m_customData == null) return;
            if (map.Count == 0) { player.m_customData.Remove(customDataKey); return; }
            var sb = new StringBuilder(map.Count * 24);
            bool first = true;
            foreach (var kv in map)
            {
                if (!first) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
                first = false;
            }
            player.m_customData[customDataKey] = sb.ToString();
        }

        // ---------------- Shrine ID helper ----------------

        /// <summary>
        /// Generates a deterministic ID for a shrine from its piece m_name and
        /// world position. Letting two shrines share a name still produces
        /// distinct IDs because position is part of the hash.
        /// </summary>
        public static string MakeShrineId(string pieceName, Vector3 worldPos)
        {
            return $"{pieceName}_{Mathf.RoundToInt(worldPos.x)}_{Mathf.RoundToInt(worldPos.z)}";
        }
    }
}
