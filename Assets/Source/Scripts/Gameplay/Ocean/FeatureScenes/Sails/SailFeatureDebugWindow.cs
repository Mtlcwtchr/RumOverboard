using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes.Sails
{
    [DefaultExecutionOrder(1300)]
    public sealed class SailFeatureDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 500f, 680f);

        private SailFeatureSceneController _scene;
        private Vector2 _scroll;
        private const int WindowId = 0x0BEEF303;

        public void Bind(SailFeatureSceneController scene)
        {
            _scene = scene;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            if (!visible)
            {
                if (GUI.Button(new Rect(12f, 12f, 150f, 24f), "Sail Debug"))
                    visible = true;
                return;
            }

            windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "Sails Feature Debug");
        }

        private void DrawWindow(int id)
        {
            if (_scene == null)
                _scene = FindAnyObjectByType<SailFeatureSceneController>();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(windowRect.width - 10f), GUILayout.Height(windowRect.height - 35f));

            DrawReferences();
            DrawWindControls();
            DrawForceControls();
            DrawMastControls();
            DrawResultants();

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
            GUILayout.Label($"Sail system: {(_scene != null && _scene.SailSystem != null ? _scene.SailSystem.name : "missing")}");
            GUILayout.Label($"WaveField: {(_scene != null && _scene.WaveField != null ? _scene.WaveField.name : "missing")}");
            GUILayout.Label("Masts are static; force/stress are shown with gizmos.");
            GUILayout.Space(4f);
        }

        private void DrawWindControls()
        {
            GUILayout.Label("Wind / Ocean", GUI.skin.box);
            if (_scene == null || _scene.WindSystem == null || _scene.WaveField == null)
            {
                GUILayout.Label("Ocean systems not found");
                return;
            }

            _scene.WindSystem.WindEnabled = GUILayout.Toggle(_scene.WindSystem.WindEnabled, "Wind enabled");
            _scene.WindSystem.DirectionDegrees = SliderRow("Wind direction", _scene.WindSystem.DirectionDegrees, 0f, 360f, "0");
            _scene.WindSystem.BaseStrength = SliderRow("Wind base", _scene.WindSystem.BaseStrength, 0f, 60f);
            _scene.WindSystem.GustStrength = SliderRow("Wind gust", _scene.WindSystem.GustStrength, 0f, 40f);
            _scene.WindSystem.GustFrequency = SliderRow("Wind gust freq", _scene.WindSystem.GustFrequency, 0.01f, 2f);
            _scene.WindSystem.TurbulenceStrength = SliderRow("Wind turbulence", _scene.WindSystem.TurbulenceStrength, 0f, 15f);

            _scene.WaveField.RuntimeCurrentSpeedMultiplier = SliderRow("Current speed x", _scene.WaveField.RuntimeCurrentSpeedMultiplier, 0f, 4f);
            _scene.WaveField.RuntimeWaveAmplitudeMultiplier = SliderRow("Wave strength x", _scene.WaveField.RuntimeWaveAmplitudeMultiplier, 0f, 4f);

            if (_scene.SurfaceRenderer != null)
                _scene.SurfaceRenderer.enabled = GUILayout.Toggle(_scene.SurfaceRenderer.enabled, "Show water surface shader");

            GUILayout.Space(4f);
        }

        private void DrawForceControls()
        {
            GUILayout.Label("Sail Aerodynamics", GUI.skin.box);
            if (_scene == null || _scene.SailSystem == null)
            {
                GUILayout.Label("Sail system not found");
                return;
            }

            var s = _scene.SailSystem;
            s.AirDensity = SliderRow("Air density", s.AirDensity, 0f, 2f);
            s.SailForceScale = SliderRow("Sail force scale", s.SailForceScale, 0f, 3f);
            s.MaxForcePerSail = SliderRow("Max force per sail", s.MaxForcePerSail, 0f, 12000f, "0");
            s.LiftFactor = SliderRow("Lift factor", s.LiftFactor, 0f, 2f);
            s.DragFactor = SliderRow("Drag factor", s.DragFactor, 0f, 2f);
            s.SideForceFactor = SliderRow("Side force factor", s.SideForceFactor, 0f, 1f);
            s.ReverseDriveFactor = SliderRow("Reverse drive factor", s.ReverseDriveFactor, 0f, 1f);
            s.VerticalForceFactor = SliderRow("Vertical force factor", s.VerticalForceFactor, 0f, 1f);
            s.BlockReverseDriveFromHeadwind = GUILayout.Toggle(s.BlockReverseDriveFromHeadwind, "Block reverse drive from headwind");

            GUILayout.Space(4f);
        }

        private void DrawMastControls()
        {
            GUILayout.Label("Masts", GUI.skin.box);
            if (_scene == null || _scene.SailSystem == null)
                return;

            var s = _scene.SailSystem;
            bool mastGeometryChanged = false;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Single mast", GUILayout.Height(22f)))
                s.AddMast(SailMastRigType.SingleSail);
            if (GUILayout.Button("+ Double mast", GUILayout.Height(22f)))
                s.AddMast(SailMastRigType.DoubleSail);
            if (GUILayout.Button("+ Tri mast", GUILayout.Height(22f)))
                s.AddMast(SailMastRigType.Triangular);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Remove last mast", GUILayout.Height(22f)))
                s.RemoveLastMast();

            for (int i = 0; i < s.MastCount; i++)
            {
                var entry = s.GetMastEntry(i);
                if (entry == null)
                    continue;

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"Mast {i + 1}: {entry.mastName}");
                GUILayout.Label($"Type: {entry.rigType}");
                if (GUILayout.Button("Cycle type (single -> double -> tri)", GUILayout.Height(20f)))
                    s.CycleMastType(i);

                float newHeight = SliderRow("Height", entry.mastHeight, 3f, 16f);
                float newRadius = SliderRow("Radius", entry.mastRadius, 0.08f, 0.5f);
                float newArea = SliderRow("Area scale", entry.areaScale, 0.2f, 2.5f);
                float newYaw = SliderRow("Yaw", entry.yawDegrees, 0f, 360f, "0");

                if (!Mathf.Approximately(newHeight, entry.mastHeight) ||
                    !Mathf.Approximately(newRadius, entry.mastRadius) ||
                    !Mathf.Approximately(newArea, entry.areaScale) ||
                    !Mathf.Approximately(newYaw, entry.yawDegrees))
                {
                    entry.mastHeight = newHeight;
                    entry.mastRadius = newRadius;
                    entry.areaScale = newArea;
                    entry.yawDegrees = newYaw;
                    mastGeometryChanged = true;
                }
                GUILayout.EndVertical();
            }

            if (mastGeometryChanged)
                s.RequestRigRebuild();
        }

        private void DrawResultants()
        {
            GUILayout.Label("Resultants", GUI.skin.box);
            if (_scene == null || _scene.SailSystem == null)
                return;

            var s = _scene.SailSystem;
            GUILayout.Label($"Resultant force: {s.ResultantForce.magnitude:0.0} N");
            GUILayout.Label($"Resultant torque: {s.ResultantTorque.magnitude:0.0} N*m");
            GUILayout.Label($"Wind sample: {s.LastWind.magnitude:0.0} m/s");
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

