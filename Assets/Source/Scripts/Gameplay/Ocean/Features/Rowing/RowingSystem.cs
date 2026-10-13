#if FUSION2
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing
{
    /// <summary>
    /// Host-side sweeps: validates the rowers, runs debug bot rowers, and turns every live drive into
    /// thrust on the hull (called from <see cref="NetworkShip.FixedUpdateNetwork"/> before the hull
    /// step, so the forces join this tick's physics).
    ///
    ///   F = PeakForce · power · sin(π·drive) · sync · falloff   (× BackingFactor when backing)
    ///     sync    = 1 + min(MaxSyncBonus, SyncBonusPerRower · others whose drive started together)
    ///     falloff = clamp01(1 − v_ahead·sign / MaxRowSpeed)    — blades can't outrun the hull
    ///
    /// The force acts along the keel at the centre of mass; its lever about the centre line (×
    /// YawLeverage — a gameplay boost) turns the ship, so one side rowing alone brings her round.
    /// </summary>
    public static class RowingSystem
    {
        public static void Tick(NetworkShip ship, float dt)
        {
            int count = ship.OarCount;
            Rigidbody rb = ship.Body;
            if (count == 0 || rb == null)
                return;

            RowingConfig cfg = ship.RowingSettings;
            var t = new RowTiming(cfg, dt);
            int now = ship.Runner.Tick;
            Transform tr = ship.transform;

            // 1) Rowers still at their oars? Bots press on the beat.
            for (int i = 0; i < count; i++)
            {
                OarState s = ship.GetOar(i);
                if (s.Rower.IsRealPlayer)
                {
                    NetworkPlayer player = ship.FindCrewPlayer(s.Rower);
                    if (player == null || !player.IsRowing || player.IsKnockedOut ||
                        ship.Oar(i) == null || !player.AttachedRef.Equals(ship.Oar(i).Ref))
                        ship.ClearOar(i);
                    continue;
                }

                int bot = ship.GetOarBot(i);
                if (bot != 0)
                {
                    int e = RowingRhythm.Elapsed(s, now);
                    if (s.Sign != bot || e == int.MaxValue || e >= t.Period - 1)
                        ship.PressOar(i, bot, now);
                }
                else if (s.Sign != 0)
                {
                    ship.ClearOar(i); // nobody on it: let the oar rest
                }
            }

            // 2) Thrust from every live drive.
            Vector3 fwd = tr.forward;
            Vector3 up = tr.up;
            Vector3 com = rb.worldCenterOfMass;
            float ahead = Vector3.Dot(rb.linearVelocity - ship.WaterVelocity, fwd);
            for (int i = 0; i < count; i++)
            {
                OarState s = ship.GetOar(i);
                float shape = RowingRhythm.DriveShape(s, now, t);
                if (shape <= 0f || (!s.Rower.IsRealPlayer && ship.GetOarBot(i) == 0))
                {
                    ship.SetOarThrust(i, 0f);
                    continue;
                }

                int synced = 0;
                for (int j = 0; j < count; j++)
                {
                    if (j == i) continue;
                    OarState o = ship.GetOar(j);
                    if (o.Sign == s.Sign && (o.Rower.IsRealPlayer || ship.GetOarBot(j) != 0) &&
                        Mathf.Abs(RowingRhythm.ActiveStrokeTick(o, now) - RowingRhythm.ActiveStrokeTick(s, now)) <= t.Sync && RowingRhythm.InCycle(o, now, t))
                        synced++;
                }
                float sync = 1f + Mathf.Min(cfg.MaxSyncBonus, cfg.SyncBonusPerRower * synced);
                float falloff = Mathf.Clamp01(1f - s.Sign * ahead / Mathf.Max(0.1f, cfg.MaxRowSpeed));
                float force = cfg.PeakForce * RowingRhythm.ActivePower(s, now) * shape * sync * falloff *
                              (s.Sign < 0 ? cfg.BackingFactor : 1f);
                ship.SetOarThrust(i, force * s.Sign);
                if (force <= 0f)
                    continue;

                Vector3 f = fwd * (force * s.Sign);
                rb.AddForce(f, ForceMode.Force);

                OarStation oar = ship.Oar(i);
                if (oar != null && oar.Pivot != null && cfg.YawLeverage > 0f)
                {
                    Vector3 lever = Vector3.Project(oar.Pivot.position - com, tr.right);
                    Vector3 torque = Vector3.Project(Vector3.Cross(lever, f), up) * cfg.YawLeverage;
                    rb.AddTorque(torque, ForceMode.Force);
                }
            }
        }
    }
}
#endif
