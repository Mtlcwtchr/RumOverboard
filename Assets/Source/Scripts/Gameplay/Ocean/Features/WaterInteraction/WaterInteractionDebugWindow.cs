using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Ocean.Features.WaterInteraction
{
    /// <summary>
    /// In-scene debug window for ship ↔ water interaction (F3 toggles; frees the cursor while open):
    /// live map preview (foam / displacement), stamp count, runtime tuning of
    /// <see cref="WaterInteractionConfig.Active"/>, clear map, reset-from-config, save-to-config.
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class WaterInteractionDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 500f, 720f);

        private const int WindowId = 0x0BEEF2A3;
        private Vector2 _scroll;
        private WaterInteractionConfig _snapshot;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame)
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
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 46f, 180f, 22f), "F3 — Water debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Water interaction — F3");
        }

        private void Draw(int id)
        {
            WaterInteractionMap map = WaterInteractionMap.Instance;
            WaterInteractionConfig cfg = map != null ? map.Config : WaterInteractionConfig.Active;
            if (_snapshot == null)
            {
                _snapshot = ScriptableObject.CreateInstance<WaterInteractionConfig>();
                _snapshot.CopyFrom(cfg);
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (map != null && map.Texture != null)
            {
                GUILayout.Label($"map {map.Texture.width}² over {cfg.MapSize:F0} m, stamps/frame {map.StampsLastFrame}  (R foam, G height)");
                Rect r = GUILayoutUtility.GetRect(220f, 220f, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(r, map.Texture, ScaleMode.ScaleToFit, false);
            }
            else
            {
                GUILayout.Label("(no interaction map yet — a ship creates it)");
            }

            GUILayout.Label("<b>Map</b>");
            cfg.MapSize = Slider("Map size m", cfg.MapSize, 32f, 512f);
            cfg.FoamDecay = Slider("Foam fade 1/s", cfg.FoamDecay, 0.01f, 2f);
            cfg.FoamSpread = Slider("Foam spread", cfg.FoamSpread, 0f, 10f);
            cfg.WaveSpeed = Slider("Ripple speed m/s", cfg.WaveSpeed, 0.2f, 8f);
            cfg.WaveDamping = Slider("Ripple damping", cfg.WaveDamping, 0f, 5f);
            cfg.HeightRestore = Slider("Height restore", cfg.HeightRestore, 0f, 3f);
            cfg.StampPush = Slider("Stamp push", cfg.StampPush, 0f, 1f);
            GUILayout.Label("<b>Hull</b>");
            cfg.MinSpeed = Slider("Min speed m/s", cfg.MinSpeed, 0f, 3f);
            cfg.FullSpeed = Slider("Full speed m/s", cfg.FullSpeed, 1f, 15f);
            cfg.HullFoam = Slider("Hull foam", cfg.HullFoam, 0f, 1f);
            cfg.HullFoamRadius = Slider("Hull foam radius m", cfg.HullFoamRadius, 0.2f, 3f);
            cfg.ImmersionFoam = Slider("Slam foam / (m/s)", cfg.ImmersionFoam, 0f, 2f);
            cfg.BowWave = Slider("Bow wave m", cfg.BowWave, 0f, 2f);
            cfg.SternTrough = Slider("Stern trough m", cfg.SternTrough, 0f, 2f);
            cfg.WakeFoam = Slider("Wake foam", cfg.WakeFoam, 0f, 1f);
            cfg.WakeWidth = Slider("Wake width × beam", cfg.WakeWidth, 0.2f, 2f);
            GUILayout.Label("<b>Splashes</b>");
            cfg.SplashMinImmersion = Slider("Splash threshold m/s", cfg.SplashMinImmersion, 0.2f, 6f);
            cfg.SplashCooldown = Slider("Splash cooldown s", cfg.SplashCooldown, 0.05f, 3f);
            cfg.SplashScale = Slider("Splash scale", cfg.SplashScale, 0.1f, 4f);
            cfg.BowSpraySpeed = Slider("Bow spray speed m/s", cfg.BowSpraySpeed, 0.5f, 15f);
            GUILayout.Label("<b>Surface</b>");
            cfg.DisplacementScale = Slider("Displacement ×", cfg.DisplacementScale, 0f, 3f);
            cfg.NormalStrength = Slider("Normal strength", cfg.NormalStrength, 0f, 5f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear map") && map != null)
                map.Clear();
            if (GUILayout.Button("Reset from config"))
                cfg.CopyFrom(_snapshot);
            if (GUILayout.Button("Save to config"))
            {
                _snapshot.CopyFrom(cfg);
                FeatureScenes.FeatureSceneConfigPersistence.SaveAsset(cfg);
            }
            if (GUILayout.Button("Hide (F3)"))
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

