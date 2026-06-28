using FiresCore.Logging;
using System.Collections.Generic;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// World-authoritative registry of every wayshrine that currently exists,
    /// loaded or not. Backed by <see cref="ZDOMan.GetAllZDOsWithPrefabIterative"/>
    /// — vanilla's prefab-hash scan over <c>ZDOMan.m_objectsByID</c>. ZDOs are
    /// the authoritative world state and sync to all peers via vanilla
    /// networking, so when a shrine is destroyed (its ZDO is removed) it
    /// disappears from this registry for every client automatically. No custom
    /// RPC needed for the destroyed-shrine case.
    ///
    /// We use this to filter out stale entries from the per-player discovery
    /// set: a shrine the player visited last week but that's since been
    /// hammered no longer shows up as a teleport target, and the Town Portal
    /// Scroll won't warp them to an empty patch of grass.
    ///
    /// Cache is invalidated on shrine register/unregister events plus a 5 s
    /// TTL fallback so out-of-band changes (admin spawn, ServerDevcommands)
    /// still get reflected.
    /// </summary>
    public static class WayshrineWorldRegistry
    {
        // List of wayshrine prefab names to scan for. Future biome variants
        // (Wayshrine_blackforest etc.) just need to be appended here.
        private static readonly string[] PrefabNames = { "Wayshrine_meadows" };

        public struct Entry
        {
            public ZDOID Id;
            public string PrefabName;
            public Vector3 Position;
            public Quaternion Rotation;
            public string DisplayName; // resolved from Piece.m_name override (or legacy key)
            public string ShrineId;    // matches WayshrineDiscovery.MakeShrineId(prefabName, position)
        }

        private static readonly List<Entry> _cache = new List<Entry>();
        private static float _cacheTimestamp;
        private const float CacheTTLSeconds = 5f;

        /// <summary>
        /// Returns all wayshrines known to the registry, refreshing from the
        /// local ZDOMan when this peer is authoritative.
        ///
        /// Topology:
        ///   Host (singleplayer / listen-server) — refreshes from ZDOMan; the
        ///     local ZDOMan IS the world.
        ///   Dedicated client                   — returns the cache as-is.
        ///     The cache is populated by <see cref="ApplyServerList"/> via the
        ///     <c>WayshrineSyncRPC</c> response. ZDOMan on a client only
        ///     contains streamed ZDOs (a few zones around the player), so a
        ///     ZDOMan-based scan would miss most of the world.
        /// </summary>
        public static IReadOnlyList<Entry> GetAll()
        {
            // On a dedicated client we never refresh from ZDOMan — the cache
            // is server-authoritative and (re)populated by the sync RPC.
            // Returning whatever is cached (possibly empty until the response
            // lands) is correct: callers see "no shrines yet" rather than a
            // misleading partial slice.
            if (ZNet.instance != null && !ZNet.instance.IsServer())
                return _cache;

            if (Time.realtimeSinceStartup - _cacheTimestamp > CacheTTLSeconds || _cache.Count == 0)
                Refresh();
            return _cache;
        }

        /// <summary>Force the next <see cref="GetAll"/> call to rescan.</summary>
        public static void Invalidate() => _cacheTimestamp = 0f;

        /// <summary>
        /// Replace the cache with a server-provided shrine list. Called by
        /// <c>WayshrineSyncRPC</c> on dedicated clients when the authoritative
        /// list arrives from the server.
        /// </summary>
        public static void ApplyServerList(IList<Entry> entries)
        {
            _cache.Clear();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++) _cache.Add(entries[i]);
            }
            _cacheTimestamp = Time.realtimeSinceStartup;
        }

        /// <summary>True if a shrine with this id currently exists in the world.</summary>
        public static bool Contains(string shrineId)
        {
            if (string.IsNullOrEmpty(shrineId)) return false;
            var all = GetAll();
            for (int i = 0; i < all.Count; i++)
                if (all[i].ShrineId == shrineId) return true;
            return false;
        }

        public static bool TryGet(string shrineId, out Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(shrineId)) return false;
            var all = GetAll();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].ShrineId == shrineId) { entry = all[i]; return true; }
            }
            return false;
        }

        public static void Refresh()
        {
            _cache.Clear();
            _cacheTimestamp = Time.realtimeSinceStartup;
            if (ZDOMan.instance == null) return;

            var temp = new List<ZDO>();
            foreach (var prefabName in PrefabNames)
            {
                temp.Clear();
                int index = 0;
                // Iterative API: returns true when finished. Loop until done.
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefabName, temp, ref index)) { }

                foreach (var zdo in temp)
                {
                    if (zdo == null || !zdo.IsValid()) continue;
                    var pos = zdo.GetPosition();
                    var rot = zdo.GetRotation();
                    string name = zdo.GetString("Piece.m_name");
                    if (string.IsNullOrEmpty(name)) name = zdo.GetString("VAWayshrine_displayName"); // legacy
                    if (string.IsNullOrEmpty(name)) name = prefabName;

                    _cache.Add(new Entry
                    {
                        Id          = zdo.m_uid,
                        PrefabName  = prefabName,
                        Position    = pos,
                        Rotation    = rot,
                        DisplayName = name,
                        ShrineId    = WayshrineDiscovery.MakeShrineId(prefabName, pos),
                    });
                }
            }
        }

        /// <summary>
        /// Prunes any shrine ids from the local player's discovery set that
        /// no longer exist in the world. Returns the number removed. Cheap
        /// to call repeatedly; only writes to customData when there's actual
        /// drift.
        ///
        /// HOST-ONLY. <see cref="ZDOMan"/> is only authoritative for the FULL
        /// world ZDO set on a peer that runs the simulation — singleplayer or
        /// listen-server (both report <c>ZNet.IsServer() == true</c>). On a
        /// dedicated CLIENT, ZDOMan only contains ZDOs that have streamed to
        /// the client (typically a few zones around the player's current
        /// position). Running the prune there sees a tiny slice of the world,
        /// concludes that every discovered shrine outside those few zones no
        /// longer exists, and silently wipes the player's discovery list —
        /// which then persists to disk on the next save. That's the "no map
        /// markers after relog" report.
        ///
        /// Hammer-destruction by the local player is handled live by
        /// <see cref="Wayshrine.CleanupOnDestruction"/> (calls
        /// <see cref="WayshrineDiscovery.RemoveDiscovered"/>). Destruction by
        /// another peer while we were offline is the only edge case this
        /// prune was originally designed to catch, and we now handle that
        /// lazily: a teleport attempt to a missing shrine prompts the user
        /// and removes the entry on demand. Better to keep stale ids than to
        /// nuke real ones.
        /// </summary>
        public static int PruneStaleDiscovery(Player player)
        {
            if (player == null) return 0;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return 0;

            var ids = WayshrineDiscovery.GetAllDiscovered(player);
            if (ids.Count == 0) return 0;

            var all = GetAll();
            // Defensive: even on a host, an empty registry result during
            // very early world-load is a "not-yet-populated" state, not a
            // legitimate "zero shrines exist" state. Skip prune in that
            // case so we don't accidentally race the chunk-stream and wipe
            // discoveries on a host with momentary empty results.
            if (all.Count == 0) return 0;

            var live = new HashSet<string>(all.Count);
            for (int i = 0; i < all.Count; i++) live.Add(all[i].ShrineId);

            int removed = 0;
            foreach (var id in ids)
            {
                if (!live.Contains(id))
                {
                    if (WayshrineDiscovery.RemoveDiscovered(player, id))
                    {
                        WayshrineMapPins.RemovePinForShrine(id);
                        removed++;
                    }
                }
            }
            if (removed > 0)
                FiresLogger.LogInfo($"[Wayshrine] Pruned {removed} stale discovery entries (shrines no longer in world).");
            return removed;
        }
    }
}
