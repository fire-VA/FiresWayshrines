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
using FiresLogger = VerdantsAscent.FiresLogger;

namespace FiresWayshrines
{
    // Standalone wayshrine mod: the Wayshrines module (namespace VerdantsAscent.Modules.Wayshrines, kept
    // for save and reflection compatibility) wrapped in a thin plugin. The plugin loads the embedded bundle, hands its "Wayshrine*" prefabs to WayshrineModule,
    // and registers them into ZNetScene + the Hammer the Jotunn-free way (mirroring VAPieceManager).
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.Fire.FiresUnifiedCore", "0.2.33")]
    public class FiresWayshrinesPlugin : FiresMod<FiresWayshrinesPlugin>
    {
        public const string PluginGUID = "com.Fire.FiresWayshrines";
        public const string PluginName = "FiresWayshrines";
        public const string PluginVersion = "1.2.13";   // 1.2.13 ([wishbone] 11-05, Fire: name the dedi errors): dedi texture unload removed (vanilla never unloads bundle textures; it freed 0 MB and unloaded mid async upload, suspected AsyncResourceUpload failed). 1.2.12 ([wishbone]; [fgn] VMMap 10-30): the asset bundle ships as a file next to the DLL and loads with AssetBundle.LoadFromFile (no embedded copy held in RAM)

        private const string BundleResourceName = "fireswayshrines";
        private const float SummaryPinWaitSeconds = 10f;

        internal static BepInEx.Logging.ManualLogSource Log;

        // The module attaches the Wayshrine component to each matching bundle prefab and appends it here.
        public static readonly List<GameObject> piecePrefabs = new List<GameObject>();

        internal ConfigSync configSync;
        private static AssetBundle _bundle;

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
            Log = Logger;
            try { FiresWayshrinesBanner.PrintBig(); }
            catch (Exception ex) { Logger.LogWarning($"big banner failed: {ex.Message}"); }

            configSync = new ConfigSync(PluginGUID)
            {
                DisplayName = PluginName,
                CurrentVersion = PluginVersion,
                MinimumRequiredVersion = "1.2.3" /* fixed: raise only when a synced key's name, type or meaning changes */,
            };

            WayshrineModule.Init(Config);
            LoadBundleAndPrefabs();

            // Admin right-click options (rename / cost / cooldown / auto-discover / admin-only / wipe).
            // Client-only: the Core driver no-ops headless, but guard registration to be safe.
            if (!Application.isBatchMode)
            {
                FiresCore.UI.ContextMenu.FiresContextMenu.Register(new WayshrineAdminContextProvider());

                try { WayshrineHelpContent.Register(); }
                catch (System.Exception ex) { Logger.LogWarning($"Help registration failed: {ex.Message}"); }
            }

            Logger.LogInfo($"{PluginName} v{PluginVersion} setup complete.");
        }

        protected override void WorldStart()
        {
            FiresWayshrinesBanner.Print();
            StartCoroutine(EmitWorldSummaryWhenPinsSettle());
        }

        // Waits for the spawn-time pin rebuild (host-fast, RPC-async on dedicated
        // clients) so the box reports real counts; times out rather than never
        // emitting (headless never rebuilds pins — no Minimap).
        private System.Collections.IEnumerator EmitWorldSummaryWhenPinsSettle()
        {
            if (!IsHeadless)
            {
                int rebuildsAtEntry = WayshrineMapPins.CompletedRebuilds;
                float deadline = Time.unscaledTime + SummaryPinWaitSeconds;
                while (WayshrineMapPins.CompletedRebuilds == rebuildsAtEntry && Time.unscaledTime < deadline)
                    yield return null;
            }

            try
            {
                LoadSummary.For(PluginName).EmitMiniBox("🌀 WAYSHRINES", new[]
                {
                    $"shrines registered: {WayshrineWorldRegistry.GetAll().Count}",
                    $"pins rebuilt: {WayshrineMapPins.PinCount}",
                    $"prefabs attached: {piecePrefabs.Count}",
                });
            }
            catch (Exception ex) { FiresLogger.LogWarning($"wayshrine summary failed: {ex.Message}"); }
        }
        protected override void TitleScene(bool isFirstBoot) { }
        protected override void Shutdown() { }

