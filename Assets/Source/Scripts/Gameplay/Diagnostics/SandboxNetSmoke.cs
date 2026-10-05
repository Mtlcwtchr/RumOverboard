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
    /// Two-process network scenario (real host + real client over Photon) for the sandbox:
    ///   host   : <c>-sandboxSmokeHost &lt;session&gt;</c>  — hosts, hoists every sail so the ship
    ///            sails and pitches, stays up for <c>-smokeDuration</c> seconds.
    ///   client : <c>-sandboxSmokeClient &lt;session&gt;</c> — joins, then measures what the CLIENT
    ///            renders: own eye relative to the rendered deck (jitter), ship motion smoothness,
    ///            walking by input, walking to the main mast and climbing it (replicated states).
    /// The client prints a report and exits 0/1.
    /// </summary>
    public sealed class SandboxNetSmoke : MonoBehaviour
    {
        private enum Role { Host, Client }

        private Role _role;
        private string _session;
        private float _duration = 150f;
        private readonly StringBuilder _report = new();
        private readonly List<string> _failures = new();

        private NetworkPlayer _player;
        private NetworkShip _ship;
        private NetworkInputData? _inject;

        private bool _sample;
        private bool _hasLast;
        private Vector3 _lastEye;
        private Vector3 _lastShipPos;
        private readonly List<float> _eyeSteps = new();
        private readonly List<float> _shipJerk = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            int host = Array.IndexOf(args, "-sandboxSmokeHost");
            int client = Array.IndexOf(args, "-sandboxSmokeClient");
            if (host < 0 && client < 0)
                return;

            var go = new GameObject("SandboxNetSmoke");
            DontDestroyOnLoad(go);
            var smoke = go.AddComponent<SandboxNetSmoke>();
            smoke._role = host >= 0 ? Role.Host : Role.Client;
            int idx = host >= 0 ? host : client;
            smoke._session = idx + 1 < args.Length ? args[idx + 1] : "rum-smoke";
            int dur = Array.IndexOf(args, "-smokeDuration");
            if (dur >= 0 && dur + 1 < args.Length && float.TryParse(args[dur + 1], out float d))
                smoke._duration = d;

            Application.runInBackground = true;
            Application.targetFrameRate = 60; // realistic frame pacing for the render measurements
            QualitySettings.vSyncCount = 0;
            smoke.TryConfigureManager();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => smoke.TryConfigureManager();
        }

        private bool _configured;

        // Runs on scene load (after Awake, before Start) so the override lands before auto-start.
        private void TryConfigureManager()
        {
            if (_configured) return;
            var cm = FindAnyObjectByType<ConnectionManager>();
            if (cm == null) return;
            cm.OverrideAutoStart(_role == Role.Host ? GameMode.Host : GameMode.Client, _session);
            _configured = true;
        }

        private IEnumerator Start()
        {
            while (!_configured)
                yield return null;
            Log($"== Net smoke ({_role}, session '{_session}') ==");
            ConnectionManager.InputInjector = d => _inject.HasValue ? Merge(d, _inject.Value) : d;

            float deadline = Time.realtimeSinceStartup + 60f;
            while ((_player == null || _ship == null) && Time.realtimeSinceStartup < deadline)
            {
                _player = NetworkPlayer.All.FirstOrDefault(p => p != null && p.HasInputAuthority);
                _ship = NetworkShip.All.FirstOrDefault();
                yield return null;
            }
            if (_ship == null || (_role == Role.Client && _player == null))
            {
                Fail($"spawn: player={_player != null} ship={_ship != null}");
                Finish();
                yield break;
            }

            if (_role == Role.Host)
                yield return HostLoop();
            else
                yield return ClientScenario();
            Finish();
        }

        // ---------------------------------------------------------------------------------------
        private IEnumerator HostLoop()
        {
            for (int l = 0; l < NetworkShip.MaxLines; l++)
                if (_ship.Line(l) is { Kind: RigLineKind.Halyard } halyard)
                    _ship.SetLineOut(l, halyard.OutFromValue(1f));
            Log("host: sails hoisted, waiting for the client");
            float end = Time.realtimeSinceStartup + _duration;
            int lastCount = 0;
            while (Time.realtimeSinceStartup < end)
            {
                int count = NetworkPlayer.All.Count;
                if (count != lastCount)
                {
                    Log($"host: crew={count} ship v={_ship.Body.linearVelocity.magnitude:F2}");
                    lastCount = count;
                }
                yield return null;
            }
        }

        private IEnumerator ClientScenario()
        {
            Check(!_player.HasStateAuthority, "client: local player is a pure proxy (host authority, no prediction)");
            var rb = _player.GetComponent<Rigidbody>();
            Check(rb.isKinematic, "client: local body is kinematic on the client");
            Log($"client: rtt={_player.Runner.GetPlayerRtt(_player.Runner.LocalPlayer) * 1000:F0}ms");

            // Wait for the ship to make way (host hoisted the sails).
            float t0 = Time.realtimeSinceStartup;
            while (ShipSpeed() < 1f && Time.realtimeSinceStartup - t0 < 25f)
                yield return null;
            yield return Wait(2f);
            Log($"client: ship speed ≈ {ShipSpeed():F2} m/s, platform={_player.Platform}");
            Check(_player.Platform == _ship.Object.Id, "client: aboard the ship (look space = ship)");

            // 1) Idle on the sailing ship — what the client renders.
            yield return Measure("idle", 6f);

            // 2) Walk by input (host-confirmed).
            Vector3 a = ShipLocal(_player.transform.position);
            _inject = new NetworkInputData { Move = new Vector2(0f, 1f), LookSpace = _ship.Object.Id, LookYaw = 180f };
            yield return Measure("walking", 1.5f);
            _inject = null;
            yield return Wait(1f);
            Vector3 b = ShipLocal(_player.transform.position);
            float moved = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            Log($"walk: moved {moved:F2}m ship-local");
            Check(moved > 1.5f, $"walk: client input moved the crew member {moved:F2} > 1.5m");

            // 3) Walk to the main mast and climb it.
            ClimbSurface rail = SimWorld.All<Interactable>().OfType<ClimbSurface>()
                .Where(r => r.TopExit != null && !r.name.Contains("DeckLadder"))
                .OrderBy(r => Vector3.Distance(r.BodyPoint(0, 0), _player.transform.position)).FirstOrDefault();
            if (rail == null) { Fail("climb: no rail"); yield break; }

            Vector3 goal = rail.BodyPoint(0f, 0f) + rail.OutwardWorld * 0.3f;
            float walkEnd = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < walkEnd)
            {
                goal = rail.BodyPoint(0f, 0f) + rail.OutwardWorld * 0.3f; // the ship is moving: track it
                Vector3 to = ShipLocal(goal) - ShipLocal(_player.transform.position);
                to.y = 0f;
                if (to.magnitude < 0.5f) break;
                float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                _inject = new NetworkInputData { Move = new Vector2(0f, Mathf.Clamp01(to.magnitude)), LookSpace = _ship.Object.Id, LookYaw = yaw };
                yield return null;
            }
            _inject = null;
            yield return Wait(0.8f);
            goal = rail.BodyPoint(0f, 0f) + rail.OutwardWorld * 0.3f;
            float distToRail = Vector3.Distance(_player.transform.position, goal);
            Log($"climb: walked to the mast, {distToRail:F2}m from the rail foot (rail {rail.name}, goal local {ShipLocal(goal)}, me local {ShipLocal(_player.transform.position)}, state {_player.ActiveStates})");

            // Interact on the rail (look straight at it).
            Vector3 dir = ShipLocal(rail.transform.position) - ShipLocal(_player.transform.position);
            float faceYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var press = new NetworkInputData { LookSpace = _ship.Object.Id, LookYaw = faceYaw, Target = rail.Ref };
            press.Buttons.Set(NetworkInputData.ButtonInteract, true);
            _inject = press;
            yield return Wait(0.2f);
            _inject = new NetworkInputData { LookSpace = _ship.Object.Id, LookYaw = faceYaw };
            yield return Wait(0.6f);
            bool climbing = (_player.ActiveStates & PlayerState.Climbing) != 0;
            Check(climbing, "climb: host accepted the client's Interact → Climbing replicated");
            if (climbing)
            {
                float y0 = ShipLocal(_player.transform.position).y;
                _inject = new NetworkInputData { Move = new Vector2(0f, 1f), LookSpace = _ship.Object.Id, LookYaw = faceYaw };
                yield return Measure("climbing", 2f);
                _inject = null;
                yield return Wait(0.5f);
                float y1 = ShipLocal(_player.transform.position).y;
                Log($"climb: client sees {y0:F2} → {y1:F2}");
                Check(y1 - y0 > 2f, "climb: client sees the crew member going up the mast");

                var jump = new NetworkInputData { LookSpace = _ship.Object.Id, LookYaw = faceYaw };
                jump.Buttons.Set(NetworkInputData.ButtonJump, true);
                _inject = jump;
                yield return Wait(0.2f);
                _inject = null;
                yield return Wait(2.5f);
                Check((_player.ActiveStates & PlayerState.Climbing) == 0, "climb: Space jumps off the mast");
            }

            yield return ClientRope();
        }

        // Walk to the main-mast halyard pin, cast it off, haul with LMB, drop it — all via client input.
        private IEnumerator ClientRope()
        {
            RigLine line = null;
            for (int l = 0; l < NetworkShip.MaxLines; l++)
                if (_ship.Line(l) is { Kind: RigLineKind.Halyard } h && h.HomePin != null &&
                    (line == null || Vector3.Distance(h.HomePin.transform.position, _player.transform.position) <
                                     Vector3.Distance(line.HomePin.transform.position, _player.transform.position)))
                    line = h;
            if (line == null) { Fail("rope: no halyard"); yield break; }
            int i = line.LineIndex;
            BelayPin pin = _ship.GetLinePin(i) ?? line.HomePin;
            Vector3 railOut = Vector3.ProjectOnPlane(pin.transform.position - line.Block.position, Vector3.up).normalized;

            float walkEnd = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < walkEnd)
            {
                Vector3 goal = pin.transform.position + railOut * 0.8f;
                Vector3 to = ShipLocal(goal) - ShipLocal(_player.transform.position);
                to.y = 0f;
                if (to.magnitude < 0.35f) break;
                _inject = new NetworkInputData { Move = new Vector2(0f, Mathf.Clamp01(to.magnitude)), LookSpace = _ship.Object.Id,
                    LookYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg };
                yield return null;
            }
            _inject = null;
            yield return Wait(0.8f);

            Vector3 d = ShipLocal(pin.transform.position) - ShipLocal(_player.transform.position);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            var press = new NetworkInputData { LookSpace = _ship.Object.Id, LookYaw = yaw, Target = pin.Ref };
            press.Buttons.Set(NetworkInputData.ButtonInteract, true);
            _inject = press;
            yield return Wait(0.2f);
            _inject = new NetworkInputData { LookSpace = _ship.Object.Id, LookYaw = yaw };
            yield return Wait(0.6f);
            bool holding = (_player.ActiveStates & PlayerState.HoldingRope) != 0 && _ship.GetLineMode(i) == RigLineMode.Held;
            float pinDist = pin.DistanceTo(_player.CameraAnchor.position, out _);
            Log($"rope (client): at {ShipLocal(_player.transform.position)} pin {ShipLocal(pin.transform.position)} eye→pin {pinDist:F2}m " +
                $"ref valid={pin.Ref.IsValid} idx={pin.Ref.IndexPlusOne} avail={pin.IsAvailable(_player.LocalInteractor)} " +
                $"mode={_ship.GetLineMode(i)} holder={_ship.GetLineHolder(i)} states={_player.ActiveStates}");
            Check(holding, $"rope (client): E on the pin casts '{line.DisplayName}' off into our hands");
            if (!holding) yield break;

            float out0 = _ship.GetLineOut(i);
            var haul = new NetworkInputData { LookSpace = _ship.Object.Id, LookYaw = yaw };
            haul.Buttons.Set(NetworkInputData.ButtonHaul, true);
            _inject = haul;
            yield return Wait(1.2f);
            _inject = new NetworkInputData { LookSpace = _ship.Object.Id, LookYaw = yaw };
            yield return Wait(0.5f);
            float out1 = _ship.GetLineOut(i);
            Log($"rope (client): out {out0:F2} → {out1:F2} (max {line.OutMax:F2})");
            Check(out1 > out0 + 0.3f || out0 >= line.OutMax - 0.05f, "rope (client): LMB hauls the line");

            var drop = new NetworkInputData { LookSpace = _ship.Object.Id, LookYaw = yaw };
            drop.Buttons.Set(NetworkInputData.ButtonDrop, true);
            _inject = drop;
            yield return Wait(0.2f);
            _inject = null;
            yield return Wait(1.5f);
            Check(_ship.GetLineMode(i) == RigLineMode.Loose && (_player.ActiveStates & PlayerState.HoldingRope) == 0,
                "rope (client): G drops it (loose end replicated)");
        }

        // Rendered eye in the RENDERED ship frame + ship smoothness, sampled every frame.
        private IEnumerator Measure(string label, float seconds)
        {
            _eyeSteps.Clear();
            _shipJerk.Clear();
            _hasLast = false;
            _sample = true;
            yield return Wait(seconds);
            _sample = false;
            float eyeMax = _eyeSteps.Count > 0 ? _eyeSteps.Max() : 0f;
            float eyeMean = _eyeSteps.Count > 0 ? _eyeSteps.Average() : 0f;
            // Ship smoothness: per-frame rendered speed should be steady (snapshot interpolation
            // hitches show up as frames moving 0 or 2x). Ratio of p95 to median frame speed.
            float med = _shipJerk.Count > 2 ? Percentile(_shipJerk, 0.5f) : 0f;
            float p95 = _shipJerk.Count > 2 ? Percentile(_shipJerk, 0.95f) : 0f;
            float p05 = _shipJerk.Count > 2 ? Percentile(_shipJerk, 0.05f) : 0f;
            int spikes = _eyeSteps.Count(e => e > 0.01f);
            Log($"{label}: frames={_eyeSteps.Count} eye-vs-deck step mean={eyeMean * 1000:F2}mm max={eyeMax * 1000:F2}mm (>10mm: {spikes}); " +
                $"ship frame speed p05/median/p95 = {p05:F2}/{med:F2}/{p95:F2} m/s");
            if (label == "idle")
                Check(eyeMax < 0.02f, $"{label}: no jitter of own body vs the deck on the client ({eyeMax * 1000:F1}mm < 20mm/frame)");
            if (med > 0.5f)
                Check(p95 / med < 1.35f && p05 / med > 0.65f, $"{label}: ship renders smoothly on the client (frame speed within ±35% of median)");
        }

        private Vector3 _lastShipVel;
        private bool _hasVel;

        private void LateUpdate()
        {
            if (!_sample || _player == null || _ship == null)
                return;
            Vector3 eye = _ship.transform.InverseTransformPoint(_player.RenderEyePosition);
            Vector3 shipPos = _ship.transform.position;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            if (_hasLast)
            {
                _eyeSteps.Add(Vector3.Distance(eye, _lastEye));
                _shipJerk.Add(Vector3.Distance(shipPos, _lastShipPos) / dt); // rendered frame speed
            }
            _lastEye = eye;
            _lastShipPos = shipPos;
            _hasLast = true;
        }

        // ---------------------------------------------------------------------------------------
        private static NetworkInputData Merge(NetworkInputData live, NetworkInputData forced)
        {
            forced.LookPitch = live.LookPitch;
            return forced;
        }

        private float ShipSpeed() => _ship != null ? EstimateSpeed() : 0f;

        private Vector3 _speedPos;
        private float _speedTime = -1f;
        private float _speed;

        private float EstimateSpeed()
        {
            float now = Time.realtimeSinceStartup;
            if (_speedTime < 0f || now - _speedTime > 0.5f)
            {
                if (_speedTime >= 0f)
                    _speed = Vector3.Distance(_ship.transform.position, _speedPos) / (now - _speedTime);
                _speedPos = _ship.transform.position;
                _speedTime = now;
            }
            return _speed;
        }

        private static float Percentile(List<float> values, float p)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
        }

        private Vector3 ShipLocal(Vector3 world) => _ship.transform.InverseTransformPoint(world);

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
                yield return null;
        }

        private void Check(bool ok, string what)
        {
            Log((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) _failures.Add(what);
        }

        private void Fail(string what) => Check(false, what);
        private void Log(string line) => _report.AppendLine(line);

        private void Finish()
        {
            ConnectionManager.InputInjector = null;
            _report.AppendLine(_failures.Count == 0 ? "RESULT: ALL PASSED" : $"RESULT: {_failures.Count} FAILED");
            Debug.Log($"[NetSmoke:{_role}]\n" + _report);
            int code = _failures.Count == 0 ? 0 : 1;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
#endif
