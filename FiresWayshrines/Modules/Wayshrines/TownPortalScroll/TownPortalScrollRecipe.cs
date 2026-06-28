using FiresWayshrines;
using FiresCore.Logging;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines.TownPortalScroll
{
    /// <summary>
    /// Registers the Town Portal Scroll recipe at ObjectDB load time. The
    /// recipe requires a <c>SwordCheat</c> (cheat-sword) as an ingredient,
    /// which only admins can spawn via <c>genitem</c> — so it's effectively
    /// admin-only craftable. Players obtain scrolls by purchase or drop.
    ///
    /// Called from <c>FiresWayshrinesPlugin.ObjectDBPatches.ObjectDB_Awake_Postfix</c>.
    /// Idempotent: re-running on relog won't add duplicates.
    /// </summary>
    public static class TownPortalScrollRecipe
    {
        public const string ScrollPrefabName = "ScrollTownPortal";
        public const string RecipeName = "Recipe_TownPortalScroll_Admin";

        // Vanilla cheat-sword prefab name. The PrivateKeyManager comment in
        // this codebase identifies it as "SwordCheat" (display) / "cheatsword"
        // (lowercase variant). We try both forms.
        private static readonly string[] CheatSwordCandidates = { "SwordCheat", "Cheatsword", "cheatsword" };

        public static void Register()
        {
            if (ObjectDB.instance == null) return;

            // Idempotent — skip if already added in a previous Awake.
            for (int i = 0; i < ObjectDB.instance.m_recipes.Count; i++)
            {
                var r = ObjectDB.instance.m_recipes[i];
                if (r != null && r.name == RecipeName)
                    return;
            }

            var scrollPrefab = ObjectDB.instance.GetItemPrefab(ScrollPrefabName);
            if (scrollPrefab == null)
            {
                FiresLogger.LogWarning($"[TownPortalScroll] '{ScrollPrefabName}' not in ObjectDB; recipe skipped. " +
                                       "Make sure the prefab is in the asset bundle and listed in VAItemManager.ItemNameList.");
                return;
            }

            var scrollItem = scrollPrefab.GetComponent<ItemDrop>();
            if (scrollItem == null)
            {
                FiresLogger.LogWarning($"[TownPortalScroll] '{ScrollPrefabName}' has no ItemDrop component; recipe skipped.");
                return;
            }

            // Find the cheat-sword ingredient — try each candidate name.
            ItemDrop swordItem = null;
            foreach (var candidate in CheatSwordCandidates)
            {
                var sword = ObjectDB.instance.GetItemPrefab(candidate);
                if (sword != null)
                {
                    swordItem = sword.GetComponent<ItemDrop>();
                    if (swordItem != null) break;
                }
            }

            if (swordItem == null)
            {
                FiresLogger.LogWarning("[TownPortalScroll] No cheat-sword prefab found (tried " +
                                       string.Join(", ", CheatSwordCandidates) +
                                       "); registering recipe WITHOUT admin gate as fallback.");
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = RecipeName;
            recipe.m_item = scrollItem;
            recipe.m_amount = 1;
            recipe.m_minStationLevel = 1;
            recipe.m_enabled = true;
            recipe.m_craftingStation = null;   // craftable anywhere (the gate is the ingredient, not the station)
            recipe.m_repairStation = null;
            recipe.m_requireOnlyOneIngredient = false;

            // Single ingredient: 1 SwordCheat — admin-only because only admins
            // can spawn cheat-swords via console (`genitem`). m_recover=false
            // means the cheat-sword IS consumed on craft (intentional — keeps
            // the gate honest; admins regen them trivially).
            if (swordItem != null)
            {
                recipe.m_resources = new[]
                {
                    new Piece.Requirement
                    {
                        m_resItem = swordItem,
                        m_amount = 1,
                        m_amountPerLevel = 0,
                        m_recover = false
                    }
                };
            }
            else
            {
                // Fallback if cheat-sword isn't found: empty resource list +
                // disabled. Better to disable than to ship a free recipe.
                recipe.m_resources = new Piece.Requirement[0];
                recipe.m_enabled = false;
            }

            ObjectDB.instance.m_recipes.Add(recipe);
            FiresLogger.LogInfo($"[TownPortalScroll] Recipe registered (gated by " +
                                $"{(swordItem != null ? swordItem.gameObject.name : "<missing — disabled>")}).");
        }
    }
}
