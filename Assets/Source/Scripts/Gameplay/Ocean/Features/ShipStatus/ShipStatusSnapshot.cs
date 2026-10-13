#if FUSION2
using System.Collections.Generic;
using RumOverboard.Gameplay.Ocean.Features.Rigging;
using RumOverboard.Gameplay.Ocean.Simulation;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.ShipStatus
{
    /// <summary>
    /// Pure read-out of what the ship is doing, for the status widget (and tests): speed / heading,
    /// true + apparent wind relative to the bow with the point of sail, rudder, every sail (furled /
    /// being worked / set, hoist, yard angle) and every running line (mode, load, tension).
    /// Reads replicated state only, so it works the same on host and clients.
    /// </summary>
    public sealed class ShipStatusSnapshot
    {
        public struct SailStatus
        {
            public int Flat;
            public string Name;
            public float Furl;
            public bool FurlGoal;        // true = being / staying furled
            public bool FurlInProgress;
            public bool FurlWorked;      // someone is on the yard right now
            public float Hoist;
            public float YardAngle;      // deg, + = starboard arm aft
            /// <summary>
            /// How well the yard is braced for the apparent wind: forward drive at the current yard
            /// angle ÷ the best drive any yard angle the braces allow would give (0..1). -1 = no wind.
            /// </summary>
            public float Trim;
            /// <summary>Sail area actually catching wind (m², hoist × unfurled).</summary>
            public float Area;
            public string State;         // «скручен», «раскручен · поднят 80%», …
        }

        public struct LineStatus
        {
            public int Index;
            public int Sail;
            public RigLineKind Kind;
            public RigLineMode Mode;
            public string Short;         // «фал», «л.брас», «п.брас»
            public float Value;          // 0..1 sail setting
            public float Load;
            public float Tension;        // 0..1 (as drawn)
            public string ModeText;      // «закреплён», «в руках», «отдан»
            public string TensionText;   // «слабина», «натянут», «перегруз»
        }

        public float SpeedKnots;
        public float HeadingDeg;

        public float TrueWindSpeed;
        /// <summary>Where the true wind comes FROM relative to the bow, deg: 0 ahead, +90 starboard, ±180 astern.</summary>
        public float TrueWindFrom;
        public float ApparentWindSpeed;
        public float ApparentWindFrom;
        public string PointOfSail = string.Empty;
        public string Tack = string.Empty;

        public bool HasHelm;
        public float Rudder;
        public float RudderMax = 35f;
        /// <summary>+1 the rudder turns the ship to starboard, -1 to port, 0 amidships.</summary>
        public float TurnDirection;
        public float Wheel;              // -1..1
        public bool HelmManned;

        public readonly List<SailStatus> Sails = new();
        public readonly List<LineStatus> Lines = new();

        /// <summary>Area-weighted <see cref="SailStatus.Trim"/> of the sails that are set (0..1); -1 = none set / no wind.</summary>
        public float SailEfficiency = -1f;
        /// <summary>Sails set and catching wind.</summary>
        public int SailsSet;

        public const float MsToKnots = 1.943844f;

        /// <param name="velocity">Hull velocity (world, m/s) — from the body or a rendered-pose difference.</param>
        public void Capture(NetworkShip ship, Vector3 velocity, float time)
        {
            Sails.Clear();
            Lines.Clear();
            if (ship == null)
                return;
            Transform t = ship.transform;

            Vector3 flatVel = Vector3.ProjectOnPlane(velocity, Vector3.up);
            SpeedKnots = Vector3.Dot(flatVel, t.forward) * MsToKnots;
            Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            HeadingDeg = fwd.sqrMagnitude > 1e-6f ? Mathf.Repeat(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 360f) : 0f;

            ShipSailsAggregator sails = ship.Sails;
            Vector3 wind = sails != null && sails.WindSystem != null ? sails.WindSystem.EvaluateWind(t.position, time) : Vector3.zero;
            wind.y = 0f;
            TrueWindSpeed = wind.magnitude;
            TrueWindFrom = FromAngle(t, wind);
            Vector3 apparent = wind - flatVel;
            ApparentWindSpeed = apparent.magnitude;
            ApparentWindFrom = FromAngle(t, apparent);
            PointOfSail = TrueWindSpeed > 0.2f ? PointOfSailName(Mathf.Abs(TrueWindFrom)) : "штиль";
            Tack = TrueWindSpeed > 0.2f ? (TrueWindFrom >= 0f ? "правый галс" : "левый галс") : string.Empty;

            ShipHelm helm = HelmOf(ship);
            HasHelm = helm != null && helm.Object != null && helm.Object.IsValid;
            if (HasHelm)
            {
                Rudder = helm.RudderAngle;
                RudderMax = Mathf.Max(1f, helm.MaxRudderAngle);
                TurnDirection = helm.TurnDirection(Rudder);
                Wheel = helm.WheelNormalized;
                HelmManned = helm.IsOccupied;
            }

            int sailCount = sails != null ? Mathf.Min(sails.SailCount, NetworkShip.MaxSails) : 0;
            Vector3 awLocal = t.InverseTransformDirection(apparent);
            awLocal.y = 0f;
            float weighted = 0f, totalArea = 0f;
            SailsSet = 0;
            for (int s = 0; s < sailCount; s++)
            {
                var st = new SailStatus
                {
                    Flat = s,
                    Name = SailName(ship, s),
                    Furl = ship.GetSailFurl(s),
                    FurlGoal = ship.GetSailFurlGoal(s),
                    FurlInProgress = ship.IsSailFurlInProgress(s),
                    FurlWorked = ship.GetSailFurlWorker(s).IsRealPlayer,
                    Hoist = sails.GetHoist(s),
                    YardAngle = ship.GetYardAngle(s),
                    Area = sails.GetEffectiveArea(s),
                };
                st.Trim = ApparentWindSpeed > 0.3f
                    ? TrimEfficiency(awLocal, st.YardAngle, BraceLimit(ship, s), RestNormal(sails, s), sails.LiftFactor, sails.DragFactor, sails.SideForceFactor)
                    : -1f;
                st.State = SailStateText(st);
                Sails.Add(st);
                if (st.Area > 0.5f && st.Trim >= 0f)
                {
                    SailsSet++;
                    weighted += st.Trim * st.Area;
                    totalArea += st.Area;
                }
            }
            SailEfficiency = totalArea > 0.01f ? weighted / totalArea : -1f;

            float slackBelow = ShipStatusConfig.Active.SlackBelow;
            float humLoad = RiggingConfig.Active.RopeHumLoad;
            for (int i = 0; i < NetworkShip.MaxLines; i++)
            {
                RigLine line = ship.Line(i);
                if (line == null)
                    continue;
                RigLineMode mode = ship.GetLineMode(i);
                float load = ship.GetLineLoad(i);
                float tension = RopeRenderSystem.Tension(ship, i);
                Lines.Add(new LineStatus
                {
                    Index = i,
                    Sail = line.SailIndex,
                    Kind = line.Kind,
                    Mode = mode,
                    Short = line.Kind switch
                    {
                        RigLineKind.BracePort => "л.брас",
                        RigLineKind.BraceStarboard => "п.брас",
                        _ => "фал",
                    },
                    Value = ship.GetLineValue(i),
                    Load = load,
                    Tension = tension,
                    ModeText = mode switch
                    {
                        RigLineMode.Held => "в руках",
                        RigLineMode.Loose => "отдан",
                        _ => "закреплён",
                    },
                    TensionText = load >= humLoad ? "перегруз" : tension < slackBelow ? "слабина" : "натянут",
                });
            }
        }

        /// <summary>Widest brace angle (deg) the sail's braces allow; 60 if it has none.</summary>
        private static float BraceLimit(NetworkShip ship, int sail)
        {
            for (int i = 0; i < NetworkShip.MaxLines; i++)
            {
                RigLine line = ship.Line(i);
                if (line != null && line.SailIndex == sail &&
                    (line.Kind == RigLineKind.BracePort || line.Kind == RigLineKind.BraceStarboard))
                    return Mathf.Max(1f, line.BraceMaxAngle);
            }
            return 60f;
        }

        /// <summary>
        /// Flat normal of the sail panel at rest (yard square), from its corners exactly like the
        /// cloth's own normal (u across the yard × v down the sail) — the side lift is reckoned from.
        /// </summary>
        public static Vector3 RestNormal(ShipSailsAggregator sails, int flat)
        {
            if (sails != null && sails.TryGetFlatSail(flat, out var mast, out int li))
            {
                var p = mast.Sails[li];
                Vector3 n = Vector3.Cross(p.topRightLocal - p.topLeftLocal, p.bottomLeftLocal - p.topLeftLocal);
                n.y = 0f;
                if (n.sqrMagnitude > 1e-6f)
                    return n.normalized;
            }
            return Vector3.back;
        }

        /// <summary>
        /// Forward drive (ship-local, per unit area and pressure) of a square sail whose rest normal is
        /// <paramref name="restNormal"/>, on a yard turned <paramref name="yardDeg"/> (+ = starboard arm
        /// aft, the panel turns about the mast like the cloth's sheet angle) in apparent wind
        /// <paramref name="awLocal"/> — the same aerodynamics as the masts (<see cref="SailAerodynamicsSolver"/>).
        /// </summary>
        public static float ForwardDrive(Vector3 awLocal, float yardDeg, Vector3 restNormal, float lift, float drag, float side)
        {
            Vector3 n = Quaternion.AngleAxis(yardDeg, Vector3.up) * restNormal;
            SailAerodynamicsResult res = SailAerodynamicsSolver.Solve(new SailAerodynamicsInput(
                awLocal.normalized, n, Vector3.forward, 1f, 1f, 1f, 0f, lift, drag, side, 0f, 0f, true));
            return res.DriveForce.z;
        }

        /// <summary>
        /// Trim efficiency 0..1: drive at the current yard angle ÷ the best drive any yard angle
        /// within ±<paramref name="maxBrace"/> gives in this wind. 0 when no angle drives (head to wind).
        /// </summary>
        public static float TrimEfficiency(Vector3 awLocal, float yardDeg, float maxBrace, Vector3 restNormal, float lift, float drag, float side)
        {
            awLocal.y = 0f;
            if (awLocal.sqrMagnitude < 1e-4f)
                return -1f;
            float best = 0f;
            for (float a = -maxBrace; a <= maxBrace + 0.01f; a += 2f)
                best = Mathf.Max(best, ForwardDrive(awLocal, a, restNormal, lift, drag, side));
            if (best < 1e-4f)
                return 0f;
            return Mathf.Clamp01(ForwardDrive(awLocal, Mathf.Clamp(yardDeg, -maxBrace, maxBrace), restNormal, lift, drag, side) / best);
        }

        /// <summary>Best yard angle (deg) within ±<paramref name="maxBrace"/> for this wind.</summary>
        public static float BestYardAngle(Vector3 awLocal, float maxBrace, Vector3 restNormal, float lift, float drag, float side)
        {
            awLocal.y = 0f;
            float best = float.MinValue, bestA = 0f;
            for (float a = -maxBrace; a <= maxBrace + 0.01f; a += 2f)
            {
                float d = ForwardDrive(awLocal, a, restNormal, lift, drag, side);
                if (d > best + 1e-6f) { best = d; bestA = a; }
            }
            return bestA;
        }

        /// <summary>Signed angle (deg) the wind comes FROM, relative to the bow: + starboard, − port.</summary>
        public static float FromAngle(Transform ship, Vector3 windVelocity)
        {
            if (windVelocity.sqrMagnitude < 1e-6f)
                return 0f;
            Vector3 local = ship.InverseTransformDirection(-windVelocity);
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }

        /// <summary>Point of sail for the wind's angle off the bow (0..180).</summary>
        public static string PointOfSailName(float offBow)
        {
            offBow = Mathf.Abs(offBow);
            if (offBow < 35f) return "в лоб";
            if (offBow < 67f) return "бейдевинд";
            if (offBow < 112f) return "галфвинд";
            if (offBow < 157f) return "бакштаг";
            return "фордевинд";
        }

        public static string SailStateText(in SailStatus s)
        {
            int furl = Mathf.RoundToInt(s.Furl * 100f);
            if (s.FurlInProgress)
            {
                string verb = s.FurlGoal ? "скручивают" : "раскручивают";
                return s.FurlWorked ? $"{verb}… {furl}%" : $"скручен на {furl}% (брошено)";
            }
            if (s.Furl >= 0.5f)
                return "скручен";
            int hoist = Mathf.RoundToInt(s.Hoist * 100f);
            return hoist <= 1 ? "раскручен · спущен" : $"раскручен · поднят {hoist}%";
        }

        private NetworkShip _helmShip;
        private ShipHelm _helm;

        private ShipHelm HelmOf(NetworkShip ship)
        {
            if (_helmShip != ship || _helm == null)
            {
                _helmShip = ship;
                _helm = ship.GetComponentInChildren<ShipHelm>();
            }
            return _helm;
        }

        private static string SailName(NetworkShip ship, int flat)
        {
            var zone = ship.FurlZone(flat);
            if (zone != null && !string.IsNullOrEmpty(zone.SailLabel))
                return zone.SailLabel;
            if (ship.Sails != null && ship.Sails.TryGetFlatSail(flat, out var mast, out int s))
                return mast.GetSailName(s);
            return $"парус {flat}";
        }
    }
}
#endif
