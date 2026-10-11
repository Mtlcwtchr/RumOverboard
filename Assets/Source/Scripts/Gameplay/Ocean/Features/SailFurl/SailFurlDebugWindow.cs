#if FUSION2
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Ocean.Features.SailFurl
{
    /// <summary>
    /// In-scene debug window for sail furling (F7 toggles; frees the cursor while open).
    /// Live per-sail table (furl progress / goal / worker / halyard hoist / deployment), host helpers
    /// (furl / cast loose instantly, reset to voyage start), runtime tuning of
    /// <see cref="SailFurlConfig.Active"/>, reset-from-config and save-to-config.
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class SailFurlDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 520f, 520f);

        private const int WindowId = 0x0BEEF2A7;
        private Vector2 _scroll;
        private SailFurlConfig _snapshot;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame)
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
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 106f, 180f, 22f), "F7 — Sail furl debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Sail furling (gaskets) — F7");
        }

        private void Draw(int id)
        {
            SailFurlConfig cfg = SailFurlConfig.Active;
            if (_snapshot == null)
            {
                _snapshot = ScriptableObject.CreateInstance<SailFurlConfig>();
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
            cfg.StartFurled = GUILayout.Toggle(cfg.StartFurled, "Voyage starts with sails furled");
            cfg.FurlTime = Slider("Furl time s", cfg.FurlTime, 0.5f, 20f);
            cfg.UnfurlTime = Slider("Cast-loose time s", cfg.UnfurlTime, 0.5f, 10f);
            cfg.WindResistance = Slider("Wind resistance", cfg.WindResistance, 0f, 6f);
            cfg.WorkSlack = Slider("Work slack m", cfg.WorkSlack, 0f, 3f);
            cfg.ZoneReach = Slider("Zone reach m", cfg.ZoneReach, 1f, 3.2f);
            cfg.RollDiameter = Slider("Roll diameter m", cfg.RollDiameter, 0.05f, 0.6f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset from config"))
                cfg.CopyFrom(_snapshot);
            if (GUILayout.Button("Save to config"))
            {
                _snapshot.CopyFrom(cfg);
                FeatureScenes.FeatureSceneConfigPersistence.SaveAsset(cfg);
            }
            if (GUILayout.Button("Hide (F7)"))
                visible = false;
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private static void DrawShip(NetworkShip ship)
        {
            GUILayout.Label($"<b>{ship.name}</b>  host={ship.HasStateAuthority}");
            if (ship.Sails == null)
                return;
            int count = Mathf.Min(ship.Sails.SailCount, NetworkShip.MaxSails);
            for (int s = 0; s < count; s++)
            {
                string name = ship.Sails.TryGetFlatSail(s, out var mast, out int local) ? mast.GetSailName(local) : $"#{s}";
                string state = ship.IsSailFurlInProgress(s)
                    ? (ship.GetSailFurlGoal(s) ? "furling" : "casting loose") +
                      (ship.GetSailFurlWorker(s).IsRealPlayer ? $" by {ship.GetSailFurlWorker(s)}" : " (paused)")
                    : (ship.GetSailFurl(s) >= 0.5f ? "FURLED" : "loose");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{s,2} {name,-14} furl {ship.GetSailFurl(s) * 100f:F0}%  {state}  hoist {ship.Sails.GetHoist(s) * 100f:F0}%" +
                                (ship.FurlZone(s) == null ? "  [no zone]" : ""), GUILayout.Width(400f));
                if (ship.HasStateAuthority)
                {
                    if (GUILayout.Button("Furl", GUILayout.Width(44f))) ship.SetSailFurl(s, 1f);
                    if (GUILayout.Button("Loose", GUILayout.Width(48f))) ship.SetSailFurl(s, 0f);
                }
                GUILayout.EndHorizontal();
            }

            if (!ship.HasStateAuthority)
            {
                GUILayout.Label("(client: furl controls are host-only)");
                return;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Furl all")) SetAll(ship, count, 1f);
            if (GUILayout.Button("Cast all loose")) SetAll(ship, count, 0f);
            if (GUILayout.Button("Reset (voyage start)")) ship.ResetSails();
            GUILayout.EndHorizontal();
        }

        private static void SetAll(NetworkShip ship, int count, float value)
        {
            for (int s = 0; s < count; s++)
                ship.SetSailFurl(s, value);
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
