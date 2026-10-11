#if FUSION2
using System.Collections.Generic;
using RumOverboard.Gameplay.Ocean.Features.Rigging;
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
                };
                st.State = SailStateText(st);
                Sails.Add(st);
            }

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
