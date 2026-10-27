#if FUSION2
using Fusion;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing
{
    /// <summary>What the last press did — shown to the rower (HUD) and in the debug window.</summary>
    public enum RowFeedback : byte
    {
        None = 0,
        Start = 1,   // stroke from rest
        InTime = 2,  // chained in rhythm: streak +1
        Early = 3,   // pressed too early: stroke fouled, streak lost
        Late = 4,    // missed the window: rhythm restarts
        Backing = 5, // started backing water
    }

    /// <summary>Replicated state of one sweep (host writes, every peer animates from it).</summary>
    public struct OarState : INetworkStruct
    {
        public PlayerRef Rower;
        /// <summary>Tick the current drive started (may be ahead of now: next stroke queued); 0 = never.</summary>
        public int StrokeTick;
        /// <summary>+1 pulling ahead, -1 backing water, 0 idle.</summary>
        public sbyte Sign;
        public byte Streak;
        /// <summary>Power of the current stroke ×100.</summary>
        public byte Power;
        // Keep the previous cycle while an early, on-beat press queues the next drive.
        public int PreviousStrokeTick;
        public byte PreviousPower;
        public byte Feedback;   // RowFeedback
        public int FeedbackTick;
    }

    /// <summary>Stroke timing in ticks (from the config's seconds).</summary>
    public readonly struct RowTiming
    {
        public readonly int Period, Drive, Early, Late, Sync;
        public readonly float Dt;

        public RowTiming(RowingConfig cfg, float dt)
        {
            Dt = Mathf.Max(0.001f, dt);
            Period = Mathf.Max(2, Mathf.RoundToInt(cfg.StrokePeriod / Dt));
            Drive = Mathf.Clamp(Mathf.RoundToInt(cfg.StrokePeriod * cfg.DriveFraction / Dt), 1, Period - 1);
            Early = Mathf.Clamp(Mathf.RoundToInt(cfg.EarlyWindow / Dt), 1, Period - 1);
            Late = Mathf.Max(1, Mathf.RoundToInt(cfg.LateWindow / Dt));
            Sync = Mathf.Max(0, Mathf.RoundToInt(cfg.SyncWindow / Dt));
        }
    }

    /// <summary>
    /// Pure stroke-rhythm rules (no Unity objects, no networking calls) — host applies them,
    /// HUD / visuals read the same math.
    /// </summary>
    public static class RowingRhythm
    {
        /// <summary>A stroke press (rising edge of W = +1 / S = -1) at tick <paramref name="now"/>.</summary>
        public static RowFeedback Press(ref OarState s, int sign, int now, in RowTiming t, RowingConfig cfg)
        {
            if (sign == 0)
                return RowFeedback.None;
            if (s.Sign == sign && s.StrokeTick > now)
                return RowFeedback.None; // the next stroke is already queued in rhythm

            int elapsed = s.Sign != 0 && s.StrokeTick > 0 ? now - s.StrokeTick : int.MaxValue;
            bool sameDir = s.Sign == sign;
            RowFeedback fb;
            if (sameDir && elapsed < t.Period - t.Early)
            {
                // Too early: the blade catches a crab — this stroke is spoiled and the rhythm is lost.
                s.Streak = 0;
                s.Power = (byte)Mathf.RoundToInt(s.Power * cfg.EarlyPenalty);
                fb = RowFeedback.Early;
            }
            else if (sameDir && elapsed <= t.Period + t.Late)
            {
                // In the window: the next drive starts exactly when this stroke ends (or now, if late-ish).
                s.Streak = (byte)Mathf.Min(s.Streak + 1, 99);
                s.PreviousStrokeTick = s.StrokeTick;
                s.PreviousPower = s.Power;
                s.StrokeTick = Mathf.Max(now, s.StrokeTick + t.Period);
                s.Power = Pct(Mathf.Min(1f, cfg.StartPower + s.Streak * cfg.StreakStep));
                fb = RowFeedback.InTime;
            }
            else
            {
                bool late = sameDir && elapsed != int.MaxValue && elapsed < t.Period * 3;
                s.Streak = 0;
                s.PreviousStrokeTick = 0;
                s.PreviousPower = 0;
                s.Sign = (sbyte)(sign > 0 ? 1 : -1);
                s.StrokeTick = Mathf.Max(1, now);
                s.Power = Pct(cfg.StartPower);
                fb = late ? RowFeedback.Late : sign < 0 ? RowFeedback.Backing : RowFeedback.Start;
            }
            s.Feedback = (byte)fb;
            s.FeedbackTick = now;
            return fb;
        }

        /// <summary>Ticks since the current drive began (negative while the next one is queued).</summary>
        public static int Elapsed(in OarState s, int now) => s.Sign == 0 || s.StrokeTick <= 0 ? int.MaxValue : now - s.StrokeTick;

        public static int ActiveStrokeTick(in OarState s, int now) =>
            s.StrokeTick > now && s.PreviousStrokeTick > 0 ? s.PreviousStrokeTick : s.StrokeTick;

        public static float ActivePower(in OarState s, int now) =>
            (s.StrokeTick > now && s.PreviousStrokeTick > 0 ? s.PreviousPower : s.Power) / 100f;

        private static int ActiveElapsed(in OarState s, int now) =>
            s.Sign == 0 || s.StrokeTick <= 0 ? int.MaxValue : now - ActiveStrokeTick(s, now);

        /// <summary>Thrust shape 0..1 at tick <paramref name="now"/> (sin over the drive, 0 on the recovery).</summary>
        public static float DriveShape(in OarState s, int now, in RowTiming t)
        {
            int e = ActiveElapsed(s, now);
            if (e < 0 || e >= t.Drive)
                return 0f;
            return Mathf.Sin(Mathf.PI * (e + 0.5f) / t.Drive);
        }

        /// <summary>The oar is in a live stroke cycle (drive or recovery).</summary>
        public static bool InCycle(in OarState s, int now, in RowTiming t)
        {
            int e = ActiveElapsed(s, now);
            return e >= 0 && e < t.Period;
        }

        /// <summary>Waiting for the next press inside the rhythm window.</summary>
        public static bool InWindow(in OarState s, int now, in RowTiming t)
        {
            int e = Elapsed(s, now);
            return e != int.MaxValue && e >= t.Period - t.Early && e <= t.Period + t.Late;
        }

        /// <summary>
        /// Visual pose: blade fore(+)/aft(-) angle and lift (0 = blade in the water, 1 = lifted) at a
        /// fractional tick. Pulling: catch forward → drive aft in the water → lift → recover forward.
        /// Backing mirrors the sweep.
        /// </summary>
        public static void Pose(in OarState s, float nowTick, in RowTiming t, out float fore01, out float lift01)
        {
            fore01 = 0f;
            lift01 = 1f;
            if (s.Sign == 0 || s.StrokeTick <= 0)
                return;
            float e = nowTick - s.StrokeTick;
            if (e < 0f)
                e += t.Period; // finishing the previous recovery while the next drive is queued
            if (e < 0f || e > t.Period + t.Late + 2f / t.Dt)
                return; // long idle: rest the blade

            float dir = s.Sign > 0 ? 1f : -1f;
            if (e < t.Drive)
            {
                float k = e / t.Drive;
                fore01 = Mathf.Lerp(1f, -1f, Mathf.SmoothStep(0f, 1f, k)) * dir;
                lift01 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 6f));
            }
            else if (e < t.Period)
            {
                float k = (e - t.Drive) / Mathf.Max(1f, t.Period - t.Drive);
                fore01 = Mathf.Lerp(-1f, 1f, Mathf.SmoothStep(0f, 1f, k)) * dir;
                lift01 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 4f));
            }
            else
            {
                fore01 = dir; // at the catch, blade up, waiting for the next press
                lift01 = 1f;
            }
        }

        private static byte Pct(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 100f), 0, 100);
    }
}
#endif
