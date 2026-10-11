#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Ocean.Features.Rigging;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.SailFurl
{
    /// <summary>
    /// Furling / casting loose (host, every tick, before <see cref="YardSystem"/>).
    ///
    /// Each flat sail has a networked furl progress (0 = loose, 1 = gasketed on the yard), a goal and
    /// the crew member working it. Progress only moves while that worker stays on the yard (within the
    /// zone's reach + slack, conscious, hands free); walking away pauses the work where it is.
    /// Furling a canvas that holds wind is slower (∝ sail load), casting loose is quick.
    ///
    /// A furled sail stays on the yard whatever the halyard says; once loose it drops as far as the
    /// halyard is hoisted (<see cref="Masts.ShipMast.SailPanel.Deployment"/>).
    /// </summary>
    public static class SailFurlSystem
    {
        public static void Tick(NetworkShip ship, ShipSailsAggregator sails, float dt, float time)
        {
            if (sails == null || !ship.HasStateAuthority)
                return;
            SailFurlConfig cfg = SailFurlConfig.Active;
            RiggingConfig rig = RiggingConfig.Active;

            Vector3 aw = YardSystem.ApparentWindLocal(ship, sails, time);
            float q = 0.5f * sails.AirDensity * aw.sqrMagnitude;

            int count = Mathf.Min(sails.SailCount, NetworkShip.MaxSails);
            for (int s = 0; s < count; s++)
            {
                PlayerRef worker = ship.GetSailFurlWorker(s);
                if (!ship.IsSailFurlInProgress(s))
                {
                    if (worker.IsRealPlayer)
                        ship.ClearSailFurlWorker(s);
                    continue;
                }
                if (!worker.IsRealPlayer)
                    continue; // paused: nobody on the yard

                if (!WorkerOnYard(ship, s, worker, cfg))
                {
                    ship.ClearSailFurlWorker(s);
                    continue;
                }

                float furl = ship.GetSailFurl(s);
                float goal = ship.GetSailFurlGoal(s) ? 1f : 0f;
                float rate;
                if (goal > furl)
                {
                    // Wind load on the canvas still set (it shrinks as the sail comes in).
                    float load = q * sails.GetEffectiveArea(s) / Mathf.Max(1f, rig.FullLoadForce);
                    rate = 1f / (cfg.FurlTime * (1f + Mathf.Clamp(load, 0f, 3f) * cfg.WindResistance));
                }
                else
                {
                    rate = 1f / cfg.UnfurlTime;
                }

                furl = Mathf.MoveTowards(furl, goal, rate * dt);
                ship.SetSailFurlProgress(s, furl);
                if (Mathf.Approximately(furl, goal))
                    ship.ClearSailFurlWorker(s);
            }
        }

        /// <summary>Every peer: push the replicated furl state into the cloth.</summary>
        public static void Apply(NetworkShip ship, ShipSailsAggregator sails)
        {
            if (sails == null)
                return;
            int count = Mathf.Min(sails.SailCount, NetworkShip.MaxSails);
            for (int s = 0; s < count; s++)
                sails.SetFurl(s, ship.GetSailFurl(s));
        }

        private static bool WorkerOnYard(NetworkShip ship, int sail, PlayerRef worker, SailFurlConfig cfg)
        {
            NetworkPlayer player = RopeSystem.FindPlayer(worker);
            if (player == null || player.IsKnockedOut || player.IsSteering || player.IsHoldingRope)
                return false;
            YardFurlZone zone = ship.FurlZone(sail);
            if (zone == null)
                return true; // no zone authored (debug ships): the work is abstract
            float dist = zone.DistanceTo(player.CameraAnchor.position, out _);
            return dist <= zone.MaxDistance + cfg.WorkSlack;
        }
    }
}
#endif
