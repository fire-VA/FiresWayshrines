using System.Collections.Generic;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines
{
    /// <summary>
    /// Pure-function cost math. No Unity dependencies in core methods so this
    /// is unit-testable.
    /// </summary>
    public static class WayshrineCostCalculator
    {
        /// <summary>
        /// Computes the final coin cost for a teleport.
        /// </summary>
        /// <param name="distance">Meters between source and destination shrines (XZ).</param>
        /// <param name="recentTeleports">Count of teleports the player has made within the cooldown window.</param>
        /// <returns>Final cost in coins (already floored, never below MinimumCost).</returns>
        public static int CalculateCost(float distance, int recentTeleports)
        {
            float coinsPerMeter = WayshrineConfig.CoinsPerMeter?.Value ?? 0.25f;
            int minCost = WayshrineConfig.MinimumCost?.Value ?? 25;

            float baseCost = distance * coinsPerMeter;
            float multiplier = CalculateMultiplier(recentTeleports);
            int cost = Mathf.FloorToInt(baseCost * multiplier);

            return Mathf.Max(cost, minCost);
        }

        /// <summary>
        /// Cooldown multiplier based on recent teleport count.
        /// 0 recent => 1x, 1 => 2x, 2 => ~3.8x, 3 => ~6.2x, 4 => 9x (with default exp 1.5).
        /// </summary>
        public static float CalculateMultiplier(int recentTeleports)
        {
            if (recentTeleports <= 0) return 1f;
            float exponent = WayshrineConfig.CooldownExponent?.Value ?? 1.5f;
            return 1f + Mathf.Pow(recentTeleports, exponent);
        }

        /// <summary>
        /// Filters a list of unix timestamps down to only those within the
        /// cooldown window relative to <paramref name="nowSeconds"/>.
        /// </summary>
        public static List<double> PruneOutsideWindow(List<double> timestamps, double nowSeconds)
        {
            float window = WayshrineConfig.CooldownWindowSeconds?.Value ?? 600f;
            double cutoff = nowSeconds - window;
            var keep = new List<double>(timestamps.Count);
            for (int i = 0; i < timestamps.Count; i++)
                if (timestamps[i] >= cutoff)
                    keep.Add(timestamps[i]);
            return keep;
        }
    }
}
