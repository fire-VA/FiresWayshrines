using FiresCore.Logging;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Server-authoritative sync of the wayshrine world list to clients.
    ///
    /// Why this exists
    /// ---------------
    /// On a dedicated client, <see cref="ZDOMan"/> only contains ZDOs that have
    /// streamed to the local peer — typically a few zones around the player's
    /// current position. The previous client-side
    /// <see cref="WayshrineWorldRegistry.Refresh"/> walked
    /// <c>ZDOMan.GetAllZDOsWithPrefabIterative</c>, which on dedicated returned
    /// only the tiny streamed slice. That broke two flows:
    ///
    ///   1. <see cref="WayshrineMapPins.RebuildPinsForLocalPlayer"/> — couldn't
    ///      place pins for discovered shrines whose zones weren't loaded.
    ///   2. <see cref="WayshrineWorldRegistry.PruneStaleDiscovery"/> — silently
    ///      wiped every discovery whose shrine wasn't in the streamed window
    ///      (the "no map markers after relog" report).
    ///
    /// The fix: server (always authoritative for the full ZDO map) scans its
    /// own ZDOMan and ships the result to clients on request. Clients apply it
    /// via <see cref="WayshrineWorldRegistry.ApplyServerList"/>, validate the
    /// player's discovery set against the authoritative list, and rebuild
    /// pins.
    ///
    /// Topology behaviour
    /// ------------------
    ///   Singleplayer / listen-host : <c>ZNet.IsServer() == true</c>.
    ///                                We scan locally, no RPC round-trip.
    ///   Dedicated client            : sends a request, applies on response.
    ///   Dedicated server            : registers the response handler, replies
    ///                                 to each client's request individually.
    ///
    /// Trigger points
    /// --------------
    ///   <see cref="OnLocalPlayerSpawned"/> — called by
    ///   <c>PlayerSpawnedPinRebuildPatch</c> after the local player has spawned
    ///   and the chunk-stream has had a moment to settle.
    ///
    /// What we ship
    /// ------------
    /// One entry per shrine: ZDOID + prefab name + position + rotation +
    /// current display name (Piece.m_name override). ShrineId is recomputed
    /// on the receiving side so MakeShrineId stays the single source of truth.
    /// Bandwidth: ~50 bytes per shrine, so a world with 100 shrines is ~5KB —
    /// negligible. Sent only at login (and on demand for admin scripts).
    /// </summary>
    [HarmonyPatch]
    public static class WayshrineSyncRPC
    {
        private const string RPC_RequestShrineList  = "FiresRPGmaker_WayshrineList_Request";
        private const string RPC_ShrineListResponse = "FiresRPGmaker_WayshrineList_Response";

        // ────────────────────────────────────────────────────────────────────
        //  RPC registration
        //
        //  Both peers register both RPC names. Branching on ZNet.IsServer()
        //  inside each handler keeps the registration symmetric (matches
        //  AdminSyncing's pattern in this mod).
        // ────────────────────────────────────────────────────────────────────

        [HarmonyPatch(typeof(ZNet), "Start")]
        [HarmonyPostfix]
        private static void ZNet_Start_Postfix()
        {
            if (ZRoutedRpc.instance == null) return;

            ZRoutedRpc.instance.Register<ZPackage>(
                RPC_RequestShrineList,
                new System.Action<long, ZPackage>(OnRequestShrineList));

            ZRoutedRpc.instance.Register<ZPackage>(
                RPC_ShrineListResponse,
                new System.Action<long, ZPackage>(OnShrineListResponse));
        }

        // ────────────────────────────────────────────────────────────────────
        //  Public entry point — called from PlayerSpawnedPinRebuildPatch
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Trigger point for the spawn-time sync. Branches on topology: hosts
        /// scan locally and rebuild immediately; dedicated clients send a
        /// request and let <see cref="OnShrineListResponse"/> do the rebuild
        /// when the response lands.
        /// </summary>
        public static void OnLocalPlayerSpawned()
        {
            if (ZNet.instance == null) return;

            if (ZNet.instance.IsServer())
            {
                // Host path — scan ZDOMan, rebuild pins, validate discovery.
                // No RPC needed: we ARE the source of truth.
                WayshrineWorldRegistry.Invalidate();
                _ = WayshrineWorldRegistry.GetAll(); // forces Refresh() (host branch in GetAll)
                ValidateAndRebuild();
                return;
            }

            // Dedicated client — ask the server for the authoritative list.
            // ApplyServerList + ValidateAndRebuild run when the response lands.
            var serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer == null || ZRoutedRpc.instance == null) return;

            var pkg = new ZPackage();
            ZRoutedRpc.instance.InvokeRoutedRPC(
                serverPeer.m_uid, RPC_RequestShrineList, pkg);

            FiresLogger.LogInfo("[Wayshrine] Requested authoritative shrine list from server.");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Server: respond to a client's request
        // ────────────────────────────────────────────────────────────────────

        private static void OnRequestShrineList(long sender, ZPackage requestPkg)
        {
            // Hosts only — clients ignore incoming requests (defensive: a
            // misbehaving peer can't spoof a request to another client because
            // the routed RPC always lands on every registered handler, and
            // we filter here).
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (ZRoutedRpc.instance == null) return;

            // Force a fresh ZDOMan scan so the response reflects current world
            // state — handy after admin renames / deletions.
            WayshrineWorldRegistry.Invalidate();
            var entries = WayshrineWorldRegistry.GetAll();

            var responsePkg = SerializeEntries(entries);
            ZRoutedRpc.instance.InvokeRoutedRPC(
                sender, RPC_ShrineListResponse, responsePkg);

            FiresLogger.LogInfo($"[Wayshrine] Sent shrine list ({entries.Count} entries) to peer {sender}.");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Client: receive list, apply, validate, rebuild
        // ────────────────────────────────────────────────────────────────────

        private static void OnShrineListResponse(long sender, ZPackage responsePkg)
        {
            // Hosts ignore self-response — they already populated via local
            // scan. The Routed RPC infrastructure self-loops on a host so
            // without this guard we'd double-rebuild.
            if (ZNet.instance == null || ZNet.instance.IsServer()) return;

            var entries = DeserializeEntries(responsePkg);
            WayshrineWorldRegistry.ApplyServerList(entries);

            FiresLogger.LogInfo($"[Wayshrine] Received authoritative shrine list ({entries.Count} entries) from server.");

            // DIAG (login-freeze investigation): bracket ValidateAndRebuild so the
            // next repro log shows whether the freeze is inside the validate/rebuild
            // path or in whatever runs after this RPC handler returns. Remove once
            // the freeze is fixed.
            Debug.Log("[Wayshrine][DIAG] ENTER ValidateAndRebuild");
            ValidateAndRebuild();
            Debug.Log("[Wayshrine][DIAG] EXIT ValidateAndRebuild (OnShrineListResponse returning)");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Common: validate discovery set + rebuild pins
        //
        //  Now safe on every topology: PruneStaleDiscovery refuses to run on
        //  dedicated clients (its ZDOMan-scan is unreliable there), but with
        //  the registry now populated server-authoritatively we can run a
        //  parallel pruning pass right here using the freshly-applied data.
        // ────────────────────────────────────────────────────────────────────

        private static void ValidateAndRebuild()
        {
            Debug.Log("[Wayshrine][DIAG] ValidateAndRebuild: enter, resolving local player…");
            var player = Player.m_localPlayer;
            if (player != null)
            {
                // Host path: PruneStaleDiscovery does the work using ZDOMan.
                // Client path: PruneStaleDiscovery is a no-op (it bails on
                //              !IsServer); we run our own server-list pass.
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    Debug.Log("[Wayshrine][DIAG] ValidateAndRebuild: host branch — PruneStaleDiscovery");
                    WayshrineWorldRegistry.PruneStaleDiscovery(player);
                }
                else
                {
                    Debug.Log("[Wayshrine][DIAG] ValidateAndRebuild: client branch — PruneAgainstServerList ENTER");
                    PruneAgainstServerList(player);
                    Debug.Log("[Wayshrine][DIAG] ValidateAndRebuild: client branch — PruneAgainstServerList EXIT");
                }
            }
            else
            {
                Debug.Log("[Wayshrine][DIAG] ValidateAndRebuild: m_localPlayer is null — skipping prune");
            }

            Debug.Log("[Wayshrine][DIAG] ValidateAndRebuild: calling RebuildPinsForLocalPlayer");
            WayshrineMapPins.RebuildPinsForLocalPlayer();
            Debug.Log("[Wayshrine][DIAG] ValidateAndRebuild: RebuildPinsForLocalPlayer returned");
        }

        /// <summary>
        /// Client-side prune that uses the server-applied registry as the
        /// ground truth. Mirrors <see cref="WayshrineWorldRegistry.PruneStaleDiscovery"/>
        /// but with a different name to make it explicit that this only runs
        /// AFTER the authoritative list has landed.
        /// </summary>
        private static void PruneAgainstServerList(Player player)
        {
            var ids = WayshrineDiscovery.GetAllDiscovered(player);
            if (ids.Count == 0) return;

            var all = WayshrineWorldRegistry.GetAll();
            // If the server response somehow gave us zero entries, treat that
            // as "no data, don't trust" rather than "world has zero shrines."
            // Better to keep stale ids than to nuke real ones.
            if (all.Count == 0) return;

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
                FiresLogger.LogInfo($"[Wayshrine] Server-validated discovery: removed {removed} stale entries.");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Wire format
        //
        //  count:int + per-entry { ZDOID, string prefab, Vector3 pos,
        //                          Quaternion rot, string displayName }
        //
        //  ShrineId is NOT shipped — it's deterministically derived from
        //  prefab + pos via WayshrineDiscovery.MakeShrineId, so keeping that
        //  helper as the only place ShrineIds are minted means a future
        //  format change to MakeShrineId is a single-file edit.
        // ────────────────────────────────────────────────────────────────────

        private static ZPackage SerializeEntries(IReadOnlyList<WayshrineWorldRegistry.Entry> entries)
        {
            var pkg = new ZPackage();
            pkg.Write(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                pkg.Write(e.Id);
                pkg.Write(e.PrefabName ?? string.Empty);
                pkg.Write(e.Position);
                pkg.Write(e.Rotation);
                pkg.Write(e.DisplayName ?? string.Empty);
            }
            return pkg;
        }

        private static List<WayshrineWorldRegistry.Entry> DeserializeEntries(ZPackage pkg)
        {
            int count = pkg.ReadInt();
            var result = new List<WayshrineWorldRegistry.Entry>(count);
            for (int i = 0; i < count; i++)
            {
                var id = pkg.ReadZDOID();
                var prefabName = pkg.ReadString();
                var pos = pkg.ReadVector3();
                var rot = pkg.ReadQuaternion();
                var name = pkg.ReadString();
                result.Add(new WayshrineWorldRegistry.Entry
                {
                    Id          = id,
                    PrefabName  = prefabName,
                    Position    = pos,
                    Rotation    = rot,
                    DisplayName = name,
                    ShrineId    = WayshrineDiscovery.MakeShrineId(prefabName, pos),
                });
            }
            return result;
        }
    }
}
