using System;
using RumOverboard.Core;
using RumOverboard.StateMachine.Serialization;
using UnityEngine;

namespace RumOverboard.StateMachine
{
    /// <summary>
    /// Every state, as a bit in a mask. The active set is a combination of bits, so
    /// "layers" are emergent: mutually-exclusive states simply aren't listed in each
    /// other's <see cref="PlayerStateBase.CompatibleWith"/>, while compatible ones
    /// (e.g. Climbing | DrinkingRum) can be active together. Ids are stable — the
    /// whole mask is what replicates. Keep it under 32 states (uint).
    /// </summary>
    [Flags]
    public enum PlayerState : uint
    {
        None = 0,

        // Locomotion group — mutually exclusive with each other.
        Grounded = 1u << 0,
        InAir = 1u << 1,
        Climbing = 1u << 2,
        Swimming = 1u << 3,
        Steering = 1u << 4, // standing at the helm; body locked, A/D turns the wheel

        // Action group — layered on top of locomotion.
        DrinkingRum = 1u << 8,
    }

    /// <summary>
    /// Shared, reused per-tick blackboard. Deliberately free of networking types, so
    /// the state machine compiles and unit-tests without Fusion — NetworkPlayer is
    /// the only bridge to the wire. The driver fills inputs/sensors before ticking
    /// and reads outputs after.
    /// </summary>
    public sealed class StateContext
    {
        // Owned references
        public Rigidbody Body;
        public Transform Transform;
        public Animator Animator;
        public GameConfig Config;
        public PlayerStateMachine Machine;

        // Sensors (filled by the driver each fixed tick)
        public bool IsGrounded;
        public bool NearClimb;
        public bool InWater;
        public bool NearHelm;

        // Velocity of whatever we're standing on (the ship deck), at the feet — includes the ship's
        // linear + rotational motion. Zero on static ground. Locomotion rides this so the crew moves
        // with the ship instead of sliding off.
        public Vector3 GroundVelocity;

        // Helm anchor (world) the crew member locks to while Steering — filled by the driver.
        public bool HasSteerAnchor;
        public Vector3 SteerAnchorPosition;
        public float SteerAnchorYaw;

        // Nearest climbable, resolved by the driver (primitives only, so this layer
        // never references the Gameplay assembly).
        public int ClimbTargetId;
        public Vector3 ClimbPoint;

        // Input (plain — translated from NetworkInputData by the driver).
        // Move is already scaled by ControlAuthority so drunk/ragdolled crew steer less.
        public Vector2 Move;
        public float LookYaw;
        public bool JumpPressed;
        public bool ClimbPressed;
        public bool InteractPressed;
        public bool RagdollPressed;

        // Jump forgiveness timers (host-only runtime helpers).
        public float JumpBufferTimer;
        public float CoyoteTimer;

        /// <summary>1 = full control, 0 = the ragdoll owns the body (knocked out).</summary>
        public float ControlAuthority = 1f;

        // Timing
        public float DeltaTime;
        public float Time;

        // In/out shared value: intoxication (0..1). States may raise it; the driver
        // writes it back to the networked state after the tick.
        public float Drunkenness;

        // Output read back by the driver.
        public float PlanarSpeed;

        // Structured data a state wants to broadcast this tick (optional).
        private IStatePayload _pendingPayload;
        public void EmitPayload(IStatePayload payload) => _pendingPayload = payload;
        public IStatePayload ConsumePayload()
        {
            var p = _pendingPayload;
            _pendingPayload = null;
            return p;
        }
    }

    /// <summary>Base for all states. Override only what a state needs.</summary>
    public abstract class PlayerStateBase
    {
        /// <summary>This state's single bit.</summary>
        public abstract PlayerState Id { get; }

        /// <summary>
        /// States allowed to stay active alongside this one. When this state
        /// activates, any active state NOT in this mask is deactivated. Example:
        /// Climbing.CompatibleWith = DrinkingRum (sip while climbing), but Grounded
        /// is absent from InAir's mask, so they can never coexist.
        /// </summary>
        public virtual PlayerState CompatibleWith => PlayerState.None;

        public virtual void Enter(StateContext ctx) { }
        public virtual void Exit(StateContext ctx) { }

        /// <summary>Simulation — host/state-authority only. Drive the Rigidbody here.</summary>
        public virtual void FixedTick(StateContext ctx) { }

        /// <summary>Per-frame cosmetics — every peer (animation, effects).</summary>
        public virtual void Render(StateContext ctx) { }

        /// <summary>Received replicated structured data while this state is active.</summary>
        public virtual void OnPayload(StateContext ctx, IStatePayload payload) { }
    }

    public enum TransitionKind : byte
    {
        Activate,   // set the target bit (clearing states it isn't compatible with)
        Deactivate, // clear the target bit
    }

    /// <summary>A rule: when <see cref="Condition"/> holds, activate/deactivate <see cref="Target"/>.</summary>
    public readonly struct StateTransition
    {
        public readonly PlayerState Target;
        public readonly TransitionKind Kind;
        public readonly Func<StateContext, bool> Condition;

        public StateTransition(PlayerState target, TransitionKind kind, Func<StateContext, bool> condition)
        {
            Target = target;
            Kind = kind;
            Condition = condition;
        }
    }
}
