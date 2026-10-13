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
using RumOverboard.Gameplay.Ocean.Features.SailFurl;
using RumOverboard.Gameplay.Ocean.Features.ShipStatus;
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
            if (Environment.GetCommandLineArgs().Contains("-sandboxBaselineScale"))
            {
                // Compare the previous hull size without rewriting the gameplay prefab.
                const float scale = 1.5f;
                _ship.transform.localScale /= scale;
                _ship.Body.mass /= scale * scale;
                _ship.Body.centerOfMass /= scale;
                _ship.Body.linearDamping = 0.05f;
#if UNITY_EDITOR
                var so = new UnityEditor.SerializedObject(_ship.GetComponent<RumOverboard.Gameplay.Ocean.ShipBuoyancyController>());
                so.FindProperty("windHullForceCoefficient").floatValue /= scale * scale;
                so.FindProperty("maxWindForce").floatValue /= scale * scale;
                so.FindProperty("lowSpeedDragRate").floatValue = 0f;
                var points = so.FindProperty("points");
                for (int i = 0; i < points.arraySize; i++)
                {
                    var p = points.GetArrayElementAtIndex(i);
                    p.FindPropertyRelative("radius").floatValue /= scale;
                    p.FindPropertyRelative("maxSubmersionDepth").floatValue /= scale;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
#endif
            }
            Log($"spawned: player at {_player.transform.position}, ship at {_ship.transform.position}");
            yield return Wait(0.3f);
            {
                int sails = _ship.Sails != null ? _ship.Sails.SailCount : 0;
                bool allFurled = sails > 0;
                for (int s = 0; s < sails; s++)
                    allFurled &= _ship.GetSailFurl(s) >= 0.999f && _ship.GetSailFurlGoal(s);
                Check(allFurled == SailFurlConfig.Active.StartFurled, $"furl: voyage starts with all {sails} sails furled = {SailFurlConfig.Active.StartFurled}");
            }
            yield return Shots("start");

            yield return Run(FurledDrift());
            if (Environment.GetCommandLineArgs().Contains("-sandboxDriftOnly"))
            {
                yield return Run(PartialRagdoll());
                Finish(); yield break;
            }
            yield return Run(ShipStability());
            yield return Run(IdleOnDeck());
            yield return Run(Walk());
            yield return Run(Stairs());
            yield return Run(Shrouds());
            yield return Run(LookAtProbe());
            yield return Run(ClimbMast());
            yield return Run(YardFurl());
            yield return Run(YardFromRatlines());
            yield return Run(StatusSnapshot());
            yield return Run(Helm());
            yield return Run(Rope());
            yield return Run(Braces());
            yield return Run(PartialRagdoll());
            yield return Run(IdleWhileSailing());
            yield return Run(SteerUnderSail());
            yield return Run(Beat());
            yield return Shots("sailing");
            Finish();
        }

        // Sails furled, nobody at the wheel: the sea current + windage must not carry her off like a raft.
        private IEnumerator FurledDrift()
        {
            var wave = FindAnyObjectByType<RumOverboard.Gameplay.Ocean.OceanWaveField>();
            var buoy = _ship.GetComponent<RumOverboard.Gameplay.Ocean.ShipBuoyancyController>();
            Vector3 start = _ship.transform.position;
            Vector3 current = Vector3.zero, wind = Vector3.zero;
            int n = 0;
            float t0 = Time.time;
            while (Time.time - t0 < 16f)
            {
                if (wave != null)
                {
                    current += wave.Sample(_ship.transform.position, wave.OceanTimeNow).currentVelocity;
                    n++;
                }
                if (buoy != null)
                    wind += buoy.LastWindForce;
                yield return null;
            }
            float secs = Time.time - t0;
            Vector3 moved = Vector3.ProjectOnPlane(_ship.transform.position - start, Vector3.up);
            current = n > 0 ? Vector3.ProjectOnPlane(current / n, Vector3.up) : Vector3.zero;
            wind = n > 0 ? Vector3.ProjectOnPlane(wind / n, Vector3.up) : Vector3.zero;
            float drift = moved.magnitude / Mathf.Max(0.1f, secs);
            Vector3 vEnd = Vector3.ProjectOnPlane(_ship.Body.linearVelocity, Vector3.up);
            Log($"drift: furled, {secs:F1}s — ship drifts {drift:F2} m/s (now {vEnd.magnitude:F2}), sea current {current.magnitude:F2} m/s " +
                $"({Vector3.Angle(moved, current):F0}° off the drift), hull windage {wind.magnitude:F0} N ({Vector3.Angle(moved, wind):F0}° off the drift)");
            Check(current.magnitude < 0.45f, $"drift: sea current is gentle ({current.magnitude:F2} m/s < 0.45)");
            Check(drift < 0.5f, $"drift: furled ship isn't swept away ({drift:F2} m/s < 0.5)");
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
            int groundedFrames = 0, frames = 0;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                frames++;
                if (Has(PlayerState.Grounded)) groundedFrames++;
                yield return null;
            }
            _input = default;
            float moved = Vector3.Distance(start, ShipLocal(_player.transform.position));
            Log($"ragdoll: walked {moved:F2}m at ragdoll {_player.RagdollControl:F2}, grounded={Has(PlayerState.Grounded)}");
            Check(moved > 1.5f && !_player.IsKnockedOut && groundedFrames > frames * 0.7f,
                $"ragdoll: walking with a partial ragdoll, grounded {groundedFrames}/{frames} frames");

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
            Debug.Log($"[SandboxSmoke] Running {step.GetType().Name}");
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
            // Cast every sail loose and hoist it (as if the crew hauled all halyards); let her pick up speed.
            for (int s = 0; s < _ship.Sails.SailCount; s++)
                _ship.SetSailFurl(s, 0f);
            for (int l = 0; l < NetworkShip.MaxLines; l++)
                if (_ship.Line(l) is { Kind: RigLineKind.Halyard } halyard)
                    _ship.SetLineOut(l, halyard.OutFromValue(1f));
            yield return Wait(6f);
            {
                float minHalyard = 1f;
                for (int l = 0; l < NetworkShip.MaxLines; l++)
                    if (_ship.Line(l) is { Kind: RigLineKind.Halyard })
                        minHalyard = Mathf.Min(minHalyard, RopeRenderSystem.Tension(_ship, l));
                Log($"sailing: hoisted halyards draw with tension ≥ {minHalyard:F2}");
                Check(minHalyard > 0.6f, "rope visuals: hoisted halyards under load are drawn taut");
            }

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

        // Under way with sails set: holding D / A at the wheel must actually turn the ship.
        private IEnumerator SteerUnderSail()
        {
            var station = SimWorld.All<Interactable>().OfType<HelmStation>().FirstOrDefault(s => s.Ship() == _ship);
            if (station == null) { Fail("steer: no HelmStation"); yield break; }
            yield return PlaceAt(station.StandPosition + Vector3.up * 0.1f, station.StandRotation);
            yield return Press(station.Ref);
            Check(Has(PlayerState.Steering), "steer: took the wheel under way");
            if (!Has(PlayerState.Steering)) yield break;

            float Heading() { Vector3 f = _ship.transform.forward; return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; }
            Log($"steer: inertia {_ship.Body.inertiaTensor} mass {_ship.Body.mass:F0} angDamp {_ship.Body.angularDamping:F2}");

            // Start clean: wheel centred (the helm test left it over) and the ship making way.
            station.Helm.WheelAngle = 0f;
            station.Helm.WheelVelocity = 0f;
            station.Helm.RudderAngle = 0f;
            float SpeedAhead() => Vector3.Dot(_ship.Body.linearVelocity, _ship.transform.forward);
            for (float w = 0f; w < 12f && (SpeedAhead() < 3f || Mathf.Abs(_ship.Body.angularVelocity.y) > 0.05f); w += Time.deltaTime)
                yield return null;
            Log($"steer: start {SpeedAhead():F2} m/s ahead, yawRate {_ship.Body.angularVelocity.y * Mathf.Rad2Deg:F1}°/s");

            // First bear away from the wind (turning into it rounds her up and kills her way — no
            // steerage), then come back: a longer pass, the wheel has to travel from stop to stop.
            var wind = new ShipStatusSnapshot();
            wind.Capture(_ship, _ship.Body.linearVelocity, _ship.Runner.SimulationTime);
            float away = wind.TrueWindFrom >= 0f ? -1f : 1f;
            Log($"steer: wind from {wind.TrueWindFrom:F0}° → bear away to {(away > 0 ? "starboard (D)" : "port (A)")} first");

            float[] turned = new float[2];
            for (int pass = 0; pass < 2; pass++)
            {
                float dir = pass == 0 ? away : -away;
                float duration = pass == 0 ? 7f : 12f;
                float h0 = Heading();
                float prev = h0, total = 0f;
                _input = default;
                _input.Move = new Vector2(dir, 0f);
                for (float t = 0f, next = 0f; t < duration; t += Time.deltaTime)
                {
                    float h = Heading();
                    total += Mathf.DeltaAngle(prev, h);
                    prev = h;
                    if (t >= next)
                    {
                        Vector3 v = _ship.Body.linearVelocity;
                        Vector3 com = _ship.Body.worldCenterOfMass;
                        float sailYaw = 0f;
                        var sailsAgg = _ship.Sails;
                        for (int si = 0; sailsAgg != null && sailsAgg.TryGetFlatSail(si, out var m, out int li); si++)
                            sailYaw += Vector3.Cross(m.Sails[li].lastForcePoint - com, m.Sails[li].lastForce).y;
                        var buoy = _ship.GetComponent<RumOverboard.Gameplay.Ocean.ShipBuoyancyController>();
                        float hullYaw = buoy != null ? buoy.LastResultantTorque.y : 0f;
                        Log($"steer {(dir > 0 ? "D" : "A")} t={t:F1}s wheel {station.Helm.WheelAngle:F0}° rudder {station.Helm.RudderAngle:F1}° " +
                            $"fwd {Vector3.Dot(v, _ship.transform.forward):F2}m/s yawRate {_ship.Body.angularVelocity.y * Mathf.Rad2Deg:F2}°/s turned {total:F1}° " +
                            $"| yaw N·m: sails {sailYaw:F0}, hull {hullYaw:F0}");
                        next += 2f;
                    }
                    // Bearing away: stop at ~35° — swinging on until the wind is abeam of square
                    // yards stalls her, and with no way on the rudder can't bring her back.
                    if (pass == 0 && total * dir > 35f)
                        break;
                    yield return null;
                }
                turned[pass] = total;
                _input = default;
                yield return Wait(0.2f);
            }
            Check(turned[0] * away > 25f, $"steer: holding {(away > 0 ? "D" : "A")} bears her away ({turned[0]:F1}° in ≤7s)");
            Check(turned[1] * -away > 15f, $"steer: holding {(away > 0 ? "A" : "D")} brings her back ({turned[1]:F1}° in 12s)");
            yield return Press(default);
        }

        // Zig-zag upwind: yards braced sharp, the helmsman holds her ~55° off the true wind. On each
        // tack she must make ground to windward, and she must come about from one tack to the other.
        private IEnumerator Beat()
        {
            var station = SimWorld.All<Interactable>().OfType<HelmStation>().FirstOrDefault(s => s.Ship() == _ship);
            if (station == null) { Fail("beat: no HelmStation"); yield break; }
            if (_ship.Sails == null || _ship.Sails.WindSystem == null) { Fail("beat: no wind"); yield break; }
            yield return PlaceAt(station.StandPosition + Vector3.up * 0.1f, station.StandRotation);
            yield return Press(station.Ref);
            Check(Has(PlayerState.Steering), "beat: took the wheel");
            if (!Has(PlayerState.Steering)) yield break;

            const float offWind = 55f;
            var snap = new ShipStatusSnapshot();
            float WindFrom()
            {
                snap.Capture(_ship, _ship.Body.linearVelocity, _ship.Runner.SimulationTime);
                return snap.TrueWindFrom;
            }
            Vector3 Upwind()
            {
                Vector3 w = _ship.Sails.WindSystem.EvaluateWind(_ship.transform.position, _ship.Runner.SimulationTime);
                w.y = 0f;
                return -w.normalized;
            }
            void BraceSharp(float windFrom)
            {
                // Wind on the starboard bow → yards round to port (−), and vice versa; both braces made fast.
                for (int l = 0; l < NetworkShip.MaxLines; l++)
                {
                    RigLine line = _ship.Line(l);
                    if (line == null || (line.Kind != RigLineKind.BracePort && line.Kind != RigLineKind.BraceStarboard))
                        continue;
                    float max = Mathf.Max(1f, line.BraceMaxAngle);
                    float yard = windFrom >= 0f ? -max : max;
                    float v = line.Kind == RigLineKind.BracePort ? (max - yard) / (2f * max) : (yard + max) / (2f * max);
                    _ship.SetLineOut(l, line.OutFromValue(v));
                }
            }

            float side = WindFrom() >= 0f ? 1f : -1f; // + = wind on the starboard bow
            float[] vmg = new float[2];
            float[] ahead = new float[2];
            bool cameAbout = false;
            for (int leg = 0; leg < 2; leg++, side = -side)
            {
                float settle = leg == 0 ? 25f : 45f;
                const float measure = 25f;
                Vector3 start = _ship.transform.position;
                Vector3 upwind = Upwind();
                float speedSum = 0f, fromSum = 0f;
                int samples = 0;
                bool tacking = leg == 1;
                for (float t = 0f, next = 0f; t < settle + measure; t += Time.deltaTime)
                {
                    float from = WindFrom();
                    BraceSharp(from);

                    // Autopilot: hold the wind at side·offWind off the bow (D turns her to starboard).
                    // Coming about: helm hard over until she's well onto the new tack, so she carries her way through.
                    float err = Mathf.DeltaAngle(side * offWind, from);
                    if (tacking && from * side >= offWind - 10f)
                    {
                        tacking = false;
                        cameAbout = true;
                        Log($"beat: came about in {t:F1}s, {Vector3.Dot(_ship.Body.linearVelocity, _ship.transform.forward):F2} m/s ahead");
                    }
                    float yawRate = _ship.Body.angularVelocity.y * Mathf.Rad2Deg;
                    float want = tacking ? Mathf.Sign(err) : Mathf.Clamp(err * 0.03f - yawRate * 0.3f, -0.8f, 0.8f);
                    _input = default;
                    _input.Move = new Vector2(tacking ? want : Mathf.Clamp((want - station.Helm.WheelNormalized) * 4f, -1f, 1f), 0f);

                    if (t < settle)
                        start = _ship.transform.position;
                    else
                    {
                        speedSum += Vector3.Dot(_ship.Body.linearVelocity, _ship.transform.forward);
                        fromSum += from;
                        samples++;
                    }
                    if (t >= next)
                    {
                        Vector3 v = Vector3.ProjectOnPlane(_ship.Body.linearVelocity, Vector3.up);
                        float leeway = v.sqrMagnitude > 0.01f ? Vector3.SignedAngle(Vector3.ProjectOnPlane(_ship.transform.forward, Vector3.up), v, Vector3.up) : 0f;
                        Vector3 sailF = Vector3.zero;
                        var sailsAgg = _ship.Sails;
                        for (int si = 0; sailsAgg.TryGetFlatSail(si, out var m, out int li); si++)
                            sailF += m.Sails[li].lastForce;
                        var buoy = _ship.GetComponent<RumOverboard.Gameplay.Ocean.ShipBuoyancyController>();
                        Vector3 windF = buoy != null ? buoy.LastWindForce : Vector3.zero;
                        Vector3 keelF = buoy != null ? buoy.LastKeelForce : Vector3.zero;
                        Vector3 waterF = buoy != null ? buoy.LastResultantForce - windF - keelF : Vector3.zero;
                        Vector3 bodyDamp = -v * (_ship.Body.mass * _ship.Body.linearDamping);
                        Vector3 L(Vector3 w) => _ship.transform.InverseTransformDirection(w);
                        Log($"beat leg {leg} t={t:F0}s wind from {from:F0}° fwd {Vector3.Dot(v, _ship.transform.forward):F2}m/s " +
                            $"leeway {leeway:F0}° to windward {Vector3.Dot(v, upwind):F2}m/s wheel {station.Helm.WheelNormalized:F2} " +
                            $"yard0 {_ship.GetYardAngle(0):F0}° | N (side/ahead): sails {L(sailF).x:F0}/{L(sailF).z:F0} " +
                            $"windage {L(windF).x:F0}/{L(windF).z:F0} keel {L(keelF).x:F0} water {L(waterF).x:F0}/{L(waterF).z:F0} body damping {L(bodyDamp).x:F0}/{L(bodyDamp).z:F0}");
                        next += 5f;
                    }
                    yield return null;
                }
                vmg[leg] = Vector3.Dot(Vector3.ProjectOnPlane(_ship.transform.position - start, Vector3.up), upwind) / measure;
                ahead[leg] = samples > 0 ? speedSum / samples : 0f;
                Log($"beat leg {leg}: avg wind from {(samples > 0 ? fromSum / samples : 0f):F0}°, {ahead[leg]:F2} m/s ahead, made good to windward {vmg[leg]:F2} m/s");
            }
            _input = default;
            Check(vmg[0] > 0.2f, $"beat: close-hauled she makes ground to windward ({vmg[0]:F2} m/s, {ahead[0]:F2} m/s ahead)");
            Check(cameAbout, "beat: she comes about onto the other tack");
            Check(vmg[1] > 0.2f, $"beat: and to windward on the other tack too ({vmg[1]:F2} m/s, {ahead[1]:F2} m/s ahead)");
            yield return Press(default);
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

        // On the masthead platform (after ClimbMast): E on a yard casts the sail loose / furls it;
        // walking away pauses the work, coming back resumes it.
        private IEnumerator YardFurl()
        {
            YardFurlZone[] zones = _ship.GetComponentsInChildren<YardFurlZone>();
            Check(zones.Length == _ship.Sails.SailCount, $"furl: one yard zone per sail ({zones.Length}/{_ship.Sails.SailCount})");
            Vector3 eye = _player.CameraAnchor.position;
            YardFurlZone zone = zones.OrderBy(z => z.DistanceTo(eye, out _)).FirstOrDefault();
            if (zone == null) yield break;
            float reach = zone.DistanceTo(eye, out _);
            int s = zone.FlatIndex;
            Log($"furl: nearest yard from the platform '{zone.name}' (sail {s}) at {reach:F2}m (reach {zone.MaxDistance:F2})");
            Check(reach <= zone.MaxDistance, "furl: a yard is within reach from the masthead platform");
            Check(s >= 0, "furl: the zone resolves its sail");
            if (s < 0) yield break;

            PlayerRef me = _player.Object.InputAuthority;
            Vector3 standLocal = ShipLocal(_player.transform.position);
            Quaternion standRot = Quaternion.Inverse(_ship.transform.rotation) * _player.transform.rotation;
            SailFurlConfig cfg = SailFurlConfig.Active;

            yield return Press(zone.Ref);
            Check(!_ship.GetSailFurlGoal(s) && _ship.GetSailFurlWorker(s) == me, "furl: E on a furled yard starts casting the sail loose");
            yield return Wait(cfg.UnfurlTime + 1f);
            Log($"furl: after {cfg.UnfurlTime + 1f:F1}s furl {_ship.GetSailFurl(s):F3}, worker {_ship.GetSailFurlWorker(s)}");
            Check(_ship.GetSailFurl(s) < 0.01f && !_ship.IsSailFurlInProgress(s) && !_ship.GetSailFurlWorker(s).IsRealPlayer,
                "furl: sail cast loose in ~UnfurlTime, the crew member is released");

            yield return Press(zone.Ref);
            yield return Wait(1f);
            float f1 = _ship.GetSailFurl(s);
            Check(_ship.GetSailFurlGoal(s) && f1 > 0.02f && _ship.GetSailFurlWorker(s) == me, $"furl: E again starts furling ({f1:F2})");
            yield return Shots("furling");

            if (_ship.TryGetCrewSpawn(0, out Vector3 deck, out Quaternion deckRot))
                yield return PlaceAt(deck + Vector3.up * 0.1f, deckRot);
            float f2 = _ship.GetSailFurl(s);
            yield return Wait(1f);
            float f3 = _ship.GetSailFurl(s);
            Log($"furl: walked off at {f2:F3} → {f3:F3} a second later, worker {_ship.GetSailFurlWorker(s)}");
            Check(!_ship.GetSailFurlWorker(s).IsRealPlayer && Mathf.Abs(f3 - f2) < 0.005f && f3 < 0.999f,
                "furl: leaving the yard pauses the work half-done");

            yield return PlaceAt(_ship.transform.TransformPoint(standLocal) + _ship.transform.up * 0.1f, _ship.transform.rotation * standRot);
            yield return Press(zone.Ref);
            Check(_ship.GetSailFurlGoal(s) && _ship.GetSailFurlWorker(s) == me, "furl: back on the yard, E carries on furling");
            float t = 0f;
            while (_ship.GetSailFurl(s) < 0.999f && t < 30f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Log($"furl: finished in {t:F1}s more (furl {_ship.GetSailFurl(s):F3})");
            Check(_ship.GetSailFurl(s) >= 0.999f && !_ship.GetSailFurlWorker(s).IsRealPlayer, "furl: sail furled back on the yard");
        }

        // The course yards hang below the top: work them from the ratlines without letting go.
        private IEnumerator YardFromRatlines()
        {
            YardFurlZone[] zones = _ship.GetComponentsInChildren<YardFurlZone>();
            var shrouds = SimWorld.All<Interactable>().OfType<ClimbSurface>()
                .Where(r => r.name.StartsWith("ShroudClimb") && r.GetComponentInParent<NetworkShip>() == _ship)
                .OrderBy(r => r.name).ToList();
            if (zones.Length == 0 || shrouds.Count == 0) { Fail("furl (ratlines): no zones / shrouds"); yield break; }

            YardFurlZone zone = null;
            foreach (ClimbSurface rail in shrouds)
            {
                yield return PlaceAt(rail.BodyPoint(0f, 0f) + rail.OutwardWorld * 0.3f + Vector3.up * 0.1f, rail.BodyRotation);
                yield return Press(rail.Ref);
                if (!Has(PlayerState.Climbing)) continue;
                _input = default;
                _input.Move = new Vector2(0f, 1f);
                for (float t = 0; t < 12f && Has(PlayerState.Climbing) && zone == null; t += Time.deltaTime)
                {
                    Vector3 eye = _player.CameraAnchor.position;
                    zone = zones.FirstOrDefault(z => z.DistanceTo(eye, out _) <= z.MaxDistance - 0.4f);
                    yield return null;
                }
                _input = default;
                if (zone != null && Has(PlayerState.Climbing))
                    break;
                zone = null;
            }
            if (zone == null) { Fail("furl (ratlines): no yard came within reach while climbing the shrouds"); yield break; }
            yield return Wait(0.3f);

            int s = zone.FlatIndex;
            PlayerRef me = _player.Object.InputAuthority;
            Log($"furl (ratlines): '{zone.name}' at {zone.DistanceTo(_player.CameraAnchor.position, out _):F2}m while climbing");
            yield return Press(zone.Ref);
            Check(Has(PlayerState.Climbing), "furl (ratlines): E on the yard keeps you on the shrouds");
            Check(_ship.GetSailFurlWorker(s) == me && !_ship.GetSailFurlGoal(s), "furl (ratlines): and starts casting the sail loose");
            yield return Wait(0.8f);
            float f = _ship.GetSailFurl(s);
            Check(f < 0.99f && Has(PlayerState.Climbing), $"furl (ratlines): the work goes on while you hang there ({f:F2})");
            yield return Press(zone.Ref);
            Check(_ship.GetSailFurlGoal(s) && Has(PlayerState.Climbing), "furl (ratlines): E again changes your mind (furls back)");

            _ship.SetSailFurl(s, 1f);
            _input = default;
            _input.Buttons.Set(NetworkInputData.ButtonJump, true);
            yield return Wait(0.15f);
            _input = default;
            yield return Wait(1.5f);
            if (_ship.TryGetCrewSpawn(0, out Vector3 deck, out Quaternion deckRot))
                yield return PlaceAt(deck + Vector3.up * 0.1f, deckRot);
        }

        // Status widget data: point-of-sail / wind-side maths and a live capture of the ship.
        private IEnumerator StatusSnapshot()
        {
            Check(ShipStatusSnapshot.PointOfSailName(10f) == "в лоб" && ShipStatusSnapshot.PointOfSailName(-50f) == "бейдевинд" &&
                  ShipStatusSnapshot.PointOfSailName(90f) == "галфвинд" && ShipStatusSnapshot.PointOfSailName(130f) == "бакштаг" &&
                  ShipStatusSnapshot.PointOfSailName(175f) == "фордевинд", "status: points of sail by wind angle");
            Transform t = _ship.transform;
            float fromStar = ShipStatusSnapshot.FromAngle(t, -t.right * 6f);
            float fromAhead = ShipStatusSnapshot.FromAngle(t, -t.forward * 6f);
            float fromPortQuarter = ShipStatusSnapshot.FromAngle(t, (t.right + t.forward).normalized * 6f);
            Log($"status: wind from starboard {fromStar:F0}°, ahead {fromAhead:F0}°, port quarter {fromPortQuarter:F0}°");
            Check(Mathf.Abs(fromStar - 90f) < 1f && Mathf.Abs(fromAhead) < 1f && Mathf.Abs(fromPortQuarter + 135f) < 1f,
                "status: wind angle is measured from the bow, + to starboard");

            var snap = new ShipStatusSnapshot();
            snap.Capture(_ship, _ship.Body.linearVelocity, _ship.Runner.SimulationTime);
            int halyards = snap.Lines.Count(l => l.Kind == RigLineKind.Halyard);
            Log($"status: {snap.SpeedKnots:F1} kn, wind {snap.TrueWindSpeed:F1} m/s from {snap.TrueWindFrom:F0}° ({snap.PointOfSail}), " +
                $"rudder {snap.Rudder:F1}°, {snap.Sails.Count} sails, {snap.Lines.Count} lines");
            Check(snap.Sails.Count == _ship.Sails.SailCount && halyards == snap.Sails.Count && snap.HasHelm,
                "status: capture lists every sail, its halyard and the helm");
            Check(snap.Sails.All(x => !string.IsNullOrEmpty(x.Name) && x.State == "скручен"), "status: furled sails read 'скручен'");

            ShipStatusWidget widget = ShipStatusWidget.Instance;
            if (widget == null)
            {
                Log("status: no widget instance (no camera rig in this scene) — skipped");
                yield break;
            }
            yield return Wait(0.3f);
            Check(widget.Ship == _ship, "status: widget follows the ship you're aboard");
            Check(widget.IsShowing == ShipStatusConfig.Active.Visible, "status: widget shown per config");
            bool before = widget.UserVisible;
            widget.UserVisible = !before;
            yield return null;
            yield return null;
            Check(widget.IsShowing == (!before), "status: toggling hides / shows it");
            widget.UserVisible = before;

            Check(ShipStatusWidget.CompassName(0f) == "С" && ShipStatusWidget.CompassName(93f) == "В" &&
                  ShipStatusWidget.CompassName(-40f) == "СЗ" && ShipStatusWidget.CompassName(530f) == "Ю",
                "status: compass point names");

            {
                var sa = _ship.Sails;
                Vector3 n0 = ShipStatusSnapshot.RestNormal(sa, 0);
                float L = sa.LiftFactor, D = sa.DragFactor, S = sa.SideForceFactor;
                float run0 = ShipStatusSnapshot.TrimEfficiency(Vector3.forward, 0f, 60f, n0, L, D, S);
                float run55 = ShipStatusSnapshot.TrimEfficiency(Vector3.forward, 55f, 60f, n0, L, D, S);
                float head = ShipStatusSnapshot.TrimEfficiency(Vector3.back, 0f, 60f, n0, L, D, S);
                var quarter = new Vector3(-0.7f, 0f, 0.7f); // from the starboard quarter
                float qBest = ShipStatusSnapshot.BestYardAngle(quarter, 60f, n0, L, D, S);
                float qGood = ShipStatusSnapshot.TrimEfficiency(quarter, -45f, 60f, n0, L, D, S);
                float qBad = ShipStatusSnapshot.TrimEfficiency(quarter, 45f, 60f, n0, L, D, S);
                Log($"trim: run square {run0:P0}, run braced 55° {run55:P0}, head {head:P0}; quarter best yard {qBest:F0}°, -45° {qGood:P0}, +45° {qBad:P0}");
                Check(run0 > 0.95f && run55 < 0.9f, "trim: running free, square yards drive best");
                Check(head == 0f, "trim: head to wind nothing drives");
                Check(qBest < 0f && qGood > qBad + 0.2f, "trim: a quarter wind wants the yards braced round like the auto-brace (broadside)");

                // Close-hauled: apparent wind 45° on the starboard bow, yards braced sharp to port.
                var closeHauled = new Vector3(-Mathf.Sin(45f * Mathf.Deg2Rad), 0f, -Mathf.Cos(45f * Mathf.Deg2Rad));
                float chBest = ShipStatusSnapshot.BestYardAngle(closeHauled, 60f, n0, L, D, S);
                float chDrive = ShipStatusSnapshot.ForwardDrive(closeHauled, chBest, n0, L, D, S);
                float chWrong = ShipStatusSnapshot.ForwardDrive(closeHauled, -chBest, n0, L, D, S);
                Log($"trim: close-hauled (AW 45°) best yard {chBest:F0}°, drive {chDrive:F2} (other way {chWrong:F2}), running {ShipStatusSnapshot.ForwardDrive(Vector3.forward, 0f, n0, L, D, S):F2}");
                Check(chBest < -30f && chDrive > 0.15f && chWrong <= 0.001f,
                    "trim: close-hauled the sails drive forward with the yards braced sharp (lift, not drag)");
            }
            yield return null;
            yield return null;
            if (widget.IsShowing && ShipStatusConfig.Active.NorthUp)
            {
                float heading = widget.Snapshot.HeadingDeg;
                Log($"status: heading {heading:F0}°, hull icon bearing {widget.HullIconBearing:F0}°");
                Check(Mathf.Abs(Mathf.DeltaAngle(widget.HullIconBearing, heading)) < 3f,
                    "status: north-up dial — the hull icon points along the ship's heading");
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
        // Optional (-sandboxShots dir): PNGs of the rig for eyeballing (furled rolls, rope sag, widget).
        private string _shotsDir;

        private IEnumerator Shots(string tag)
        {
            if (_shotsDir == null)
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-sandboxShots");
                _shotsDir = at >= 0 && at + 1 < args.Length ? args[at + 1] : string.Empty;
                if (_shotsDir.Length > 0) System.IO.Directory.CreateDirectory(_shotsDir);
            }
            if (_shotsDir.Length == 0 || Camera.main == null)
                yield break;
            Transform s = _ship.transform;
            YardFurlZone zone = _ship.GetComponentsInChildren<YardFurlZone>().FirstOrDefault();
            Vector3 yard = zone != null ? zone.transform.position : s.TransformPoint(new Vector3(0f, 8f, 3f));
            yield return Shot($"{tag}_side", s.TransformPoint(new Vector3(22f, 9f, 0f)), s.TransformPoint(new Vector3(0f, 7f, 0f)), false);
            yield return Shot($"{tag}_yard", yard + s.right * 4.5f + s.forward * 3.5f + s.up * 1.2f, yard - s.up * 1.5f, false);
            yield return Shot($"{tag}_deck", s.TransformPoint(new Vector3(-3.5f, 3.2f, -7f)), s.TransformPoint(new Vector3(0f, 3f, 2f)), false);
            Transform cam = Camera.main.transform;
            yield return Shot($"{tag}_eye", cam.position, cam.position + cam.forward, true);
        }

        private IEnumerator Shot(string name, Vector3 eye, Vector3 target, bool withWidget)
        {
            yield return null;
            Camera cam = Camera.main;
            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            Transform t = cam.transform;
            t.GetPositionAndRotation(out Vector3 oldPos, out Quaternion oldRot);
            t.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, _ship.transform.up));
            Canvas widget = withWidget && ShipStatusWidget.Instance != null ? ShipStatusWidget.Instance.GetComponent<Canvas>() : null;
            RenderTexture oldTarget = cam.targetTexture;
            cam.targetTexture = rt;
            if (widget != null)
            {
                widget.renderMode = RenderMode.ScreenSpaceCamera;
                widget.worldCamera = cam;
                widget.planeDistance = cam.nearClipPlane + 0.05f;
                Canvas.ForceUpdateCanvases();
            }
            cam.Render();
            if (widget != null)
                widget.renderMode = RenderMode.ScreenSpaceOverlay;
            cam.targetTexture = oldTarget;
            t.SetPositionAndRotation(oldPos, oldRot);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(_shotsDir, $"sandbox_{name}.png"), tex.EncodeToPNG());
            Destroy(rt);
            Destroy(tex);
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
