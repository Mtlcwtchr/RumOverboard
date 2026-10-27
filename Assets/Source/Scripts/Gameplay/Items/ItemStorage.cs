#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Attributes;
using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// A container on the ship (chest, rack…) that holds real items — not counters (GDD §3.4): a stored
    /// item stays a networked object, parked hidden inside the container (<see cref="WorldItem.Container"/>).
    /// Capacity = the container's <c>StorageSlots</c> stat. Last in, first out (E with empty hands takes
    /// the top item straight into your hands). When the container breaks, everything spills out.
    /// Host-only mutations; the slot list replicates for prompts ("3/8", "пусто").
    /// </summary>
    [RequireComponent(typeof(WorldItem))]
    public sealed class ItemStorage : NetworkBehaviour
    {
        public const int MaxSlots = 16;

        [Networked, Capacity(MaxSlots)] private NetworkArray<NetworkId> Slots { get; }
        [Networked] public byte Count { get; private set; }

        private WorldItem _self;

        public WorldItem Self => _self != null ? _self : _self = GetComponent<WorldItem>();

        public int Capacity
        {
            get
            {
                AttributesComponent a = Self.Attributes;
                float slots = a != null ? a.Get(Attr.StorageSlots, 4f) : 4f;
                return Mathf.Clamp(Mathf.RoundToInt(slots), 0, MaxSlots);
            }
        }

        public bool HasSpace => Count < Capacity;
        public string DisplayName => Self.DisplayName;

        public NetworkId SlotAt(int index) => index >= 0 && index < Count ? Slots[index] : default;

        /// <summary>Host: put <paramref name="item"/> inside (it stops being held and hides).</summary>
        public bool TryStore(WorldItem item)
        {
            if (!HasStateAuthority || item == null || item == Self || Self.Broken || !HasSpace || item.IsStored)
                return false;
            Slots.Set(Count, item.Object.Id);
            Count++;
            item.SetContainer(Object.Id);
            return true;
        }

        /// <summary>Host: take the top item out (null when empty). Skips items that no longer exist.</summary>
        public WorldItem TakeLast()
        {
            if (!HasStateAuthority) return null;
            while (Count > 0)
            {
                NetworkId id = Slots[Count - 1];
                Slots.Set(Count - 1, default);
                Count--;
                if (Runner.TryFindObject(id, out NetworkObject obj) && obj != null && obj.TryGetComponent(out WorldItem item))
                {
                    item.SetContainer(default);
                    return item;
                }
            }
            return null;
        }

        /// <summary>Host: empty everything around the container (it broke).</summary>
        public void SpillAll()
        {
            if (!HasStateAuthority) return;
            Vector3 center = transform.position + Vector3.up * 0.6f;
            int i = 0;
            WorldItem item;
            while ((item = TakeLast()) != null)
            {
                float angle = i++ * 137.5f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                item.Teleport(center + dir * 0.7f);
                item.Body.linearVelocity = dir * 1.5f + Vector3.up * 1.5f;
            }
        }
    }
}
#endif
