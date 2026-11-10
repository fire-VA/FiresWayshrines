using HarmonyLib;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines.Patches
{
    /// <summary>
    /// On local player spawn, kick off the wayshrine sync flow.
    ///
    /// The actual work runs through <see cref="WayshrineSyncRPC.OnLocalPlayerSpawned"/>,
    /// which branches on topology:
    ///   • Singleplayer / listen-host → scan ZDOMan locally, validate, rebuild pins.
    ///   • Dedicated client            → request the authoritative shrine list
    ///                                   from the server, apply + validate +
    ///                                   rebuild pins on response.
    ///
    /// We use <c>save: false</c> when adding pins so they don't accumulate in
    /// the map's persistent store across sessions — instead we re-add them
    /// each session, this time from a server-validated list rather than the
    /// streamed-zone slice, which misses shrines in unloaded zones.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class PlayerSpawnedPinRebuildPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;

            // Verbose-only trace of the login path.
            FiresLogger.LogVerbose("[LoginFreeze][DIAG] ENTER PlayerSpawnedPinRebuildPatch.Postfix");

            // Defer one frame so Minimap.instance + ZRoutedRpc.instance are
            // guaranteed to be live. The sync flow is host-fast / client-async
            // — see WayshrineSyncRPC.OnLocalPlayerSpawned for the branching.
            __instance.StartCoroutine(KickOffSyncNextFrame());

            // Minimal heartbeat (1s/5s/30s ticks) to confirm main thread is alive
            // past those checkpoints. ENTER/EXIT logs on each postfix and on
            // PlayerSpawnGate callbacks are what actually localise the freeze.
            __instance.StartCoroutine(LoginFreezeHeartbeat());

            // Verbose-only trace of the login path.
            FiresLogger.LogVerbose("[LoginFreeze][DIAG] EXIT PlayerSpawnedPinRebuildPatch.Postfix");
        }

        private static System.Collections.IEnumerator KickOffSyncNextFrame()
        {
            yield return null;
            WayshrineSyncRPC.OnLocalPlayerSpawned();
        }

        private static System.Collections.IEnumerator LoginFreezeHeartbeat()
        {
            // Verbose-only "main thread alive" marker after spawn. With the ENTER/EXIT
            // lines around each spawn step it shows whether a login stall happened and
            // roughly how long it lasted.
            FiresLogger.LogVerbose("[LoginFreeze][DIAG] heartbeat START (Player.OnSpawned postfix)");
            yield return new WaitForSecondsRealtime(1f);
            FiresLogger.LogVerbose("[LoginFreeze][DIAG] heartbeat +1s — main thread alive");
            yield return new WaitForSecondsRealtime(4f);
            FiresLogger.LogVerbose("[LoginFreeze][DIAG] heartbeat +5s — main thread alive");
            yield return new WaitForSecondsRealtime(25f);
            FiresLogger.LogVerbose("[LoginFreeze][DIAG] heartbeat +30s — main thread alive (diagnostic window closing)");
        }
    }

}
