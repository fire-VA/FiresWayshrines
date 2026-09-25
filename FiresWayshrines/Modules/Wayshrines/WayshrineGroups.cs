using System;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Wayshrine networks. A shrine with no group is on the world network. A named group (one dungeon, say) is a
    /// closed network: its children reach only shrines of their own group, while a group PARENT also stays on the
    /// world network, so the parent is the way in and out. Every travel check, map click and map pin asks this class,
    /// so the rule lives in one place.
    /// </summary>
    public static class WayshrineGroups
    {
        public const int MaxGroupNameLength = 32;

        public static string Normalize(string group)
        {
            if (string.IsNullOrWhiteSpace(group))
                return "";
            string trimmed = group.Trim();
            return trimmed.Length > MaxGroupNameLength ? trimmed.Substring(0, MaxGroupNameLength) : trimmed;
        }

        public static bool IsOnWorldNetwork(string group, bool isParent) => string.IsNullOrEmpty(group) || isParent;

        public static bool CanTravel(string fromGroup, bool fromIsParent, string toGroup, bool toIsParent)
        {
            if (IsOnWorldNetwork(fromGroup, fromIsParent) && IsOnWorldNetwork(toGroup, toIsParent))
                return true;
            return !string.IsNullOrEmpty(fromGroup) && string.Equals(fromGroup, toGroup, StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanTravel(Wayshrine from, DiscoveredShrine to) =>
            from != null && CanTravel(from.GetGroup(), from.IsGroupParent(), to.Group, to.IsGroupParent);

        /// <summary>
        /// Map visibility. While a teleport is armed the map shows exactly the reachable shrines (plus the source);
        /// otherwise it shows the world network only, so a dungeon's inner shrines never clutter the world map.
        /// </summary>
        public static bool ShowsOnMap(string shrineId, string group, bool isParent, Wayshrine armedSource)
        {
            if (armedSource == null)
                return IsOnWorldNetwork(group, isParent);
            return shrineId == armedSource.ShrineId
                || CanTravel(armedSource.GetGroup(), armedSource.IsGroupParent(), group, isParent);
        }

        public static string Describe(string group, bool isParent)
        {
            if (string.IsNullOrEmpty(group))
                return "world network";
            return isParent ? $"group '{group}', parent" : $"group '{group}'";
        }
    }
}
