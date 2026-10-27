using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// Immutable slot layout of one <see cref="AttributeProfile"/> (parent chain flattened), shared by
    /// all its instances. Maps <see cref="AttributeId"/> → dense slot in O(1) via a small lookup table,
    /// so <see cref="AttributeSet"/> is just a few float arrays.
    /// </summary>
    public sealed class AttributeLayout
    {
        /// <summary>Per-profile cap: change masks are a single ulong.</summary>
        public const int MaxSlots = 64;

        private readonly short[] _slotById; // index = AttributeId.Value, -1 = absent

        public readonly int Count;
        public readonly AttributeDefinition[] Definitions; // by slot
        public readonly float[] BaseValues;                // by slot
        public readonly ulong BaseFlags;

        /// <summary>Slots sent to clients, in wire order.</summary>
        public readonly int[] ReplicatedSlots;

        /// <summary>Floats needed on the wire: 1 per replicated stat, 2 per replicated resource.</summary>
        public readonly int ReplicatedFloatCount;

        /// <summary>Deterministic hash of ids/kinds/replication — host and clients must match.</summary>
        public readonly int Hash;

        private AttributeLayout(List<AttributeEntry> entries, ulong flags)
        {
            Count = entries.Count;
            Definitions = new AttributeDefinition[Count];
            BaseValues = new float[Count];
            BaseFlags = flags;

            int maxId = 0;
            for (int i = 0; i < Count; i++)
                maxId = Mathf.Max(maxId, entries[i].Attribute.Id.Value);
            _slotById = new short[maxId + 1];
            for (int i = 0; i < _slotById.Length; i++)
                _slotById[i] = -1;

            var replicated = new List<int>();
            unchecked
            {
                int hash = (int)2166136261;
                for (int slot = 0; slot < Count; slot++)
                {
                    AttributeDefinition def = entries[slot].Attribute;
                    Definitions[slot] = def;
                    BaseValues[slot] = def.Clamp(entries[slot].BaseValue);
                    _slotById[def.Id.Value] = (short)slot;

                    if (def.Replicated)
                    {
                        replicated.Add(slot);
                        ReplicatedFloatCount += def.Kind == AttributeKind.Resource ? 2 : 1;
                    }

                    hash = (hash ^ def.Id.Value) * 16777619;
                    hash = (hash ^ (int)def.Kind) * 16777619;
                    hash = (hash ^ (def.Replicated ? 1 : 0)) * 16777619;
                }
                Hash = hash;
            }
            ReplicatedSlots = replicated.ToArray();
        }

        public int SlotOf(AttributeId id) => id.Value < _slotById.Length ? _slotById[id.Value] : -1;

        public bool Contains(AttributeId id) => SlotOf(id) >= 0;

        public static AttributeLayout Build(AttributeProfile profile)
        {
            // Root first so children override inherited base values in place (order stays stable).
            var chain = new List<AttributeProfile>();
            for (AttributeProfile p = profile; p != null && !chain.Contains(p); p = p.Parent)
                chain.Add(p);
            chain.Reverse();

            var entries = new List<AttributeEntry>();
            ulong flags = 0;
            foreach (AttributeProfile p in chain)
            {
                foreach (AttributeEntry e in p.Attributes)
                {
                    if (e.Attribute == null) continue;
                    if (!e.Attribute.Id.IsValid)
                    {
                        Debug.LogError($"[Attributes] '{e.Attribute.name}' has no id — run RumOverboard ▸ Attributes ▸ Rebuild Registry.", e.Attribute);
                        continue;
                    }
                    int existing = entries.FindIndex(x => x.Attribute == e.Attribute);
                    if (existing >= 0) entries[existing] = e;
                    else entries.Add(e);
                }
                foreach (FlagDefinition f in p.Flags)
                    if (f != null)
                        flags |= f.Id.Mask;
            }

            if (entries.Count > MaxSlots)
            {
                Debug.LogError($"[Attributes] Profile '{profile.name}' has {entries.Count} attributes (max {MaxSlots}); extra ones dropped.", profile);
                entries.RemoveRange(MaxSlots, entries.Count - MaxSlots);
            }

            return new AttributeLayout(entries, flags);
        }
    }
}
