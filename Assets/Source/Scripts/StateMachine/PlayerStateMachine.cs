using System.Collections.Generic;

namespace RumOverboard.StateMachine
{
    /// <summary>
    /// Mask-based state machine. The active set is a <see cref="PlayerState"/> bit
    /// mask; several states can be active at once (e.g. Climbing | DrinkingRum).
    /// Activating a state clears any active state it isn't compatible with, which is
    /// how mutual exclusion ("locomotion layer") falls out without hard-coded layers.
    ///
    /// Transitions are evaluated host-side; clients call <see cref="SetActiveFromNetwork"/>
    /// with the replicated mask, which fires the same Enter/Exit so cosmetics stay right.
    /// </summary>
    public sealed class PlayerStateMachine
    {
        private readonly Dictionary<PlayerState, PlayerStateBase> _states = new();
        private readonly List<StateTransition> _transitions = new();

        public PlayerState Active { get; private set; }

        public PlayerStateMachine(PlayerState initial) => Active = initial;

        public bool IsActive(PlayerState state) => state != PlayerState.None && (Active & state) == state;

        public PlayerStateMachine AddState(PlayerStateBase state)
        {
            _states[state.Id] = state;
            return this;
        }

        public PlayerStateMachine AddActivate(PlayerState target, System.Func<StateContext, bool> condition)
        {
            _transitions.Add(new StateTransition(target, TransitionKind.Activate, condition));
            return this;
        }

        public PlayerStateMachine AddDeactivate(PlayerState target, System.Func<StateContext, bool> condition)
        {
            _transitions.Add(new StateTransition(target, TransitionKind.Deactivate, condition));
            return this;
        }

        public void Start(StateContext ctx)
        {
            foreach (var kv in _states)
                if (IsActive(kv.Key))
                    kv.Value.Enter(ctx);
        }

        /// <summary>Host: tick active states, evaluate transitions, apply the resulting mask.</summary>
        public void FixedTick(StateContext ctx)
        {
            foreach (var kv in _states)
                if (IsActive(kv.Key))
                    kv.Value.FixedTick(ctx);

            PlayerState next = Active;
            for (int i = 0; i < _transitions.Count; i++)
            {
                var t = _transitions[i];
                if (!t.Condition(ctx)) continue;

                if (t.Kind == TransitionKind.Activate)
                    next = Activate(t.Target, next);
                else
                    next &= ~t.Target;
            }

            if (next != Active)
                SetActive(next, ctx);
        }

        public void Render(StateContext ctx)
        {
            foreach (var kv in _states)
                if (IsActive(kv.Key))
                    kv.Value.Render(ctx);
        }

        /// <summary>Client: adopt the replicated mask, firing Enter/Exit for cosmetics.</summary>
        public void SetActiveFromNetwork(PlayerState mask, StateContext ctx)
        {
            if (mask != Active)
                SetActive(mask, ctx);
        }

        /// <summary>Dispatch a decoded payload to every currently-active state.</summary>
        public void DispatchPayload(StateContext ctx, Serialization.IStatePayload payload)
        {
            foreach (var kv in _states)
                if (IsActive(kv.Key))
                    kv.Value.OnPayload(ctx, payload);
        }

        // Set target bit, clearing active states not compatible with it.
        private PlayerState Activate(PlayerState target, PlayerState mask)
        {
            PlayerState keep = target; // target is compatible with itself
            if (_states.TryGetValue(target, out var state))
                keep |= state.CompatibleWith;
            return (mask & keep) | target;
        }

        private void SetActive(PlayerState next, StateContext ctx)
        {
            PlayerState entered = next & ~Active;
            PlayerState exited = Active & ~next;
            Active = next;

            // Exit first, then Enter, so cosmetics don't fight.
            foreach (var kv in _states)
                if ((exited & kv.Key) == kv.Key && kv.Key != PlayerState.None)
                    kv.Value.Exit(ctx);

            foreach (var kv in _states)
                if ((entered & kv.Key) == kv.Key && kv.Key != PlayerState.None)
                    kv.Value.Enter(ctx);
        }
    }
}
