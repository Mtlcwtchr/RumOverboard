#if FUSION2
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Ocean.Features.Helm
{
    /// <summary>
    /// In-scene debug window for the helm feel (F4 toggles; frees the cursor while open).
    /// Live wheel state (angle / spin / rudder / load), runtime tuning of
    /// <see cref="HelmFeelConfig.Active"/>, reset-from-config, save-to-config, and host helpers.
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class HelmDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 470f, 560f);

        private const int WindowId = 0x0BEEF2A4;
        private Vector2 _scroll;
        private HelmFeelConfig _snapshot;
        private ShipHelm[] _helms = System.Array.Empty<ShipHelm>();
        private float _nextScan;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f4Key.wasPressedThisFrame)
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
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 66f, 180f, 22f), "F4 — Helm debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Helm feel — F4");
        }

        private void Draw(int id)
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _helms = FindObjectsByType<ShipHelm>(FindObjectsSortMode.None);
                _nextScan = Time.unscaledTime + 1f;
            }

            HelmFeelConfig cfg = HelmFeelConfig.Active;
            if (_snapshot == null)
            {
                _snapshot = ScriptableObject.CreateInstance<HelmFeelConfig>();
                _snapshot.CopyFrom(cfg);
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (ShipHelm helm in _helms)
            {
                if (helm == null || helm.Object == null || !helm.Object.IsValid) continue;
                GUILayout.Label($"<b>{helm.name}</b>  occupant={helm.Occupant}");
                GUILayout.Label($"wheel {helm.WheelAngle:F0}° ({helm.WheelNormalized * 100f:F0}%)  spin {helm.WheelVelocity:F0}°/s  " +
                                $"rudder {helm.RudderAngle:F1}°  load {helm.Load:+0.00;-0.00}  kicks {helm.KickCount}");
                if (helm.HasStateAuthority && GUILayout.Button("Centre wheel"))
                {
                    helm.WheelAngle = 0f;
                    helm.WheelVelocity = 0f;
                    helm.RudderAngle = 0f;
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("<b>Helmsman</b>");
            cfg.SteerAccel = Slider("Steer accel °/s²", cfg.SteerAccel, 100f, 5000f);
            cfg.HoldStrength = Slider("Hold strength °/s²", cfg.HoldStrength, 0f, 5000f);
            cfg.HoldDamping = Slider("Hold damping 1/s", cfg.HoldDamping, 0f, 40f);
            GUILayout.Label("<b>Wheel</b>");
            cfg.WheelFriction = Slider("Friction (manned)", cfg.WheelFriction, 0.1f, 20f);
            cfg.UnmannedFriction = Slider("Friction (free)", cfg.UnmannedFriction, 0f, 10f);
            cfg.StaticFriction = Slider("Dry friction °/s²", cfg.StaticFriction, 0f, 1000f);
            cfg.StopBounce = Slider("Stop bounce", cfg.StopBounce, 0f, 1f);
            GUILayout.Label("<b>Water</b>");
            cfg.RudderCentering = Slider("Rudder centring", cfg.RudderCentering, 0f, 150f);
            cfg.FlowBuffet = Slider("Side-flow buffet", cfg.FlowBuffet, 0f, 400f);
            cfg.Turbulence = Slider("Turbulence °/s²", cfg.Turbulence, 0f, 800f);
            cfg.TurbulenceFullFlow = Slider("Turbulence full flow m/s", cfg.TurbulenceFullFlow, 0.5f, 15f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset from config"))
                cfg.CopyFrom(_snapshot);
            if (GUILayout.Button("Save to config"))
            {
                _snapshot.CopyFrom(cfg);
                FeatureScenes.FeatureSceneConfigPersistence.SaveAsset(cfg);
            }
            if (GUILayout.Button("Hide (F4)"))
                visible = false;
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:F2}", GUILayout.Width(210f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
#endif

