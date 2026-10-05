#if FUSION2
using RumOverboard.Core;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Crew
{
    /// <summary>
    /// In-scene debug window for intoxication + the partial/full ragdoll (F5 toggles; frees the
    /// cursor while open). Live per-player drunkenness / ragdoll / control, runtime tuning of the
    /// GameConfig ragdoll section, reset-from-config, save-to-config, and host helpers (sip, sober
    /// up, set ragdoll %, knockout).
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class CrewRagdollDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 480f, 560f);

        private const int WindowId = 0x0BEEF2A5;
        private Vector2 _scroll;
        private GameConfig _snapshot;
        private GameConfig _config;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f5Key.wasPressedThisFrame)
            {
                visible = !visible;
                Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = visible;
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;
            if (!visible)
            {
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 86f, 180f, 22f), "F5 — Crew/ragdoll debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Crew: rum + ragdoll — F5");
        }

        private void Draw(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (NetworkPlayer p in NetworkPlayer.All)
            {
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                if (_config == null) _config = p.Config;
                float control = p.Config != null ? p.Config.ControlAuthorityFor(p.RagdollControl) : 1f - p.RagdollControl;
                GUILayout.Label($"<b>{p.Object.InputAuthority}</b>{(p.HasInputAuthority ? " (you)" : "")}  drunk {p.Drunkenness * 100f:F0}%  " +
                                $"ragdoll {p.RagdollControl * 100f:F0}%  control {control * 100f:F0}%  {(p.IsKnockedOut ? "KNOCKED OUT" : "")}");
                if (!p.HasStateAuthority) continue;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Sip +15%")) p.DebugSetDrunkRagdoll(p.Drunkenness + 0.15f, p.RagdollControl);
                if (GUILayout.Button("Sober")) p.DebugSetDrunkRagdoll(0f, 0f);
                if (GUILayout.Button("Ragdoll 10%")) p.DebugSetDrunkRagdoll(p.Drunkenness, 0.1f);
                if (GUILayout.Button("50%")) p.DebugSetDrunkRagdoll(p.Drunkenness, 0.5f);
                if (GUILayout.Button("Knockout")) p.Knockout();
                GUILayout.EndHorizontal();
            }

            GameConfig cfg = _config;
            if (cfg != null)
            {
                if (_snapshot == null)
                {
                    _snapshot = ScriptableObject.CreateInstance<GameConfig>();
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(cfg), _snapshot);
                }

                GUILayout.Space(8f);
                GUILayout.Label("<b>Tuning (GameConfig)</b>");
                cfg.DrunkRagdollInfluence = Slider("Drunk → ragdoll baseline", cfg.DrunkRagdollInfluence, 0f, 1f);
                cfg.RagdollRecoverRate = Slider("Recover /s", cfg.RagdollRecoverRate, 0.02f, 3f);
                cfg.RagdollKnockoutThreshold = Slider("Knockout threshold", cfg.RagdollKnockoutThreshold, 0.1f, 1f);
                cfg.RagdollReleaseThreshold = Slider("Get-up threshold", cfg.RagdollReleaseThreshold, 0f, cfg.RagdollKnockoutThreshold);
                cfg.PartialRagdollMaxControlLoss = Slider("Partial: max control loss", cfg.PartialRagdollMaxControlLoss, 0f, 1f);
                cfg.DrunkSwayDegrees = Slider("Body sway deg", cfg.DrunkSwayDegrees, 0f, 45f);
                cfg.DrunkSwayFrequency = Slider("Body sway speed", cfg.DrunkSwayFrequency, 0.05f, 3f);
                cfg.DrunkCameraSway = Slider("Camera sway deg", cfg.DrunkCameraSway, 0f, 15f);
                cfg.DrunkMaxWobbleDeg = Slider("Walk wobble deg", cfg.DrunkMaxWobbleDeg, 0f, 90f);
                cfg.RumSipIntoxication = Slider("Per sip", cfg.RumSipIntoxication, 0f, 1f);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset from config"))
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_snapshot), cfg);
                if (GUILayout.Button("Save to config"))
                {
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(cfg), _snapshot);
                    Ocean.FeatureScenes.FeatureSceneConfigPersistence.SaveAsset(cfg);
                }
                if (GUILayout.Button("Hide (F5)"))
                    visible = false;
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("(no player with a GameConfig yet)");
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:F2}", GUILayout.Width(220f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
#endif

