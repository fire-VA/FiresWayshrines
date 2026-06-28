using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using FiresCore.Lifecycle;
using FiresCore.Logging;
using FiresCore.Sync;
using HarmonyLib;
using UnityEngine;
using VerdantsAscent.Modules.Wayshrines;
using VerdantsAscent.Modules.Wayshrines.TownPortalScroll;

namespace FiresWayshrines
{
    // Standalone wayshrine mod: the original, working Wayshrines module (ported verbatim from the
    // pre-refactor RPG Maker project, namespace VerdantsAscent.Modules.Wayshrines) wrapped in a thin
    // plugin. The plugin loads the embedded bundle, hands its "Wayshrine*" prefabs to WayshrineModule,
    // and registers them into ZNetScene + the Hammer the Jotunn-free way (mirroring VAPieceManager).
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.Fire.FiresUnifiedCore", BepInDependency.DependencyFlags.HardDependency)]
    public class FiresWayshrinesPlugin : FiresMod<FiresWayshrinesPlugin>
    {
        public const string PluginGUID = "com.Fire.FiresWayshrines";
        public const string PluginName = "FiresWayshrines";
        public const string PluginVersion = "1.1.1";

        private const string BundleResourceName = "fireswayshrines";

        // The module attaches the Wayshrine component to each matching bundle prefab and appends it here.
        public static readonly List<GameObject> piecePrefabs = new List<GameObject>();

        internal ConfigSync configSync;
        private static AssetBundle _bundle;
        private static bool _shaderWarmed;

        // Client-only patch classes — skipped on a dedicated server (their bodies reference
        // Minimap/UnifiedPopup/InventoryGui, which native-crash Mono's IL rewriter headless).
        protected override IReadOnlyCollection<string> DedicatedServerSkipPatchTypes => new[]
        {
            "VerdantsAscent.Modules.Wayshrines.Patches.MinimapClickPatch",
            "VerdantsAscent.Modules.Wayshrines.Patches.PlayerSpawnedPinRebuildPatch",
            "VerdantsAscent.Modules.Wayshrines.Patches.UnifiedPopupButtonLabelPatch",
            "VerdantsAscent.Modules.Wayshrines.TownPortalScroll.TownPortalScrollHandler",
        };

        protected override void Setup()
        {
            configSync = new ConfigSync(PluginGUID)
            {
                DisplayName = PluginName,
                CurrentVersion = PluginVersion,
                MinimumRequiredVersion = PluginVersion,
            };

            WayshrineModule.Init(Config);
            LoadBundleAndPrefabs();
            Logger.LogInfo($"{PluginName} v{PluginVersion} setup complete.");
        }

        protected override void WorldStart() { }
        protected override void TitleScene(bool isFirstBoot) { }
        protected override void Shutdown() { }

        private void LoadBundleAndPrefabs()
        {
            try
            {
                _bundle = GetAssetBundleFromResources(BundleResourceName);
                if (_bundle == null) { FiresLogger.LogWarning("[FiresWayshrines] bundle not found; no wayshrine pieces."); return; }

                GameObject[] prefabs = _bundle.LoadAllAssets<GameObject>();
                foreach (GameObject p in prefabs)
                    if (p != null) UnityEngine.Object.DontDestroyOnLoad(p);   // survive scene unload

                WayshrineModule.RegisterPrefabsFromBundle(prefabs);
                FiresLogger.LogInfo($"[FiresWayshrines] bundle loaded; {piecePrefabs.Count} wayshrine piece prefab(s).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresWayshrines] bundle/prefab load failed: {ex}");
            }
        }

        private AssetBundle GetAssetBundleFromResources(string name)
        {
            foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                if (loaded != null && string.Equals(loaded.name, name, StringComparison.OrdinalIgnoreCase))
                    return loaded;

            Assembly asm = Assembly.GetExecutingAssembly();
            string resourceName = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) { Debug.LogError($"[FiresWayshrines] no embedded resource ending with '{name}'"); return null; }

