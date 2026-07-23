using RumOverboard.StateMachine;
using RumOverboard.StateMachine.Serialization;
using UnityEngine;

namespace RumOverboard.Gameplay.States
{
    /// <summary>Animator parameter hashes shared by the states. Wire these names in the controller.</summary>
    public static class AnimatorParams
    {
        public static readonly int Speed = Animator.StringToHash("Speed");
        public static readonly int Grounded = Animator.StringToHash("Grounded");
        public static readonly int Climbing = Animator.StringToHash("Climbing");
        public static readonly int Swimming = Animator.StringToHash("Swimming");
        public static readonly int Drinking = Animator.StringToHash("Drinking");
        public static readonly int Steering = Animator.StringToHash("Steering");
    }

    /// <summary>Walking the deck. Full input-driven control, rum wobble, and jumping.</summary>
    public sealed class GroundedState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Grounded;
        public override PlayerState CompatibleWith => PlayerState.DrinkingRum;

        public override void Enter(StateContext ctx) => ctx.Body.useGravity = true;

        public override void FixedTick(StateContext ctx)
        {
            float speed = ctx.Config != null ? ctx.Config.MoveSpeed : 42.5f;
            float accel = ctx.Config != null ? ctx.Config.GroundAcceleration : 70.0f;
            float decel = ctx.Config != null ? ctx.Config.GroundDeceleration : 80.0f;
            float coyote = ctx.Config != null ? ctx.Config.CoyoteTime : 0.12f;

            // While grounded we keep refreshing coyote timer.
            ctx.CoyoteTimer = coyote;

            // Velocity of the deck under our feet (0 on static ground). We move RELATIVE to it so the
            // crew rides the ship — walking is added on top of the deck's own motion.
            Vector3 pv = ctx.GroundVelocity;
            bool onMovingDeck = pv.sqrMagnitude > 1e-4f;

            Vector3 wish = StateMovement.InputToWorld(ctx);
            wish = StateMovement.ApplyRumWobble(ctx, wish);

            Vector3 vel = ctx.Body.linearVelocity;
            Vector2 currentPlanar = new Vector2(vel.x - pv.x, vel.z - pv.z); // relative to deck
            Vector2 inputPlanar = new Vector2(wish.x, wish.z);
            float inputMagnitude = Mathf.Clamp01(inputPlanar.magnitude);

            float currentSpeed = currentPlanar.magnitude;
            float targetSpeed = speed * inputMagnitude;

            bool accelerating = targetSpeed > currentSpeed;
            float rate = accelerating ? Mathf.Max(0.01f, accel) : Mathf.Max(0.01f, decel);
            float nextSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * ctx.DeltaTime);

            Vector2 nextDirection;
            if (inputMagnitude > 0.001f)
                nextDirection = inputPlanar / inputMagnitude;
            else if (currentSpeed > 0.001f)
                nextDirection = currentPlanar / currentSpeed;
            else
                nextDirection = Vector2.zero;

            Vector2 nextPlanar = nextDirection * nextSpeed; // walking velocity relative to the deck

            // Reorient the walk onto the ground slope so the crew climbs ramps/stairs instead of
            // pushing horizontally into them. Only for slopes flat enough to be walkable.
            float maxWalkAngle = ctx.Config != null ? ctx.Config.MaxWalkableAngle : 50f;
            Vector3 walk = new Vector3(nextPlanar.x, 0f, nextPlanar.y);
            Vector3 groundN = ctx.GroundNormal;
            bool walkableSlope = groundN.sqrMagnitude > 1e-4f && Vector3.Angle(groundN, Vector3.up) <= maxWalkAngle;
            if (walkableSlope && walk.sqrMagnitude > 1e-6f)
            {
                Vector3 onSlope = Vector3.ProjectOnPlane(walk, groundN);
                if (onSlope.sqrMagnitude > 1e-6f)
                    walk = onSlope.normalized * walk.magnitude; // keep speed, add up/down slope component
            }

