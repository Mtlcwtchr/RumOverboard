#if FUSION2
using Fusion;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>One replicated active effect: id + stacks + timing (clients derive remaining time locally).</summary>
    public struct EffectNetEntry : INetworkStruct
    {
        public ushort Effect;
        public byte Stacks;
        public int StartTick;
        public int EndTick;

        public bool SameAs(in EffectNetEntry o) =>
            Effect == o.Effect && Stacks == o.Stacks && StartTick == o.StartTick && EndTick == o.EndTick;
    }

    /// <summary>
    /// Replicates an <see cref="AttributesComponent"/> — host authority, no client prediction
    /// (same model as the rest of the game):
    ///   • the host ticks effects in FixedUpdateNetwork and writes, when something changed:
    ///       – final value of every replicated attribute (+ current for resources) as floats,
    ///       – the effective flag mask (one ulong),
    ///       – the replicated active effects (id, stacks, start/end tick);
    ///   • modifiers are NOT replicated — clients don't compute stats, they only show results;
    ///   • clients copy the state into their local AttributeSet in Render(), so UI/VFX code reads
    ///     the same API on every peer and polls Set.Version / ConsumeChanges().
    /// Fusion only sends changed words, so quiet attributes cost nothing after spawn.
    ///
    /// Add only where values change at runtime and clients must see them. Objects whose attributes
    /// never change (pure profile data) need just AttributesComponent — every peer has the asset.
    /// </summary>
    [RequireComponent(typeof(AttributesComponent))]
    public sealed class NetworkAttributes : NetworkBehaviour
    {
        public const int MaxValues = 32;
        public const int MaxEffects = 8;

        [Networked, Capacity(MaxValues)] private NetworkArray<float> Values { get; }
        [Networked, Capacity(MaxEffects)] private NetworkArray<EffectNetEntry> Effects { get; }
        [Networked] private ulong Flags { get; set; }
        [Networked] private byte EffectCount { get; set; }
        [Networked] private int LayoutHash { get; set; }

        private AttributesComponent _attributes;
        private int _writtenVersion = -1;

        // Client-side cache to rebuild the effect mirror only when it changed.
        private readonly EffectNetEntry[] _seenEffects = new EffectNetEntry[MaxEffects];
        private int _seenEffectCount = -1;

        public AttributesComponent Attributes => _attributes;

        /// <summary>Spawned by Fusion and replicating.</summary>
        public bool IsReady => Object != null && Object.IsValid;

        private void Awake()
        {
            _attributes = GetComponent<AttributesComponent>();
            // Until Spawned decides, nobody may mutate or self-tick (scene objects exist before the
            // runner starts; on a client, trigger zones would otherwise apply effects locally).
            _attributes.SetNetworkDriven(authority: false);
        }

        public override void Spawned()
        {
            _attributes.EnsureBuilt();
            _attributes.SetNetworkDriven(HasStateAuthority);
            AttributeSet set = _attributes.Set;
            if (set == null) return;

            if (set.Layout.ReplicatedFloatCount > MaxValues)
                Debug.LogError($"[Attributes] '{name}': {set.Layout.ReplicatedFloatCount} replicated floats > {MaxValues}; the rest is not sent.", this);

            if (HasStateAuthority)
            {
                LayoutHash = set.Layout.Hash;
                Write(set);
            }
            else
            {
                if (LayoutHash != set.Layout.Hash)
                    Debug.LogError($"[Attributes] '{name}': layout hash mismatch with host — different profile/registry build.", this);
                Read(set);
            }
        }

        public override void FixedUpdateNetwork()
        {
            AttributeSet set = _attributes.Set;
            if (!HasStateAuthority || set == null) return;

            _attributes.SetClock(Runner.Tick, 1f / Runner.DeltaTime);
            set.Tick(Runner.Tick, Runner.DeltaTime);

            if (set.Version != _writtenVersion)
                Write(set);
        }

        public override void Render()
        {
            AttributeSet set = _attributes.Set;
            if (HasStateAuthority || set == null) return;

            _attributes.SetClock(Runner.Tick, 1f / Runner.DeltaTime);
            Read(set);
        }

        private void Write(AttributeSet set)
        {
            AttributeLayout layout = set.Layout;
            int k = 0;
            foreach (int slot in layout.ReplicatedSlots)
            {
                if (k >= MaxValues) break;
                Values.Set(k++, set.GetSlotValue(slot));
                if (set.IsResource(slot) && k < MaxValues)
                    Values.Set(k++, set.GetSlotCurrent(slot));
            }

            Flags = set.Flags;

            int n = 0;
            for (int i = 0; i < set.Effects.Count && n < MaxEffects; i++)
            {
                AttributeSet.ActiveEffect e = set.Effects[i];
                if (!e.Definition.Replicated || !e.Definition.Id.IsValid) continue;
                Effects.Set(n++, new EffectNetEntry
                {
                    Effect = e.Definition.Id.Value,
                    Stacks = (byte)Mathf.Min(e.Stacks, 255),
                    StartTick = e.StartTick,
                    EndTick = e.EndTick,
                });
            }
            EffectCount = (byte)n;

            _writtenVersion = set.Version;
        }

        private void Read(AttributeSet set)
        {
            AttributeLayout layout = set.Layout;
            int k = 0;
            foreach (int slot in layout.ReplicatedSlots)
            {
                if (k >= MaxValues) break;
                float value = Values[k++];
                float current = value;
                if (set.IsResource(slot) && k < MaxValues)
                    current = Values[k++];
                set.ApplyReplicated(slot, value, current);
            }

            set.ApplyReplicatedFlags(Flags);

            int count = EffectCount;
            bool changed = count != _seenEffectCount;
            for (int i = 0; i < count && !changed; i++)
                changed = !Effects[i].SameAs(_seenEffects[i]);
            if (!changed) return;

            AttributeRegistry registry = AttributeRegistry.Instance;
            set.BeginReplicatedEffects();
            for (int i = 0; i < count; i++)
            {
                EffectNetEntry e = Effects[i];
                _seenEffects[i] = e;
                EffectDefinition def = registry != null ? registry.Get(new EffectId(e.Effect)) : null;
                if (def != null)
                    set.AddReplicatedEffect(def, e.Stacks, e.StartTick, e.EndTick);
            }
            set.EndReplicatedEffects();
            _seenEffectCount = count;
        }
    }
}
#endif
