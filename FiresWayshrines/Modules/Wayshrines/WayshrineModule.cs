using FiresWayshrines;
using FiresCore.Logging;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Entry point for the Wayshrines module. Binds config, attaches the
    /// <see cref="Wayshrine"/> MonoBehaviour to any wayshrine prefab(s) the
    /// asset bundle exposes, and adds them to the build piece list so they
    /// register through <c>VAPieceManager.AddPiecesToHammer()</c>.
    ///
    /// Harmony patches in <c>Modules/Wayshrines/Patches/</c> are picked up
    /// automatically by <c>harmony.PatchAll()</c> in Ascend.cs.
    /// </summary>
    public static class WayshrineModule
    {
        private static bool _initialized;

        // Naming convention for wayshrine prefabs in the asset bundle.
        // Anything matching one of these gets the Wayshrine MonoBehaviour
        // attached and is registered as a build piece.
        private static readonly string[] PrefabPrefixes = { "Wayshrine", "wayshrine" };

        public static void Init(ConfigFile cfg)
        {
            if (_initialized) return;
            _initialized = true;

            WayshrineConfig.Bind(cfg);
            FiresLogger.LogInfo("[Wayshrine] Module initialized.");
        }

        /// <summary>
        /// Called from the asset-bundle prefab walk (after the bundle is
        /// loaded but before pieces register). Examines each prefab and, if
        /// it looks like a wayshrine prefab, attaches the Wayshrine
        /// MonoBehaviour and adds it to <c>FiresWayshrinesPlugin.piecePrefabs</c>.
        /// </summary>
        public static void RegisterPrefabsFromBundle(IEnumerable<GameObject> bundlePrefabs)
        {
            if (bundlePrefabs == null) return;
            int registered = 0;
            foreach (var prefab in bundlePrefabs)
            {
                if (prefab == null) continue;
                if (!IsWayshrinePrefab(prefab)) continue;
                if (AttachComponent(prefab))
                    registered++;
            }
            if (registered > 0)
                FiresLogger.LogInfo($"[Wayshrine] Registered {registered} wayshrine prefab(s).");
        }

        /// <summary>
        /// Manual registration entry point — used when a single prefab needs
        /// the Wayshrine component attached and registered as a build piece.
        /// </summary>
        public static bool AttachComponent(GameObject prefab)
        {
            if (prefab == null) return false;
            if (prefab.GetComponent<Wayshrine>() == null)
                prefab.AddComponent<Wayshrine>();
            HardenAgainstWeather(prefab);

            // Mirror the existing piece-registration pattern (VAPieceManager
            // expects it on the FiresWayshrinesPlugin.piecePrefabs list).
            if (!FiresWayshrinesPlugin.piecePrefabs.Contains(prefab))
                FiresWayshrinesPlugin.piecePrefabs.Add(prefab);

            FiresLogger.LogVerbose($"[Wayshrine] Attached Wayshrine component to '{prefab.name}'.");
            return true;
        }

        /// <summary>
        /// Wayshrines are network infrastructure, so weather must never wear them down. Valheim 1.0's Deep North
        /// buries unimmune pieces under heavy snow, the Ashlands burn them, rain wears unroofed ones, and deep snow
        /// forbids placing ordinary pieces at all. The wear flag names read backwards: TRUE enables wear. With
        /// support wear off, heavy snow can't hurt the piece even under the AllHeavySnow world modifier.
        /// </summary>
        private static void HardenAgainstWeather(GameObject prefab)
        {
            if (prefab.TryGetComponent(out Piece piece))
                piece.m_allowedInDeepSnow = true;
            if (!prefab.TryGetComponent(out WearNTear wear))
                return;
            wear.m_noRoofWear = false;
            wear.m_noSupportWear = false;
            wear.m_snowDamageImmune = true;
            wear.m_ashDamageImmune = true;
        }

        private static bool IsWayshrinePrefab(GameObject prefab)
        {
            if (prefab == null || string.IsNullOrEmpty(prefab.name)) return false;
            for (int i = 0; i < PrefabPrefixes.Length; i++)
            {
                if (prefab.name.StartsWith(PrefabPrefixes[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