        private void LoadBundleAndPrefabs()
        {
            try
            {
                _bundle = GetAssetBundleFromResources(BundleResourceName);
                if (_bundle == null) { FiresLogger.LogWarning("bundle not found; no wayshrine pieces."); return; }
                TownPortalScrollItem.UseBundle(_bundle);

                GameObject[] prefabs = _bundle.LoadAllAssets<GameObject>();
                foreach (GameObject p in prefabs)
                    if (p != null) UnityEngine.Object.DontDestroyOnLoad(p);   // survive scene unload

                WayshrineModule.RegisterPrefabsFromBundle(prefabs);
                FiresLogger.LogInfo($"bundle loaded; {piecePrefabs.Count} wayshrine piece prefab(s).");
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
            // 1.2.12 ([fgn] VMMap 10-30, 22 MB mapped + the byte[] copy): the bundle ships as a file next to the DLL; LoadFromFile maps
            // it and reads lazily (no managed copy, nothing to dispose, dedi-safe). The embedded path below stays only for an old build.
            string file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(asm.Location) ?? "", name);
            if (System.IO.File.Exists(file))
            {
                var fromFile = AssetBundle.LoadFromFile(file);
                FiresLogger.LogInfo($"bundle '{name}' loaded from file ({new System.IO.FileInfo(file).Length / 1048576f:0.0} MB, mapped, no copy in RAM)");
                return fromFile;
            }
            string resourceName = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) { Debug.LogError($"[FiresWayshrines] no embedded resource ending with '{name}'"); return null; }
            FiresLogger.LogWarning($"bundle file '{file}' missing: loading the embedded copy (whole bundle held in RAM); ship the '{name}' file next to the DLL");

