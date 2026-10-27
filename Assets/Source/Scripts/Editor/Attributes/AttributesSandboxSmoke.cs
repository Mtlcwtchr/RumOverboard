using System.Text;
using RumOverboard.Core.Sim;
using RumOverboard.Gameplay.Attributes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.EditorTools.Attributes
{
    /// <summary>
    /// Play-mode smoke test of AttributesScene: enters Play, waits a few seconds, checks that the
    /// zones applied their effects (Cold drains the player's stamina, Wet adds mass + flag to the
    /// barrels only), logs [AttributesSmoke] PASS/FAIL and leaves Play mode.
    /// Menu: RumOverboard ▸ Attributes ▸ Run Sandbox Smoke (Play Mode).
    /// </summary>
    public static class AttributesSandboxSmoke
    {
        private const string PendingKey = "RumOverboard.AttributesSmoke.Pending";
        private const float WaitSeconds = 4f;
        private const string ScenePath = "Assets/Scenes/AttributesScene.unity";

        private const float SpawnTimeout = 30f;

        private static float _playStart = -1f;
        private static float _enteredAt;

        [MenuItem("RumOverboard/Attributes/Run Sandbox Smoke (Play Mode)", priority = 21)]
        public static void Run()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            _enteredAt = Time.realtimeSinceStartup;
            _playStart = -1f;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying) return;

            // The scene hosts a Fusion session: attributes become authoritative only once spawned.
            if (_playStart < 0f)
            {
                bool ready = SimWorld.All<AttributesComponent>().Count > 0;
                foreach (AttributesComponent a in SimWorld.All<AttributesComponent>())
                    ready &= a.IsAuthority;
                if (!ready && Time.realtimeSinceStartup - _enteredAt < SpawnTimeout) return;
                _playStart = Time.realtimeSinceStartup;
            }
            if (Time.realtimeSinceStartup - _playStart < WaitSeconds) return;

            SessionState.SetBool(PendingKey, false);
            _playStart = -1f;
            Evaluate();
            EditorApplication.isPlaying = false;
        }

        private static void Evaluate()
        {
            var report = new StringBuilder();
            int failures = 0;

            void Expect(string what, bool ok)
            {
                report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what}");
                if (!ok) failures++;
            }

            AttributesComponent Find(string name)
            {
                foreach (AttributesComponent a in SimWorld.All<AttributesComponent>())
                    if (a != null && a.name == name) return a;
                return null;
            }

            AttributesComponent player = Find("Player");
            AttributesComponent barrel = Find("Barrel");
            AttributesComponent powder = Find("PowderBarrel");
            AttributesComponent board = Find("Board");
            AttributesComponent crate = Find("Crate (static)");

            Expect("all 5 entities registered", player && barrel && powder && board && crate);
            if (failures == 0)
            {
                Expect("spawned with authority (host / single)", player.IsAuthority && barrel.IsAuthority && crate.IsAuthority);
                Expect($"player in cold: ColdFx active", player.HasEffect(AttrEffect.ColdFx));
                float regen = player.Get(Attr.StaminaRegen), regenBase = player.Set.GetBase(Attr.StaminaRegen);
                Expect($"cold lowers stamina regen ({regenBase:0} → {regen:0})", regen < regenBase - 0.5f);
                Expect($"barrel wet flag", barrel.HasFlag(AttrFlag.Wet));
                Expect($"barrel mass 40+2 ({barrel.Get(Attr.Mass)})", Mathf.Approximately(barrel.Get(Attr.Mass), 42f));
                Expect($"powder barrel (inherits Barrel) mass 60+2 ({powder.Get(Attr.Mass)})", Mathf.Approximately(powder.Get(Attr.Mass), 62f));
                Expect("powder barrel inherits Flammable", powder.HasFlag(AttrFlag.Flammable));
                Expect("board outside zones: not wet", !board.HasFlag(AttrFlag.Wet));
                Expect("board critical item", board.HasFlag(AttrFlag.CriticalItem));
                Expect("crate static data present", crate.GetData<BreakableData>() != null);
                Expect("registry resolves ColdFx", AttributeRegistry.Instance != null && AttributeRegistry.Instance.Get(AttrEffect.ColdFx) != null);
            }

            string text = $"[AttributesSmoke] {(failures == 0 ? "PASS" : $"{failures} FAILED")}\n{report}";
            if (failures == 0) Debug.Log(text);
            else Debug.LogError(text);
        }
    }
}
