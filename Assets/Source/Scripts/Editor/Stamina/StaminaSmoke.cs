#if FUSION2
using System.Text;
using Fusion;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Crew;
using RumOverboard.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.EditorTools.Stamina
{
    /// <summary>
    /// Play-mode scenario on StaminaScene with the real crew member (host, injected input):
    /// sprint back and forth along the keel → stamina drains; stop → nothing during the regen delay, then
    /// it regenerates; drain to zero → Exhausted: sprint gives walking speed and jumping is blocked;
    /// recover past the threshold → flag cleared; a fresh jump costs StaminaJumpCost.
    /// Menu: RumOverboard ▸ Stamina ▸ Run Smoke (Play Mode). Logs [StaminaSmoke] PASS/FAIL, leaves Play.
    /// </summary>
    public static class StaminaSmoke
    {
        private const string PendingKey = "RumOverboard.StaminaSmoke.Pending";

        private enum Step { WaitSpawn, Sprint, Pause, Regen, Exhaust, Exhausted, Recover, Jump, Done }

        private static Step _step;
        private static double _stepStart;
        private static NetworkPlayer _player;
        private static NetworkInputData _input;
        private static readonly StringBuilder Report = new();
        private static int _failures;

        private static float _mark;      // stamina sampled at the start of a step
        private static float _maxSpeed;     // max planar speed in a step, after 0.3 s settled on deck
        private static float _maxSpeedAny;  // max planar speed in a step, any on-deck sample
        private static float _speedSum, _sprintMean; // mean on-deck speed (single-tick spikes from bumps don't matter)
        private static int _speedCount;
        private static bool _sawFlag;
        private static bool _wentAirborne;
        private static double _settledSince = -1;

        [MenuItem("RumOverboard/Stamina/Run Smoke (Play Mode)", priority = 21)]
        public static void Run()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(StaminaSetup.ScenePath);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            Report.Clear();
            _failures = 0;
            _player = null;
            _settledSince = -1;
            Go(Step.WaitSpawn);
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying) return;
            double t = EditorApplication.timeSinceStartup - _stepStart;

            if (_step == Step.WaitSpawn)
            {
                foreach (NetworkPlayer p in NetworkPlayer.All)
                    if (p != null && p.HasStateAuthority && p.HasInputAuthority && p.Attributes != null && p.Attributes.IsAuthority)
                        _player = p;
                bool settled = _player != null && !_player.IsInAir && !_player.IsSwimming && !_player.IsClimbing;
                double now = EditorApplication.timeSinceStartup;
                if (!settled) _settledSince = -1;
                else if (_settledSince < 0) _settledSince = now;
                if (!settled || now - _settledSince < 1.0) // standing on the deck for a second
                {
                    if (t > 90.0) Finish("local host crew member standing on deck within 90 s", false);
                    return;
                }
                Check(_player.Attributes.Has(Attr.Stamina), "player prefab has the Stamina attribute");
                _input = default;
                _player.InputOverride = () => _input;
                Set().SetCurrent(Attr.Stamina, Set().Get(Attr.Stamina));
                Go(Step.Sprint);
                return;
            }

            if (_player == null || _player.Object == null || !_player.Object.IsValid)
            {
                Finish("player still alive", false);
                return;
            }

            AttributeSet set = Set();
            float cur = set.GetCurrent(Attr.Stamina);
            // Deck speed only, after 0.3 s on the deck: airborne PlanarSpeed is world velocity, and a
            // landing after a step pop carries air-steering speed that decelerates over a few ticks.
            bool onDeck = !_player.IsInAir && !_player.IsSwimming && !_player.IsClimbing;
            double now2 = EditorApplication.timeSinceStartup;
            if (!onDeck) _deckSince = -1;
            else if (_deckSince < 0) _deckSince = now2;
            if (onDeck && t > 0.25 && now2 - _deckSince > 0.3) _maxSpeed = Mathf.Max(_maxSpeed, _player.PlanarSpeed);
            if (onDeck && t > 0.25) { _maxSpeedAny = Mathf.Max(_maxSpeedAny, _player.PlanarSpeed); _speedSum += _player.PlanarSpeed; _speedCount++; }
            _sawFlag |= set.HasFlag(AttrFlag.Exhausted);
            _wentAirborne |= _player.IsInAir;
            CrewStamina model = _player.Stamina;
            int jumps = model.Jumps - _jumps0;
            float sprintSeconds = model.SprintSeconds - _sprint0;
            float spentTotal = model.TotalSpent - _spent0;

            switch (_step)
            {
                case Step.Sprint: // 3 s of sprint, alternating forward/back every 0.5 s so we stay on deck
                    Drive(move: Mathf.Sin((float)t * Mathf.PI * 2f) >= 0f ? 1f : -1f, sprint: true, jump: false);
                    if (t < 3.0) return;
                    float rate = set.Get(Attr.StaminaSprintCost);
                    float expected = rate * sprintSeconds;
                    Check(sprintSeconds > 1f, $"sprinted on deck for {sprintSeconds:0.0} of 3 s (rest: step pops / airborne)");
                    Check(Mathf.Abs(spentTotal - expected) <= 0.5f + expected * 0.05f,
                          $"sprint drains {rate:0}/s of sprinting (spent {spentTotal:0.0}, expected {expected:0.0})");
                    Check(Mathf.Abs((_mark - cur) - spentTotal) < 0.5f, "no regen while sprinting");
                    // Back-and-forth sprinting pops over steps too often for "settled" samples; ×1.4 is
                    // above anything a walk + landing can reach (sprint target ×1.6).
                    Check(_maxSpeedAny > MoveSpeed() * 1.4f, $"sprint is faster than walking (max {_maxSpeedAny:0.0} m/s on deck, walk {MoveSpeed():0.0})");
                    _sprintMean = MeanSpeed();
                    Go(Step.Pause);
                    return;

                case Step.Pause: // inside the 1 s regen delay nothing comes back
                    Drive(0f, false, false);
                    if (t < 0.6) return;
                    Check(cur - _mark < 1f, $"no regen during the delay (Δ {cur - _mark:0.0})");
                    Go(Step.Regen);
                    return;

                case Step.Regen:
                    Drive(0f, false, false);
                    if (t < 2.0) return;
                    Check(cur - _mark > 8f, $"regenerates after the delay (+{cur - _mark:0.0} in 2 s)");
                    set.SetCurrent(Attr.Stamina, 4f);
                    Go(Step.Exhaust);
                    return;

                case Step.Exhaust: // burn the last 4 points
                    Drive(Mathf.Sin((float)t * Mathf.PI * 2f) >= 0f ? 1f : -1f, true, false);
                    if (!set.HasFlag(AttrFlag.Exhausted) && t < 3.0) return;
                    Check(set.HasFlag(AttrFlag.Exhausted), "Exhausted flag set at zero");
                    Go(Step.Exhausted);
                    return;

                case Step.Exhausted: // sprint + jump held: walking speed only, never airborne
                    Drive(Mathf.Sin((float)t * Mathf.PI * 2f) >= 0f ? 1f : -1f, true, jump: ((int)(t * 4.0)) % 2 == 0);
                    if (t < 1.5) return;
                    // Mean on-deck speed (the same back-and-forth pattern as the sprint step): single-tick
                    // spikes from bumps / the rolling deck don't move it, the sprint multiplier would.
                    float exMean = MeanSpeed();
                    Check(exMean <= MoveSpeed() * 1.1f && exMean < _sprintMean * 0.85f,
                          $"exhausted sprint ≈ walking (mean {exMean:0.0} m/s ≤ walk {MoveSpeed():0.0}, sprint step mean {_sprintMean:0.0})");
                    Check(sprintSeconds == 0f, $"exhausted: sprint not charged ({sprintSeconds:0.00} s)");
                    Check(jumps == 0, $"exhausted: jump blocked ({jumps} jumps from 3 presses)");
                    Go(Step.Recover);
                    return;

                case Step.Recover:
                    Drive(0f, false, false);
                    if (set.HasFlag(AttrFlag.Exhausted) && t < 10.0) return;
                    float threshold = set.Get(Attr.StaminaRecoverThreshold);
                    Check(!set.HasFlag(AttrFlag.Exhausted), $"recovers within 10 s (took {t:0.0} s)");
                    Check(cur >= threshold - 0.5f, $"recovered at the threshold ({cur:0.0} ≥ {threshold:0})");
                    set.SetCurrent(Attr.Stamina, set.Get(Attr.Stamina));
                    Go(Step.Jump);
                    return;

                case Step.Jump: // one press on a fresh pool
                    Drive(0f, false, jump: t < 0.1);
                    if (t < 0.5) return;
                    float cost = set.Get(Attr.StaminaJumpCost);
                    Check(jumps == 1 && _wentAirborne, $"fresh: one press = one jump ({jumps}, airborne {_wentAirborne})");
                    Check(Mathf.Abs((_mark - cur) - cost) < 1f, $"jump costs {cost:0} (spent {_mark - cur:0.0})");
                    Finish(null, true);
                    return;
            }
        }

        private static AttributeSet Set() => _player.Attributes.Set;

        private static float MoveSpeed() => _player.Config != null ? _player.Config.MoveSpeed : 4.5f;

        private static float MeanSpeed() => _speedCount > 0 ? _speedSum / _speedCount : 0f;

        private static void Go(Step step)
        {
            _step = step;
            _stepStart = EditorApplication.timeSinceStartup;
            _maxSpeed = 0f;
            _maxSpeedAny = 0f;
            _speedSum = 0f;
            _speedCount = 0;
            _sawFlag = false;
            _wentAirborne = false;
            if (_player != null && _player.Attributes != null && _player.Attributes.Set != null)
            {
                _mark = _player.Attributes.Set.GetCurrent(Attr.Stamina);
                _jumps0 = _player.Stamina.Jumps;
                _sprint0 = _player.Stamina.SprintSeconds;
                _spent0 = _player.Stamina.TotalSpent;
            }
        }

        private static double _deckSince = -1;
        private static int _jumps0;
        private static float _sprint0, _spent0;

        private static void Drive(float move, bool sprint, bool jump)
        {
            float yaw = 0f;
            foreach (NetworkShip ship in NetworkShip.All)
                if (ship != null) { yaw = ship.transform.eulerAngles.y; break; }
            _input.LookYaw = yaw; // along the keel (world yaw: LookSpace left default)
            _input.Move = new Vector2(0f, move);
            _input.Buttons.Set(NetworkInputData.ButtonSprint, sprint);
            _input.Buttons.Set(NetworkInputData.ButtonJump, jump);
        }

        private static void Check(bool ok, string what)
        {
            Report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) _failures++;
        }

        private static void Finish(string failure, bool completed)
        {
            if (failure != null) Check(false, failure);
            if (!completed) Check(false, $"scenario stopped at step {_step}");
            SessionState.SetBool(PendingKey, false);
            if (_player != null) _player.InputOverride = null;
            _step = Step.Done;
            string text = $"[StaminaSmoke] {(_failures == 0 ? "PASS" : $"{_failures} FAILED")}\n{Report}";
            if (_failures == 0) Debug.Log(text);
            else Debug.LogError(text);
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
