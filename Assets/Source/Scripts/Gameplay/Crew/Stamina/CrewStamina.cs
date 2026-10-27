using RumOverboard.Gameplay.Attributes;
using UnityEngine;

namespace RumOverboard.Gameplay.Crew
{
    /// <summary>What a crew member spent effort on during one tick (built by the driver after the state machine ran).</summary>
    public struct StaminaActivity
    {
        public bool Sprinting;      // sprinting on deck
        public bool Jumped;         // jump / swim lunge executed
        public bool Climbing;       // moving on a rail
        public bool ClimbSprint;    // …with sprint held
        public bool Swimming;
        public float SwimEffort;    // 0..1 stroke input (0 = treading water)
        public bool Hauling;        // hauling, or walking with a loaded line
        public float LineLoad;      // 0..2+ load of the line in hand
        public bool Carrying;       // moving with an item in hands
        public float CarryLoad;     // item mass / CarryCapacity (0..1)

        public bool Any => Sprinting || Jumped || Climbing || Swimming || Hauling || Carrying;
    }

    /// <summary>Gates the stamina model hands to the state machine before a tick.</summary>
    public readonly struct StaminaGates
    {
        public static readonly StaminaGates Open = new StaminaGates(true, true, 1f);

        public readonly bool CanSprint;
        public readonly bool CanJump;
        public readonly float SpeedFactor; // climb / swim speed multiplier

        public StaminaGates(bool canSprint, bool canJump, float speedFactor)
        {
            CanSprint = canSprint;
            CanJump = canJump;
            SpeedFactor = speedFactor;
        }
    }

    /// <summary>
    /// Crew stamina (GDD §13.4) on top of the attribute system — host only, plain C#.
    ///   • Spent by sprinting, jumping, climbing, swimming and hauling / walking with a loaded line.
    ///   • Regenerates automatically after <c>StaminaRegenDelay</c> s without spending, at <c>StaminaRegen</c>/s
    ///     (a stat — the Cold effect lowers it with a modifier).
    ///   • Hitting zero sets the <c>Exhausted</c> flag until <c>StaminaRecoverThreshold</c> is regained:
    ///     no sprint, no jump, climb/swim at <c>StaminaExhaustedSpeed</c>. Walking is always free.
    /// Every number is an attribute of the crew member's profile (Player), so it is tunable live (F6/F7),
    /// can be modified by effects (wet, drunk, cold…) and saved back to the profile.
    /// The only replicated outputs are the Stamina resource and the Exhausted flag (NetworkAttributes).
    /// Without a Stamina attribute everything stays open and nothing is spent.
    /// </summary>
    public sealed class CrewStamina
    {
        private float _sinceSpend = float.MaxValue;

        /// <summary>Stamina spent during the last <see cref="Step"/> (per tick, for debug).</summary>
        public float LastSpent { get; private set; }

        /// <summary>Seconds since stamina was last spent (host-side, for debug).</summary>
        public float SinceSpend => _sinceSpend;

        // Running debug counters (host-side; tests and the F7 window read them).
        public int Jumps { get; private set; }
        public float SprintSeconds { get; private set; }
        public float TotalSpent { get; private set; }

        public static StaminaGates GatesFor(AttributeSet set)
        {
            if (set == null || !set.Has(Attr.Stamina))
                return StaminaGates.Open;

            bool exhausted = set.HasFlag(AttrFlag.Exhausted);
            float current = set.GetCurrent(Attr.Stamina);
            float jumpCost = set.Get(Attr.StaminaJumpCost);
            float slow = Mathf.Clamp(set.Get(Attr.StaminaExhaustedSpeed, 1f), 0.05f, 1f);
            return new StaminaGates(
                canSprint: !exhausted && current > 0f,
                canJump: !exhausted && current >= jumpCost,
                speedFactor: exhausted ? slow : 1f);
        }

        /// <summary>Per-second drain of an activity (jump excluded — it's a one-off cost).</summary>
        public static float DrainPerSecond(AttributeSet set, in StaminaActivity a)
        {
            float drain = 0f;
            if (a.Sprinting) drain += set.Get(Attr.StaminaSprintCost);
            if (a.Climbing) drain += set.Get(Attr.StaminaClimbCost) * (a.ClimbSprint ? 2f : 1f);
            if (a.Swimming) drain += set.Get(Attr.StaminaSwimCost) * (0.3f + 0.7f * Mathf.Clamp01(a.SwimEffort));
            if (a.Hauling) drain += set.Get(Attr.StaminaHaulCost) * Mathf.Clamp(a.LineLoad, 0.25f, 2f);
            if (a.Carrying) drain += set.Get(Attr.StaminaCarryCost) * Mathf.Clamp01(a.CarryLoad);
            return drain;
        }

        /// <summary>One host tick: spend for the activity, regenerate, update the Exhausted flag.</summary>
        public void Step(AttributeSet set, in StaminaActivity activity, float dt)
        {
            LastSpent = 0f;
            if (set == null || !set.Has(Attr.Stamina) || dt <= 0f)
                return;

            float cost = DrainPerSecond(set, activity) * dt;
            if (activity.Jumped)
            {
                cost += set.Get(Attr.StaminaJumpCost);
                Jumps++;
            }
            if (activity.Sprinting) SprintSeconds += dt;

            if (cost > 0f)
            {
                LastSpent = -set.AddCurrent(Attr.Stamina, -cost);
                TotalSpent += LastSpent;
                _sinceSpend = 0f;
            }
            else
            {
                if (_sinceSpend < float.MaxValue) _sinceSpend += dt;
                if (_sinceSpend >= set.Get(Attr.StaminaRegenDelay))
                    set.AddCurrent(Attr.Stamina, Mathf.Max(0f, set.Get(Attr.StaminaRegen)) * dt);
            }

            float current = set.GetCurrent(Attr.Stamina);
            bool exhausted = set.HasFlag(AttrFlag.Exhausted);
            if (!exhausted && current <= 0f)
                set.SetFlag(AttrFlag.Exhausted, true);
            else if (exhausted && current >= Mathf.Min(set.Get(Attr.StaminaRecoverThreshold), set.Get(Attr.Stamina)))
                set.SetFlag(AttrFlag.Exhausted, false);
        }

        /// <summary>Forget the spend timer (respawn / reset): regeneration starts immediately.</summary>
        public void Reset() => _sinceSpend = float.MaxValue;
    }
}
