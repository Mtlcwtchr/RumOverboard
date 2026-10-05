#if FUSION2
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rigging
{
    /// <summary>
    /// Yards, braces and line loads (host, every tick, before <see cref="RopeSystem"/>).
    ///
    /// Each yard has TWO braces, one per yardarm (like a real square-rigger):
    ///   • the PORT brace pulls the port arm aft  → it limits how far the yard can swing to +angle:
    ///         angle ≤ hi = lerp(+max, −max, vPort)
    ///   • the STARBOARD brace pulls the starboard arm aft → it limits the swing to −angle:
    ///         angle ≥ lo = lerp(−max, +max, vStarboard)
    /// (v = how much of the brace is hauled, 0..1). The feasible range is [lo, hi]; with both braces
    /// set so that vPort + vStarboard = 1 the yard is locked, otherwise it has play.
    ///
    /// The wind decides where the yard goes inside that range: a free yard turns broadside to the
    /// apparent wind, so it swings until one brace goes taut. That brace carries the load
    /// (∝ wind pressure × set canvas × how hard the wind pushes against it); the slack one carries
    /// nothing. To brace the yard round you haul one side and ease (or cast off) the other —
    /// hauling against a tied partner is blocked (RopeSystem).
    ///
    /// Halyard load = canvas/yard weight × hoist + a share of the sail's wind force.
    /// Loads drive how the rope behaves in hands (haul speed, slip, pull on the crew member) and
    /// are replicated (byte) for client feedback: HUD strain meter + camera strain.
    /// </summary>
    public static class YardSystem
    {
        private static readonly int[] PortBrace = new int[NetworkShip.MaxSails];
        private static readonly int[] StarBrace = new int[NetworkShip.MaxSails];
        private static readonly int[] Halyard = new int[NetworkShip.MaxSails];

        public static void Tick(NetworkShip ship, ShipSailsAggregator sails, float dt, float time)
        {
            if (sails == null || !ship.HasStateAuthority)
                return;
            RiggingConfig cfg = RiggingConfig.Active;

            for (int s = 0; s < NetworkShip.MaxSails; s++)
                PortBrace[s] = StarBrace[s] = Halyard[s] = -1;
            for (int i = 0; i < NetworkShip.MaxLines; i++)
            {
                RigLine line = ship.Line(i);
                if (line == null || line.SailIndex < 0 || line.SailIndex >= NetworkShip.MaxSails)
                    continue;
                switch (line.Kind)
                {
                    case RigLineKind.Halyard: Halyard[line.SailIndex] = i; break;
                    case RigLineKind.BracePort: PortBrace[line.SailIndex] = i; break;
                    case RigLineKind.BraceStarboard: StarBrace[line.SailIndex] = i; break;
                }
            }

            Vector3 aw = ApparentWindLocal(ship, sails, time);
            float awSpeed = aw.magnitude;
            float q = 0.5f * sails.AirDensity * awSpeed * awSpeed;
            bool windy = awSpeed > cfg.YardMinWind;
            float broadside = windy ? BroadsideAngle(aw) : 0f;

            int count = Mathf.Min(sails.SailCount, NetworkShip.MaxSails);
            for (int s = 0; s < count; s++)
            {
                float forceNorm = q * sails.GetEffectiveArea(s) / Mathf.Max(1f, cfg.FullLoadForce);

                int h = Halyard[s];
                if (h >= 0)
                {
                    float hoist = ship.GetLineValue(h);
                    ship.SetLineLoad(h, hoist * cfg.HalyardWeight + forceNorm * cfg.HalyardWindShare);
                }

                int port = PortBrace[s];
                int star = StarBrace[s];
                if (port < 0 && star < 0)
                    continue;

                float max = (port >= 0 ? ship.Line(port) : ship.Line(star)).BraceMaxAngle;
                Limits(ship, port, star, max, out float lo, out float hi);

                // The braces are rope: the yard can never sit outside what they allow.
                float current = Mathf.Clamp(ship.GetYardAngle(s), lo, hi);
                float free = windy ? Mathf.Clamp(broadside, -max, max) : current;
                float target = Mathf.Clamp(free, lo, hi);
                float rate = cfg.YardSwingRate + cfg.YardSwingWindRate * Mathf.Clamp01(forceNorm);
                ship.SetYardAngle(s, Mathf.MoveTowards(current, target, rate * dt));

                // The taut brace is the one the wind pushes the yard against.
                float press = windy ? Mathf.Clamp01(Mathf.Abs(free - target) / Mathf.Max(1f, cfg.BraceLoadAngle)) : 0f;
                float braceLoad = press > 0.001f ? 0.05f + forceNorm * press : 0f;
                if (port >= 0) ship.SetLineLoad(port, free > target + 0.01f ? braceLoad : 0f);
                if (star >= 0) ship.SetLineLoad(star, free < target - 0.01f ? braceLoad : 0f);
            }
        }

        /// <summary>Allowed yard range [lo, hi] from the two braces (a missing brace leaves its side free).</summary>
        public static void Limits(NetworkShip ship, int port, int star, float max, out float lo, out float hi)
        {
            hi = port >= 0 ? Mathf.Lerp(max, -max, ship.GetLineValue(port)) : max;
            lo = star >= 0 ? Mathf.Lerp(-max, max, ship.GetLineValue(star)) : -max;
            if (lo > hi)
                lo = hi = (lo + hi) * 0.5f;
        }

        /// <summary>
        /// Max deck-side rope a brace may have given its partner: vThis + vPartner ≤ 1 (both taut =
        /// yard locked). Returns OutMax when there is no partner.
        /// </summary>
        public static float MaxOutWithPartner(NetworkShip ship, RigLine line, int partner) =>
            partner >= 0 ? line.OutFromValue(1f - ship.GetLineValue(partner)) : line.OutMax;

        // Sail normal at yard angle a is (sin a, 0, cos a) in ship space: broadside when ∥ wind.
        private static float BroadsideAngle(Vector3 awLocal)
        {
            float a = Mathf.Atan2(awLocal.x, awLocal.z) * Mathf.Rad2Deg;
            if (a > 90f) a -= 180f;
            else if (a < -90f) a += 180f;
            return a;
        }

        private static Vector3 ApparentWindLocal(NetworkShip ship, ShipSailsAggregator sails, float time)
        {
            Vector3 wind = sails.WindSystem != null ? sails.WindSystem.EvaluateWind(ship.transform.position, time) : Vector3.zero;
            Vector3 vel = ship.Body != null && !ship.Body.isKinematic ? ship.Body.linearVelocity : Vector3.zero;
            Vector3 local = ship.transform.InverseTransformDirection(wind - vel);
            local.y = 0f;
            return local;
        }
    }
}
#endif

