using Alchemy.Inspector;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    public enum AttributeKind : byte
    {
        /// <summary>Plain number: value = clamp(base + Σ additive modifiers).</summary>
        Stat,

        /// <summary>
        /// Pool with a current amount (stamina, integrity, grog…): the stat value is the max,
        /// <c>Current</c> lives in [Min, value] and is spent / restored by gameplay.
        /// </summary>
        Resource,
    }

    /// <summary>
    /// One numeric attribute type (asset = definition, no class per attribute).
    /// The id is assigned by <c>RumOverboard ▸ Attributes ▸ Rebuild Registry</c>; code refers to it
    /// through the generated <c>Attr.&lt;AssetName&gt;</c> constant.
    /// </summary>
    [CreateAssetMenu(fileName = "Attribute", menuName = "RumOverboard/Attributes/Attribute")]
    public sealed class AttributeDefinition : ScriptableObject
    {
        [SerializeField, ReadOnly] private ushort id;

        public AttributeKind Kind = AttributeKind.Stat;

        [Tooltip("Base value used when a profile lists the attribute without overriding it.")]
        public float DefaultValue;

        public float Min;
        public float Max = 1_000_000f;

        [Tooltip("Resource only: start with Current = max (otherwise Current = Min).")]
        public bool StartFull = true;

        [Tooltip("Sent to clients by NetworkAttributes (value, and current for resources). " +
                 "Off = host-only (internal tuning, AI, physics).")]
        public bool Replicated = true;

        [TextArea] public string Description;

        public AttributeId Id => new AttributeId(id);

        public float Clamp(float value) => value < Min ? Min : value > Max ? Max : value;

#if UNITY_EDITOR
        /// <summary>Tooling only (registry rebuild / self-tests).</summary>
        public void EditorSetId(ushort value) => id = value;
#endif
    }
}
