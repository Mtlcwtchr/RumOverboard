#if FUSION2
using System.Text;
using RumOverboard.EditorTools.ShipSandbox;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Crew;
using RumOverboard.Gameplay.Items;
using RumOverboard.Networking;
using RumOverboard.StateMachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.EditorTools.Intoxication
{
    /// <summary>
    /// Play-mode scenario on CrewRagdollScene with the real crew member (host, injected input):
    /// one sip of rum from a grog bottle in hand (Drink key) → Drunkenness attribute = the sip strength and the ragdoll
    /// baseline follows; with DrunkSoberRate the crew member sobers up (not during a sip); the debug API
    /// (DebugSetDrunkRagdoll) writes the attribute.
    /// Menu: RumOverboard ▸ Intoxication ▸ Run Smoke (Play Mode). Logs [IntoxicationSmoke] PASS/FAIL.
    /// </summary>
    public static class IntoxicationSmoke
    {
        private const string PendingKey = "RumOverboard.IntoxicationSmoke.Pending";

        private enum Step { WaitSpawn, Drink, Sober, DebugApi, Done }

        private static Step _step;
        private static double _stepStart;
        private static double _settledSince = -1;
        private static NetworkPlayer _player;
        private static NetworkInputData _input;
        private static readonly StringBuilder Report = new();
        private static int _failures;
        private static bool _sawDrinking;
        private static float _mark;
        private static WorldItem _bottle;

        [MenuItem("RumOverboard/Intoxication/Run Smoke (Play Mode)", priority = 21)]
        public static void Run()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ShipSandboxSetup.CrewScenePath);
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
            double now = EditorApplication.timeSinceStartup;
            double t = now - _stepStart;

            if (_step == Step.WaitSpawn)
            {
                foreach (NetworkPlayer p in NetworkPlayer.All)
                    if (p != null && p.HasStateAuthority && p.HasInputAuthority && p.Attributes != null && p.Attributes.IsAuthority)
                        _player = p;
                bool settled = _player != null && !_player.IsInAir && !_player.IsSwimming && !_player.IsClimbing;
                if (!settled) _settledSince = -1;
                else if (_settledSince < 0) _settledSince = now;
                if (!settled || now - _settledSince < 1.0)
                {
                    if (t > 90.0) Finish("local host crew member standing on deck within 90 s", false);
                    return;
                }
                Check(CrewIntoxication.Has(Set()), "player prefab has the Drunkenness attribute");
                _input = default;
                _player.InputOverride = () => _input;
                _player.DebugSetDrunkRagdoll(0f, 0f);
                // Rum only from a bottle: put a grog bottle in the hands first.
                var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(RumOverboard.EditorTools.Items.ItemsSetup.CatalogPath);
                Fusion.NetworkObject bottleObj = catalog != null ? _player.Runner.Spawn(catalog.Get("GrogBottle"), _player.transform.position + Vector3.up * 1.2f + _player.transform.forward * 0.8f) : null;
                _bottle = bottleObj != null ? bottleObj.GetComponent<WorldItem>() : null;
                if (_bottle == null) { Finish("grog bottle spawned from the item catalog", false); return; }
                _player.DebugGiveItem(_bottle);
                Set().SetBase(Attr.DrunkSoberRate, 0f);
                Go(Step.Drink);
                return;
            }

            if (_player == null || _player.Object == null || !_player.Object.IsValid)
            {
                Finish("player still alive", false);
                return;
            }

            AttributeSet set = Set();
            bool drinking = (_player.ActiveStates & PlayerState.DrinkingRum) != 0;
            _sawDrinking |= drinking;

            switch (_step)
            {
                case Step.Drink: // one press of the Drink key
                    _input.Buttons.Set(NetworkInputData.ButtonDrink, t >= 0.2 && t < 0.6 && !_sawDrinking); // robust to slow editor updates
                    float duration = _player.Config != null ? _player.Config.RumSipDuration : 2f;
                    if (!(_sawDrinking && !drinking) && t < duration + 3.0) return;
                    float perSip = _player.Config != null ? _player.Config.RumSipIntoxication : 0.15f;
                    float influence = _player.Config != null ? _player.Config.DrunkRagdollInfluence : 0.5f;
                    float drunk = _player.Drunkenness;
                    Check(_player.CarriedId == _bottle.Object.Id, "holding the bottle");
                    Check(_sawDrinking, "Drink key with a bottle → DrinkingRum state");
                    Check(Mathf.Abs(drunk - perSip) < 0.01f, $"one sip = {perSip:0.00} intoxication (attribute {drunk:0.000})");
                    Check(Mathf.Approximately(drunk, set.GetCurrent(Attr.Drunkenness)), "NetworkPlayer.Drunkenness reads the attribute");
                    Check(_player.RagdollControl >= drunk * influence - 0.005f,
                          $"ragdoll baseline follows intoxication ({_player.RagdollControl:0.000} ≥ {drunk * influence:0.000})");
                    set.SetBase(Attr.DrunkSoberRate, 0.1f);
                    Go(Step.Sober);
                    return;

                case Step.Sober:
                    if (t < 1.0) return;
                    float lost = _mark - _player.Drunkenness;
                    float expected = 0.1f * (float)t;
                    Check(Mathf.Abs(lost - expected) < 0.03f, $"sobers up at DrunkSoberRate 0.1/s (lost {lost:0.000} in {t:0.00} s)");
                    set.SetBase(Attr.DrunkSoberRate, 0f);
                    _player.DebugSetDrunkRagdoll(0.3f, 0.1f);
                    Go(Step.DebugApi);
                    return;

                case Step.DebugApi:
                    if (t < 0.3) return;
                    Check(Mathf.Abs(_player.Drunkenness - 0.3f) < 0.001f, $"DebugSetDrunkRagdoll writes the attribute ({_player.Drunkenness:0.000})");
                    _player.DebugSetDrunkRagdoll(0f, 0f);
                    Finish(null, true);
                    return;
            }
        }

        private static AttributeSet Set() => _player.Attributes.Set;

        private static void Go(Step step)
        {
            _step = step;
            _stepStart = EditorApplication.timeSinceStartup;
            _sawDrinking = false;
            if (_player != null && _player.Attributes != null && _player.Attributes.Set != null)
                _mark = _player.Drunkenness;
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
            string text = $"[IntoxicationSmoke] {(_failures == 0 ? "PASS" : $"{_failures} FAILED")}\n{Report}";
            if (_failures == 0) Debug.Log(text);
            else Debug.LogError(text);
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
