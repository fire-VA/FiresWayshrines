using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VerdantsAscent.Modules.Wayshrines.TownPortalScroll
{
    // The scroll ships in the fireswayshrines bundle and is registered into ZNetScene and ObjectDB on every world load.
    // A relogin's asset collection can destroy the loaded prefab, so it is read from the bundle again when that happens.
    internal static class TownPortalScrollItem
    {
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> NamedPrefabs =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");
        private static readonly AccessTools.FieldRef<ObjectDB, Dictionary<int, GameObject>> ItemsByHash =
            AccessTools.FieldRefAccess<ObjectDB, Dictionary<int, GameObject>>("m_itemByHash");
        private static readonly AccessTools.FieldRef<ObjectDB, Dictionary<ItemDrop.ItemData.SharedData, GameObject>> ItemsBySharedData =
            AccessTools.FieldRefAccess<ObjectDB, Dictionary<ItemDrop.ItemData.SharedData, GameObject>>("m_itemByData");

        private static AssetBundle _bundle;
        private static GameObject _prefab;

        internal static void UseBundle(AssetBundle bundle) => _bundle = bundle;

        internal static void RegisterWithZNetScene(ZNetScene zNetScene)
        {
            GameObject prefab = LoadedPrefab();
            if (prefab == null || zNetScene.GetPrefab(prefab.name) != null) return;
            zNetScene.m_prefabs.Add(prefab);
            NamedPrefabs(zNetScene)[prefab.name.GetStableHashCode()] = prefab;
        }

        internal static void RegisterWithObjectDB(ObjectDB objectDB)
        {
            GameObject prefab = LoadedPrefab();
            if (prefab == null || objectDB.GetItemPrefab(prefab.name) != null) return;
            objectDB.m_items.Add(prefab);
            ItemsByHash(objectDB)[prefab.name.GetStableHashCode()] = prefab;
            ItemsBySharedData(objectDB)[prefab.GetComponent<ItemDrop>().m_itemData.m_shared] = prefab;
        }

        private static GameObject LoadedPrefab()
        {
            if (_prefab == null && _bundle != null)
                _prefab = _bundle.LoadAsset<GameObject>(TownPortalScrollRecipe.ScrollPrefabName);
            return _prefab;
        }
    }
}
