using System.Collections.Generic;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Resolves a wayshrine to a usable teleport target. The world is the
    /// source of truth — <see cref="WayshrineWorldRegistry"/> scans
    /// <c>ZDOMan</c> for every existing wayshrine ZDO (loaded or not) on each
    /// query — so a shrine that was destroyed by another player or that the
    /// local player hammered in a previous session no longer qualifies as a
    /// teleport target.
    ///
    /// We then filter the world list down to shrines the local player has
    /// discovered (their per-player discovery set in customData). Stale
    /// discovery entries (id known to the player but no shrine in the world)
    /// are pruned in the same pass via
    /// <see cref="WayshrineWorldRegistry.PruneStaleDiscovery"/>.
    ///
    /// For each resulting hit we prefer the live <see cref="WayshrineRegistry"/>
    /// instance when its zone is loaded (authoritative transform + freshest
    /// display name), falling back to the ZDO data for unloaded zones.
    /// </summary>
    public struct DiscoveredShrine
    {
        public string Id;
        public string DisplayName;
        public Vector3 Position;
        public Quaternion Rotation;
        public Wayshrine LiveInstance;
        public string Group;
        public bool IsGroupParent;

        public bool IsLive => LiveInstance != null;
    }

    public static class WayshrineLookup
    {
        /// <summary>The discovered shrine nearest a map click that the armed source can actually reach.</summary>
        public static DiscoveredShrine? FindClosestReachable(Player player, Wayshrine source, Vector3 from, float maxDistanceXZ) =>
            FindClosestDiscovered(player, from, maxDistanceXZ, shrine => WayshrineGroups.CanTravel(source, shrine));

        /// <summary>The nearest discovered shrine on the world network — never a closed group's inner shrine.</summary>
        public static DiscoveredShrine? FindClosestOnWorldNetwork(Player player, Vector3 from) =>
            FindClosestDiscovered(player, from, float.MaxValue, shrine => WayshrineGroups.IsOnWorldNetwork(shrine.Group, shrine.IsGroupParent));

        public static DiscoveredShrine? FindClosestDiscovered(Player player, Vector3 from, float maxDistanceXZ = float.MaxValue,
            System.Func<DiscoveredShrine, bool> accept = null)
        {
            if (player == null) return null;
            DiscoveredShrine? best = null;
            float bestSqr = maxDistanceXZ * maxDistanceXZ;

            foreach (var s in EnumerateDiscovered(player))
            {
                if (accept != null && !accept(s)) continue;
                Vector3 d = Ring.Reach(s.Position - from, from);   // XZ; halo ring: 3D
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
        /// Yields every wayshrine that BOTH (a) currently exists in the world
        /// and (b) the player has discovered. Live registry data takes
        /// precedence over ZDO snapshot data when the zone is loaded.
        /// Implicitly prunes the player's discovery set of any ids that no
        /// longer correspond to a real shrine in the world.
        /// </summary>
        public static IEnumerable<DiscoveredShrine> EnumerateDiscovered(Player player)
        {
            if (player == null) yield break;

            // Drop any ids the player remembers but the world no longer has.
            WayshrineWorldRegistry.PruneStaleDiscovery(player);

            var world = WayshrineWorldRegistry.GetAll();
            if (world.Count == 0) yield break;

            // Build a one-shot map from ShrineId -> live Wayshrine for the
            // shrines whose zone is currently loaded.
            var live = new Dictionary<string, Wayshrine>();
            var liveAll = WayshrineRegistry.All;
            for (int i = 0; i < liveAll.Count; i++)
            {
                var w = liveAll[i];
                if (w == null || string.IsNullOrEmpty(w.ShrineId)) continue;
                live[w.ShrineId] = w;
            }

            bool isAdmin = Wayshrine.IsLocalPlayerAdmin();

            for (int i = 0; i < world.Count; i++)
            {
                var entry = world[i];
                if (!WayshrineDiscovery.HasDiscovered(player, entry.ShrineId)) continue;

                // Admin-only shrines are not valid teleport targets (to OR from) for non-admins.
                if (!isAdmin)
                {
                    bool adminOnly = live.TryGetValue(entry.ShrineId, out var lw) && lw != null
                        ? lw.GetAdminOnly()
                        : entry.AdminOnly;
                    if (adminOnly) continue;
                }

                if (live.TryGetValue(entry.ShrineId, out var liveShrine) && liveShrine != null)
                {
                    yield return new DiscoveredShrine
                    {
                        Id           = liveShrine.ShrineId,
                        DisplayName  = liveShrine.DisplayName,
                        Position     = liveShrine.transform.position,
                        Rotation     = liveShrine.transform.rotation,
                        LiveInstance = liveShrine,
                        Group        = liveShrine.GetGroup(),
                        IsGroupParent = liveShrine.IsGroupParent(),
                    };
                }
                else
                {
                    yield return new DiscoveredShrine
                    {
                        Id           = entry.ShrineId,
                        DisplayName  = entry.DisplayName,
                        Position     = entry.Position,
                        Rotation     = entry.Rotation,
                        LiveInstance = null,
                        Group        = entry.Group ?? "",
                        IsGroupParent = entry.IsGroupParent,
                    };
                }
            }
        }
    }
}
