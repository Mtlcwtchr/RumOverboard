using System;
using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    [Serializable]
    public struct AttributeEntry
    {
        public AttributeDefinition Attribute;
        public float BaseValue;

        public AttributeEntry(AttributeDefinition attribute, float baseValue)
        {
            Attribute = attribute;
            BaseValue = baseValue;
        }
    }

    /// <summary>
    /// Everything about one entity type in one asset (player, barrel, board, powder keg, crate…):
    ///   • numeric attributes with base values (stats / resources),
    ///   • flags it starts with,
    ///   • static data blocks (<see cref="IEntityData"/>: prefabs, VFX, texts, tuning).
    ///
    /// Optional <see cref="Parent"/>: the child inherits everything and overrides base values / data
    /// blocks of the same type (Barrel → PowderBarrel). Flags are a union.
    /// </summary>
    [CreateAssetMenu(fileName = "Profile", menuName = "RumOverboard/Attributes/Entity Profile")]
    public sealed class AttributeProfile : ScriptableObject
    {
        [SerializeField] private AttributeProfile parent;
        [SerializeField] private List<AttributeEntry> attributes = new();
        [SerializeField] private List<FlagDefinition> flags = new();
        [SerializeReference] private List<IEntityData> data = new();

        [NonSerialized] private AttributeLayout _layout;

        public AttributeProfile Parent => parent;
        public IReadOnlyList<AttributeEntry> Attributes => attributes;
        public IReadOnlyList<FlagDefinition> Flags => flags;
        public IReadOnlyList<IEntityData> Data => data;

        /// <summary>Slot layout shared by every instance of this profile (built once, lazily).</summary>
        public AttributeLayout Layout => _layout ??= AttributeLayout.Build(this);

        /// <summary>First data block of type T (own first, then parent chain). Null if absent.</summary>
        public T GetData<T>() where T : class, IEntityData
        {
            for (AttributeProfile p = this; p != null; p = p.parent)
            {
                List<IEntityData> list = p.data;
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] is T found)
                        return found;
            }
            return null;
        }

        public bool TryGetData<T>(out T value) where T : class, IEntityData => (value = GetData<T>()) != null;

        private void OnValidate() => _layout = null;

#if UNITY_EDITOR
        /// <summary>Tooling only: replace content (sample assets, self-tests).</summary>
        public void EditorSetup(AttributeProfile parentProfile, List<AttributeEntry> entries,
                                List<FlagDefinition> startFlags, List<IEntityData> dataBlocks = null)
        {
            parent = parentProfile;
            attributes = entries ?? new List<AttributeEntry>();
            flags = startFlags ?? new List<FlagDefinition>();
            data = dataBlocks ?? new List<IEntityData>();
            _layout = null;
        }

        /// <summary>Tooling only: write a base value into this asset (debug window "save to config").</summary>
        public void EditorSetBaseValue(AttributeDefinition attribute, float value)
        {
            for (int i = 0; i < attributes.Count; i++)
            {
                if (attributes[i].Attribute != attribute) continue;
                attributes[i] = new AttributeEntry(attribute, value);
                _layout = null;
                return;
            }
            attributes.Add(new AttributeEntry(attribute, value)); // override of an inherited entry
            _layout = null;
        }

        public void EditorInvalidateLayout() => _layout = null;
#endif
    }
}
