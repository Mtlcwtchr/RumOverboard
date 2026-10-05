using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Helm
{
    public struct HelmWheelState
    {
        public float Wheel;     // deg, ±MaxWheel
        public float Velocity;  // deg/s
        public float Rudder;    // deg
    }

    public readonly struct HelmWheelGeometry
    {
        public readonly float MaxWheel;
        public readonly float MaxRudder;
        public readonly float RudderResponse;
        public readonly float RudderYawCoefficient;
        public readonly bool Invert;

        public HelmWheelGeometry(float maxWheel, float maxRudder, float rudderResponse, float yawCoefficient, bool invert)
        {
            MaxWheel = maxWheel;
            MaxRudder = maxRudder;
            RudderResponse = rudderResponse;
            RudderYawCoefficient = yawCoefficient;
            Invert = invert;
        }
    }

    public readonly struct HelmWheelResult
    {
        /// <summary>Yaw torque the rudder puts on the hull this step.</summary>
        public readonly float YawTorque;
        /// <summary>Water's push on the wheel relative to the helmsman's strength (signed, ~-1..1+).</summary>
        public readonly float Load;
        /// <summary>The wheel hit a stop, or the sea tore it out of a holding helmsman's hands.</summary>
        public readonly bool Kick;

        public HelmWheelResult(float yawTorque, float load, bool kick)
        {
            YawTorque = yawTorque;
            Load = load;
            Kick = kick;
        }
    }

    /// <summary>
    /// The wheel as a heavy rotating body (pure function, host-side):
    ///
    ///   wheel accel = helmsman + water on the rudder + friction
    ///     helmsman  : A/D → ±SteerAccel; hands-on but idle → resists motion up to HoldStrength
    ///     water     : self-centring ∝ flow·|flow|·sin(rudder) (astern it flips and slams the rudder
    ///                 over), sideways flow buffet (current / waves / drift), turbulent shudder
    ///     friction  : ∝ spin speed (much lower when unmanned — the sea spins a free wheel)
    ///
    /// So at rest the wheel spins light and fast; at speed it gets heavy, fights back toward centre,
    /// shudders, and a big sea can rip it out of your hands. The rudder follows the wheel; its yaw
    /// torque scales with flow past it (no steerage way → no steering).
    /// </summary>
    public static class HelmWheelPhysics
    {
        public static HelmWheelResult Step(ref HelmWheelState s, float steer, bool manned, float forwardFlow,
            float lateralFlow, float time, float dt, in HelmWheelGeometry g, HelmFeelConfig cfg)
        {
            dt = Mathf.Max(1e-4f, dt);
            steer = Mathf.Clamp(steer, -1f, 1f);

            float rudderRad = s.Rudder * Mathf.Deg2Rad;
            float water = -cfg.RudderCentering * forwardFlow * Mathf.Abs(forwardFlow) * Mathf.Sin(rudderRad);
            water += cfg.FlowBuffet * lateralFlow * Mathf.Abs(lateralFlow);
            float turbulenceAmt = Mathf.Clamp01(Mathf.Abs(forwardFlow) / Mathf.Max(0.1f, cfg.TurbulenceFullFlow) + Mathf.Abs(lateralFlow) * 0.3f);
            water += cfg.Turbulence * turbulenceAmt * (Mathf.PerlinNoise(time * 1.9f, 0.37f) - 0.5f) * 2f;

            float hands = 0f;
            bool slipped = false;
            if (manned)
            {
                if (Mathf.Abs(steer) > 0.01f)
                {
                    hands = steer * cfg.SteerAccel;
                }
                else
                {
                    float want = -water - s.Velocity * cfg.HoldDamping;
                    hands = Mathf.Clamp(want, -cfg.HoldStrength, cfg.HoldStrength);
                    slipped = Mathf.Abs(want) > cfg.HoldStrength * 1.05f && Mathf.Abs(water) > cfg.HoldStrength;
                }
            }

            float friction = -s.Velocity * (manned ? cfg.WheelFriction : cfg.UnmannedFriction);
            float drive = water + hands;

            // Dry (Coulomb) friction: a resting wheel needs a real push to start turning, a moving one
            // is braked by a constant amount — no creeping at anchor.
            float dry = Mathf.Max(0f, cfg.StaticFriction);
            if (Mathf.Abs(s.Velocity) < 1f)
            {
                s.Velocity = Mathf.Abs(drive) <= dry
                    ? 0f
                    : s.Velocity + (drive - Mathf.Sign(drive) * dry + friction) * dt;
            }
            else
            {
                float next = s.Velocity + (drive + friction - Mathf.Sign(s.Velocity) * dry) * dt;
                if (Mathf.Sign(next) != Mathf.Sign(s.Velocity) && Mathf.Abs(drive) <= dry)
                    next = 0f; // braked to a stop
                s.Velocity = next;
            }
            s.Wheel += s.Velocity * dt;

            bool kick = slipped;
            if (Mathf.Abs(s.Wheel) > g.MaxWheel)
            {
                float side = Mathf.Sign(s.Wheel);
                s.Wheel = side * g.MaxWheel;
                if (s.Velocity * side > 0f)
                {
                    kick |= Mathf.Abs(s.Velocity) > 120f;
                    s.Velocity = -s.Velocity * cfg.StopBounce;
                }
            }

            float wheelNorm = g.MaxWheel > 0.01f ? Mathf.Clamp(s.Wheel / g.MaxWheel, -1f, 1f) : 0f;
            s.Rudder = Mathf.MoveTowards(s.Rudder, wheelNorm * g.MaxRudder, g.RudderResponse * g.MaxRudder * dt);

            float yaw = g.RudderYawCoefficient * forwardFlow * Mathf.Sin(s.Rudder * Mathf.Deg2Rad);
            if (g.Invert) yaw = -yaw;

            return new HelmWheelResult(yaw, water / Mathf.Max(1f, cfg.SteerAccel), kick);
        }
    }
}

