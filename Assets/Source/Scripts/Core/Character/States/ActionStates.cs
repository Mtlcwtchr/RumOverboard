using RumOverboard.StateMachine;
using RumOverboard.StateMachine.Serialization;
using UnityEngine;

namespace RumOverboard.Core.Character.States
{
    /// <summary>
    /// Taking a swig of rum. Layered on top of locomotion (you can drink while
    /// grounded, airborne, or climbing — but not swimming). Raises intoxication over
    /// the sip and announces the sip once via a payload. "Idle" is simply the absence
    /// of this bit, so there's no separate idle state.
    /// </summary>
    public sealed class DrinkingRumState : PlayerStateBase
    {
        private float _elapsed;
        private byte _sips;

        public bool Finished { get; private set; }

        public override PlayerState Id => PlayerState.DrinkingRum;
        public override PlayerState CompatibleWith =>
            PlayerState.Grounded | PlayerState.InAir | PlayerState.Climbing;

        public override void Enter(StateContext ctx)
        {
            _elapsed = 0f;
            Finished = false;
            _sips++;

            // One payload per sip announces sip index + fresh start (progress 0).
            ctx.EmitPayload(new RumSipPayload { Progress = 0f, SipCount = _sips });
        }

        public override void FixedTick(StateContext ctx)
        {
            float duration = ctx.Config != null ? ctx.Config.RumSipDuration : 2f;
            float perSip = ctx.Config != null ? ctx.Config.RumSipIntoxication : 0.15f;

            _elapsed += ctx.DeltaTime;
            float rate = perSip / Mathf.Max(0.01f, duration);
            ctx.Drunkenness = Mathf.Clamp01(ctx.Drunkenness + rate * ctx.DeltaTime);

            if (_elapsed >= duration)
                Finished = true;
        }

        public override void Render(StateContext ctx) { }

        public override void Exit(StateContext ctx) { }

        public override void OnPayload(StateContext ctx, IStatePayload payload)
        {
            // On clients: e.g. show a sip counter / splash. Data already decoded.
        }
    }
}

