using RumOverboard.Gameplay.Ocean;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools
{
    public class WindRuntimeDebugWindowEditor : EditorWindow
    {
        private OceanWindSystem _windSystem;
        private OceanWaveField _waveField;
        private readonly OceanDebugGizmos.MiniMap _map = new OceanDebugGizmos.MiniMap();

        [MenuItem("RumOverboard/Debug/Wind Runtime Window")]
        public static void Open()
        {
            var window = GetWindow<WindRuntimeDebugWindowEditor>("Wind Runtime");
            window.minSize = new Vector2(340f, 360f);
            window.Show();
        }

        private void OnFocus()
        {
            if (_windSystem == null)
                _windSystem = FindAnyObjectByType<OceanWindSystem>();
            if (_waveField == null)
                _waveField = FindAnyObjectByType<OceanWaveField>();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Wind Runtime Tweaks", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Настройки ветра влияют на корпус и через coupling на течение.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                _windSystem = (OceanWindSystem)EditorGUILayout.ObjectField("Wind System", _windSystem, typeof(OceanWindSystem), true);
                if (GUILayout.Button("Find", GUILayout.Width(70f)))
                    _windSystem = FindAnyObjectByType<OceanWindSystem>();
            }

            _waveField = (OceanWaveField)EditorGUILayout.ObjectField("Wave Field", _waveField, typeof(OceanWaveField), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Wind", GUILayout.Height(22f)))
                    EnsureWindSystemExists();
                if (GUILayout.Button("Bind To WaveField", GUILayout.Height(22f)))
                    BindWindToWaveField();
            }

            if (_windSystem == null)
            {
                EditorGUILayout.HelpBox("OceanWindSystem не найден. Создай его в сцене или через Ocean Runtime Window.", MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();

            _windSystem.WindEnabled = EditorGUILayout.Toggle("Enabled", _windSystem.WindEnabled);
            _windSystem.DirectionDegrees = EditorGUILayout.Slider("Direction", _windSystem.DirectionDegrees, 0f, 360f);
            _windSystem.BaseStrength = EditorGUILayout.Slider("Base Strength", _windSystem.BaseStrength, 0f, 60f);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Gusts", EditorStyles.boldLabel);
            _windSystem.GustStrength = EditorGUILayout.Slider("Gust Strength", _windSystem.GustStrength, 0f, 30f);
            _windSystem.GustFrequency = EditorGUILayout.Slider("Gust Frequency", _windSystem.GustFrequency, 0.01f, 2f);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Turbulence", EditorStyles.boldLabel);
            _windSystem.TurbulenceStrength = EditorGUILayout.Slider("Turbulence Strength", _windSystem.TurbulenceStrength, 0f, 10f);
            _windSystem.TurbulenceScale = EditorGUILayout.Slider("Turbulence Scale", _windSystem.TurbulenceScale, 0.001f, 0.2f);

            EditorGUILayout.Space(10f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Calm", GUILayout.Height(24f)))
                {
                    ApplyPreset(6f, 1f, 0.08f, 0.8f, 0.03f);
                }
                if (GUILayout.Button("Breeze", GUILayout.Height(24f)))
                {
                    ApplyPreset(12f, 4f, 0.12f, 1.5f, 0.05f);
                }
                if (GUILayout.Button("Storm", GUILayout.Height(24f)))
                {
                    ApplyPreset(24f, 10f, 0.25f, 3.5f, 0.09f);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_windSystem);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Wind compass", EditorStyles.boldLabel);
            DrawWindCompass();
            Repaint();
        }

        // Wind heading + strength, with the ship heading and point-of-sail angle overlaid.
        private void DrawWindCompass()
        {
            _map.Clear();

            Vector3 dir = _windSystem.DirectionVector;
            float strength01 = _windSystem.WindEnabled ? Mathf.Clamp01((_windSystem.BaseStrength + _windSystem.GustStrength) / 40f) : 0f;
            _map.Add($"Wind {_windSystem.BaseStrength:0}", new Vector2(dir.x, dir.z), Mathf.Max(0.3f, strength01), OceanDebugGizmos.WindColor);

            var ship = FindAnyObjectByType<ShipBuoyancyController>();
            if (ship != null)
            {
                _map.hasShip = true;
                _map.shipHeadingDeg = ship.transform.eulerAngles.y;

                Vector3 fwd = ship.transform.forward;
                float rel = Vector3.SignedAngle(new Vector3(fwd.x, 0f, fwd.z), new Vector3(dir.x, 0f, dir.z), Vector3.up);
                EditorGUILayout.LabelField($"Point of sail: {Mathf.Abs(rel):0}° off {(Mathf.Abs(rel) < 45f ? "bow (into wind)" : Mathf.Abs(rel) > 135f ? "stern (running)" : "beam")}", EditorStyles.miniLabel);
            }

            OceanDebugGizmos.DrawMiniMapLayout(_map);
        }

        private void ApplyPreset(float baseStrength, float gustStrength, float gustFreq, float turbulenceStrength, float turbulenceScale)
        {
            if (_windSystem == null)
                return;

            Undo.RecordObject(_windSystem, "Apply Wind Preset");
            _windSystem.WindEnabled = true;
            _windSystem.BaseStrength = baseStrength;
            _windSystem.GustStrength = gustStrength;
            _windSystem.GustFrequency = gustFreq;
            _windSystem.TurbulenceStrength = turbulenceStrength;
            _windSystem.TurbulenceScale = turbulenceScale;
            EditorUtility.SetDirty(_windSystem);
        }

        private void EnsureWindSystemExists()
        {
            if (_windSystem != null)
                return;

            if (_waveField == null)
                _waveField = FindAnyObjectByType<OceanWaveField>();

            if (_waveField == null)
            {
                EditorUtility.DisplayDialog("Wind Runtime", "OceanWaveField not found.", "OK");
                return;
            }

            _windSystem = _waveField.GetComponentInChildren<OceanWindSystem>(true);
            if (_windSystem == null)
            {
                var go = new GameObject("OceanWindSystem");
                Undo.RegisterCreatedObjectUndo(go, "Create OceanWindSystem");
                go.transform.SetParent(_waveField.transform, false);
                _windSystem = go.AddComponent<OceanWindSystem>();
            }

            BindWindToWaveField();
            EditorUtility.SetDirty(_windSystem);
        }

        private void BindWindToWaveField()
        {
            if (_waveField == null || _windSystem == null)
                return;

            Undo.RecordObject(_waveField, "Bind Wind System");
            _waveField.WindSystem = _windSystem;
            EditorUtility.SetDirty(_waveField);
        }
    }
}

