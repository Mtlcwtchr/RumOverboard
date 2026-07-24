using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes
{
    [DefaultExecutionOrder(1300)]
    public class HullFeatureDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 450f, 620f);

        private HullFeatureSceneController _scene;
        private Vector2 _scroll;
        private const int WindowId = 0x0BEEF101;

        public void Bind(HullFeatureSceneController sceneController)
        {
            _scene = sceneController;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            if (!visible)
            {
                if (GUI.Button(new Rect(12f, 12f, 150f, 24f), "Hull Debug"))
                    visible = true;
                return;
            }

            windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "Hull Feature Debug");
        }

        private void DrawWindow(int id)
        {
            if (_scene == null)
                _scene = FindAnyObjectByType<HullFeatureSceneController>();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(windowRect.width - 10f), GUILayout.Height(windowRect.height - 35f));

            DrawReferences();
            DrawOceanControls();
            DrawWindControls();
            DrawHullControls();

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
            GUILayout.Label($"WaveField: {(_scene != null && _scene.WaveField != null ? _scene.WaveField.name : "missing")}");
            GUILayout.Label($"Hull: {(_scene != null && _scene.HullBuoyancy != null ? _scene.HullBuoyancy.name : "missing")}");
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
            wave.OceanTimeScale = SliderRow("Time scale", wave.OceanTimeScale, 0f, 4f);
            wave.RuntimeWaveAmplitudeMultiplier = SliderRow("Wave strength", wave.RuntimeWaveAmplitudeMultiplier, 0f, 4f);
            wave.RuntimeWaveSpeedMultiplier = SliderRow("Wave speed", wave.RuntimeWaveSpeedMultiplier, 0.1f, 4f);
            wave.RuntimeCurrentSpeedMultiplier = SliderRow("Current speed", wave.RuntimeCurrentSpeedMultiplier, 0f, 4f);
            wave.RuntimeCurrentDirectionOffsetDegrees = SliderRow("Current dir offset", wave.RuntimeCurrentDirectionOffsetDegrees, -180f, 180f, "0");

            if (_scene.SurfaceRenderer != null)
                _scene.SurfaceRenderer.enabled = GUILayout.Toggle(_scene.SurfaceRenderer.enabled, "Show shader water surface");

            GUILayout.Space(4f);
        }

        private void DrawWindControls()
        {
            GUILayout.Label("Wind", GUI.skin.box);
            if (_scene == null || _scene.WindSystem == null)
            {
                GUILayout.Label("WindSystem not found");
                return;
            }

            OceanWindSystem wind = _scene.WindSystem;
            wind.WindEnabled = GUILayout.Toggle(wind.WindEnabled, "Wind enabled");
            wind.DirectionDegrees = SliderRow("Direction", wind.DirectionDegrees, 0f, 360f, "0");
            wind.BaseStrength = SliderRow("Base", wind.BaseStrength, 0f, 60f);
            wind.GustStrength = SliderRow("Gust", wind.GustStrength, 0f, 30f);
            wind.GustFrequency = SliderRow("Gust freq", wind.GustFrequency, 0.01f, 2f);
            wind.TurbulenceStrength = SliderRow("Turbulence", wind.TurbulenceStrength, 0f, 10f);
            wind.TurbulenceScale = SliderRow("Turb scale", wind.TurbulenceScale, 0.001f, 0.2f, "0.000");

            GUILayout.Space(4f);
        }

        private void DrawHullControls()
        {
            GUILayout.Label("Hull", GUI.skin.box);
            if (_scene == null || _scene.HullBuoyancy == null)
            {
                GUILayout.Label("ShipBuoyancyController not found");
                return;
            }

            ShipBuoyancyController hull = _scene.HullBuoyancy;
            hull.BuoyancyForce = SliderRow("Buoyancy", hull.BuoyancyForce, 0f, 120f);
            hull.VerticalDamping = SliderRow("Vertical damping", hull.VerticalDamping, 0f, 30f);
            hull.LongitudinalDrag = SliderRow("Longitudinal drag", hull.LongitudinalDrag, 0f, 20f);
            hull.LateralDrag = SliderRow("Lateral drag", hull.LateralDrag, 0f, 30f);
            hull.AngularDragCoefficient = SliderRow("Angular drag", hull.AngularDragCoefficient, 0f, 20f);
            hull.CurrentRelativeDrag = SliderRow("Current-relative drag", hull.CurrentRelativeDrag, 0f, 20f);
            hull.RollStability = SliderRow("Roll stability", hull.RollStability, 0f, 60f);
            hull.PitchStability = SliderRow("Pitch stability", hull.PitchStability, 0f, 60f);

            hull.DebugDrawAlways = GUILayout.Toggle(hull.DebugDrawAlways, "Draw hull debug always");
            hull.DebugDrawForces = GUILayout.Toggle(hull.DebugDrawForces, "Draw per-point forces");
            hull.DebugDrawResultants = GUILayout.Toggle(hull.DebugDrawResultants, "Draw resultant force/torque");
            hull.DebugDrawNormals = GUILayout.Toggle(hull.DebugDrawNormals, "Draw wave normals");

            GUILayout.Label($"Submerged points: {hull.LastSubmergedPoints}");
            GUILayout.Label($"Resultant force: {hull.LastResultantForce.magnitude:0.0} N");
            GUILayout.Label($"Impact: {hull.LastImpactStrength:0.00}");
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