            // Deck velocity + our own walking. Ride the deck heave; add the slope's vertical
            // component when climbing up so ramps are walkable; otherwise keep gravity-driven Y.
            vel.x = pv.x + walk.x;
            vel.z = pv.z + walk.z;
            if (walkableSlope && walk.y > 0.001f)
                vel.y = (onMovingDeck ? pv.y : 0f) + walk.y; // climb the ramp
            else if (onMovingDeck)
                vel.y = pv.y;
            ctx.Body.linearVelocity = vel;

            if (inputMagnitude > 0.05f)
                StateMovement.FaceMoveDirection(ctx, wish);

            if (ctx.JumpPressed)
            {
                float impulse = ctx.Config != null ? ctx.Config.JumpImpulse : 20f;
                vel = ctx.Body.linearVelocity;
                vel.y = onMovingDeck ? pv.y : 0f; // jump relative to the deck we're riding
                ctx.Body.linearVelocity = vel;
                ctx.Body.AddForce(Vector3.up * impulse, ForceMode.VelocityChange);
                ctx.CoyoteTimer = 0f;
            }

            ctx.PlanarSpeed = nextPlanar.magnitude; // animation reflects walking, not ship drift
        }

        public override void Render(StateContext ctx)
        {
            // Animator params are applied centrally from replicated state in NetworkPlayer.Render().
        }
    }

    /// <summary>Airborne — reduced control, gravity does the rest.</summary>
    public sealed class InAirState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.InAir;
        public override PlayerState CompatibleWith => PlayerState.DrinkingRum;

        public override void Enter(StateContext ctx) => ctx.Body.useGravity = true;

        public override void FixedTick(StateContext ctx)
        {
            float speed = ctx.Config != null ? ctx.Config.MoveSpeed : 42.5f;
            float riseExtraGravity = ctx.Config != null ? ctx.Config.ExtraRiseGravity : 90f;
            float fallExtraGravity = ctx.Config != null ? ctx.Config.ExtraFallGravity : 180f;

            ctx.CoyoteTimer = Mathf.Max(0f, ctx.CoyoteTimer - ctx.DeltaTime);
            if (ctx.JumpPressed && ctx.CoyoteTimer > 0f)
            {
                float impulse = ctx.Config != null ? ctx.Config.JumpImpulse : 20f;
                Vector3 preJump = ctx.Body.linearVelocity;
                preJump.y = Mathf.Max(0f, preJump.y);
                ctx.Body.linearVelocity = preJump;
                ctx.Body.AddForce(Vector3.up * impulse, ForceMode.VelocityChange);
                ctx.CoyoteTimer = 0f;
            }

            Vector3 wish = StateMovement.InputToWorld(ctx);
            ctx.Body.AddForce(wish * (speed * 0.4f), ForceMode.Acceleration); // light air steering

            float extraGravity = ctx.Body.linearVelocity.y > 0f ? riseExtraGravity : fallExtraGravity;
            if (extraGravity > 0f)
                ctx.Body.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);

            Vector3 vel = ctx.Body.linearVelocity;
            ctx.PlanarSpeed = new Vector2(vel.x, vel.z).magnitude;
        }

        public override void Render(StateContext ctx)
        {
            // Animator params are applied centrally from replicated state in NetworkPlayer.Render().
        }
    }

    /// <summary>Clinging to the mast/rigging. Gravity off; move up/down with a lateral shimmy.</summary>
    public sealed class ClimbingState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Climbing;
        public override PlayerState CompatibleWith => PlayerState.DrinkingRum;

        public override void Enter(StateContext ctx)
        {
            ctx.Body.useGravity = false;
            ctx.Body.linearVelocity = Vector3.zero;

            // Demonstrate the payload channel: announce what we grabbed and where.
            ctx.EmitPayload(new ClimbAttachPayload
            {
                ClimbableId = ctx.ClimbTargetId,
                LocalPoint = ctx.ClimbPoint,
            });
        }

        public override void FixedTick(StateContext ctx)
        {
            float speed = ctx.Config != null ? ctx.Config.ClimbSpeed : 2.5f;
            ctx.Body.linearVelocity = new Vector3(ctx.Move.x * speed * 0.5f, ctx.Move.y * speed, 0f);
            ctx.PlanarSpeed = Mathf.Abs(ctx.Move.y) * speed;
        }

        public override void Render(StateContext ctx)
        {
            // Animator params are applied centrally from replicated state in NetworkPlayer.Render().
        }

        public override void OnPayload(StateContext ctx, IStatePayload payload)
        {
            // On clients: could place the hands at LocalPoint on the grabbed climbable.
        }
    }

    /// <summary>In the drink. Gravity off, buoyancy toward the surface, swim in-plane. No sipping.</summary>
    public sealed class SwimmingState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Swimming;
        // CompatibleWith None: both hands are busy — can't drink while swimming.

        public override void Enter(StateContext ctx) => ctx.Body.useGravity = false;

        public override void FixedTick(StateContext ctx)
        {
            float speed = (ctx.Config != null ? ctx.Config.MoveSpeed : 42.5f) * 0.6f;
            Vector3 wish = StateMovement.InputToWorld(ctx);

            Vector3 vel = ctx.Body.linearVelocity;
            vel.x = wish.x * speed;
            vel.z = wish.z * speed;
            vel.y = Mathf.Lerp(vel.y, 0f, 0.1f); // damp bobbing toward neutral buoyancy
            ctx.Body.linearVelocity = vel;

            StateMovement.FaceMoveDirection(ctx, wish);
            ctx.PlanarSpeed = new Vector2(vel.x, vel.z).magnitude;
        }

        public override void Render(StateContext ctx)
        {
            // Animator params are applied centrally from replicated state in NetworkPlayer.Render().
        }
    }

    /// <summary>
    /// Standing at the helm. The body is locked to the wheel stand (no locomotion); A/D is consumed
    /// by the driver as the steering signal, not as movement. Gravity off so the crew member doesn't
    /// slide off the deck while pinned.
    /// </summary>
    public sealed class SteeringState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Steering;
        // Both hands on the wheel — nothing layers on top.

        public override void Enter(StateContext ctx) => ctx.Body.useGravity = false;

        public override void FixedTick(StateContext ctx)
        {
            // The body is pinned to the helm by NetworkPlayer (kinematic ride-along with the moving
            // ship), so there's no locomotion to drive here — just report zero planar speed.
            ctx.PlanarSpeed = 0f;
        }

        public override void Exit(StateContext ctx) => ctx.Body.useGravity = true;

        public override void Render(StateContext ctx) { }
    }

    /// <summary>Shared movement helpers so states don't duplicate math.</summary>
    internal static class StateMovement
    {
        public static Vector3 InputToWorld(StateContext ctx)
        {
            Quaternion yaw = Quaternion.Euler(0f, ctx.LookYaw, 0f);
            Vector3 dir = yaw * new Vector3(ctx.Move.x, 0f, ctx.Move.y);
            return Vector3.ClampMagnitude(dir, 1f);
        }

        public static Vector3 ApplyRumWobble(StateContext ctx, Vector3 wish)
        {
            if (ctx.Config == null || wish.sqrMagnitude <= 0.001f) return wish;
            float loss = ctx.Config.DrunkControlLoss.Evaluate(ctx.Drunkenness);
            if (loss <= 0f) return wish;
            float wobble = Mathf.Sin(ctx.Time * 3.3f) * loss * ctx.Config.DrunkMaxWobbleDeg;
            return Quaternion.Euler(0f, wobble, 0f) * wish;
        }

        public static void FaceMoveDirection(StateContext ctx, Vector3 wish)
        {
            if (wish.sqrMagnitude <= 0.01f) return;
            Quaternion look = Quaternion.LookRotation(wish, Vector3.up);
            float turn = (ctx.Config != null ? ctx.Config.TurnSpeedDeg : 540f) * ctx.DeltaTime;
            ctx.Body.MoveRotation(Quaternion.RotateTowards(ctx.Body.rotation, look, turn));
        }
    }
}
