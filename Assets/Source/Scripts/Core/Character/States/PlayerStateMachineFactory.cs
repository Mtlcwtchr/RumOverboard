using RumOverboard.StateMachine;

namespace RumOverboard.Core.Character.States
{
    /// <summary>
    /// Wires the crew member's mask-based state machine. Locomotion states are mutually exclusive
    /// (none lists another in CompatibleWith); DrinkingRum layers on top of most of them.
    ///
    /// Attached states (Climbing / Steering / HoldingRope) all start from one Interact press on a
    /// validated look-at target and end on Interact again, on a state's own ReleaseRequested
    /// (top/bottom of a rail, jump off, lost anchor) or when the ragdoll takes over.
    /// Returns a fresh machine per player (states hold per-instance data).
    /// </summary>
    public static class PlayerStateMachineFactory
    {
        private const PlayerState Attached = PlayerState.Climbing | PlayerState.Steering;

        public static PlayerStateMachine Build()
        {
            var drink = new DrinkingRumState();

            var machine = new PlayerStateMachine(PlayerState.Grounded)
                .AddState(new GroundedState())
                .AddState(new InAirState())
                .AddState(new ClimbingState())
                .AddState(new SwimmingState())
                .AddState(new SteeringState())
                .AddState(new HoldingRopeState())
                .AddState(drink);

            // --- Free locomotion: only when not attached to something ---
            machine
                .AddActivate(PlayerState.Grounded,
                    c => c.IsGrounded && !c.InWater && !IsAttached(c))
                .AddActivate(PlayerState.InAir,
                    c => (!c.IsGrounded || c.JumpPressed) && !c.InWater && !IsAttached(c))
                .AddActivate(PlayerState.Swimming,
                    c => c.InWater && !IsAttached(c));

            // --- Attach on Interact (target already validated by the driver) ---
            machine
                .AddActivate(PlayerState.Climbing,
                    c => c.InteractPressed && c.ClimbTarget != null && !IsAttached(c) && c.ControlAuthority > 0.5f
                         && !c.Machine.IsActive(PlayerState.HoldingRope))
                .AddActivate(PlayerState.Steering,
                    c => c.InteractPressed && c.HelmTarget != null && !IsAttached(c) && !c.InWater && c.ControlAuthority > 0.5f);
            // HoldingRope (a line end in hand) is driven by NetworkPlayer: take / tie / drop.

            // --- Detach: Interact again, the state asked to let go, or knocked out ---
            machine
                .AddDeactivate(Attached,
                    c => IsAttached(c) && (c.InteractPressed || c.ReleaseRequested || c.ControlAuthority < 0.3f));

            // --- Action layer: rum on its own key ---
            machine
                .AddActivate(PlayerState.DrinkingRum,
                    c => c.DrinkPressed
                         && !c.Machine.IsActive(PlayerState.Steering)
                         && !c.Machine.IsActive(PlayerState.HoldingRope)
                         && !c.Machine.IsActive(PlayerState.Swimming)
                         && !c.Machine.IsActive(PlayerState.DrinkingRum))
                .AddDeactivate(PlayerState.DrinkingRum,
                    c => c.Machine.IsActive(PlayerState.DrinkingRum) && drink.Finished);

            return machine;
        }

        private static bool IsAttached(StateContext c) => (c.Machine.Active & Attached) != 0;
    }
}
