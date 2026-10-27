using System.Collections.Generic;
using RumOverboard.Core.Sim;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// Puts an entity profile on a GameObject (player, loot, physical prop, static destructible):
    /// builds the runtime <see cref="AttributeSet"/> from <see cref="AttributeProfile"/> + per-instance
    /// overrides, registers in <see cref="SimWorld"/> (so a collider hit resolves to attributes in O(1))
    /// and exposes the profile's static data blocks.
    ///
    /// Offline / non-networked objects tick their effects themselves (FixedUpdate). With a
    /// <c>NetworkAttributes</c> next to it the network drives the clock and authority instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttributesComponent : SimComponent<AttributesComponent>
    {
        [SerializeField] private AttributeProfile profile;

        [Tooltip("Per-instance base values on top of the profile (only attributes the profile has).")]
        [SerializeField] private List<AttributeEntry> overrides = new();

        [Tooltip("Tick effects in FixedUpdate. Turned off automatically by NetworkAttributes.")]
        [SerializeField] private bool selfTick = true;

        public AttributeProfile Profile => profile;
        public AttributeSet Set { get; private set; }

        /// <summary>False on network clients: values are replicated, gameplay must not mutate them.</summary>
        public bool IsAuthority { get; private set; } = true;

        /// <summary>Clock used for effect timing (network tick when networked).</summary>
        public int Tick { get; private set; }
        public float TickRate { get; private set; } = 50f;

        private void Awake() => EnsureBuilt();

        public void EnsureBuilt()
        {
            if (Set != null) return;
            if (profile == null)
            {
                Debug.LogWarning($"[Attributes] '{name}' has no profile.", this);
                return;
            }
            Set = new AttributeSet(profile.Layout);
            ApplyOverrides();
        }

        /// <summary>Back to profile + overrides (drops modifiers and effects). Host/offline only.</summary>
        public void ResetToProfile()
        {
            if (Set == null) return;
            Set.Reset();
            ApplyOverrides();
        }

        /// <summary>Called by NetworkAttributes on spawn.</summary>
        public void SetNetworkDriven(bool authority)
        {
            selfTick = false;
            IsAuthority = authority;
        }

        public void SetClock(int tick, float tickRate)
        {
            Tick = tick;
            TickRate = tickRate;
        }

        private void FixedUpdate()
        {
            if (!selfTick || Set == null) return;
            Tick++;
            TickRate = 1f / Time.fixedDeltaTime;
            Set.Tick(Tick, Time.fixedDeltaTime);
        }

        private void ApplyOverrides()
        {
            for (int i = 0; i < overrides.Count; i++)
            {
                AttributeEntry o = overrides[i];
                if (o.Attribute == null) continue;
                int slot = Set.SlotOf(o.Attribute.Id);
                if (slot < 0) continue;
                Set.SetSlotBase(slot, o.BaseValue);
                if (Set.IsResource(slot) && o.Attribute.StartFull)
                    Set.SetSlotCurrent(slot, Set.GetSlotValue(slot));
            }
        }

        // ───────────────────────── convenience ─────────────────────────

        public bool Has(AttributeId id) => Set != null && Set.Has(id);
        public float Get(AttributeId id, float fallback = 0f) => Set != null ? Set.Get(id, fallback) : fallback;
        public float GetCurrent(AttributeId id, float fallback = 0f) => Set != null ? Set.GetCurrent(id, fallback) : fallback;
        public bool HasFlag(FlagId flag) => Set != null && Set.HasFlag(flag);
        public bool HasEffect(EffectId id) => Set != null && Set.HasEffect(id);

        public T GetData<T>() where T : class, IEntityData => profile != null ? profile.GetData<T>() : null;

        public bool ApplyEffect(EffectId id, int stacks = 1) =>
            ApplyEffect(AttributeRegistry.Instance != null ? AttributeRegistry.Instance.Get(id) : null, stacks);

        public bool ApplyEffect(EffectDefinition effect, int stacks = 1)
        {
            if (Set == null || effect == null) return false;
            if (!IsAuthority)
            {
                Debug.LogWarning($"[Attributes] ApplyEffect('{effect.name}') on a client ignored — host only.", this);
                return false;
            }
            return Set.ApplyEffect(effect, Tick, TickRate, stacks);
        }

        public bool RemoveEffect(EffectId id) =>
            RemoveEffect(AttributeRegistry.Instance != null ? AttributeRegistry.Instance.Get(id) : null);

        public bool RemoveEffect(EffectDefinition effect) => Set != null && IsAuthority && Set.RemoveEffect(effect);

        public static bool TryGet(Collider collider, out AttributesComponent attributes) =>
            SimWorld.TryGetByCollider(collider, out attributes);
    }
}
