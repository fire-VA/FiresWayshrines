using HarmonyLib;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines.Patches
{
    /// <summary>
    /// Catches wayshrine destruction that doesn't go through the hammer-remove
    /// flow (and therefore doesn't fire <see cref="IRemoved.OnRemoved"/>).
    /// Specifically: <see cref="WearNTear.Destroy"/> from damage and any other
    /// caller of <see cref="ZNetScene.Destroy(GameObject)"/>.
    ///
    /// Scene unloads do NOT pass through ZNetScene.Destroy — they go through
    /// <c>ZNetScene.RemoveObjects</c> which calls <see cref="Object.Destroy"/>
    /// directly on the GameObject and only destroys the ZDO if it's
    /// non-persistent. Wayshrines are persistent, so unloads won't trigger
    /// our cleanup, which is what we want.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Destroy")]
    internal static class WayshrineDestructionPatch
    {
        [HarmonyPrefix]
        private static void Prefix(GameObject go)
        {
            if (go == null) return;
            var shrine = go.GetComponent<Wayshrine>();
            if (shrine == null) return;
            shrine.CleanupOnDestruction("damage / network destroy");
        }
    }
}
