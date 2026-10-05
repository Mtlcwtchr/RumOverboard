#if FUSION2
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Ocean.Features.Rigging
{
    /// <summary>
    /// In-scene debug window for running rigging (F2 toggles; frees the cursor while open).
    /// Live line table (mode / rope out / sail value / holder), runtime tuning of
    /// <see cref="RiggingConfig.Active"/>, reset-from-config, save-to-config, and host helpers
    /// (reset all lines to their pins, set / furl all sails, drop a line).
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class RiggingDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 520f, 620f);

        private const int WindowId = 0x0BEEF2A1;
        private Vector2 _scroll;
        private RiggingConfig _snapshot;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame)
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
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 26f, 180f, 22f), "F2 — Rigging debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Rigging (running lines) — F2");
        }

        private void Draw(int id)
        {
            RiggingConfig cfg = RiggingConfig.Active;
            if (_snapshot == null)
            {
                _snapshot = ScriptableObject.CreateInstance<RiggingConfig>();
                _snapshot.CopyFrom(cfg);
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (NetworkShip ship in NetworkShip.All)
            {
                if (ship == null || ship.Object == null || !ship.Object.IsValid) continue;
                DrawShip(ship);
            }

            GUILayout.Space(8f);
            GUILayout.Label("<b>Tuning</b>");
            cfg.HaulRate = Slider("Haul (LMB) m/s", cfg.HaulRate, 0.1f, 5f);
            cfg.EaseRate = Slider("Ease (RMB) m/s", cfg.EaseRate, 0.1f, 5f);
            cfg.WalkHaulRate = Slider("Walk-haul m/s", cfg.WalkHaulRate, 0f, 5f);
            cfg.LoadPull = Slider("Load pull N", cfg.LoadPull, 0f, 1500f);
            cfg.LeashStiffness = Slider("Leash stiffness", cfg.LeashStiffness, 0f, 40f);
            cfg.RunRate = Slider("Run-out m/s (loaded)", cfg.RunRate, 0f, 10f);
            cfg.RunRateLight = Slider("Run-out factor (light)", cfg.RunRateLight, 0f, 1f);
            cfg.EndAirDamping = Slider("End air damping", cfg.EndAirDamping, 0f, 5f);
            cfg.EndBounce = Slider("End bounce", cfg.EndBounce, 0f, 1f);
            cfg.EndFriction = Slider("End friction", cfg.EndFriction, 0f, 1f);
            cfg.TieSlack = Slider("Tie slack m", cfg.TieSlack, 0f, 2f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset from config"))
                cfg.CopyFrom(_snapshot);
            if (GUILayout.Button("Save to config"))
            {
                _snapshot.CopyFrom(cfg);
                FeatureScenes.FeatureSceneConfigPersistence.SaveAsset(cfg);
            }
            if (GUILayout.Button("Hide (F2)"))
                visible = false;
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private static void DrawShip(NetworkShip ship)
        {
            GUILayout.Label($"<b>{ship.name}</b>  host={ship.HasStateAuthority}");
            for (int i = 0; i < NetworkShip.MaxLines; i++)
            {
                RigLine line = ship.Line(i);
                if (line == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{i,2} {line.DisplayName,-22}", GUILayout.Width(190f));
                GUILayout.Label($"{ship.GetLineMode(i),-5} out {ship.GetLineOut(i):F1}m  {ship.GetLineValue(i) * 100f:F0}%  {ship.GetLineHolder(i)}");
                if (ship.HasStateAuthority && ship.GetLineMode(i) == RigLineMode.Held &&
                    GUILayout.Button("Drop", GUILayout.Width(48f)))
                    ship.DropLine(i, ship.GetLineHolder(i));
                GUILayout.EndHorizontal();
            }

            if (!ship.HasStateAuthority)
            {
                GUILayout.Label("(client: line controls are host-only)");
                return;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset lines to pins")) ship.ResetLines();
            if (GUILayout.Button("Set all sails")) SetAll(ship, 1f);
            if (GUILayout.Button("Furl all sails")) SetAll(ship, 0f);
            GUILayout.EndHorizontal();
        }

        private static void SetAll(NetworkShip ship, float value)
        {
            for (int i = 0; i < NetworkShip.MaxLines; i++)
            {
                RigLine line = ship.Line(i);
                if (line != null && line.Kind == RigLineKind.Halyard)
                    ship.SetLineOut(i, line.OutFromValue(value));
            }
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:F2}", GUILayout.Width(200f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
#endif
