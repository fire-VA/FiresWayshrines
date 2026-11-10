using FiresCore.Logging;
using FiresCore.Net;
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
    /// current position. A client-side
    /// <see cref="WayshrineWorldRegistry.Refresh"/> walking
    /// <c>ZDOMan.GetAllZDOsWithPrefabIterative</c> would see only that
    /// small streamed slice, which breaks two flows:
    ///
    ///   1. <see cref="WayshrineMapPins.RebuildPinsForLocalPlayer"/> — could not
    ///      place pins for discovered shrines whose zones weren't loaded.
    ///   2. <see cref="WayshrineWorldRegistry.PruneStaleDiscovery"/> — would
    ///      wipe every discovery whose shrine is outside the streamed window.
    ///
    /// So the server (always authoritative for the full ZDO map) scans its
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
        private const string RPC_RequestWipe        = "FiresWayshrines_WipeRequest";   // admin client -> server
        private const string RPC_WayshrineWipe      = "FiresWayshrines_Wipe";          // server -> all clients
        private const string RPC_SettingsChanged    = "FiresWayshrines_SettingsChanged"; // admin client -> server
        private const float ZdoPropagationDelaySeconds = 2f;
        private const int MaxListEntries = 4096;
        private const int MinEntryBytes = 50;   // ZDOID 12 + vec3 12 + quat 16 + int 4 + float 4 + bools/strings
        private const float ListRequestCooldownSeconds = 10f;
        private static readonly Dictionary<long, float> _lastListRequest = new Dictionary<long, float>();

        // Server-to-client traffic must come from the server peer (a routed RPC can be sent by any peer).
        private static bool IsFromServer(long sender)
        {
            if (ZRoutedRpc.instance == null) return false;
            long serverId = ZRoutedRpc.instance.GetServerPeerID();
            return serverId != 0L && sender == serverId;
        }

        private static bool _listBroadcastPending;

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

            ZRoutedRpc.instance.Register<ZPackage>(
                RPC_RequestWipe,
                new System.Action<long, ZPackage>(OnRequestWipe));

            ZRoutedRpc.instance.Register<ZPackage>(
                RPC_WayshrineWipe,
                new System.Action<long, ZPackage>(OnWayshrineWipe));

            ZRoutedRpc.instance.Register<ZPackage>(
                RPC_SettingsChanged,
                new System.Action<long, ZPackage>(OnSettingsChanged));
            _listBroadcastPending = false;
            _settingsSnapshot.Clear();
            _adminEditUntil = 0f;
            if (ZNet.instance != null && ZNet.instance.IsServer())
                ZNet.instance.StartCoroutine(SettingsGuardLoop());
        }

        // ────────────────────────────────────────────────────────────────────
        //  Server guard for admin per-shrine settings.
        //  The settings are ZDO fields any owner can write, so the server keeps a
        //  snapshot and reverts changes that did not come with an admin's
        //  SettingsChanged / wipe (an admin edit opens a short accept window).
        // ────────────────────────────────────────────────────────────────────

        private struct ShrineSettings
        {
            public int Cost; public float Cooldown; public bool AutoDiscover, AdminOnly, GroupParent; public int Epoch; public string Group;
            public bool SameAs(ShrineSettings o) =>
                Cost == o.Cost && Cooldown == o.Cooldown && AutoDiscover == o.AutoDiscover && AdminOnly == o.AdminOnly &&
                GroupParent == o.GroupParent && Epoch == o.Epoch && string.Equals(Group ?? "", o.Group ?? "");
        }

        private const float SettingsGuardInterval = 5f;
        private const float AdminEditWindowSeconds = 15f;
        private static readonly Dictionary<ZDOID, ShrineSettings> _settingsSnapshot = new Dictionary<ZDOID, ShrineSettings>();
        private static float _adminEditUntil;

        private static void MarkAdminEdit() => _adminEditUntil = UnityEngine.Time.realtimeSinceStartup + AdminEditWindowSeconds;

        private static ShrineSettings ReadSettings(ZDO z) => new ShrineSettings
        {
            Cost = z.GetInt(Wayshrine.ZdoCostOverride, -1),
            Cooldown = z.GetFloat(Wayshrine.ZdoCooldownSeconds, 0f),
            AutoDiscover = z.GetBool(Wayshrine.ZdoAutoDiscover, false),
            AdminOnly = z.GetBool(Wayshrine.ZdoAdminOnly, false),
            GroupParent = z.GetBool(Wayshrine.ZdoGroupParent, false),
            Epoch = z.GetInt(Wayshrine.ZdoDiscoveryEpoch, 0),
            Group = z.GetString(Wayshrine.ZdoGroup, ""),
        };

        private static void WriteSettings(ZDO z, ShrineSettings s)
        {
            z.Set(Wayshrine.ZdoCostOverride, s.Cost);
            z.Set(Wayshrine.ZdoCooldownSeconds, s.Cooldown);
            z.Set(Wayshrine.ZdoAutoDiscover, s.AutoDiscover);
            z.Set(Wayshrine.ZdoAdminOnly, s.AdminOnly);
            z.Set(Wayshrine.ZdoGroupParent, s.GroupParent);
            z.Set(Wayshrine.ZdoDiscoveryEpoch, s.Epoch);
            z.Set(Wayshrine.ZdoGroup, s.Group ?? "");
        }

        private static System.Collections.IEnumerator SettingsGuardLoop()
        {
            var wait = new WaitForSeconds(SettingsGuardInterval);
            while (ZNet.instance != null && ZNet.instance.IsServer())
            {
                yield return wait;
                try { GuardSettingsOnce(); }
                catch (System.Exception ex) { FiresLogger.LogWarning($"[Wayshrine] settings guard: {ex.Message}"); }
            }
        }

        private static void GuardSettingsOnce()
        {
            if (ZDOMan.instance == null) return;
            bool accept = UnityEngine.Time.realtimeSinceStartup < _adminEditUntil;
            var entries = WayshrineWorldRegistry.GetAll();
            var seen = new HashSet<ZDOID>();
            for (int i = 0; i < entries.Count; i++)
            {
                var id = entries[i].Id;
                var z = ZDOMan.instance.GetZDO(id);
                if (z == null) continue;
                seen.Add(id);
                var cur = ReadSettings(z);
                if (accept || !_settingsSnapshot.TryGetValue(id, out var snap)) { _settingsSnapshot[id] = cur; continue; }
                if (cur.SameAs(snap)) continue;
                FiresLogger.LogWarning($"[Wayshrine] Reverted non-admin change to shrine settings on {id} (owner {z.GetOwner()}).");
                z.SetOwner(ZDOMan.GetSessionID());
                WriteSettings(z, snap);
                WayshrineWorldRegistry.Invalidate();
            }
            if (_settingsSnapshot.Count > seen.Count)
            {
                var stale = new List<ZDOID>();
                foreach (var k in _settingsSnapshot.Keys) if (!seen.Contains(k)) stale.Add(k);
                for (int i = 0; i < stale.Count; i++) _settingsSnapshot.Remove(stale[i]);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  Admin setting changed → everyone's cached list refreshes
        //
        //  Dedicated clients hold a server list for UNLOADED shrines, fetched
        //  once at login. Without a push, a shrine an admin just moved into a
        //  dungeon group (or made admin-only) stayed reachable from that stale
        //  cache until the next relog. The admin's ZDO write reaches the server
        //  on ZDOMan's own schedule, so the server waits a moment before it
        //  rescans and pushes the list to every peer.
        // ────────────────────────────────────────────────────────────────────

        public static void AnnounceSettingsChanged()
        {
            if (ZNet.instance == null || ZRoutedRpc.instance == null) return;
            if (ZNet.instance.IsServer())
            {
                MarkAdminEdit();
                ScheduleListBroadcast();
                return;
            }
            var serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer != null)
                ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer.m_uid, RPC_SettingsChanged, new ZPackage());
        }

        private static void OnSettingsChanged(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!FiresCore.Sync.AdminSyncing.IsAdmin(sender)) { FiresLogger.LogWarning($"[Wayshrine] Refused settings change from non-admin {sender}."); return; }
            MarkAdminEdit();
            ScheduleListBroadcast();
        }

        private static void ScheduleListBroadcast()
        {
            if (_listBroadcastPending) return;
            _listBroadcastPending = true;
            ZNet.instance.StartCoroutine(BroadcastListAfterDelay());
        }

        private static System.Collections.IEnumerator BroadcastListAfterDelay()
        {
            yield return new WaitForSeconds(ZdoPropagationDelaySeconds);
            _listBroadcastPending = false;
            if (ZNet.instance == null || ZRoutedRpc.instance == null) yield break;

            WayshrineWorldRegistry.Invalidate();
            var entries = WayshrineWorldRegistry.GetAll();
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RPC_ShrineListResponse, SerializeEntries(entries));
            if (Player.m_localPlayer != null)
                ValidateAndRebuild();
            FiresLogger.LogInfo($"[Wayshrine] Pushed the shrine list ({entries.Count} entries) to every peer after an admin change.");
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
        //  Wipe map marker (admin)
        //
        //  Server-authoritative: the shrine's discovery "epoch" (a ZDO int) is
        //  bumped on the server. Online clients are told to clear immediately
        //  via a broadcast; offline clients self-heal on next login because the
        //  bumped epoch rides the shrine-list sync and the login prune clears
        //  any discovery stamped at an older epoch (see WipeStaleByEpoch).
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Admin-triggered from the context menu. Routes to the server, which performs the wipe.</summary>
        public static void RequestWipe(Wayshrine shrine)
        {
            if (shrine == null || ZNet.instance == null) return;
            var nview = shrine.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null) return;

            if (ZNet.instance.IsServer())
            {
                DoServerWipe(zdo.m_uid, shrine.ShrineId);
                return;
            }

            var serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer == null || ZRoutedRpc.instance == null) return;

            var pkg = new ZPackage();
            pkg.Write(zdo.m_uid);
            pkg.Write(shrine.ShrineId ?? string.Empty);
            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer.m_uid, RPC_RequestWipe, pkg);
            FiresLogger.LogInfo($"[Wayshrine] Requested marker wipe for '{shrine.DisplayName}' from server.");
        }

        private static void OnRequestWipe(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!FiresCore.Sync.AdminSyncing.IsAdmin(sender)) { FiresLogger.LogWarning($"[Wayshrine] Refused wipe request from non-admin {sender}."); return; }
            MarkAdminEdit();
            var zdoid = pkg.ReadZDOID();
            var shrineId = pkg.ReadString();
            DoServerWipe(zdoid, shrineId);
        }

        private static void DoServerWipe(ZDOID zdoid, string shrineId)
        {
            int next;

            // Prefer the live instance: BumpEpoch claims ZNetView ownership so the change persists +
            // replicates via vanilla ZDO sync. The shrine is loaded (an admin is standing at it), so
            // on a listen-host or dedicated server simulating that zone this is the path taken.
            var live = FindLiveByZdoid(zdoid);
            if (live != null)
            {
                next = live.BumpEpoch();
            }
            else
            {
                var zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(zdoid) : null;
                if (zdo == null) { FiresLogger.LogWarning($"[Wayshrine] Wipe: ZDO {zdoid} not found."); return; }
                next = zdo.GetInt(Wayshrine.ZdoDiscoveryEpoch, 0) + 1;
                zdo.Set(Wayshrine.ZdoDiscoveryEpoch, next);
                WayshrineWorldRegistry.Invalidate();
            }

            // Online clients clear immediately.
            var pkg = new ZPackage();
            pkg.Write(shrineId ?? string.Empty);
            pkg.Write(next);
            int sent = SafeRoutedRpc.InvokeSafeToAllExcept(0L, RPC_WayshrineWipe, pkg);

            // Listen-host is also a player — clear our own discovery directly (the broadcast skips the server).
            ApplyWipeLocally(shrineId, next);

            FiresLogger.LogInfo($"[Wayshrine] Wiped marker for shrine id='{shrineId}' epoch={next}; notified {sent} client(s).");
        }

        private static Wayshrine FindLiveByZdoid(ZDOID id)
        {
            var all = WayshrineRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var w = all[i];
                if (w == null) continue;
                var nv = w.GetComponent<ZNetView>();
                var z = nv != null ? nv.GetZDO() : null;
                if (z != null && z.m_uid == id) return w;
            }
            return null;
        }

        private static void OnWayshrineWipe(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer()) return;  // host handled itself in DoServerWipe
            if (!IsFromServer(sender)) { FiresLogger.LogWarning($"[Wayshrine] Dropped wipe from non-server sender {sender}."); return; }
            var shrineId = pkg.ReadString();
            int epoch = pkg.ReadInt();
            ApplyWipeLocally(shrineId, epoch);
        }

        private static void ApplyWipeLocally(string shrineId, int epoch)
        {
            var player = Player.m_localPlayer;
            if (player == null || string.IsNullOrEmpty(shrineId)) return;
            WayshrineDiscovery.RemoveDiscovered(player, shrineId);
            WayshrineDiscovery.SetDiscoveredEpoch(player, shrineId, epoch); // remember the wipe so re-discovery is allowed
            WayshrineMapPins.RemovePinForShrine(shrineId);
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

            // Security: per-sender cooldown so a client can't force a full ZDOMan scan + serialize on every packet.
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_lastListRequest.TryGetValue(sender, out float last) && now - last < ListRequestCooldownSeconds)
            {
                FiresLogger.LogWarning($"[Wayshrine] Shrine list request from {sender} throttled.");
                return;
            }
            if (_lastListRequest.Count > 512) _lastListRequest.Clear();
            _lastListRequest[sender] = now;

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
            if (!IsFromServer(sender)) { FiresLogger.LogWarning($"[Wayshrine] Dropped shrine list from non-server sender {sender}."); return; }

            var entries = DeserializeEntries(responsePkg);
            if (entries == null) { FiresLogger.LogWarning($"[Wayshrine] Dropped malformed shrine list from {sender}."); return; }
            WayshrineWorldRegistry.ApplyServerList(entries);

            FiresLogger.LogInfo($"[Wayshrine] Received authoritative shrine list ({entries.Count} entries) from server.");

            // Verbose-only trace around ValidateAndRebuild, so a log shows whether a
            // stall is inside the validate/rebuild path or after this handler returns.
            FiresLogger.LogVerbose("[Wayshrine][DIAG] ENTER ValidateAndRebuild");
            ValidateAndRebuild();
            FiresLogger.LogVerbose("[Wayshrine][DIAG] EXIT ValidateAndRebuild (OnShrineListResponse returning)");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Common: validate discovery set + rebuild pins
        //
        //  Safe on every topology: PruneStaleDiscovery refuses to run on
        //  dedicated clients (its ZDOMan-scan is unreliable there), but with
        //  the registry populated server-authoritatively we can run a
        //  parallel pruning pass right here using the freshly-applied data.
        // ────────────────────────────────────────────────────────────────────

        private static void ValidateAndRebuild()
        {
            FiresLogger.LogVerbose("[Wayshrine][DIAG] ValidateAndRebuild: enter, resolving local player…");
            var player = Player.m_localPlayer;
            if (player != null)
            {
                // Host path: PruneStaleDiscovery does the work using ZDOMan.
                // Client path: PruneStaleDiscovery is a no-op (it bails on
                //              !IsServer); we run our own server-list pass.
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    FiresLogger.LogVerbose("[Wayshrine][DIAG] ValidateAndRebuild: host branch — PruneStaleDiscovery");
                    WayshrineWorldRegistry.PruneStaleDiscovery(player);
                }
                else
                {
                    FiresLogger.LogVerbose("[Wayshrine][DIAG] ValidateAndRebuild: client branch — PruneAgainstServerList ENTER");
                    PruneAgainstServerList(player);
                    FiresLogger.LogVerbose("[Wayshrine][DIAG] ValidateAndRebuild: client branch — PruneAgainstServerList EXIT");
                }

                // Catch wipes that happened while this player was offline: any discovery stamped at an
                // epoch older than the shrine's current (synced) epoch is cleared before pins rebuild.
                WipeStaleByEpoch(player);
            }
            else
            {
                FiresLogger.LogVerbose("[Wayshrine][DIAG] ValidateAndRebuild: m_localPlayer is null — skipping prune");
            }

            FiresLogger.LogVerbose("[Wayshrine][DIAG] ValidateAndRebuild: calling RebuildPinsForLocalPlayer");
            WayshrineMapPins.RebuildPinsForLocalPlayer();
            FiresLogger.LogVerbose("[Wayshrine][DIAG] ValidateAndRebuild: RebuildPinsForLocalPlayer returned");
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

        /// <summary>
        /// Clears any discovery whose recorded epoch is older than the shrine's current epoch — i.e. the
        /// marker was wiped (possibly while this player was logged out). Works on every topology because
        /// the epoch rides the authoritative shrine list.
        /// </summary>
        private static void WipeStaleByEpoch(Player player)
        {
            var all = WayshrineWorldRegistry.GetAll();
            if (all.Count == 0) return;

            int removed = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e.Epoch <= 0) continue;
                if (!WayshrineDiscovery.HasDiscovered(player, e.ShrineId)) continue;
                if (WayshrineDiscovery.GetDiscoveredEpoch(player, e.ShrineId) >= e.Epoch) continue;

                WayshrineDiscovery.RemoveDiscovered(player, e.ShrineId);
                WayshrineDiscovery.SetDiscoveredEpoch(player, e.ShrineId, e.Epoch);
                WayshrineMapPins.RemovePinForShrine(e.ShrineId);
                removed++;
            }
            if (removed > 0)
                FiresLogger.LogInfo($"[Wayshrine] Wiped {removed} discovery marker(s) stale by epoch (admin map-marker wipe).");
        }

        // ────────────────────────────────────────────────────────────────────
        //  Wire format
        //
        //  count:int + per-entry { ZDOID, string prefab, Vector3 pos,
        //                          Quaternion rot, string displayName,
        //                          int epoch, bool autoDiscover, bool adminOnly,
        //                          float cooldown, string group, bool groupParent }
        //  Append-only; client and server must run the same build.
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
                pkg.Write(e.Epoch);
                pkg.Write(e.AutoDiscover);
                pkg.Write(e.AdminOnly);
                pkg.Write(e.Cooldown);
                pkg.Write(e.Group ?? string.Empty);
                pkg.Write(e.IsGroupParent);
            }
            return pkg;
        }

        private static List<WayshrineWorldRegistry.Entry> DeserializeEntries(ZPackage pkg)
        {
            int count = pkg.ReadInt();
            // Security: clamp the wire count and make sure the package can actually hold that many entries.
            if (count < 0 || count > MaxListEntries) return null;
            if ((long)count * MinEntryBytes > (long)(pkg.Size() - pkg.GetPos())) return null;
            var result = new List<WayshrineWorldRegistry.Entry>(count);
            for (int i = 0; i < count; i++)
            {
                var id = pkg.ReadZDOID();
                var prefabName = pkg.ReadString();
                var pos = pkg.ReadVector3();
                var rot = pkg.ReadQuaternion();
                var name = pkg.ReadString();
                int epoch = pkg.ReadInt();
                bool autoDiscover = pkg.ReadBool();
                bool adminOnly = pkg.ReadBool();
                float cooldown = pkg.ReadSingle();
                string group = WayshrineGroups.Normalize(pkg.ReadString());
                bool isGroupParent = pkg.ReadBool();
                result.Add(new WayshrineWorldRegistry.Entry
                {
                    Id           = id,
                    PrefabName   = prefabName,
                    Position     = pos,
                    Rotation     = rot,
                    DisplayName  = name,
                    ShrineId     = WayshrineDiscovery.MakeShrineId(prefabName, pos),
                    Epoch        = epoch,
                    AutoDiscover = autoDiscover,
                    AdminOnly    = adminOnly,
                    Cooldown     = cooldown,
                    Group        = group,
                    IsGroupParent = isGroupParent,
                });
            }
            return result;
        }
    }
}
