using RumOverboard.StateMachine;
using UnityEngine;

namespace RumOverboard.Core.Character.States
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
        // Standing still on a moving deck: remember the spot (in the deck's frame) and hold it, so
        // the crew member doesn't slowly skate while the ship accelerates, heels or yaws.
        private bool _anchored;
        private Rigidbody _anchorBody;
        private Vector3 _anchorLocal;

        public override PlayerState Id => PlayerState.Grounded;
        public override PlayerState CompatibleWith => PlayerState.DrinkingRum | PlayerState.HoldingRope;

        public override void Enter(StateContext ctx)
        {
            ctx.Body.useGravity = true;
            _anchored = false;
        }

        public override void FixedTick(StateContext ctx)
        {
            float speed = ctx.Config != null ? ctx.Config.MoveSpeed : 4.5f;
            if (ctx.SprintHeld)
                speed *= ctx.Config != null ? ctx.Config.SprintMultiplier : 1.6f;
            float accel = ctx.Config != null ? ctx.Config.GroundAcceleration : 40f;
            float decel = ctx.Config != null ? ctx.Config.GroundDeceleration : 55f;
            float coyote = ctx.Config != null ? ctx.Config.CoyoteTime : 0.12f;

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
                    walk = onSlope.normalized * walk.magnitude;
            }

            vel.x = pv.x + walk.x;
            vel.z = pv.z + walk.z;
            HoldDeckSpot(ctx, ref vel, inputMagnitude, onMovingDeck);
            if (ctx.StepAhead && inputMagnitude > 0.1f)
                vel.y = (onMovingDeck ? pv.y : 0f) + 2.6f; // pop up the step
            else if (walkableSlope && walk.y > 0.001f)
                vel.y = (onMovingDeck ? pv.y : 0f) + walk.y;
            else if (onMovingDeck)
                vel.y = pv.y;
            ctx.Body.linearVelocity = vel;

            if (ctx.JumpPressed)
            {
                float impulse = ctx.Config != null ? ctx.Config.JumpImpulse : 6f;
                vel = ctx.Body.linearVelocity;
                vel.y = onMovingDeck ? pv.y : 0f; // jump relative to the deck we're riding
                ctx.Body.linearVelocity = vel + Vector3.up * impulse;
                ctx.CoyoteTimer = 0f;
            }

            ctx.PlanarSpeed = nextPlanar.magnitude; // animation reflects walking, not ship drift
        }

        private void HoldDeckSpot(StateContext ctx, ref Vector3 vel, float inputMagnitude, bool onMovingDeck)
        {
            Rigidbody deck = ctx.GroundBody;
            bool idle = inputMagnitude < 0.05f && !ctx.JumpPressed && deck != null && onMovingDeck;
            if (!idle)
            {
                _anchored = false;
                return;
            }

            Vector3 pos = ctx.Body.position;
            if (!_anchored || _anchorBody != deck)
            {
                _anchored = true;
                _anchorBody = deck;
                _anchorLocal = Quaternion.Inverse(deck.rotation) * (pos - deck.position);
                return;
            }

            Vector3 target = deck.position + deck.rotation * _anchorLocal;
            Vector3 error = target - pos;
            error.y = 0f;
            if (error.sqrMagnitude > 1f) // shoved away (wave, crewmate): accept the new spot
            {
                _anchorLocal = Quaternion.Inverse(deck.rotation) * (pos - deck.position);
                return;
            }
            Vector3 correction = Vector3.ClampMagnitude(error / 0.15f, 2f);
            vel.x += correction.x;
            vel.z += correction.z;
        }
    }

    /// <summary>Airborne — reduced control, gravity does the rest.</summary>
    public sealed class InAirState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.InAir;
        public override PlayerState CompatibleWith => PlayerState.DrinkingRum | PlayerState.HoldingRope;

        public override void Enter(StateContext ctx) => ctx.Body.useGravity = true;

        public override void FixedTick(StateContext ctx)
        {
            float speed = ctx.Config != null ? ctx.Config.MoveSpeed : 4.5f;
            float riseExtraGravity = ctx.Config != null ? ctx.Config.ExtraRiseGravity : 8f;
            float fallExtraGravity = ctx.Config != null ? ctx.Config.ExtraFallGravity : 20f;

            ctx.CoyoteTimer = Mathf.Max(0f, ctx.CoyoteTimer - ctx.DeltaTime);
            if (ctx.JumpPressed && ctx.CoyoteTimer > 0f)
            {
                float impulse = ctx.Config != null ? ctx.Config.JumpImpulse : 6f;
                Vector3 preJump = ctx.Body.linearVelocity;
                preJump.y = Mathf.Max(0f, preJump.y);
                ctx.Body.linearVelocity = preJump + Vector3.up * impulse;
                ctx.CoyoteTimer = 0f;
            }

            Vector3 wish = StateMovement.InputToWorld(ctx);
            ctx.Body.AddForce(wish * (speed * 0.8f), ForceMode.Acceleration); // light air steering

            float extraGravity = ctx.Body.linearVelocity.y > 0f ? riseExtraGravity : fallExtraGravity;
            if (extraGravity > 0f)
                ctx.Body.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);

            Vector3 vel = ctx.Body.linearVelocity;
            ctx.PlanarSpeed = new Vector2(vel.x, vel.z).magnitude;
        }
    }

    /// <summary>
    /// On a climb rail (mast, ladder). The body stays dynamic but gravity-free and is driven to the
    /// rail point (u, v) by <see cref="AttachMotor"/>, so it rides the moving mast exactly.
    /// W/S climbs, A/D shimmies; past the top it steps onto the exit platform (if any), below the
    /// bottom it lets go; Space jumps off backwards. Collisions are relaxed by the driver while
    /// attached so yards/platforms can't wedge the climber.
    /// </summary>
    public sealed class ClimbingState : PlayerStateBase
    {
        private const float MantleDuration = 0.4f;
        private float _mantleT = -1f;
        private Vector3 _mantleFrom;

        public override PlayerState Id => PlayerState.Climbing;
        public override PlayerState CompatibleWith => PlayerState.DrinkingRum;

        public override void Enter(StateContext ctx)
        {
            ctx.Body.useGravity = false;
            ctx.ActiveClimb = ctx.ClimbTarget;
            ctx.ReleaseRequested = false;
            _mantleT = -1f;
            if (ctx.ActiveClimb != null)
                ctx.ActiveClimb.Project(ctx.TargetPoint, out ctx.ClimbU, out ctx.ClimbV);
        }

        public override void Exit(StateContext ctx)
        {
            ctx.Body.useGravity = true;
            ctx.ActiveClimb = null;
        }

        public override void FixedTick(StateContext ctx)
        {
            IClimbRail rail = ctx.ActiveClimb;
            if (rail == null || rail.Body == null)
            {
                ctx.ReleaseRequested = true;
                return;
            }

            float speed = (ctx.Config != null ? ctx.Config.ClimbSpeed : 2.5f) * rail.SpeedMultiplier;
            if (ctx.SprintHeld)
                speed *= 1.5f;

            float dt = ctx.DeltaTime;
            ctx.ClimbU += ctx.Move.y * speed * dt;
            ctx.ClimbV += ctx.Move.x * speed * 0.6f * dt;
            ctx.ClimbV = Mathf.Clamp(ctx.ClimbV, -rail.HalfWidth, rail.HalfWidth);
            ctx.PlanarSpeed = Mathf.Abs(ctx.Move.y) * speed;

            Vector3 railVel = rail.Body.GetPointVelocity(ctx.Body.position);

            // Jump off: push away from the rail, up a little.
            if (ctx.JumpPressed)
            {
                ctx.Body.linearVelocity = railVel + rail.OutwardWorld * 3f + Vector3.up * 2.5f;
                ctx.ReleaseRequested = true;
                return;
            }

            // Mantling onto the exit platform: a short motor-driven move (never a teleport, which
            // would pop on every peer's interpolation), then let go once standing on it.
            if (_mantleT >= 0f)
            {
                _mantleT += dt / MantleDuration;
                Vector3 exit = rail.TopExit.position + Vector3.up * 0.05f;
                Vector3 over = Vector3.Lerp(_mantleFrom, exit, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_mantleT)));
                over.y = Mathf.Max(over.y, exit.y); // up first, then over the lip
                AttachMotor.Drive(ctx.Body, over, rail.Body.GetPointVelocity(exit), rail.BodyRotation, dt);
                if (_mantleT >= 1f && (ctx.Body.position - exit).sqrMagnitude < 0.15f * 0.15f)
                {
                    ctx.Body.linearVelocity = rail.Body.GetPointVelocity(exit);
                    ctx.ReleaseRequested = true;
                }
                else if (_mantleT > 2f)
                {
                    ctx.ReleaseRequested = true; // something blocked the mantle: just let go
                }
                return;
            }

            // Top: start mantling onto the exit platform, or clamp at the end of the rail.
            if (ctx.ClimbU >= rail.Length)
            {
                ctx.ClimbU = rail.Length;
                if (rail.TopExit != null && ctx.Move.y > 0.5f)
                {
                    _mantleT = 0f;
                    _mantleFrom = ctx.Body.position;
                    return;
                }
            }

            // Bottom: holding down at the foot of the rail lets go onto the deck.
            if (ctx.ClimbU <= 0f)
            {
                ctx.ClimbU = 0f;
                if (ctx.Move.y < -0.5f)
                {
                    ctx.Body.linearVelocity = railVel;
                    ctx.ReleaseRequested = true;
                    return;
                }
            }

            Vector3 target = rail.BodyPoint(ctx.ClimbU, ctx.ClimbV);
            AttachMotor.Drive(ctx.Body, target, rail.Body.GetPointVelocity(target), rail.BodyRotation, dt);
        }
    }

    /// <summary>In the drink. Gravity off, buoyancy toward the surface, swim in-plane. No sipping.</summary>
    public sealed class SwimmingState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Swimming;
        public override PlayerState CompatibleWith => PlayerState.HoldingRope;

        public override void Enter(StateContext ctx) => ctx.Body.useGravity = false;
        public override void Exit(StateContext ctx) => ctx.Body.useGravity = true;

        public override void FixedTick(StateContext ctx)
        {
            float speed = (ctx.Config != null ? ctx.Config.MoveSpeed : 4.5f) * 0.6f;
            Vector3 wish = StateMovement.InputToWorld(ctx);

            Vector3 vel = ctx.Body.linearVelocity;
            vel.x = Mathf.Lerp(vel.x, wish.x * speed, 0.2f);
            vel.z = Mathf.Lerp(vel.z, wish.z * speed, 0.2f);

            // Float the chest at the surface: spring toward (surface - chest), damped.
            float chestY = ctx.Body.position.y + 1.2f;
            float error = ctx.WaterSurfaceY - chestY;
            vel.y = Mathf.Lerp(vel.y, Mathf.Clamp(error * 3f, -3f, 3f), 0.15f);
            if (ctx.JumpPressed && error > -0.3f)
                vel.y = 4f;
            ctx.Body.linearVelocity = vel;

            StateMovement.FaceMoveDirection(ctx, wish);
            ctx.PlanarSpeed = new Vector2(vel.x, vel.z).magnitude;
        }
    }

    /// <summary>
    /// Standing at a ship station (the helm). Glued to the stand point by <see cref="AttachMotor"/>
    /// (dynamic body, rides the deck); A/D is consumed by the driver as the steering signal.
    /// </summary>
    public sealed class SteeringState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.Steering;

        public override void Enter(StateContext ctx)
        {
            ctx.Body.useGravity = false;
            ctx.ActiveStation = ctx.HelmTarget;
        }

        public override void Exit(StateContext ctx)
        {
            ctx.Body.useGravity = true;
            ctx.ActiveStation = null;
        }

        public override void FixedTick(StateContext ctx)
        {
            StationMotor.Hold(ctx);
            ctx.PlanarSpeed = 0f;
        }
    }

    /// <summary>
    /// A line end in your hands (action layer over Grounded / InAir / Swimming): you walk around
    /// freely; the rigging system hauls / eases / leashes. Started and ended by the driver
    /// (take / tie / drop), not by the transition table.
    /// </summary>
    public sealed class HoldingRopeState : PlayerStateBase
    {
        public override PlayerState Id => PlayerState.HoldingRope;
        public override PlayerState CompatibleWith => PlayerState.Grounded | PlayerState.InAir | PlayerState.Swimming;
    }

    internal static class StationMotor
    {
        public static void Hold(StateContext ctx)
        {
            IStationAnchor station = ctx.ActiveStation;
            if (station == null || station.Body == null)
            {
                ctx.ReleaseRequested = true;
                return;
            }
            Vector3 p = station.StandPosition;
            AttachMotor.Drive(ctx.Body, p, station.Body.GetPointVelocity(p), station.StandRotation, ctx.DeltaTime);
        }
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