            using (System.IO.Stream stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using var ms = new System.IO.MemoryStream();
                stream.CopyTo(ms);
                return AssetBundle.LoadFromMemory(ms.ToArray());   // full copy — disposed-stream lazy re-read crashes the dedi
            }
        }

        // ---- Jotunn-free piece registration (mirrors FiresNPCs VAPieceManager) ----

        // ZNetScene.Awake runs on every world load BEFORE ZDOMan.Load — register here so placed
        // shrines resolve their prefab hash instead of getting the "Destroyed invalid prefab ZDO" wipe.
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        internal static class ZNetScenePatches
        {
            [HarmonyPostfix]
            private static void Postfix(ZNetScene __instance)
            {
                try { RegisterPiecesWithZNetScene(); }
                catch (Exception ex) { Debug.LogError($"[FiresWayshrines] ZNetScene register failed: {ex}"); }
                try { TownPortalScrollItem.RegisterWithZNetScene(__instance); }
                catch (Exception ex) { Debug.LogError($"[FiresWayshrines] scroll ZNetScene register failed: {ex}"); }
            }
        }

        // ObjectDB.Awake — wire the Hammer entry + requirements, fix shaders, register the scroll item and its recipe.
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        internal static class ObjectDBPatches
        {
            [HarmonyPostfix]
            private static void Postfix(ObjectDB __instance)
            {
                if (FiresCore.Pieces.HammerPieces.IsMainMenuObjectDB(__instance)) return;
                try { AddPiecesToHammer(); }
                catch (Exception ex) { Debug.LogError($"[FiresWayshrines] Hammer register failed: {ex}"); }
                try
                {
                    TownPortalScrollItem.RegisterWithObjectDB(__instance);
                    TownPortalScrollRecipe.Register();
                }
                catch (Exception ex) { Debug.LogWarning($"[FiresWayshrines] scroll register skipped: {ex.Message}"); }
            }
        }

        private static void RegisterPiecesWithZNetScene()
        {
            ZNetScene znet = ZNetScene.instance;
            if (znet == null) return;

            EnsurePrefabsLoaded();   // re-acquire bundle prefabs if a relogin collected them (else placed shrines never spawn)
            FiresCore.World.NetworkPrefabs.ScrubDeadEntries(znet);

            foreach (GameObject prefab in piecePrefabs)
            {
                if (prefab == null) continue;
                if (znet.GetPrefab(prefab.name) != null) continue;

                ZNetView nview = prefab.GetComponent<ZNetView>();
                if (nview == null) nview = prefab.AddComponent<ZNetView>();
                nview.m_persistent = true;

                FiresCore.World.NetworkPrefabs.RegisterByName(znet, prefab);
            }
        }

        private static void AddPiecesToHammer()
        {
            if (ObjectDB.instance == null || ZNetScene.instance == null) return;

            EnsurePrefabsLoaded();   // heal stale prefabs regardless of ObjectDB.Awake vs ZNetScene.Awake order

            PieceTable table = FiresCore.Pieces.HammerPieces.HammerTable();
            if (table == null) { Debug.LogWarning("[FiresWayshrines] Hammer PieceTable not ready"); return; }

            // Re-point bundle materials to the live game shaders (Custom/ shaders go invisible otherwise), once per
            // prefab instance.
            FiresCore.Services.VanillaAssetResolver.RepairPrefabsOnce(piecePrefabs, FiresCore.Services.VanillaAssetResolver.AssetSwapMode.Shader, "[FiresWayshrines]");

            foreach (GameObject prefab in piecePrefabs)
            {
                if (prefab == null) continue;

                Piece piece = prefab.GetComponent<Piece>();
                if (piece == null) piece = prefab.AddComponent<Piece>();
                if (string.IsNullOrEmpty(piece.m_name)) piece.m_name = prefab.name;
                piece.m_category = Piece.PieceCategory.Misc;
                piece.m_enabled = true;
                piece.m_canBeRemoved = true;

                // Trigger box: native scale persistence (server + client) + the client-only wireframe visual.
                if (prefab.name == "Wayshrine_Trigger")
                {
                    ZNetView tnv = prefab.GetComponent<ZNetView>();
                    if (tnv != null) tnv.m_syncInitialScale = true;
                    if (!Application.isBatchMode && prefab.GetComponent<WayshrineTriggerWire>() == null)
                        prefab.AddComponent<WayshrineTriggerWire>();
                }

                if (ZNetScene.instance.GetPrefab(prefab.name) == null) RegisterPiecesWithZNetScene();
                if (FiresCore.Pieces.HammerPieces.AddToTable(table, prefab))
                    FiresLogger.LogVerbose($"added {prefab.name} to Hammer");
            }
        }

        // On a relogin (logout to main menu -> back in) the bundle's prefab GameObjects can be collected by
        // Resources.UnloadUnusedAssets (DontDestroyOnLoad is a no-op on bundle ASSETS), turning piecePrefabs
        // entries Unity-fake-null. They then never re-register into the brand-new ZNetScene, so placed shrine
        // ZDOs stop spawning until a full game restart reloads the bundle. Re-acquire from the still-loaded
        // bundle so every world load has live GameObjects. No-op when the list is already healthy.
        private static void EnsurePrefabsLoaded()
        {
            if (_bundle == null) return;
            if (piecePrefabs.Count > 0 && !piecePrefabs.Exists(p => p == null)) return;

            int before = piecePrefabs.Count;
            piecePrefabs.RemoveAll(p => p == null);
            GameObject[] fresh = _bundle.LoadAllAssets<GameObject>();
            foreach (GameObject p in fresh)
                if (p != null) UnityEngine.Object.DontDestroyOnLoad(p);
            WayshrineModule.RegisterPrefabsFromBundle(fresh);   // Contains-guarded re-add + component-guarded attach
            FiresLogger.LogInfo($"re-acquired piece prefabs after relogin ({before} -> {piecePrefabs.Count}).");
        }
    }
}
