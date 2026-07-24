using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes
{
    [DefaultExecutionOrder(1300)]
    public class WheelFeatureDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 450f, 560f);

        private WheelFeatureSceneController _scene;
        private Vector2 _scroll;
        private const int WindowId = 0x0BEEF202;

        public void Bind(WheelFeatureSceneController sceneController)
        {
            _scene = sceneController;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            if (!visible)
            {
                if (GUI.Button(new Rect(12f, 12f, 160f, 24f), "Wheel Debug"))
                    visible = true;
                return;
            }

            windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "Wheel Feature Debug");
        }

        private void DrawWindow(int id)
        {
            if (_scene == null)
                _scene = FindAnyObjectByType<WheelFeatureSceneController>();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(windowRect.width - 10f), GUILayout.Height(windowRect.height - 35f));

            DrawReferences();
            DrawInteractionHelp();
            DrawWheelControls();
            DrawOceanControls();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset from config", GUILayout.Height(24f)))
                _scene?.ResetFromConfig();
            if (GUILayout.Button("Save to config", GUILayout.Height(24f)))
                _scene?.SaveToConfig();
            if (GUILayout.Button("Hide", GUILayout.Height(24f)))
                visible = false;
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private void DrawReferences()
        {
            GUILayout.Label("Scene", GUI.skin.box);
            GUILayout.Label($"Wheel system: {(_scene != null && _scene.WheelSystem != null ? _scene.WheelSystem.name : "missing")}");
            GUILayout.Label($"WaveField: {(_scene != null && _scene.WaveField != null ? _scene.WaveField.name : "missing")}");
            GUILayout.Space(4f);
        }

        private static void DrawInteractionHelp()
        {
            GUILayout.Label("Interaction", GUI.skin.box);
            GUILayout.Label("Approach helm stand and press E to take/leave the wheel.");
            GUILayout.Label("While occupied, A/D (or arrows) steer the wheel.");
            GUILayout.Space(4f);
        }

        private void DrawWheelControls()
        {
            GUILayout.Label("Wheel / Rudder", GUI.skin.box);
            if (_scene == null || _scene.WheelSystem == null)
            {
                GUILayout.Label("Wheel system not found");
                return;
            }

            WheelFeatureStandaloneSystem wheel = _scene.WheelSystem;
            wheel.Occupied = GUILayout.Toggle(wheel.Occupied, "Occupied");
            wheel.SimulateWater = GUILayout.Toggle(wheel.SimulateWater, "Use wave/current flow on rudder");
            wheel.InvertSteering = GUILayout.Toggle(wheel.InvertSteering, "Invert steering");

            wheel.MaxWheelDegrees = SliderRow("Max wheel deg", wheel.MaxWheelDegrees, 90f, 1440f, "0");
            wheel.WheelTurnRate = SliderRow("Wheel turn rate", wheel.WheelTurnRate, 10f, 800f, "0");
            wheel.MaxRudderAngle = SliderRow("Max rudder angle", wheel.MaxRudderAngle, 5f, 85f, "0");
            wheel.RudderResponse = SliderRow("Rudder response", wheel.RudderResponse, 0.1f, 8f);
            wheel.RudderYawCoefficient = SliderRow("Yaw coefficient", wheel.RudderYawCoefficient, 0f, 20000f, "0");
            wheel.WaveWheelDisturbance = SliderRow("Wave wheel disturbance", wheel.WaveWheelDisturbance, 0f, 120f);
            wheel.WaveRudderBuffet = SliderRow("Wave rudder buffet", wheel.WaveRudderBuffet, 0f, 8f);
            wheel.UnmannedCentering = SliderRow("Unmanned centering", wheel.UnmannedCentering, 0f, 40f);

            GUILayout.Label($"Wheel angle: {wheel.WheelAngle:0.0}");
            GUILayout.Label($"Rudder angle: {wheel.RudderAngle:0.0}");
            GUILayout.Label($"Yaw torque: {wheel.LastYawTorque:0.0}");

            GUILayout.Space(4f);
        }

        private void DrawOceanControls()
        {
            GUILayout.Label("Ocean", GUI.skin.box);
            if (_scene == null || _scene.WaveField == null)
            {
                GUILayout.Label("WaveField not found");
                return;
            }

            OceanWaveField wave = _scene.WaveField;
            wave.RuntimeWaveAmplitudeMultiplier = SliderRow("Wave strength", wave.RuntimeWaveAmplitudeMultiplier, 0f, 4f);
            wave.RuntimeWaveSpeedMultiplier = SliderRow("Wave speed", wave.RuntimeWaveSpeedMultiplier, 0.1f, 4f);
            wave.RuntimeCurrentSpeedMultiplier = SliderRow("Current speed", wave.RuntimeCurrentSpeedMultiplier, 0f, 4f);

            if (_scene.SurfaceRenderer != null)
                _scene.SurfaceRenderer.enabled = GUILayout.Toggle(_scene.SurfaceRenderer.enabled, "Show shader water surface");
        }

        private static float SliderRow(string label, float value, float min, float max, string fmt = "0.00")
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"{label}: {value.ToString(fmt)}");
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndVertical();
            return value;
        }
    }
}

