using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    // FiresWayshrines' sections for the shared FiresCore help panel (registration only).
    internal static class WayshrineHelpContent
    {
        private const string ModId = "FiresWayshrines";
        private static bool _registered;

        public static void Register()
        {
            if (_registered || Application.isBatchMode) return;
            _registered = true;

            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Wayshrine Travel", 0, BuildOverview);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Costs & Rules", 1, BuildCosts);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Admin Management", 2, BuildAdmin, adminOnly: true);
        }

        private static void BuildOverview(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Wayshrine Travel");
            w.Paragraph("Wayshrines are a fast-travel network. Discover shrines by finding them; " +
                "travel between any two you know for a coin toll.");
            w.Divider();

            w.SubHeader("Discovering");
            w.Bullet("Walk within ~20m of a wayshrine — 'Discovered: <name>' and it pins to your map.");
            w.Bullet("Shrines name themselves after their biome, so 'Frostveil Sanctum' tells you " +
                "where it is.");
            w.Bullet("Undiscovered shrines are hidden from the map (a server option can show them as " +
                "locked '???' pins instead).");
            w.Divider();

            w.SubHeader("Traveling");
            w.Bullet("Press E on any wayshrine — the map opens, armed for a destination pick.");
            w.Bullet("Left-click any DISCOVERED shrine pin (or an admin hub pin).");
            w.Bullet("The confirm popup shows the destination, distance, any cooldown multiplier, and " +
                "the coin cost. Confirm to travel.");
            w.Bullet("Cancelling keeps the map armed so you can pick a different destination. Closing " +
                "the map disarms.");
            w.Bullet("You arrive in a small ring around the destination shrine, on the same floor as it, " +
                "never in deep water, on lava, or up on a roof.");
            w.Divider();

            w.SubHeader("Dungeon Networks");
            w.Bullet("Some shrines belong to a closed network, such as the shrines inside one dungeon.");
            w.Bullet("A network's inner shrines only connect to each other and to its parent shrine, " +
                "once you have discovered them. They never show on the world map.");
            w.Bullet("The parent shrine is also on the world network: it is the way into and out of " +
                "the network.");
            w.Divider();

            w.SubHeader("Building Your Own");
            w.Bullet("The wayshrine piece is on the hammer's Misc tab — place shrines at your bases " +
                "and they join the network for everyone who discovers them.");
            w.Divider();

            w.SubHeader("Town Portal Scroll");
            w.Paragraph("The Scroll of Town Portal offers a Bed / Wayshrine / Cancel choice and " +
                "teleports you FREE — a consumable escape rope. Scrolls arrive via loot or vendors.");
        }

        private static void BuildCosts(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Costs & Rules");
            w.SubHeader("The Toll");
            w.Bullet("Cost = distance x CoinsPerMeter (default 0.25/m — 1km costs 250 Coins), with a " +
                "minimum of 25.");
            w.Bullet("Paid in Coins from your inventory at confirm time.");
            w.Divider();

            w.SubHeader("The Cooldown Multiplier");
            w.Paragraph("Rapid-fire teleporting gets expensive. Recent teleports inside a sliding " +
                "10-minute window raise the price: the first is normal, the second ~2x, the third " +
                "~4x, the fifth ~9x. Wait and it decays.");
            w.Divider();

            w.SubHeader("Restrictions (server-configurable)");
            w.Bullet("Carrying non-teleportable items (ore) blocks travel — same rule as portals.");
            w.Bullet("The NoPortals world modifier disables wayshrines too.");
            w.Bullet("An active boss with NoBossPortals blocks travel.");
            w.Bullet("Creative worlds (NoCraftCost) can waive the toll entirely.");
        }

        private static void BuildAdmin(FiresCore.Help.HelpContentWriter w)
        {
            w.AdminHeader("Admin Management");
            w.SubHeader("Per-Shrine Options (hold Alt+Shift + right-click a shrine)");
            w.Bullet("Rename… — custom shrine name");
            w.Bullet("Set teleport cost… — fixed price (0 = free, blank = automatic)");
            w.Bullet("Adjust cooldown… — per-shrine cooldown (0 = none)");
            w.Bullet("Auto-discover ON/OFF — everyone knows this shrine the moment it loads");
            w.Bullet("Admin-only ON/OFF — hides the pin and blocks use for non-admins");
            w.Bullet("Set trigger size… — resize trigger-box shrines (Right Shift+Scroll while placing)");
            w.Bullet("Network group… — put the shrine in a named closed network (blank = world " +
                "network). Every shrine with the same name is one network, e.g. one dungeon.");
            w.Bullet("Group parent ON/OFF — makes this shrine the network's way in and out; it also " +
                "stays on the world network. A network may have more than one parent, or none.");
            w.Bullet("Setting changes reach every online player within a couple of seconds.");
            w.Bullet("Wipe map marker (all players) — server-authoritative forget");
            w.Bullet("Alt+right-click a shrine's MAP PIN also opens rename.");
            w.AdminDivider();

            w.SubHeader("Coordinate Hubs");
            w.Paragraph("Fixed teleport destinations that aren't physical shrines — town squares, " +
                "event arenas. Defined in .cfg files under config/FiresWayshrines/Teleporters (or the " +
                "Marketplace Teleporters folder), hot-reloaded every few seconds:");
            w.CodeBlock("[hubprofile]\n" +
                "Town Square<speed=1>,120,32,-450,portal\n" +
                "Arena,-200,45,780\n" +
                "@from:otherhub");
            w.AdminDivider();

            w.SubHeader("Config (server-synced)");
            w.Bullet("Wayshrines.Discovery.DiscoveryRange — auto-discover distance (20m)");
            w.Bullet("Wayshrines.Cost.CoinsPerMeter / MinimumCost / WaiveCostInCreative");
            w.Bullet("Wayshrines.Cooldown.WindowSeconds / Exponent — the multiplier curve");
            w.Bullet("Wayshrines.Rules.RespectNoTeleportItems / RespectNoPortalsKey / RespectBossLockout");
            w.Bullet("Wayshrines.Map.ShowUndiscoveredAsLocked — '???' pins for unknown shrines");
        }
    }
}
