#if FUSION2
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fusion;
using RumOverboard.Core.Sim;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Ocean.Features.Rigging;
using RumOverboard.Networking;
using RumOverboard.StateMachine;
using UnityEngine;

namespace RumOverboard.Gameplay.Diagnostics
{
    /// <summary>
    /// Automated play-mode scenario for the ship sandbox. Activated only by the command-line flag
    /// <c>-sandboxSmoke</c> (see ShipSandboxSmokeTest editor runner). Runs offline (Single mode) and
    /// drives the local crew member through scripted input on the host:
    ///   idle on the rolling deck (ship-local drift + render jitter), walk, climb a mast to the
    ///   platform, take the helm and steer, work a halyard and secure it, ship stability.
    /// Prints a report and exits the editor with 0 (all passed) or 1.
    /// </summary>
    public sealed class SandboxSmokeTest : MonoBehaviour
    {
        private const string Flag = "-sandboxSmoke";

        private readonly StringBuilder _report = new();
        private readonly List<string> _failures = new();
        private NetworkInputData _input;
        private NetworkPlayer _player;
        private NetworkShip _ship;

        // Render-jitter sampling (LateUpdate, rendered poses in ship space).
        private bool _sampleRender;
        private Vector3 _lastEyeLocal;
        private bool _hasLast;
        private readonly List<float> _renderSteps = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains(Flag))
                return;
            var cm = FindAnyObjectByType<ConnectionManager>();
            cm?.OverrideAutoStart(GameMode.Single);
            new GameObject("SandboxSmokeTest").AddComponent<SandboxSmokeTest>();
        }

        private IEnumerator Start()
        {
            Log("== Ship sandbox smoke test ==");
            float deadline = Time.realtimeSinceStartup + 30f;
            while ((_player == null || _ship == null) && Time.realtimeSinceStartup < deadline)
            {
                _player = NetworkPlayer.All.FirstOrDefault(p => p != null && p.HasInputAuthority);
                _ship = NetworkShip.All.FirstOrDefault();
                yield return null;
            }
            if (_player == null || _ship == null)
            {
                Fail($"spawn: player={_player != null} ship={_ship != null}");
                Finish();
                yield break;
            }
            _player.InputOverride = () => _input;
            Log($"spawned: player at {_player.transform.position}, ship at {_ship.transform.position}");

            yield return Run(ShipStability());
            yield return Run(IdleOnDeck());
            yield return Run(Walk());
            yield return Run(Stairs());
            yield return Run(Shrouds());
            yield return Run(LookAtProbe());
            yield return Run(ClimbMast());
            yield return Run(Helm());
            yield return Run(Rope());
            yield return Run(Braces());
            yield return Run(PartialRagdoll());
            yield return Run(IdleWhileSailing());
            Finish();
        }

        // Two braces per yard: hauling one against a tied partner is blocked; with the partner
        // loose it runs out and the yard's range moves.
        private IEnumerator Braces()
        {
            _ship.ResetLines();
            yield return Wait(0.5f);
            RigLine port = null, star = null;
            for (int l = 0; l < NetworkShip.MaxLines; l++)
            {
                RigLine line = _ship.Line(l);
                if (line == null || line.SailIndex != 0) continue;
                if (line.Kind == RigLineKind.BracePort) port = line;
                if (line.Kind == RigLineKind.BraceStarboard) star = line;
            }
            if (port == null || star == null) { Fail("braces: sail #0 has no port+starboard brace pair"); yield break; }
            int p = port.LineIndex, s = star.LineIndex;
            Check(_ship.BracePartner(p) == s && _ship.BracePartner(s) == p, "braces: port/starboard braces are paired");
            Check(_ship.GetLineMode(p) == RigLineMode.Tied && _ship.GetLineMode(s) == RigLineMode.Tied, "braces: both start made fast");

            // 1) Cast off the port brace and haul: the tied starboard brace stops the yard.
            yield return TakeFromPin(port.HomePin, port);
            Check(_ship.GetLineMode(p) == RigLineMode.Held, "braces: E casts the port brace off into your hands");
            float out0 = _ship.GetLineOut(p);
            _input = default;
            _input.Buttons.Set(NetworkInputData.ButtonHaul, true);
            yield return Wait(1.2f);
            _input = default;
            float out1 = _ship.GetLineOut(p);
            Log($"braces: haul port vs tied starboard: out {out0:F2} → {out1:F2}, load {_ship.GetLineLoad(p):F2}");
            Check(out1 <= out0 + 0.05f, "braces: can't haul one brace while the other is made fast");

            // 2) Let the port brace go, take the starboard one and haul: the loose port runs out.
            _input.Buttons.Set(NetworkInputData.ButtonDrop, true);
            yield return Wait(0.1f);
            _input = default;
            yield return Wait(0.3f);
            Check(_ship.GetLineMode(p) == RigLineMode.Loose, "braces: G lets the port brace go");
            YardSystem.Limits(_ship, p, s, star.BraceMaxAngle, out float lo0, out _);
            yield return TakeFromPin(star.HomePin, star);
            float sOut0 = _ship.GetLineOut(s), pOut0 = _ship.GetLineOut(p);
            _input = default;
            _input.Buttons.Set(NetworkInputData.ButtonHaul, true);
            yield return Wait(1.5f);
            _input = default;
            yield return Wait(0.2f);
            float sOut1 = _ship.GetLineOut(s), pOut1 = _ship.GetLineOut(p);
            YardSystem.Limits(_ship, p, s, star.BraceMaxAngle, out float lo1, out float hi1);
            float yard = _ship.GetYardAngle(0);
            Log($"braces: haul starboard: out {sOut0:F2} → {sOut1:F2}, loose port {pOut0:F2} → {pOut1:F2}, yard range lo {lo0:F0}° → {lo1:F0}° (hi {hi1:F0}°), yard {yard:F0}°");
            Check(sOut1 > sOut0 + 0.5f, "braces: with the partner cast off the brace hauls");
            Check(pOut1 <= pOut0 + 0.01f, "braces: the loose partner gives (never gains rope)");
            Check(lo1 > lo0 + 5f, "braces: hauling the starboard brace swings the yard's range to starboard");
            Check(yard >= lo1 - 1f && yard <= hi1 + 1f, $"braces: yard ({yard:F0}°) stays inside what the braces allow");

            _ship.ResetLines();
            yield return Wait(0.5f);
            Check(!Has(PlayerState.HoldingRope), "braces: reset takes the line out of your hands");
        }

        private IEnumerator TakeFromPin(BelayPin pin, RigLine line)
        {
            Vector3 railOut = Vector3.ProjectOnPlane(pin.transform.position - line.Block.position, Vector3.up).normalized;
            yield return PlaceAt(pin.transform.position + railOut * 0.75f + Vector3.down * 0.85f + Vector3.up * 0.1f,
                Quaternion.LookRotation(-railOut, Vector3.up));
            yield return Press(pin.Ref);
        }

        // A small ragdoll amount (a few sips) must NOT floor the crew member: still animated,
        // still walking. Only a knockout goes fully physical, and it gets back up.
        private IEnumerator PartialRagdoll()
        {
            if (_ship.TryGetCrewSpawn(0, out Vector3 pos, out Quaternion rot))
                yield return PlaceAt(pos + Vector3.up * 0.1f, rot);
            _player.DebugSetDrunkRagdoll(0.3f, 0.1f);
            yield return Wait(0.8f);
            Check(!_player.IsKnockedOut, $"ragdoll: 10% ragdoll (drunk 30%) is not a knockout (amount {_player.RagdollControl:F2})");
            var ragdoll = _player.GetComponentInChildren<RagdollController>();
            Check(ragdoll == null || !ragdoll.IsPhysical, "ragdoll: partial ragdoll keeps the body animated (bones kinematic)");

            Vector3 start = ShipLocal(_player.transform.position);
            _input = default;
            _input.LookSpace = default;
            _input.LookYaw = _ship.transform.eulerAngles.y;
            _input.Move = new Vector2(0f, 1f);
            yield return Wait(1.5f);
            _input = default;
            float moved = Vector3.Distance(start, ShipLocal(_player.transform.position));
            Log($"ragdoll: walked {moved:F2}m at ragdoll {_player.RagdollControl:F2}, grounded={Has(PlayerState.Grounded)}");
            Check(moved > 1.5f && Has(PlayerState.Grounded), "ragdoll: still on your feet and walking with a partial ragdoll");

            _player.Knockout();
            yield return Wait(0.3f);
            Check(_player.IsKnockedOut, "ragdoll: knockout goes fully physical");
            yield return Wait(3f);
            Check(!_player.IsKnockedOut, "ragdoll: gets back up after a knockout");
            _player.DebugSetDrunkRagdoll(0f, 0f);
            yield return Wait(1f);
        }

        private IEnumerator Run(IEnumerator step)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!step.MoveNext()) yield break;
                    current = step.Current;
                }
                catch (Exception e)
                {
                    Fail($"exception: {e}");
                    yield break;
                }
                yield return current;
            }
        }

        // ---------------------------------------------------------------------------------------
        private IEnumerator ShipStability()
        {
            Vector3 start = _ship.transform.position;
            float maxTilt = 0f, minY = float.MaxValue, maxY = float.MinValue;
            for (float t = 0; t < 6f; t += Time.deltaTime)
            {
                Vector3 up = _ship.transform.up;
                maxTilt = Mathf.Max(maxTilt, Vector3.Angle(up, Vector3.up));
                minY = Mathf.Min(minY, _ship.transform.position.y);
                maxY = Mathf.Max(maxY, _ship.transform.position.y);
                yield return null;
            }
            Vector3 end = _ship.transform.position;
            Log($"ship: start={start} end={end} y∈[{minY:F2},{maxY:F2}] maxTilt={maxTilt:F1}° bodies={_ship.GetComponentsInChildren<Rigidbody>().Length}");
            Check(_ship.GetComponentsInChildren<Rigidbody>().Length == 1, "ship must be exactly one Rigidbody");
            Check(maxTilt < 25f, $"ship tilt {maxTilt:F1}° < 25°");
            Check(maxY - minY < 6f, $"ship heave {maxY - minY:F2} < 6m");
        }

        private IEnumerator IdleOnDeck()
        {
            _input = default;
            yield return Wait(1.5f);
            Vector3 startLocal = ShipLocal(_player.transform.position);
            _renderSteps.Clear();
            _hasLast = false;
            _sampleRender = true;
            yield return Wait(4f);
            _sampleRender = false;
            Vector3 endLocal = ShipLocal(_player.transform.position);
            float drift = Vector3.Distance(startLocal, endLocal);
            float maxStep = _renderSteps.Count > 0 ? _renderSteps.Max() : 0f;
            float mean = _renderSteps.Count > 0 ? _renderSteps.Average() : 0f;
            Log($"idle: grounded={Has(PlayerState.Grounded)} ship-local drift={drift:F3}m, rendered eye step/frame mean={mean * 1000:F2}mm max={maxStep * 1000:F2}mm over {_renderSteps.Count} frames");
            Check(Has(PlayerState.Grounded), "idle: crew grounded on deck");
            Check(drift < 0.25f, $"idle: ship-local drift {drift:F3} < 0.25m");
            Check(maxStep < 0.02f, $"idle: rendered eye jitter {maxStep * 1000:F1}mm < 20mm/frame");
        }

        private IEnumerator IdleWhileSailing()
        {
            // Hoist every sail (as if the crew hauled all halyards) and let her pick up speed.
            for (int l = 0; l < NetworkShip.MaxLines; l++)
                if (_ship.Line(l) is { Kind: RigLineKind.Halyard } halyard)
                    _ship.SetLineOut(l, halyard.OutFromValue(1f));
            yield return Wait(6f);

            if (_ship.TryGetCrewSpawn(0, out Vector3 p, out Quaternion r))
                yield return PlaceAt(p + Vector3.up * 0.1f, r);
            yield return Wait(1f);

            float speed = _ship.Body.linearVelocity.magnitude;
            float yawRate = _ship.Body.angularVelocity.y * Mathf.Rad2Deg;
            Vector3 startLocal = ShipLocal(_player.transform.position);
            _renderSteps.Clear();
            _hasLast = false;
            _sampleRender = true;
            yield return Wait(4f);
            _sampleRender = false;
            float drift = Vector3.Distance(startLocal, ShipLocal(_player.transform.position));
            float maxStep = _renderSteps.Count > 0 ? _renderSteps.Max() : 0f;
            float mean = _renderSteps.Count > 0 ? _renderSteps.Average() : 0f;
            Log($"sailing idle: ship speed={speed:F2}m/s yawRate={yawRate:F1}°/s grounded={Has(PlayerState.Grounded)} " +
                $"drift={drift:F3}m eye step mean={mean * 1000:F2}mm max={maxStep * 1000:F2}mm");
            Check(speed > 0.5f, $"sailing: sails drive the ship ({speed:F2} m/s)");
            Check(Has(PlayerState.Grounded), "sailing: crew still on deck");
            Check(drift < 0.25f, $"sailing: ship-local drift {drift:F3} < 0.25m");
            Check(maxStep < 0.02f, $"sailing: rendered eye jitter {maxStep * 1000:F1}mm < 20mm/frame");
        }

        private IEnumerator Walk()
        {
            Vector3 startLocal = ShipLocal(_player.transform.position);
            _input = default;
            _input.LookSpace = _ship.Object.Id;
            _input.LookYaw = 180f; // aft along the deck, relative to the ship
            _input.Move = new Vector2(0f, 1f);
            yield return Wait(1.2f);
            _input = default;
            yield return Wait(0.8f);
            Vector3 endLocal = ShipLocal(_player.transform.position);
            float moved = Vector3.Distance(new Vector3(startLocal.x, 0, startLocal.z), new Vector3(endLocal.x, 0, endLocal.z));
            Log($"walk: moved {moved:F2}m (ship-local) {startLocal} → {endLocal}");
            Check(moved > 1.5f, $"walk: moved {moved:F2} > 1.5m");
        }

        // Waist → helm at the raised stern, on foot only (closed-loop walk, no interaction).
        private IEnumerator Stairs()
        {
            var helm = SimWorld.All<Interactable>().OfType<HelmStation>().FirstOrDefault(h => h.Ship() == _ship);
            if (helm == null) { Fail("walk to helm: no helm"); yield break; }
            if (_ship.TryGetCrewSpawn(0, out Vector3 sp, out Quaternion sr))
                yield return PlaceAt(sp + Vector3.up * 0.1f, sr);
            float y0 = ShipLocal(_player.transform.position).y;
            float t = 0f;
            while (t < 10f)
            {
                Vector3 to = ShipLocal(helm.StandPosition) - ShipLocal(_player.transform.position);
                to.y = 0f;
                if (to.magnitude < 0.6f) break;
                _input = default;
                _input.LookSpace = _ship.Object.Id;
                _input.LookYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                _input.Move = new Vector2(0f, Mathf.Clamp01(to.magnitude));
                t += Time.deltaTime;
                yield return null;
            }
            _input = default;
            yield return Wait(0.5f);
            float dist = Vector3.Distance(ShipLocal(_player.transform.position), ShipLocal(helm.StandPosition));
            float y1 = ShipLocal(_player.transform.position).y;
            Log($"walk to helm: {t:F1}s, ship-local y {y0:F2} → {y1:F2}, {dist:F2}m from the helm stand, state={_player.ActiveStates}");
            Check(dist < 1.0f && Has(PlayerState.Grounded) && !Has(PlayerState.Climbing),
                $"walk to helm: reached the wheel on foot ({dist:F2}m)");
        }

        // Shroud ratlines up to the top.
        private IEnumerator Shrouds()
        {
            ClimbSurface rail = SimWorld.All<Interactable>().OfType<ClimbSurface>()
                .FirstOrDefault(r => r.name.StartsWith("ShroudClimb") && r.GetComponentInParent<NetworkShip>() == _ship);
            if (rail == null) { Fail("shrouds: none"); yield break; }
            yield return PlaceAt(rail.BodyPoint(0f, 0f) + rail.OutwardWorld * 0.3f + Vector3.up * 0.1f, rail.BodyRotation);
            yield return Press(rail.Ref);
            Check(Has(PlayerState.Climbing), "shrouds: Interact → climbing the ratlines");
            float y0 = ShipLocal(_player.transform.position).y;
            _input = default;
            _input.Move = new Vector2(0f, 1f);
            for (float t = 0; t < 12f && Has(PlayerState.Climbing); t += Time.deltaTime)
                yield return null;
            _input = default;
            yield return Wait(1f);
            float y1 = ShipLocal(_player.transform.position).y;
            float toExit = rail.TopExit != null ? Vector3.Distance(_player.transform.position, rail.TopExit.position) : -1f;
            Log($"shrouds {rail.name}: y {y0:F2} → {y1:F2}, {toExit:F2}m from the top exit, state={_player.ActiveStates}");
            Check(y1 - y0 > 4f && Has(PlayerState.Grounded) && toExit < 1f, "shrouds: climbed onto the top");
        }

        private IEnumerator LookAtProbe()
        {
            ClimbSurface rail = Rails().FirstOrDefault();
            if (rail == null) { Fail("probe: no climb rails on ship"); yield break; }
            yield return PlaceAt(rail.BodyPoint(0f, 0f) + rail.OutwardWorld * 0.6f + Vector3.up * 0.1f, rail.BodyRotation);
            Vector3 eye = _player.CameraAnchor.position;
            Vector3 toTrunk = rail.transform.position + Vector3.up * (eye.y - rail.transform.position.y) - eye;
            bool hit = _player.ProbeLocalTarget(eye, toTrunk.normalized, out Interactable target, out RaycastHit h);
            Log($"probe: from eye, hit={hit} target={(target != null ? target.name : "none")} dist={h.distance:F2}");
            Check(hit && target == rail, "probe: view ray at the mast resolves to its climb rail");
        }

        private IEnumerator ClimbMast()
        {
            ClimbSurface rail = Rails().FirstOrDefault(r => r.TopExit != null) ?? Rails().FirstOrDefault();
            if (rail == null) yield break;

            yield return PlaceAt(rail.BodyPoint(0f, 0f) + rail.OutwardWorld * 0.4f + Vector3.up * 0.1f, rail.BodyRotation);
            yield return Press(rail.Ref);
            Check(Has(PlayerState.Climbing), "climb: Interact on rail → Climbing");
            if (!Has(PlayerState.Climbing)) yield break;

            float startY = ShipLocal(_player.transform.position).y;
            _input = default;
            _input.Move = new Vector2(0f, 1f);
            _renderSteps.Clear();
            _hasLast = false;
            _sampleRender = true;
            float t = 0f;
            float maxErr = 0f;
            while (Has(PlayerState.Climbing) && t < 25f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            _input = default;
            yield return Wait(0.5f);
            _sampleRender = false;
            float climbMaxStep = _renderSteps.Count > 0 ? _renderSteps.Max() : 0f;
            Log($"climb: max rendered eye step {climbMaxStep * 1000:F0}mm/frame (incl. mantle onto the platform)");
            Check(climbMaxStep < 0.15f, $"climb: no pop when stepping onto the platform ({climbMaxStep * 1000:F0}mm < 150mm/frame)");
            float endY = ShipLocal(_player.transform.position).y;
            Log($"climb: {startY:F2} → {endY:F2} (ship-local y) in {t:F1}s, exit={(rail.TopExit != null)} state={_player.ActiveStates} maxErr={maxErr:F2}");
            Check(endY - startY > 5f, $"climb: gained {endY - startY:F1}m > 5m");
            if (rail.TopExit != null)
            {
                Check(Has(PlayerState.Grounded), "climb: stepped off onto the platform (Grounded)");
                yield return Wait(2f);
                float platformDrift = Vector3.Distance(ShipLocal(_player.transform.position), ShipLocal(rail.TopExit.position));
                Log($"climb: on platform, distance to exit point {platformDrift:F2}m");
                Check(platformDrift < 1.0f, $"climb: stays on the platform ({platformDrift:F2} < 1m)");
            }
        }

        private IEnumerator Helm()
        {
            var station = SimWorld.All<Interactable>().OfType<HelmStation>().FirstOrDefault(s => s.Ship() == _ship);
            if (station == null) { Fail("helm: no HelmStation"); yield break; }
            yield return PlaceAt(station.StandPosition + Vector3.up * 0.1f, station.StandRotation);
            yield return Press(station.Ref);
            Check(Has(PlayerState.Steering), "helm: Interact → Steering");
            float before = station.Helm.WheelAngle;
            Check(Mathf.Abs(before) < station.Helm.MaxWheelDegrees * 0.6f,
                $"helm: an unmanned wheel doesn't wind itself onto its stop at rest ({before:F0}°)");
            _input = default;
            _input.Move = new Vector2(1f, 0f);
            yield return Wait(1f);
            float after = station.Helm.WheelAngle;
            float standErr = Vector3.Distance(_player.transform.position, station.StandPosition);
            Log($"helm: wheel {before:F0}° → {after:F0}°, occupant={station.Helm.Occupant}, stand error {standErr:F2}m");
            Check(Mathf.Abs(after - before) > 30f, "helm: A/D turns the wheel");
            Check(standErr < 0.3f, $"helm: glued to the stand ({standErr:F2} < 0.3m)");
            _input = default;
            yield return Wait(0.5f);
            float held0 = station.Helm.WheelAngle;
            yield return Wait(1f);
            float held1 = station.Helm.WheelAngle;
            Log($"helm: hands on, no input: wheel {held0:F0}° → {held1:F0}° (spin {station.Helm.WheelVelocity:F0}°/s, load {station.Helm.Load:F2})");
            Check(Mathf.Abs(held1 - held0) < 90f, "helm: the helmsman holds the wheel when not steering");
            _input = default;
            yield return Press(default);
            Check(!Has(PlayerState.Steering) && !station.Helm.IsOccupied, "helm: Interact again releases the wheel");
        }

        // Full running-rigging cycle on the foremast halyard.
        private IEnumerator Rope()
        {
            RigLine line = null;
            for (int l = 0; l < NetworkShip.MaxLines && line == null; l++)
                if (_ship.Line(l) is { Kind: RigLineKind.Halyard } h && h.HomePin != null) line = h;
            if (line == null) { Fail("rope: no halyard with a home pin"); yield break; }
            int i = line.LineIndex;
            BelayPin home = line.HomePin;
            BelayPin spare = home.transform.parent.GetComponentsInChildren<BelayPin>()
                .FirstOrDefault(p => p != home && _ship.LineTiedTo(p) < 0);
            Vector3 railOut = Vector3.ProjectOnPlane(home.transform.position - line.Block.position, Vector3.up).normalized;

            Check(_ship.GetLineMode(i) == RigLineMode.Tied && _ship.GetLinePin(i) == home, $"rope: '{line.DisplayName}' starts made fast on its pin");
            float hoist0 = _ship.Sails.GetHoist(line.SailIndex);

            // 1) Cast off the pin into our hands.
            yield return PlaceAt(home.transform.position + railOut * 0.75f + Vector3.down * 0.85f + Vector3.up * 0.1f,
                Quaternion.LookRotation(-railOut, Vector3.up));
            yield return Press(home.Ref);
            Check(Has(PlayerState.HoldingRope) && _ship.GetLineMode(i) == RigLineMode.Held, "rope: E on the pin casts it off into your hands");
            Check(Has(PlayerState.Grounded), "rope: holding a line you still stand / walk normally");

            // 2) Haul hand over hand (LMB).
            float out0 = _ship.GetLineOut(i);
            _input = default;
            _input.Buttons.Set(NetworkInputData.ButtonHaul, true);
            yield return Wait(1.5f);
            _input = default;
            float out1 = _ship.GetLineOut(i);
            float hoist1 = _ship.Sails.GetHoist(line.SailIndex);
            Log($"rope: haul LMB out {out0:F2} → {out1:F2}m, hoist {hoist0:F2} → {hoist1:F2}");
            Check(out1 > out0 + 1f && hoist1 > hoist0 + 0.15f, "rope: hauling brings rope down and hoists the sail");

            // 3) Walk away from the mast with the rope taut: hauls more, then leashes.
            Vector3 startPos = _player.transform.position;
            _input = default;
            _input.LookSpace = default;
            _input.LookYaw = Quaternion.LookRotation(railOut).eulerAngles.y;
            _input.Move = new Vector2(0f, 1f);
            yield return Wait(2.5f);
            _input = default;
            yield return Wait(0.3f);
            float out2 = _ship.GetLineOut(i);
            float distBlock = Vector3.Distance(RumOverboard.Gameplay.Ocean.Features.Rigging.RopeSystem.HandPosition(_player.transform.position, _player.ViewYaw), line.Block.position);
            Log($"rope: walked away {Vector3.Distance(startPos, _player.transform.position):F2}m, out {out1:F2} → {out2:F2}, hand-to-block {distBlock:F2}");
            Check(out2 >= out1 - 0.01f, "rope: walking away with the line hauls (never loses rope)");
            Check(distBlock <= out2 + 0.35f, $"rope: taut rope leashes the crew ({distBlock:F2} ≤ {out2:F2}+0.35)");

            // 4) Make fast on a spare pin.
            if (spare != null)
            {
                yield return PlaceAt(spare.transform.position + railOut * 0.75f + Vector3.down * 0.85f + Vector3.up * 0.1f,
                    Quaternion.LookRotation(-railOut, Vector3.up));
                yield return Press(spare.Ref);
                float hoistTied = _ship.Sails.GetHoist(line.SailIndex);
                Check(_ship.GetLineMode(i) == RigLineMode.Tied && _ship.GetLinePin(i) == spare && !Has(PlayerState.HoldingRope),
                    "rope: E on another pin ties the line there");
                yield return Wait(1.5f);
                Check(Mathf.Abs(_ship.Sails.GetHoist(line.SailIndex) - hoistTied) < 0.02f, "rope: tied line holds the sail");
                yield return Press(spare.Ref); // cast off again
                Check(_ship.GetLineMode(i) == RigLineMode.Held, "rope: and casts off from it again");
            }

            // 5) Drop it (G): the load runs the line out, the sail falls, the end flies up to its stopper.
            float outBeforeDrop = _ship.GetLineOut(i);
            _input = default;
            _input.Buttons.Set(NetworkInputData.ButtonDrop, true);
            yield return Wait(0.1f);
            _input = default;
            yield return Wait(3f);
            Vector3 endWorld = _ship.transform.TransformPoint(_ship.GetLineEndLocal(i));
            Log($"rope: dropped → mode {_ship.GetLineMode(i)}, out {outBeforeDrop:F2} → {_ship.GetLineOut(i):F2} (min {line.OutMin:F2}), " +
                $"hoist {_ship.Sails.GetHoist(line.SailIndex):F2}, end {Vector3.Distance(endWorld, line.Block.position):F2}m from block");
            Check(_ship.GetLineMode(i) == RigLineMode.Loose && !Has(PlayerState.HoldingRope), "rope: G drops the line (loose)");
            Check(_ship.GetLineOut(i) < outBeforeDrop - 1f && _ship.Sails.GetHoist(line.SailIndex) < 0.2f, "rope: a loose halyard runs out and the sail comes down");
            Check(Vector3.Distance(endWorld, line.Block.position) <= _ship.GetLineOut(i) + 0.1f, "rope: the free end stays on its rope (≤ length from the block)");

            // 6) Catch the free end and make it fast back on its home pin.
            yield return PlaceAt(home.transform.position + railOut * 0.75f + Vector3.down * 0.85f + Vector3.up * 0.1f,
                Quaternion.LookRotation(-railOut, Vector3.up));
            yield return Press(line.End.Ref);
            Check(_ship.GetLineMode(i) == RigLineMode.Held, "rope: E on the loose end catches it");
            yield return Press(home.Ref);
            Check(_ship.GetLineMode(i) == RigLineMode.Tied && _ship.GetLinePin(i) == home, "rope: made fast on its home pin again");
        }

        // ---------------------------------------------------------------------------------------
        private IEnumerable<ClimbSurface> Rails() =>
            SimWorld.All<Interactable>().OfType<ClimbSurface>().Where(r => r.GetComponentInParent<NetworkShip>() == _ship)
                .OrderBy(r => r.name);

        private IEnumerator PlaceAt(Vector3 position, Quaternion rotation)
        {
            _input = default;
            var rb = _player.GetComponent<Rigidbody>();
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = _ship.Body.GetPointVelocity(position);
            _player.transform.SetPositionAndRotation(position, rotation);
            yield return Wait(0.6f);
        }

        // Hold Interact for a few ticks with the target set, then release.
        private IEnumerator Press(InteractableRef target)
        {
            _input = default;
            _input.Target = target;
            _input.Buttons.Set(NetworkInputData.ButtonInteract, true);
            yield return Wait(0.15f);
            _input.Buttons.Set(NetworkInputData.ButtonInteract, false);
            yield return Wait(0.25f);
            _input = default;
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
                yield return null;
        }

        private void LateUpdate()
        {
            if (!_sampleRender || _player == null || _ship == null)
                return;
            Vector3 eyeLocal = _ship.transform.InverseTransformPoint(_player.RenderEyePosition);
            if (_hasLast)
                _renderSteps.Add(Vector3.Distance(eyeLocal, _lastEyeLocal));
            _lastEyeLocal = eyeLocal;
            _hasLast = true;
        }

        private bool Has(PlayerState s) => (_player.ActiveStates & s) != 0;
        private Vector3 ShipLocal(Vector3 world) => _ship.transform.InverseTransformPoint(world);

        private void Check(bool ok, string what)
        {
            Log((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) _failures.Add(what);
        }

        private void Fail(string what) => Check(false, what);
        private void Log(string line) => _report.AppendLine(line);

        private void Finish()
        {
            _report.AppendLine(_failures.Count == 0 ? "RESULT: ALL PASSED" : $"RESULT: {_failures.Count} FAILED");
            Debug.Log("[SandboxSmoke]\n" + _report);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(_failures.Count == 0 ? 0 : 1);
#endif
        }
    }

    internal static class SmokeExtensions
    {
        public static NetworkShip Ship(this HelmStation s) => s.GetComponentInParent<NetworkShip>();
    }
}
#endif
