#if FUSION2
using RumOverboard.Core.Sim;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rigging
{
    /// <summary>
    /// Running-rigging system (ECS-style: stateless, iterates RigLine data, reads/writes the
    /// ship's networked line arrays). Host only, inside the network tick, before sails/buoyancy:
    ///
    ///   Tied  — the end sits on its pin; Out is fixed.
    ///   Held  — the end is in the holder's hand. LMB hauls (Out↑), RMB eases (Out↓). Walking away
    ///           from the block with the rope taut hauls it too (up to WalkHaulRate); past that the
    ///           rope is a leash and holds the crew member back. Everything is weighted by the
    ///           line's LOAD (YardSystem): a loaded line hauls slowly, eases fast, pulls the holder
    ///           toward the block and, past the grip, slips through the hands.
    ///           Braces: hauling one is limited by its partner (other yardarm) — a tied partner
    ///           blocks, a loose one runs out.
    ///   Loose — nobody on it: the load runs the rope back up (Out↓ to the stopper), the
    ///           free end swings/falls (simulated in ship space, collides with the ship) and can be
    ///           caught again.
    ///
    /// <see cref="Apply"/> then maps every line onto its sail (all peers; cosmetic on proxies).
    /// </summary>
    public static class RopeSystem
    {
        private const float HandHeight = 1.15f;
        private const float HandForward = 0.35f;
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        public static void Tick(NetworkShip ship, float dt)
        {
            RiggingConfig cfg = RiggingConfig.Active;
            for (int i = 0; i < NetworkShip.MaxLines; i++)
            {
                RigLine line = ship.Line(i);
                if (line == null)
                    continue;

                switch (ship.GetLineMode(i))
                {
                    case RigLineMode.Tied:
                        BelayPin pin = ship.GetLinePin(i);
                        if (pin != null)
                            ship.SetLineEndLocal(i, ship.transform.InverseTransformPoint(pin.TiePoint));
                        break;

                    case RigLineMode.Held:
                        TickHeld(ship, line, i, dt, cfg);
                        break;

                    case RigLineMode.Loose:
                        TickLoose(ship, line, i, dt, cfg);
                        break;
                }

                SyncEndHandle(ship, line, i);
            }
        }

        private static void TickHeld(NetworkShip ship, RigLine line, int i, float dt, RiggingConfig cfg)
        {
            NetworkPlayer holder = FindPlayer(ship.GetLineHolder(i));
            if (holder == null)
            {
                ship.DropLine(i, ship.GetLineHolder(i)); // holder left the session
                return;
            }

            NetworkShip.LineInput input = ship.ConsumeLineInput(i);
            float load = ship.GetLineLoad(i);
            float heavy = Mathf.Lerp(1f, cfg.HeavyHaulFactor, Mathf.Clamp01(load));
            float before = ship.GetLineOut(i);
            float rope = before;
            if (input.Haul) rope += cfg.HaulRate * heavy * dt;
            if (input.Ease) rope -= cfg.EaseRate * (1f + cfg.EaseLoadBoost * load) * dt;

            // A load beyond what hands can hold drags the rope out through them — you feel the sail.
            if (load > cfg.HoldGrip)
                rope -= cfg.SlipRate * (load - cfg.HoldGrip) * dt;

            // Braces: the other brace of the yard must give. A loose partner just runs out through its
            // block; a tied (or held) one stops the yard — hauling is blocked until it's eased.
            float maxOut = line.OutMax;
            int partner = ship.BracePartner(i);
            bool partnerGives = partner >= 0 && ship.GetLineMode(partner) == RigLineMode.Loose;
            if (partner >= 0 && !partnerGives)
                maxOut = Mathf.Max(line.OutMin, YardSystem.MaxOutWithPartner(ship, line, partner));

            Rigidbody body = holder.GetComponent<Rigidbody>();
            Vector3 block = line.Block.position;
            Vector3 hand = HandPosition(body.position, holder.ViewYaw);
            Vector3 toHand = hand - block;
            float distance = toHand.magnitude;
            Vector3 dir = distance > 1e-3f ? toHand / distance : Vector3.down;

            // Walking away with the rope taut drags more rope down through the block (just as heavy).
            if (distance > rope && rope < maxOut)
                rope = Mathf.Min(maxOut, Mathf.Min(distance, rope + cfg.WalkHaulRate * heavy * dt));

            bool blocked = false;
            if (rope > maxOut && rope > before)
            {
                rope = Mathf.Max(before, maxOut);
                blocked = true;
            }
            rope = Mathf.Clamp(rope, line.OutMin, line.OutMax);

            if (partnerGives)
            {
                RigLine pl = ship.Line(partner);
                float allowed = pl.OutFromValue(1f - line.ValueFromOut(rope));
                if (ship.GetLineOut(partner) > allowed)
                    ship.SetLineOut(partner, allowed);
            }

            // Past the rope's length it's a leash: cancel the outward motion and pull back in.
            float excess = distance - rope;
            if (excess > 0f && !body.isKinematic)
            {
                Vector3 anchorVel = ship.Body != null ? ship.Body.GetPointVelocity(block) : Vector3.zero;
                Vector3 v = body.linearVelocity;
                float outward = Vector3.Dot(v - anchorVel, dir);
                if (outward > 0f)
                    v -= dir * outward;
                v -= dir * Mathf.Min(excess * cfg.LeashStiffness, 3f);
                body.linearVelocity = v;
            }

            // A loaded line tugs at the hands (toward the block).
            if (excess > -0.15f && load > 0.01f && !body.isKinematic)
                body.AddForce(-dir * cfg.LoadPull * Mathf.Min(load, 1.5f), ForceMode.Force);

            // Hauling into a stop (tied partner) is pure strain — show it.
            if (blocked && input.Haul)
                ship.SetLineLoad(i, Mathf.Max(load, 1f));

            ship.SetLineOut(i, rope);
            ship.SetLineEndLocal(i, ship.transform.InverseTransformPoint(hand));
        }

        private static void TickLoose(NetworkShip ship, RigLine line, int i, float dt, RiggingConfig cfg)
        {
            // The load runs the line out until the stopper knot hits the block. Halyards always have
            // the yard's weight on them; a brace only runs while the wind presses the yard on it.
            float rope = ship.GetLineOut(i);
            float load = ship.GetLineLoad(i);
            float run = line.IsBrace
                ? cfg.RunRate * Mathf.Clamp01(load)
                : cfg.RunRate * Mathf.Lerp(cfg.RunRateLight, 1f, Mathf.Clamp01(load));
            rope = Mathf.Max(line.OutMin, rope - run * dt);
            ship.SetLineOut(i, rope);

            // Free end: a point mass in SHIP space (rides the deck), gravity in ship axes.
            Transform t = ship.transform;
            Vector3 blockLocal = t.InverseTransformPoint(line.Block.position);
            Vector3 end = ship.GetLineEndLocal(i);
            ref Vector3 vel = ref ship.EndVelocity(i);

            vel += t.InverseTransformDirection(Vector3.down) * (cfg.EndGravity * dt);
            vel *= Mathf.Clamp01(1f - cfg.EndAirDamping * dt);
            Vector3 next = end + vel * dt;

            // Rope length: the end can't be farther from the block than the deck-side rope.
            Vector3 fromBlock = next - blockLocal;
            if (fromBlock.magnitude > rope)
            {
                Vector3 n = fromBlock.normalized;
                next = blockLocal + n * rope;
                float radial = Vector3.Dot(vel, n);
                if (radial > 0f) vel -= n * radial; // swing, don't stretch
            }

            // Collide with the ship (and anything solid) between the old and new positions.
            Vector3 a = t.TransformPoint(end);
            Vector3 b = t.TransformPoint(next);
            Vector3 step = b - a;
            float len = step.magnitude;
            if (len > 1e-4f)
            {
                int count = Physics.SphereCastNonAlloc(a, cfg.EndRadius, step / len, Hits, len, ~LayerMask.GetMask("Player", "Ragdoll", "UI"),
                    QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                RaycastHit hit = default;
                for (int h = 0; h < count; h++)
                    if (Hits[h].distance > 0f && Hits[h].distance < best)
                    {
                        best = Hits[h].distance;
                        hit = Hits[h];
                    }
                if (best < float.MaxValue)
                {
                    Vector3 nLocal = t.InverseTransformDirection(hit.normal);
                    next = t.InverseTransformPoint(hit.point + hit.normal * cfg.EndRadius);
                    float into = Vector3.Dot(vel, nLocal);
                    if (into < 0f)
                        vel -= nLocal * into * (1f + cfg.EndBounce);
                    vel -= Vector3.ProjectOnPlane(vel, nLocal) * cfg.EndFriction;
                }
            }

            ship.SetLineEndLocal(i, next);
        }

        // Grab handle follows the end; only catchable (collider on) while loose.
        public static void SyncEndHandle(NetworkShip ship, RigLine line, int i)
        {
            RopeEnd handle = line.End;
            if (handle == null)
                return;
            handle.transform.position = ship.transform.TransformPoint(ship.GetLineEndLocal(i));
            bool loose = ship.GetLineMode(i) == RigLineMode.Loose;
            foreach (Collider c in handle.GetComponents<Collider>())
                if (c.enabled != loose)
                    c.enabled = loose;
        }

        public static void Apply(NetworkShip ship, ShipSailsAggregator sails)
        {
            if (sails == null)
                return;
            for (int i = 0; i < NetworkShip.MaxLines; i++)
            {
                RigLine line = ship.Line(i);
                if (line == null || line.SailIndex < 0 || line.SailIndex >= sails.SailCount)
                    continue;
                if (line.Kind == RigLineKind.Halyard)
                    sails.SetHoist(line.SailIndex, ship.GetLineValue(i));
                else
                    sails.SetSheetAngle(line.SailIndex, ship.GetYardAngle(line.SailIndex)); // yard set by YardSystem
            }
        }

        public static Vector3 HandPosition(Vector3 bodyRoot, float viewYaw) =>
            bodyRoot + Vector3.up * HandHeight + Quaternion.Euler(0f, viewYaw, 0f) * Vector3.forward * HandForward;

        public static NetworkPlayer FindPlayer(Fusion.PlayerRef player)
        {
            if (!player.IsRealPlayer)
                return null;
            var all = NetworkPlayer.All;
            for (int p = 0; p < all.Count; p++)
                if (all[p] != null && all[p].Object != null && all[p].Object.InputAuthority == player)
                    return all[p];
            return null;
        }

        /// <summary>Debug / tests: total registered lines.</summary>
        public static int CountLines() => SimWorld.All<RigLine>().Count;
    }
}
#endif
