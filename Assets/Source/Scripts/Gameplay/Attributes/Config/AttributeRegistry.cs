using System.Collections.Generic;
using Alchemy.Inspector;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// All attribute / flag / effect definitions of the game, indexed by id. Rebuilt (ids assigned +
    /// <c>Attr.g.cs</c> generated) by <c>RumOverboard ▸ Attributes ▸ Rebuild Registry</c>.
    /// Loaded from Resources at <see cref="ResourcesPath"/>; needed at runtime to resolve replicated
    /// effect ids on clients and for debug UI. Gameplay code uses the generated constants.
    /// </summary>
    public sealed class AttributeRegistry : ScriptableObject
    {
        public const string ResourcesPath = "AttributeRegistry";

        [SerializeField, ReadOnly] private List<AttributeDefinition> attributes = new();
        [SerializeField, ReadOnly] private List<FlagDefinition> flags = new();
        [SerializeField, ReadOnly] private List<EffectDefinition> effects = new();
        [SerializeField, ReadOnly] private int hash;

        private static AttributeRegistry _instance;

        private AttributeDefinition[] _attributeById;
        private FlagDefinition[] _flagById;
        private EffectDefinition[] _effectById;

        public static AttributeRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<AttributeRegistry>(ResourcesPath);
                    if (_instance == null)
                        Debug.LogError("[Attributes] No AttributeRegistry in Resources — run RumOverboard ▸ Attributes ▸ Rebuild Registry.");
                }
                return _instance;
            }
        }

        public IReadOnlyList<AttributeDefinition> AllAttributes => attributes;
        public IReadOnlyList<FlagDefinition> AllFlags => flags;
        public IReadOnlyList<EffectDefinition> AllEffects => effects;

        /// <summary>Hash of every id ↔ asset pair. Peers with different hashes can't share a session.</summary>
        public int Hash => hash;

        public AttributeDefinition Get(AttributeId id)
        {
            EnsureLookups();
            return id.Value < _attributeById.Length ? _attributeById[id.Value] : null;
        }

        public FlagDefinition Get(FlagId id)
        {
            EnsureLookups();
            return id.Value < _flagById.Length ? _flagById[id.Value] : null;
        }

        public EffectDefinition Get(EffectId id)
        {
            EnsureLookups();
            return id.Value < _effectById.Length ? _effectById[id.Value] : null;
        }

        private void EnsureLookups()
        {
            if (_attributeById != null) return;

            int max = 0;
            foreach (var a in attributes) if (a != null) max = Mathf.Max(max, a.Id.Value);
            _attributeById = new AttributeDefinition[max + 1];
            foreach (var a in attributes) if (a != null) _attributeById[a.Id.Value] = a;

            _flagById = new FlagDefinition[FlagId.MaxFlags + 1];
            foreach (var f in flags) if (f != null && f.Id.IsValid) _flagById[f.Id.Value] = f;

            max = 0;
            foreach (var e in effects) if (e != null) max = Mathf.Max(max, e.Id.Value);
            _effectById = new EffectDefinition[max + 1];
            foreach (var e in effects) if (e != null) _effectById[e.Id.Value] = e;
        }

        private void OnValidate()
        {
            _attributeById = null;
            _flagById = null;
            _effectById = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

#if UNITY_EDITOR
        /// <summary>Tooling only (registry rebuild).</summary>
        public void EditorSet(List<AttributeDefinition> attributeDefs, List<FlagDefinition> flagDefs,
                              List<EffectDefinition> effectDefs, int registryHash)
        {
            attributes = attributeDefs;
            flags = flagDefs;
            effects = effectDefs;
            hash = registryHash;
            OnValidate();
        }
#endif
    }
}
