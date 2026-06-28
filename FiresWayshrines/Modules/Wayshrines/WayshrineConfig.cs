using FiresCore.Logging;
using BepInEx.Configuration;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// BepInEx configuration for the Wayshrines module. All values are
    /// adjustable from the BepInEx config file without recompiling.
    /// </summary>
    public static class WayshrineConfig
    {
        // --- Discovery ---
        public static ConfigEntry<float> DiscoveryRange;

        // --- Cost ---
        public static ConfigEntry<float> CoinsPerMeter;
        public static ConfigEntry<int> MinimumCost;
        public static ConfigEntry<bool> WaiveCostInCreative;

        // --- Cooldown abuse-prevention multiplier ---
        public static ConfigEntry<float> CooldownWindowSeconds;
        public static ConfigEntry<float> CooldownExponent;

        // --- Teleport rules ---
        public static ConfigEntry<bool> RespectNoTeleportItems;
        public static ConfigEntry<bool> RespectNoPortalsKey;
        public static ConfigEntry<bool> RespectBossLockout;

        // --- Map UI ---
        public static ConfigEntry<bool> ShowUndiscoveredAsLocked;

        public static void Bind(ConfigFile cfg)
        {
            const string SEC_DISCO = "Wayshrines.Discovery";
            const string SEC_COST = "Wayshrines.Cost";
            const string SEC_COOLDOWN = "Wayshrines.Cooldown";
            const string SEC_RULES = "Wayshrines.Rules";
            const string SEC_MAP = "Wayshrines.Map";

            DiscoveryRange = cfg.Bind(SEC_DISCO, "DiscoveryRange", 20f,
                "Distance (in meters) at which a player automatically discovers a wayshrine they walk near.");

            CoinsPerMeter = cfg.Bind(SEC_COST, "CoinsPerMeter", 0.25f,
                "Base coin cost per meter of travel. 0.25 = 1 km costs 250 coins, 4 km costs 1000.");
            MinimumCost = cfg.Bind(SEC_COST, "MinimumCost", 25,
                "Minimum coin cost for any teleport, regardless of distance.");
            WaiveCostInCreative = cfg.Bind(SEC_COST, "WaiveCostInCreative", true,
                "If true, NoCraftCost (creative-mode) global key disables the coin cost.");

            CooldownWindowSeconds = cfg.Bind(SEC_COOLDOWN, "WindowSeconds", 600f,
                "Sliding window (in seconds) within which recent teleports raise the cost multiplier.");
            CooldownExponent = cfg.Bind(SEC_COOLDOWN, "Exponent", 1.5f,
                "Exponent applied to recent-teleport count: multiplier = 1 + (count ^ exponent). " +
                "Default 1.5 -> 1st free, 2nd ~2x, 3rd ~3.8x, 4th ~6.2x, 5th ~9x.");

            RespectNoTeleportItems = cfg.Bind(SEC_RULES, "RespectNoTeleportItems", true,
                "If true, carrying an unteleportable item (ore etc.) blocks wayshrine use, like vanilla portals.");
            RespectNoPortalsKey = cfg.Bind(SEC_RULES, "RespectNoPortalsKey", true,
                "If true, the NoPortals world-modifier global key disables wayshrines too.");
            RespectBossLockout = cfg.Bind(SEC_RULES, "RespectBossLockout", true,
                "If true, NoBossPortals + active boss event blocks wayshrines.");

            ShowUndiscoveredAsLocked = cfg.Bind(SEC_MAP, "ShowUndiscoveredAsLocked", false,
                "If true, undiscovered wayshrines show as faded locked icons on the map. " +
                "If false (default), undiscovered wayshrines are completely hidden.");

            // Live-toggle: when the player flips the config in the BepInEx
            // ConfigManager UI mid-session, rebuild pins immediately so
            // locked shrines appear/disappear without needing a relog.
            ShowUndiscoveredAsLocked.SettingChanged += (_, __) =>
            {
                try { WayshrineMapPins.RebuildPinsForLocalPlayer(); }
                catch (System.Exception ex)
                {
                    FiresLogger.LogWarning($"[Wayshrine] Pin rebuild on config toggle failed: {ex.Message}");
                }
            };
        }
    }
}
