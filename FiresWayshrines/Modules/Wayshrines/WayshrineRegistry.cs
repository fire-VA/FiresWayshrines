using System.Collections.Generic;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Tracks all currently-loaded wayshrine instances on the local client.
    /// Each <see cref="Wayshrine"/> registers itself in Awake and unregisters
    /// in OnDestroy. Clients use this to enumerate shrines in range, find the
    /// nearest discovered shrine, and resolve map-pin clicks.
    /// </summary>
    public static class WayshrineRegistry
    {
        private static readonly List<Wayshrine> _live = new List<Wayshrine>();

        public static IReadOnlyList<Wayshrine> All => _live;

        public static void Register(Wayshrine shrine)
        {
            if (shrine == null) return;
            if (!_live.Contains(shrine)) _live.Add(shrine);
        }

        public static void Unregister(Wayshrine shrine)
        {
            if (shrine == null) return;
            _live.Remove(shrine);
        }

        /// <summary>
        /// Finds the wayshrine whose position is closest to <paramref name="worldPos"/>
        /// (XZ distance only) and is within <paramref name="maxDistanceXZ"/>.
        /// Returns null if no shrine matches. Optionally restricts the search
        /// to shrines the player has discovered.
        /// </summary>
        public static Wayshrine FindClosest(
            Vector3 worldPos,
            float maxDistanceXZ = float.MaxValue,
            Player onlyDiscoveredFor = null)
        {
            Wayshrine best = null;
            float bestSqr = maxDistanceXZ * maxDistanceXZ;

            for (int i = 0; i < _live.Count; i++)
            {
                var s = _live[i];
                if (s == null) continue;

                if (onlyDiscoveredFor != null &&
                    !WayshrineDiscovery.HasDiscovered(onlyDiscoveredFor, s.ShrineId))
                    continue;

                Vector3 d = s.transform.position - worldPos;
                d.y = 0f; // XZ only
                float sqr = d.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>
        /// Finds the wayshrine at the given world position, within
        /// <paramref name="maxXZ"/> meters. Used to resolve a map-pin click
        /// back to the shrine instance.
        /// </summary>
        public static Wayshrine FindAtPosition(Vector3 worldPos, float maxXZ)
        {
            return FindClosest(worldPos, maxXZ);
        }

        public static void ClearAll()
        {
            _live.Clear();
        }
    }
}
