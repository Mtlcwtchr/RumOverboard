using System;
using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// Id → networked item prefab (NetworkObject + WorldItem). Used by the scene spawner and the debug
    /// window to spawn items. Fusion registers the prefabs itself (any prefab with a NetworkObject).
    /// </summary>
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "RumOverboard/Items/Item Catalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Id;
            public GameObject Prefab;
        }

        public List<Entry> Items = new();

        public GameObject Get(string id)
        {
            foreach (Entry e in Items)
                if (e.Id == id) return e.Prefab;
            return null;
        }
    }
}
