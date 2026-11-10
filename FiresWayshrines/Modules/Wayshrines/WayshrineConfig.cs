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

        // --- Logging ---
        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<bool> SpreadShrineScan;

        public static void Bind(ConfigFile cfg)
        {
            const string SEC_GENERAL = "General";
            const string SEC_DISCO = "Wayshrines.Discovery";
            const string SEC_COST = "Wayshrines.Cost";
            const string SEC_COOLDOWN = "Wayshrines.Cooldown";
            const string SEC_RULES = "Wayshrines.Rules";
            const string SEC_MAP = "Wayshrines.Map";

            VerboseLogging = cfg.Bind(SEC_GENERAL, "Verbose Logging", false,
                Hid("Write extra debug lines to the log."));
            SpreadShrineScan = cfg.Bind(SEC_GENERAL, "Spread shrine scan over frames", true,
                Adv("Perf (server/host): the 5 s shrine list refresh walks the world over several frames (about 1 ms each) instead of in one frame. Off = the old one-frame scan."));

            DiscoveryRange = cfg.Bind(SEC_DISCO, "DiscoveryRange", 20f,
                Adv("How close (meters) you must walk to a wayshrine to discover it."));

            CoinsPerMeter = cfg.Bind(SEC_COST, "CoinsPerMeter", 0.25f,
                Adv("Coins per meter of travel. 0.25 means 1 km costs 250 coins."));
            MinimumCost = cfg.Bind(SEC_COST, "MinimumCost", 25,
                Adv("Lowest coin cost for any teleport."));
            WaiveCostInCreative = cfg.Bind(SEC_COST, "WaiveCostInCreative", true,
                Vis("Teleports are free when the no-crafting-cost world setting is on."));

            CooldownWindowSeconds = cfg.Bind(SEC_COOLDOWN, "WindowSeconds", 600f,
                Adv("Seconds during which recent teleports make the next one cost more."));
            // multiplier = 1 + count^exponent; 1.5 gives 1st free, 2nd ~2x, 3rd ~3.8x, 4th ~6.2x, 5th ~9x.
            CooldownExponent = cfg.Bind(SEC_COOLDOWN, "Exponent", 1.5f,
                Adv("How fast the cost rises with repeated teleports. Higher rises faster."));

            RespectNoTeleportItems = cfg.Bind(SEC_RULES, "RespectNoTeleportItems", true,
                Vis("Block wayshrines while carrying items portals refuse, such as ore."));
            RespectNoPortalsKey = cfg.Bind(SEC_RULES, "RespectNoPortalsKey", true,
                Vis("Disable wayshrines when the world's no-portals setting is on."));
            RespectBossLockout = cfg.Bind(SEC_RULES, "RespectBossLockout", true,
                Vis("Block wayshrines during a boss fight when the world's no-boss-portals setting is on."));

            ShowUndiscoveredAsLocked = cfg.Bind(SEC_MAP, "ShowUndiscoveredAsLocked", false,
                Vis("Show undiscovered wayshrines on the map as faded locked icons instead of hiding them."));

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

        // Player-facing settings show normally; tuning knobs sit under Configuration Manager's advanced view;
        // debug switches are hidden from it entirely but still live in the .cfg file.
        private static ConfigDescription Vis(string d) => new ConfigDescription(d);

        private static ConfigDescription Adv(string d) =>
            new ConfigDescription(d, null, new ConfigurationManagerAttributes { IsAdvanced = true });

        private static ConfigDescription Hid(string d) =>
            new ConfigDescription(d, null, new ConfigurationManagerAttributes { Browsable = false });
    }
}
