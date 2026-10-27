using System;
using System.Collections.Generic;
using Alchemy.Inspector;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    public enum EffectStacking : byte
    {
        /// <summary>Re-applying restarts the timer, stacks stay at 1.</summary>
        Refresh,

        /// <summary>Re-applying adds stacks (up to MaxStacks) and restarts the timer.</summary>
        Stack,

        /// <summary>Re-applying while active does nothing.</summary>
        Ignore,
    }

    [Serializable]
    public struct EffectModifier
    {
        public AttributeDefinition Attribute;

        [Tooltip("Additive change of the attribute value, multiplied by the stack count.")]
        public float ValuePerStack;
    }

    [Serializable]
    public struct EffectPeriodic
    {
        [Tooltip("Resource whose Current is changed every tick (stats are ignored).")]
        public AttributeDefinition Resource;

        [Tooltip("Change of Current per second per stack (negative = drain).")]
        public float PerSecondPerStack;
    }

    /// <summary>
    /// Temporary state on an entity (Wet, Cold, Burning, Drunk…): additive modifiers + granted flags
    /// + periodic resource change, timed in network ticks. Applied only on the host; clients see the
    /// replicated (id, stacks, end tick) list and the resulting values.
    /// Code refers to it through the generated <c>AttrEffect.&lt;AssetName&gt;</c> constant.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect", menuName = "RumOverboard/Attributes/Effect")]
    public sealed class EffectDefinition : ScriptableObject
    {
        [SerializeField, ReadOnly] private ushort id;

        [Tooltip("Seconds. 0 = lasts until removed explicitly.")]
        public float Duration = 5f;

        public EffectStacking Stacking = EffectStacking.Refresh;
        [Min(1)] public int MaxStacks = 1;

        [Tooltip("Sent to clients (icons, VFX, remaining time).")]
        public bool Replicated = true;

        public List<EffectModifier> Modifiers = new();
        public List<FlagDefinition> GrantedFlags = new();
        public List<EffectPeriodic> Periodic = new();

        [TextArea] public string Description;

        public EffectId Id => new EffectId(id);

        public ulong GrantedMask
        {
            get
            {
                ulong mask = 0;
                for (int i = 0; i < GrantedFlags.Count; i++)
                    if (GrantedFlags[i] != null)
                        mask |= GrantedFlags[i].Id.Mask;
                return mask;
            }
        }

#if UNITY_EDITOR
        /// <summary>Tooling only (registry rebuild / self-tests).</summary>
        public void EditorSetId(ushort value) => id = value;
#endif
    }
}