            using (System.IO.Stream stream = asm.GetManifestResourceStream(resourceName))
                return AssetBundle.LoadFromStream(stream);
        }

        // ---- Jotunn-free piece registration (mirrors FiresNPCs VAPieceManager) ----

        // ZNetScene.Awake runs on every world load BEFORE ZDOMan.Load — register here so placed
        // shrines resolve their prefab hash instead of getting the "Destroyed invalid prefab ZDO" wipe.
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        internal static class ZNetScenePatches
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                try { RegisterPiecesWithZNetScene(); }
                catch (Exception ex) { Debug.LogError($"[FiresWayshrines] ZNetScene register failed: {ex}"); }
            }
        }

        // ObjectDB.Awake — wire the Hammer entry + requirements, fix shaders, register the scroll recipe.
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        internal static class ObjectDBPatches
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                try { AddPiecesToHammer(); }
                catch (Exception ex) { Debug.LogError($"[FiresWayshrines] Hammer register failed: {ex}"); }
                try { TownPortalScrollRecipe.Register(); }
                catch (Exception ex) { Debug.LogWarning($"[FiresWayshrines] scroll recipe register skipped: {ex.Message}"); }
            }
        }

        private static void RegisterPiecesWithZNetScene()
        {
            ZNetScene znet = ZNetScene.instance;
            if (znet == null) return;

            FieldInfo namedField = typeof(ZNetScene).GetField("m_namedPrefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            var named = namedField?.GetValue(znet) as Dictionary<int, GameObject>;

            foreach (GameObject prefab in piecePrefabs)
            {
                if (prefab == null) continue;
                if (znet.GetPrefab(prefab.name) != null) continue;

                ZNetView nview = prefab.GetComponent<ZNetView>();
                if (nview == null) nview = prefab.AddComponent<ZNetView>();
                nview.m_persistent = true;

                znet.m_prefabs.Add(prefab);
                int hash = prefab.name.GetStableHashCode();
                if (named != null && !named.ContainsKey(hash)) named.Add(hash, prefab);
            }
        }

        private static void AddPiecesToHammer()
        {
            if (ObjectDB.instance == null || ZNetScene.instance == null) return;

            WarmShadersOnce();

            GameObject hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            PieceTable table = hammer?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
            if (table == null) { Debug.LogWarning("[FiresWayshrines] Hammer PieceTable not ready"); return; }
            table.m_pieces.RemoveAll(p => p == null);

            foreach (GameObject prefab in piecePrefabs)
            {
                if (prefab == null) continue;

                // Re-point bundle materials to the live game shaders (Custom/ shaders go invisible otherwise).
                try { FiresCore.Services.VanillaAssetResolver.ApplyToPrefab(prefab, FiresCore.Services.VanillaAssetResolver.AssetSwapMode.Shader); }
                catch (Exception ex) { Debug.LogWarning($"[FiresWayshrines] shader fix {prefab.name}: {ex.Message}"); }

                Piece piece = prefab.GetComponent<Piece>();
                if (piece == null) piece = prefab.AddComponent<Piece>();
                if (string.IsNullOrEmpty(piece.m_name)) piece.m_name = prefab.name;
                piece.m_category = Piece.PieceCategory.Misc;
                piece.m_enabled = true;
                piece.m_canBeRemoved = true;

                if (ZNetScene.instance.GetPrefab(prefab.name) == null) RegisterPiecesWithZNetScene();
                if (!table.m_pieces.Contains(prefab))
                {
                    table.m_pieces.Add(prefab);
                    FiresLogger.LogInfo($"[FiresWayshrines] added {prefab.name} to Hammer");
                }
            }
        }

        private static void WarmShadersOnce()
        {
            if (_shaderWarmed) return;
            try { FiresCore.Services.VanillaAssetResolver.Warm(); _shaderWarmed = true; }
            catch (Exception ex) { Debug.LogWarning($"[FiresWayshrines] shader warm failed: {ex.Message}"); }
        }
    }
}
