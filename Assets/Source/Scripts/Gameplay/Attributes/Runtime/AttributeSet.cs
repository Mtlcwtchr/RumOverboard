using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// Runtime attribute values of ONE entity, built from an <see cref="AttributeLayout"/>.
    /// Plain C# (no Unity objects / Fusion) so it is cheap, testable and works offline.
    ///
    ///   value(slot)   = clamp(base + Σ additive modifiers)        — stats; for resources it is the max
    ///   current(slot) = clamp(current, Min, value)                — resources only
    ///   flags         = base flags | flags granted by sources (effects, zones…)
    ///
    /// Reads are O(1), no allocations. There are no per-value C# events: every change bumps
    /// <see cref="Version"/> and sets bits in the change masks; consumers poll once per tick/frame
    /// (<see cref="ConsumeChanges"/>) — cheap with hundreds of objects and no event cascades.
    ///
    /// AUTHORITY: mutate only on the host (or offline). On clients <c>NetworkAttributes</c> overwrites
    /// values / flags / effects from the replicated state through the <c>ApplyReplicated*</c> methods.
    /// </summary>
    public sealed class AttributeSet
    {
        public struct ActiveEffect
        {
            public EffectDefinition Definition;
            public int Stacks;
            public int StartTick;
            public int EndTick; // int.MaxValue = until removed
            public ModifierSource Source;

            public bool IsInfinite => EndTick == int.MaxValue;

            public float RemainingSeconds(int nowTick, float tickRate) =>
                IsInfinite ? float.PositiveInfinity : Mathf.Max(0f, (EndTick - nowTick) / tickRate);
        }

        private struct Modifier
        {
            public int Slot;
            public float Value;
            public ModifierSource Source;
        }

        private struct FlagGrant
        {
            public ulong Mask;
            public ModifierSource Source;
        }

        private readonly float[] _base;
        private readonly float[] _modSum;
        private readonly float[] _value;
        private readonly float[] _current;
        private ulong _baseFlags;
        private ulong _flags;

        private readonly List<Modifier> _modifiers = new();
        private readonly List<FlagGrant> _grants = new();
        private readonly List<ActiveEffect> _effects = new();

        public AttributeLayout Layout { get; }

        /// <summary>Incremented on every observable change (values, current, flags, effects).</summary>
        public int Version { get; private set; }

        public ulong ChangedSlots { get; private set; }
        public bool FlagsChanged { get; private set; }
        public bool EffectsChanged { get; private set; }

        public ulong Flags => _flags;
        public IReadOnlyList<ActiveEffect> Effects => _effects;

        public AttributeSet(AttributeLayout layout)
        {
            Layout = layout;
            int n = layout.Count;
            _base = new float[n];
            _modSum = new float[n];
            _value = new float[n];
            _current = new float[n];
            Reset();
        }

        // ───────────────────────── reads ─────────────────────────

        public int SlotOf(AttributeId id) => Layout.SlotOf(id);
        public bool Has(AttributeId id) => Layout.SlotOf(id) >= 0;

        /// <summary>Effective value (for a resource: its max).</summary>
        public float Get(AttributeId id, float fallback = 0f)
        {
            int s = Layout.SlotOf(id);
            return s < 0 ? fallback : _value[s];
        }

        public float GetBase(AttributeId id, float fallback = 0f)
        {
            int s = Layout.SlotOf(id);
            return s < 0 ? fallback : _base[s];
        }

        /// <summary>Current amount of a resource (for a stat: same as <see cref="Get"/>).</summary>
        public float GetCurrent(AttributeId id, float fallback = 0f)
        {
            int s = Layout.SlotOf(id);
            if (s < 0) return fallback;
            return IsResource(s) ? _current[s] : _value[s];
        }

        /// <summary>Current / max in 0..1 (resources). 0 when absent or max is 0.</summary>
        public float GetNormalized(AttributeId id)
        {
            int s = Layout.SlotOf(id);
            if (s < 0 || _value[s] <= 0f) return 0f;
            return Mathf.Clamp01((IsResource(s) ? _current[s] : _value[s]) / _value[s]);
        }

        public bool HasFlag(FlagId flag) => (_flags & flag.Mask) != 0;

        public float GetSlotValue(int slot) => _value[slot];
        public float GetSlotBase(int slot) => _base[slot];
        public float GetSlotCurrent(int slot) => IsResource(slot) ? _current[slot] : _value[slot];
        public bool IsResource(int slot) => Layout.Definitions[slot].Kind == AttributeKind.Resource;

        // ───────────────────────── writes (host) ─────────────────────────

        public void SetBase(AttributeId id, float value)
        {
            int s = Layout.SlotOf(id);
            if (s >= 0) SetSlotBase(s, value);
        }

        public void SetSlotBase(int slot, float value)
        {
            value = Layout.Definitions[slot].Clamp(value);
            if (_base[slot] == value) return;
            _base[slot] = value;
            Recompute(slot);
        }

        public void SetCurrent(AttributeId id, float value)
        {
            int s = Layout.SlotOf(id);
            if (s >= 0) SetSlotCurrent(s, value);
        }

        public void SetSlotCurrent(int slot, float value)
        {
            if (!IsResource(slot)) return;
            value = Mathf.Clamp(value, Layout.Definitions[slot].Min, _value[slot]);
            if (_current[slot] == value) return;
            _current[slot] = value;
            MarkSlot(slot);
        }

        /// <summary>Adds to a resource's current amount; returns the change actually applied.</summary>
        public float AddCurrent(AttributeId id, float delta)
        {
            int s = Layout.SlotOf(id);
            if (s < 0 || !IsResource(s)) return 0f;
            float before = _current[s];
            SetSlotCurrent(s, before + delta);
            return _current[s] - before;
        }

        /// <summary>Spends <paramref name="amount"/> only if the whole amount is available.</summary>
        public bool TrySpend(AttributeId id, float amount)
        {
            int s = Layout.SlotOf(id);
            if (s < 0 || !IsResource(s) || _current[s] - amount < Layout.Definitions[s].Min) return false;
            SetSlotCurrent(s, _current[s] - amount);
            return true;
        }

        public void SetFlag(FlagId flag, bool on)
        {
            ulong mask = flag.Mask;
            _baseFlags = on ? _baseFlags | mask : _baseFlags & ~mask;
            RecomputeFlags();
        }

        // ───────────────────────── modifiers ─────────────────────────

        /// <summary>Additive modifier owned by <paramref name="source"/>. Ignored if the entity lacks the attribute.</summary>
        public void AddModifier(AttributeId id, float value, ModifierSource source)
        {
            int s = Layout.SlotOf(id);
            if (s < 0 || value == 0f) return;
            _modifiers.Add(new Modifier { Slot = s, Value = value, Source = source });
            _modSum[s] += value;
            Recompute(s);
        }

        public void GrantFlag(FlagId flag, ModifierSource source)
        {
            if (!flag.IsValid) return;
            _grants.Add(new FlagGrant { Mask = flag.Mask, Source = source });
            RecomputeFlags();
        }

        /// <summary>Removes every modifier and flag grant of <paramref name="source"/>.</summary>
        public void RemoveSource(ModifierSource source)
        {
            ulong touched = 0;
            for (int i = _modifiers.Count - 1; i >= 0; i--)
            {
                if (_modifiers[i].Source != source) continue;
                touched |= 1UL << _modifiers[i].Slot;
                _modifiers.RemoveAt(i);
            }

            if (touched != 0)
            {
                // Re-sum instead of subtracting to avoid float drift.
                for (int s = 0; s < Layout.Count; s++)
                    if ((touched & (1UL << s)) != 0)
                        _modSum[s] = 0f;
                for (int i = 0; i < _modifiers.Count; i++)
                    if ((touched & (1UL << _modifiers[i].Slot)) != 0)
                        _modSum[_modifiers[i].Slot] += _modifiers[i].Value;
                for (int s = 0; s < Layout.Count; s++)
                    if ((touched & (1UL << s)) != 0)
                        Recompute(s);
            }

            bool grantsRemoved = false;
            for (int i = _grants.Count - 1; i >= 0; i--)
            {
                if (_grants[i].Source != source) continue;
                _grants.RemoveAt(i);
                grantsRemoved = true;
            }
            if (grantsRemoved) RecomputeFlags();
        }

        // ───────────────────────── effects ─────────────────────────

        public int IndexOfEffect(EffectDefinition def)
        {
            for (int i = 0; i < _effects.Count; i++)
                if (_effects[i].Definition == def)
                    return i;
            return -1;
        }

        public bool HasEffect(EffectDefinition def) => IndexOfEffect(def) >= 0;

        public bool HasEffect(EffectId id)
        {
            for (int i = 0; i < _effects.Count; i++)
                if (_effects[i].Definition != null && _effects[i].Definition.Id == id)
                    return true;
            return false;
        }

        public int GetStacks(EffectDefinition def)
        {
            int i = IndexOfEffect(def);
            return i < 0 ? 0 : _effects[i].Stacks;
        }

        /// <summary>
        /// Applies (or re-applies per <see cref="EffectDefinition.Stacking"/>) an effect.
        /// <paramref name="tick"/>/<paramref name="tickRate"/> = network tick + ticks per second.
        /// Returns false when ignored.
        /// </summary>
        public bool ApplyEffect(EffectDefinition def, int tick, float tickRate, int stacks = 1)
        {
            if (def == null || stacks <= 0) return false;
            int endTick = def.Duration > 0f
                ? tick + Mathf.Max(1, Mathf.RoundToInt(def.Duration * tickRate))
                : int.MaxValue;

            int index = IndexOfEffect(def);
            if (index < 0)
            {
                var e = new ActiveEffect
                {
                    Definition = def,
                    Stacks = Mathf.Clamp(stacks, 1, Mathf.Max(1, def.MaxStacks)),
                    StartTick = tick,
                    EndTick = endTick,
                    Source = ModifierSource.New(),
                };
                ApplyEffectModifiers(e);
                _effects.Add(e);
                MarkEffects();
                return true;
            }

            ActiveEffect active = _effects[index];
            switch (def.Stacking)
            {
                case EffectStacking.Ignore:
                    return false;
                case EffectStacking.Refresh:
                    active.EndTick = endTick;
                    break;
                case EffectStacking.Stack:
                    int newStacks = Mathf.Min(active.Stacks + stacks, Mathf.Max(1, def.MaxStacks));
                    active.EndTick = endTick;
                    if (newStacks != active.Stacks)
                    {
                        active.Stacks = newStacks;
                        RemoveSource(active.Source);
                        ApplyEffectModifiers(active);
                    }
                    break;
            }
            _effects[index] = active;
            MarkEffects();
            return true;
        }

        public bool RemoveEffect(EffectDefinition def)
        {
            int index = IndexOfEffect(def);
            if (index < 0) return false;
            RemoveEffectAt(index);
            return true;
        }

        public void ClearEffects()
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
                RemoveEffectAt(i);
        }

        /// <summary>Host simulation step: periodic resource changes + expiry.</summary>
        public void Tick(int tick, float deltaTime)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                ActiveEffect e = _effects[i];
                List<EffectPeriodic> periodic = e.Definition.Periodic;
                for (int p = 0; p < periodic.Count; p++)
                {
                    AttributeDefinition res = periodic[p].Resource;
                    if (res != null)
                        AddCurrent(res.Id, periodic[p].PerSecondPerStack * e.Stacks * deltaTime);
                }

                if (tick >= e.EndTick)
                    RemoveEffectAt(i);
            }
        }

        private void ApplyEffectModifiers(in ActiveEffect e)
        {
            List<EffectModifier> mods = e.Definition.Modifiers;
            for (int m = 0; m < mods.Count; m++)
                if (mods[m].Attribute != null)
                    AddModifier(mods[m].Attribute.Id, mods[m].ValuePerStack * e.Stacks, e.Source);

            List<FlagDefinition> flags = e.Definition.GrantedFlags;
            for (int f = 0; f < flags.Count; f++)
                if (flags[f] != null)
                    GrantFlag(flags[f].Id, e.Source);
        }

        private void RemoveEffectAt(int index)
        {
            ModifierSource source = _effects[index].Source;
            _effects.RemoveAt(index);
            if (source.IsValid) RemoveSource(source);
            MarkEffects();
        }

        // ───────────────────────── reset ─────────────────────────

        /// <summary>Back to the profile: base values, full/empty resources, base flags; drops modifiers and effects.</summary>
        public void Reset()
        {
            _modifiers.Clear();
            _grants.Clear();
            _effects.Clear();
            for (int s = 0; s < Layout.Count; s++)
            {
                AttributeDefinition def = Layout.Definitions[s];
                _base[s] = Layout.BaseValues[s];
                _modSum[s] = 0f;
                _value[s] = def.Clamp(_base[s]);
                _current[s] = def.Kind == AttributeKind.Resource && def.StartFull ? _value[s] : def.Min;
            }
            _baseFlags = Layout.BaseFlags;
            _flags = _baseFlags;

            Version++;
            ChangedSlots = Layout.Count >= 64 ? ulong.MaxValue : (1UL << Layout.Count) - 1;
            FlagsChanged = true;
            EffectsChanged = true;
        }

        // ───────────────────────── change tracking ─────────────────────────

        /// <summary>Returns and clears what changed since the previous call.</summary>
        public void ConsumeChanges(out ulong slots, out bool flags, out bool effects)
        {
            slots = ChangedSlots;
            flags = FlagsChanged;
            effects = EffectsChanged;
            ChangedSlots = 0;
            FlagsChanged = false;
            EffectsChanged = false;
        }

        // ───────────────────────── replication (clients) ─────────────────────────

        public void ApplyReplicated(int slot, float value, float current)
        {
            bool changed = false;
            if (_value[slot] != value) { _value[slot] = value; changed = true; }
            if (IsResource(slot) && _current[slot] != current) { _current[slot] = current; changed = true; }
            if (changed) MarkSlot(slot);
        }

        public void ApplyReplicatedFlags(ulong flags)
        {
            if (_flags == flags) return;
            _flags = flags;
            FlagsChanged = true;
            Version++;
        }

        /// <summary>Client mirror of the host's effect list (no modifiers applied — values come replicated).</summary>
        public void BeginReplicatedEffects() => _effects.Clear();

        public void AddReplicatedEffect(EffectDefinition def, int stacks, int startTick, int endTick)
        {
            _effects.Add(new ActiveEffect { Definition = def, Stacks = stacks, StartTick = startTick, EndTick = endTick });
        }

        public void EndReplicatedEffects() => MarkEffects();

        // ───────────────────────── internals ─────────────────────────

        private void Recompute(int slot)
        {
            AttributeDefinition def = Layout.Definitions[slot];
            float v = def.Clamp(_base[slot] + _modSum[slot]);
            bool changed = _value[slot] != v;
            _value[slot] = v;
            if (def.Kind == AttributeKind.Resource)
            {
                float c = Mathf.Clamp(_current[slot], def.Min, v);
                if (c != _current[slot]) { _current[slot] = c; changed = true; }
            }
            if (changed) MarkSlot(slot);
        }

        private void RecomputeFlags()
        {
            ulong flags = _baseFlags;
            for (int i = 0; i < _grants.Count; i++)
                flags |= _grants[i].Mask;
            if (flags == _flags) return;
            _flags = flags;
            FlagsChanged = true;
            Version++;
        }

        private void MarkSlot(int slot)
        {
            ChangedSlots |= 1UL << slot;
            Version++;
        }

        private void MarkEffects()
        {
            EffectsChanged = true;
            Version++;
        }
    }
}
