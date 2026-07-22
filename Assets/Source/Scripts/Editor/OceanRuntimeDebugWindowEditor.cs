using RumOverboard.Gameplay.Ocean;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools
{
    public class OceanRuntimeDebugWindowEditor : EditorWindow
    {
        private OceanWaveField _waveField;
        private OceanCurrentSystem _currentSystem;
        private OceanWindSystem _windSystem;
        private readonly OceanDebugGizmos.MiniMap _miniMap = new OceanDebugGizmos.MiniMap();

        [MenuItem("RumOverboard/Debug/Ocean Runtime Window")]
        public static void Open()
        {
            var window = GetWindow<OceanRuntimeDebugWindowEditor>("Ocean Runtime");
            window.minSize = new Vector2(360f, 420f);
            window.Show();
        }

        private void OnFocus()
        {
            AutoFindReferences();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Ocean Runtime Tweaks", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Работает в Play Mode и Edit Mode. В Play Mode изменения применяются сразу.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                _waveField = (OceanWaveField)EditorGUILayout.ObjectField("Wave Field", _waveField, typeof(OceanWaveField), true);
                if (GUILayout.Button("Find", GUILayout.Width(70f)))
                    AutoFindReferences();
            }

            _currentSystem = (OceanCurrentSystem)EditorGUILayout.ObjectField("Current System", _currentSystem, typeof(OceanCurrentSystem), true);
            _windSystem = (OceanWindSystem)EditorGUILayout.ObjectField("Wind System", _windSystem, typeof(OceanWindSystem), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Wind System", GUILayout.Height(22f)))
                    EnsureWindSystemExists();
                if (GUILayout.Button("Bind Wind To WaveField", GUILayout.Height(22f)))
                    BindWindToWaveField();
            }

            if (_waveField == null)
            {
                EditorGUILayout.HelpBox("OceanWaveField не найден. Нажми Find или назначь вручную.", MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Waves", EditorStyles.boldLabel);
            _waveField.OceanTimeScale = EditorGUILayout.Slider("Ocean Time Scale", _waveField.OceanTimeScale, 0f, 4f);
            _waveField.RuntimeWaveAmplitudeMultiplier = EditorGUILayout.Slider("Wave Strength", _waveField.RuntimeWaveAmplitudeMultiplier, 0f, 4f);
            _waveField.RuntimeWaveSpeedMultiplier = EditorGUILayout.Slider("Wave Speed", _waveField.RuntimeWaveSpeedMultiplier, 0.1f, 4f);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Currents", EditorStyles.boldLabel);
            _waveField.RuntimeCurrentSpeedMultiplier = EditorGUILayout.Slider("Current Speed x", _waveField.RuntimeCurrentSpeedMultiplier, 0f, 4f);
            _waveField.RuntimeCurrentDirectionOffsetDegrees = EditorGUILayout.Slider("Current Direction Offset", _waveField.RuntimeCurrentDirectionOffsetDegrees, -180f, 180f);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Wind -> Current Coupling", EditorStyles.boldLabel);
            _waveField.WindToCurrentFactor = EditorGUILayout.Slider("Wind To Current", _waveField.WindToCurrentFactor, 0f, 0.5f);
            _waveField.MaxWindDrivenCurrent = EditorGUILayout.Slider("Max Wind Current", _waveField.MaxWindDrivenCurrent, 0f, 6f);

            if (_currentSystem != null)
            {
                EditorGUILayout.Space(4f);
                Vector3 g = _currentSystem.GlobalCurrent;
                g = EditorGUILayout.Vector3Field("Global Current Vec", g);
                _currentSystem.GlobalCurrent = g;
            }

            EditorGUILayout.Space(10f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset Ocean Tweaks", GUILayout.Height(24f)))
                {
                    Undo.RecordObject(_waveField, "Reset Ocean Runtime Tweaks");
                    _waveField.ResetRuntimeTuning();
                    MarkDirty(_waveField);
                }

                if (_currentSystem != null && GUILayout.Button("Rebuild Current Zones", GUILayout.Height(24f)))
                {
                    Undo.RecordObject(_currentSystem, "Rebuild Current Zones");
                    _currentSystem.RebuildZoneCache();
                    MarkDirty(_currentSystem);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                MarkDirty(_waveField);
                if (_currentSystem != null)
                    MarkDirty(_currentSystem);
                if (_windSystem != null)
                    MarkDirty(_windSystem);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Mini-map — {_waveField.ActiveProfileName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Sea height ≈ {_waveField.EstimatedWaveHeight:0.0} m", EditorStyles.miniLabel);
            DrawMiniMap();
            Repaint(); // keep arrows live while the sim runs

            if (_windSystem == null)
                EditorGUILayout.HelpBox("Wind System is missing: currents won't receive wind-driven drift until OceanWindSystem is created and bound.", MessageType.Warning);
        }

        // Top-down overview: dominant wave, current and wind vectors, plus the ship heading.
        private void DrawMiniMap()
        {
            _miniMap.Clear();

            var ship = FindAnyObjectByType<ShipBuoyancyController>();
            Vector3 samplePos = ship != null ? ship.transform.position : Vector3.zero;
            if (ship != null)
            {
                _miniMap.hasShip = true;
                _miniMap.shipHeadingDeg = ship.transform.eulerAngles.y;
            }

            Vector3 wave = _waveField.DominantWaveDirection;
            float waveW = Mathf.Clamp01(_waveField.EstimatedWaveHeight / 4f);
            _miniMap.Add("Wave", new Vector2(wave.x, wave.z), Mathf.Max(0.35f, waveW), OceanDebugGizmos.WaveColor);

            Vector3 current = _currentSystem != null
                ? _currentSystem.EvaluateCurrent(samplePos, _waveField.OceanTimeNow)
                : (_waveField.ActiveProfile != null ? _waveField.ActiveProfile.GlobalCurrent : Vector3.zero);
            if (current.sqrMagnitude > 1e-5f)
                _miniMap.Add($"Current {current.magnitude:0.0}", new Vector2(current.x, current.z), Mathf.Clamp01(current.magnitude / 2f), OceanDebugGizmos.CurrentColor);

            if (_windSystem != null && _windSystem.WindEnabled)
            {
                Vector3 wind = _windSystem.DirectionVector;
                _miniMap.Add($"Wind {_windSystem.BaseStrength:0}", new Vector2(wind.x, wind.z), Mathf.Clamp01(_windSystem.BaseStrength / 20f), OceanDebugGizmos.WindColor);
            }

            OceanDebugGizmos.DrawMiniMapLayout(_miniMap);
        }

        private void AutoFindReferences()
        {
            if (_waveField == null)
                _waveField = FindAnyObjectByType<OceanWaveField>();
            if (_currentSystem == null && _waveField != null)
                _currentSystem = _waveField.GetComponentInChildren<OceanCurrentSystem>(true);
            if (_currentSystem == null)
                _currentSystem = FindAnyObjectByType<OceanCurrentSystem>();

            if (_windSystem == null && _waveField != null)
                _windSystem = _waveField.GetComponentInChildren<OceanWindSystem>(true);
            if (_windSystem == null)
                _windSystem = FindAnyObjectByType<OceanWindSystem>();

            if (_waveField != null && _windSystem != null && _waveField.WindSystem == null)
                _waveField.WindSystem = _windSystem;
        }

        private void EnsureWindSystemExists()
        {
            if (_windSystem != null)
                return;

            if (_waveField == null)
                _waveField = FindAnyObjectByType<OceanWaveField>();

            var host = _waveField != null ? _waveField.gameObject : null;
            if (host == null)
            {
                EditorUtility.DisplayDialog("Ocean Runtime", "WaveField not found. Create/select OceanWaveField first.", "OK");
                return;
            }

            Undo.RegisterCompleteObjectUndo(host, "Create OceanWindSystem");
            _windSystem = host.GetComponentInChildren<OceanWindSystem>(true);
            if (_windSystem == null)
            {
                var go = new GameObject("OceanWindSystem");
                Undo.RegisterCreatedObjectUndo(go, "Create OceanWindSystem");
                go.transform.SetParent(host.transform, false);
                _windSystem = go.AddComponent<OceanWindSystem>();
            }

            BindWindToWaveField();
            MarkDirty(host);
            if (_windSystem != null)
                MarkDirty(_windSystem);
        }

        private void BindWindToWaveField()
        {
            if (_waveField == null || _windSystem == null)
                return;

            Undo.RecordObject(_waveField, "Bind Wind To WaveField");
            _waveField.WindSystem = _windSystem;
            MarkDirty(_waveField);
        }

        private static void MarkDirty(Object obj)
        {
            if (obj == null)
                return;

            EditorUtility.SetDirty(obj);
        }
    }
}

