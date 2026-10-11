#if FUSION2
using System.Text;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Ocean.Features.ShipStatus
{
    /// <summary>
    /// In-scene debug window for the ship status widget (F8 toggles; frees the cursor while open).
    /// Raw snapshot read-out (what the widget is drawing), widget show / hide, runtime tuning of
    /// <see cref="ShipStatusConfig.Active"/>, reset-from-config and save-to-config.
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class ShipStatusDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 520f, 560f);

        private const int WindowId = 0x0BEEF2A8;
        private Vector2 _scroll;
        private ShipStatusConfig _saved;
        private readonly ShipStatusSnapshot _own = new();
        private readonly StringBuilder _sb = new();

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame)
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
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 126f, 180f, 22f), "F8 — Ship status debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Ship status widget — F8");
        }

        private void Draw(int id)
        {
            ShipStatusConfig cfg = ShipStatusConfig.Active;
            if (_saved == null)
            {
                _saved = ScriptableObject.CreateInstance<ShipStatusConfig>();
                _saved.CopyFrom(cfg);
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            ShipStatusWidget widget = ShipStatusWidget.Instance;
            ShipStatusSnapshot snap = null;
            if (widget != null && widget.Ship != null)
            {
                snap = widget.Snapshot;
                GUILayout.Label($"Widget: {(widget.IsShowing ? "shown" : "hidden")} · ship {widget.Ship.name}");
            }
            else
            {
                GUILayout.Label("Widget: no local player aboard — showing the first ship (velocity from its body).");
                foreach (NetworkShip ship in NetworkShip.All)
                {
                    if (ship == null || ship.Object == null || !ship.Object.IsValid) continue;
                    var rb = ship.GetComponent<Rigidbody>();
                    Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero;
                    _own.Capture(ship, v, ship.Runner != null ? ship.Runner.SimulationTime : Time.time);
                    snap = _own;
                    break;
                }
            }
            if (snap != null)
                GUILayout.Label(Describe(snap));

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (widget != null && GUILayout.Button(widget.UserVisible ? "Hide widget" : "Show widget"))
                widget.UserVisible = !widget.UserVisible;
            GUILayout.EndHorizontal();

            GUILayout.Label("<b>Tuning</b>");
            cfg.Visible = GUILayout.Toggle(cfg.Visible, $"Visible at start (toggle key: {cfg.ToggleKey})");
            cfg.Scale = Slider("Scale", cfg.Scale, 0.5f, 1.5f);
            cfg.Opacity = Slider("Opacity", cfg.Opacity, 0.2f, 1f);
            cfg.Offset.x = Slider("Offset X px", cfg.Offset.x, -600f, 0f);
            cfg.Offset.y = Slider("Offset Y px", cfg.Offset.y, -600f, 0f);
            cfg.NorthUp = GUILayout.Toggle(cfg.NorthUp, "North-up dial (hull turns)");
            cfg.ShowApparentWind = GUILayout.Toggle(cfg.ShowApparentWind, "Show apparent wind");
            cfg.ShowSails = GUILayout.Toggle(cfg.ShowSails, "Show sails");
            cfg.ShowLines = GUILayout.Toggle(cfg.ShowLines, "Show lines");
            cfg.SpeedSmoothing = Slider("Speed smoothing s", cfg.SpeedSmoothing, 0.05f, 3f);
            cfg.SlackBelow = Slider("'Slack' below tension", cfg.SlackBelow, 0f, 1f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset from config"))
                cfg.CopyFrom(_saved);
            if (GUILayout.Button("Save to config"))
            {
                _saved.CopyFrom(cfg);
                FeatureScenes.FeatureSceneConfigPersistence.SaveAsset(cfg);
            }
            if (GUILayout.Button("Hide (F8)"))
                visible = false;
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private string Describe(ShipStatusSnapshot s)
        {
            _sb.Clear();
            _sb.AppendLine($"speed {s.SpeedKnots:F1} kn  heading {s.HeadingDeg:F0}°");
            _sb.AppendLine($"true wind {s.TrueWindSpeed:F1} m/s from {s.TrueWindFrom:+0;-0;0}° ({s.PointOfSail}, {s.Tack})");
            _sb.AppendLine($"apparent {s.ApparentWindSpeed:F1} m/s from {s.ApparentWindFrom:+0;-0;0}°");
            if (s.HasHelm)
                _sb.AppendLine($"rudder {s.Rudder:F1}°/{s.RudderMax:F0}  turn {s.TurnDirection:+0;-0;0}  wheel {s.Wheel * 100f:F0}%  manned {s.HelmManned}");
            foreach (var sail in s.Sails)
                _sb.AppendLine($"sail {sail.Flat} {sail.Name}: furl {sail.Furl * 100f:F0}% goal {(sail.FurlGoal ? "furl" : "loose")} hoist {sail.Hoist * 100f:F0}% yard {sail.YardAngle:F1}° — {sail.State}");
            foreach (var line in s.Lines)
                _sb.AppendLine($"  line {line.Index} sail {line.Sail} {line.Short}: {line.ModeText} value {line.Value:F2} load {line.Load:F2} tension {line.Tension:F2} ({line.TensionText})");
            return _sb.ToString();
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
