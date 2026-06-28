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
        public static bool MarkDiscovered(Player player, string shrineId)
        {
            if (player == null || string.IsNullOrEmpty(shrineId)) return false;
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
