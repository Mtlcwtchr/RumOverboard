#if FUSION2
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing
{
    [DefaultExecutionOrder(1300)]
    public sealed class RowingDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        private Rect _rect = new Rect(16f, 16f, 620f, 600f);
        private Vector2 _scroll;
        private RowingConfig _snapshot;
        private RowingConfig _settings;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) visible = !visible;
            UiInputCapture.SetDebugWindow(this, visible);
        }
        private void OnDisable() => UiInputCapture.SetDebugWindow(this, false);
        private void OnDestroy() { if (_snapshot != null) Destroy(_snapshot); }
        private void OnGUI()
        {
            if (Application.isPlaying && visible) _rect = GUI.Window(0x0BEEF2A9, _rect, Draw, "Гребля — F9");
        }
        private void Draw(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            NetworkShip authority = null;
            foreach (var ship in NetworkShip.All)
            {
                if (ship == null || ship.Object == null || !ship.Object.IsValid) continue;
                if (ship.HasStateAuthority && authority == null) authority = ship;
                GUILayout.Label($"{ship.name}: {ship.Body.linearVelocity.magnitude * 1.94384f:F2} узла");
                for (int i = 0; i < ship.OarCount; i++)
                {
                    if (!ship.HasOar(i)) continue;
                    var s = ship.GetOar(i);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{i + 1} {(ship.Oar(i).Side < 0f ? "ЛБ" : "ПБ")} · {s.Rower} · серия {s.Streak} · {ship.GetOarThrust(i):F0} Н", GUILayout.Width(330f));
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = ship.HasStateAuthority && !s.Rower.IsRealPlayer;
                    if (GUILayout.Button("Вперёд")) ship.SetOarBot(i, 1);
                    if (GUILayout.Button("Назад")) ship.SetOarBot(i, -1);
                    if (GUILayout.Button("Стоп")) ship.SetOarBot(i, 0);
                    GUI.enabled = wasEnabled;
                    GUILayout.EndHorizontal();
                }
                if (ship.HasStateAuthority && GUILayout.Button("Сбросить гребки и остановить ботов")) ship.ResetOars();
            }
            var cfg = authority != null ? authority.RowingSettings : RowingConfig.Active;
            if (_settings != cfg)
            {
                _settings = cfg;
                if (_snapshot == null) _snapshot = ScriptableObject.CreateInstance<RowingConfig>();
                _snapshot.CopyFrom(cfg);
            }
            bool enabledBefore = GUI.enabled;
            GUI.enabled = authority != null;
            cfg.StrokePeriod = Slider("Период, с", cfg.StrokePeriod, 0.6f, 4f);
            cfg.DriveFraction = Slider("Доля тяги", cfg.DriveFraction, 0.2f, 0.9f);
            cfg.EarlyWindow = Slider("Окно до такта, с", cfg.EarlyWindow, 0.05f, 1f);
            cfg.LateWindow = Slider("Окно после такта, с", cfg.LateWindow, 0.05f, 1f);
            cfg.StartPower = Slider("Начальная сила", cfg.StartPower, 0.1f, 1f);
            cfg.StreakStep = Slider("Бонус серии", cfg.StreakStep, 0f, 0.5f);
            cfg.EarlyPenalty = Slider("Сила раннего гребка", cfg.EarlyPenalty, 0f, 1f);
            cfg.SyncWindow = Slider("Окно синхронности, с", cfg.SyncWindow, 0f, 1f);
            cfg.SyncBonusPerRower = Slider("Бонус за гребца", cfg.SyncBonusPerRower, 0f, 0.5f);
            cfg.MaxSyncBonus = Slider("Предел бонуса", cfg.MaxSyncBonus, 0f, 2f);
            cfg.PeakForce = Slider("Тяга весла, Н", cfg.PeakForce, 0f, 15000f);
            cfg.BackingFactor = Slider("Тяга назад", cfg.BackingFactor, 0f, 1.5f);
            cfg.MaxRowSpeed = Slider("Предел скорости, м/с", cfg.MaxRowSpeed, 0.1f, 5f);
            cfg.YawLeverage = Slider("Сила разворота", cfg.YawLeverage, 0f, 30f);
            cfg.SweepAngle = Slider("Размах, градусы", cfg.SweepAngle, 5f, 45f);
            cfg.LiftAngle = Slider("Подъём, градусы", cfg.LiftAngle, 0f, 30f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset from config")) cfg.CopyFrom(_snapshot);
            if (GUILayout.Button("Save to config")) { authority.SaveRowingConfig(); _snapshot.CopyFrom(cfg); }
            GUILayout.EndHorizontal();
            GUI.enabled = enabledBefore;
            var scene = GetComponent<RowingSceneController>();
            if (scene != null)
            {
                scene.WaterVisible = GUILayout.Toggle(scene.WaterVisible, "Показывать воду");
                scene.WindEnabled = GUILayout.Toggle(scene.WindEnabled, "Ветер (для выхода носом к ветру)");
                scene.WavesEnabled = GUILayout.Toggle(scene.WavesEnabled, "Волны");
                if (GUILayout.Button("Сбросить положение корабля")) scene.ResetShip();
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }
        private static float Slider(string name, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: {value:F2}", GUILayout.Width(240f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
#endif
