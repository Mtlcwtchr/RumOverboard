using RumOverboard.StateMachine;

namespace RumOverboard.Gameplay.States
{
    /// <summary>
    /// Wires the crew member's mask-based state machine. Locomotion states
    /// (Grounded/InAir/Climbing/Swimming) are mutually exclusive because none lists
    /// another in its CompatibleWith; DrinkingRum layers on top of most of them, so
    /// you can sip while climbing but not while swimming. Ragdoll is NOT a state
    /// anymore — it's the 0..1 RagdollControl value on NetworkPlayer.
    /// Returns a fresh machine per player (states hold per-instance data).
    /// </summary>
    public static class PlayerStateMachineFactory
    {
        public static PlayerStateMachine Build()
        {
            var drink = new DrinkingRumState();

            var machine = new PlayerStateMachine(PlayerState.Grounded)
                .AddState(new GroundedState())
                .AddState(new InAirState())
                .AddState(new ClimbingState())
                .AddState(new SwimmingState())
                .AddState(drink);

            // --- Locomotion (mutual exclusion handled by compatibility on activate) ---
            // Guards keep ground/air probes from stealing control while climbing.
            machine
                .AddActivate(PlayerState.Grounded,
                    c => c.IsGrounded && !c.InWater && !c.Machine.IsActive(PlayerState.Climbing))
                .AddActivate(PlayerState.InAir,
                    c => (!c.IsGrounded || c.JumpPressed) && !c.InWater && !c.Machine.IsActive(PlayerState.Climbing))
                .AddActivate(PlayerState.Swimming,
                    c => c.InWater)
                .AddActivate(PlayerState.Climbing,
                    c => c.ClimbPressed && c.NearClimb && !c.Machine.IsActive(PlayerState.Climbing))
                .AddDeactivate(PlayerState.Climbing,
                    c => c.Machine.IsActive(PlayerState.Climbing) && (!c.ClimbPressed || !c.NearClimb));

            // --- Action layer ---
            machine
                .AddActivate(PlayerState.DrinkingRum,
                    c => c.InteractPressed
                         && !c.Machine.IsActive(PlayerState.Swimming)
                         && !c.Machine.IsActive(PlayerState.DrinkingRum))
                .AddDeactivate(PlayerState.DrinkingRum,
                    c => c.Machine.IsActive(PlayerState.DrinkingRum) && drink.Finished);

            return machine;
        }
    }
}
