using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Runtime IMGUI panel for live ocean/current/wind tuning.
    /// </summary>
    [DefaultExecutionOrder(1200)]
    public class OceanRuntimeDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private bool showInNonDevelopmentBuilds;
        [SerializeField] private bool autoCreateWindSystem = true;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 420f, 560f);

        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanCurrentSystem currentSystem;
        [SerializeField] private OceanWindSystem windSystem;

        private readonly List<ShipBuoyancyController> _ships = new List<ShipBuoyancyController>(8);
        private Vector2 _scroll;
        private float _nextAutoRefreshTime;

        private const int WindowId = 0x0CEA0D0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isPlaying)
                return;
            if (!Application.isEditor && !Debug.isDebugBuild)
                return;
            if (FindAnyObjectByType<OceanRuntimeDebugWindow>() != null)
                return;
            if (FindAnyObjectByType<OceanWaveField>() == null)
                return;

            var go = new GameObject("OceanRuntimeDebugWindow");
            DontDestroyOnLoad(go);
            go.AddComponent<OceanRuntimeDebugWindow>();
        }

        private void Awake()
        {
            if (!showInNonDevelopmentBuilds && !Application.isEditor && !Debug.isDebugBuild)
            {
                enabled = false;
                return;
            }

            RefreshReferences();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextAutoRefreshTime)
                return;

            _nextAutoRefreshTime = Time.unscaledTime + 1.5f;
            if (waveField == null || windSystem == null || _ships.Count == 0)
                RefreshReferences();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            if (!visible)
            {
                if (GUI.Button(new Rect(10f, 10f, 130f, 24f), "Ocean Debug"))
                    visible = true;
                return;
            }

            windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "Ocean Runtime Debug");
        }

        private void DrawWindow(int windowId)
        {
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(windowRect.width - 10f), GUILayout.Height(windowRect.height - 35f));

            DrawReferenceSection();
            DrawWaveSection();
            DrawCurrentSection();
            DrawWindSection();
            DrawShipWindSection();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh refs", GUILayout.Height(24f)))
                RefreshReferences();
            if (GUILayout.Button("Reset tweaks", GUILayout.Height(24f)))
                ResetTweaks();
            if (GUILayout.Button("Hide", GUILayout.Height(24f)))
                visible = false;
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private void DrawReferenceSection()
        {
            GUILayout.Label("References", GUI.skin.box);
            GUILayout.Label($"WaveField: {(waveField != null ? waveField.name : "missing")}");
            GUILayout.Label($"CurrentSystem: {(currentSystem != null ? currentSystem.name : "missing")}");
            GUILayout.Label($"WindSystem: {(windSystem != null ? windSystem.name : "missing")}");
            GUILayout.Label($"Ships: {_ships.Count}");
            GUILayout.Space(6f);
        }

        private void DrawWaveSection()
        {
            GUILayout.Label("Waves", GUI.skin.box);
            if (waveField == null)
            {
                GUILayout.Label("WaveField not found.");
                GUILayout.Space(6f);
                return;
            }

            waveField.OceanTimeScale = SliderRow("Time Scale", waveField.OceanTimeScale, 0f, 4f);
            waveField.RuntimeWaveAmplitudeMultiplier = SliderRow("Wave Strength", waveField.RuntimeWaveAmplitudeMultiplier, 0f, 4f);
            waveField.RuntimeWaveSpeedMultiplier = SliderRow("Wave Speed", waveField.RuntimeWaveSpeedMultiplier, 0.1f, 4f);
            GUILayout.Space(6f);
        }

        private void DrawCurrentSection()
        {
            GUILayout.Label("Currents", GUI.skin.box);
            if (waveField == null)
            {
                GUILayout.Label("WaveField not found.");
                GUILayout.Space(6f);
                return;
            }

            waveField.RuntimeCurrentSpeedMultiplier = SliderRow("Current Speed x", waveField.RuntimeCurrentSpeedMultiplier, 0f, 4f);
            waveField.RuntimeCurrentDirectionOffsetDegrees = SliderRow("Current Dir Offset", waveField.RuntimeCurrentDirectionOffsetDegrees, -180f, 180f, "0");
            GUILayout.Space(6f);
        }

        private void DrawWindSection()
        {
            GUILayout.Label("Wind", GUI.skin.box);
            if (windSystem == null)
            {
                GUILayout.Label("WindSystem not found.");
                if (autoCreateWindSystem && GUILayout.Button("Create wind system", GUILayout.Height(24f)))
                {
                    CreateWindSystem();
                    RefreshReferences();
                }
                GUILayout.Space(6f);
                return;
            }

            windSystem.WindEnabled = GUILayout.Toggle(windSystem.WindEnabled, "Wind Enabled");
            windSystem.DirectionDegrees = SliderRow("Wind Direction", windSystem.DirectionDegrees, 0f, 360f, "0");
            windSystem.BaseStrength = SliderRow("Wind Strength", windSystem.BaseStrength, 0f, 60f);
            windSystem.GustStrength = SliderRow("Gust Strength", windSystem.GustStrength, 0f, 30f);
            windSystem.GustFrequency = SliderRow("Gust Frequency", windSystem.GustFrequency, 0.01f, 2f);
            windSystem.TurbulenceStrength = SliderRow("Turbulence", windSystem.TurbulenceStrength, 0f, 10f);
            windSystem.TurbulenceScale = SliderRow("Turbulence Scale", windSystem.TurbulenceScale, 0.001f, 0.2f, "0.000");
            GUILayout.Space(6f);
        }

        private void DrawShipWindSection()
        {
            GUILayout.Label("Ship Wind Coupling", GUI.skin.box);
            if (_ships.Count == 0)
            {
                GUILayout.Label("No ShipBuoyancyController found.");
                GUILayout.Space(6f);
                return;
            }

            ShipBuoyancyController first = _ships[0];
            float hullForce = SliderRow("Hull Force Coef", first.WindHullForceCoefficient, 0f, 500f);
            float longitudinal = SliderRow("Longitudinal Factor", first.WindLongitudinalFactor, 0f, 2f);
            float lateral = SliderRow("Lateral Factor", first.WindLateralFactor, 0f, 2f);
            float maxForce = SliderRow("Max Wind Force", first.MaxWindForce, 0f, 10000f, "0");

            for (int i = 0; i < _ships.Count; i++)
            {
                ShipBuoyancyController ship = _ships[i];
                if (ship == null)
                    continue;

                ship.WindHullForceCoefficient = hullForce;
                ship.WindLongitudinalFactor = longitudinal;
                ship.WindLateralFactor = lateral;
                ship.MaxWindForce = maxForce;
                if (windSystem != null)
                    ship.WindSystem = windSystem;
            }

            GUILayout.Space(6f);
        }

        private void RefreshReferences()
        {
            waveField = FindAnyObjectByType<OceanWaveField>();
            if (waveField != null)
            {
                currentSystem = waveField.GetComponentInChildren<OceanCurrentSystem>(true);
                if (currentSystem == null)
                    currentSystem = FindAnyObjectByType<OceanCurrentSystem>();

                if (windSystem == null)
                    windSystem = waveField.GetComponentInChildren<OceanWindSystem>(true);
            }

            if (windSystem == null)
            {
                windSystem = FindAnyObjectByType<OceanWindSystem>();
                if (windSystem == null && autoCreateWindSystem)
                    CreateWindSystem();
            }

            _ships.Clear();
            var foundShips = FindObjectsByType<ShipBuoyancyController>();
            for (int i = 0; i < foundShips.Length; i++)
            {
                if (foundShips[i] != null)
                    _ships.Add(foundShips[i]);
            }
        }

        private void CreateWindSystem()
        {
            if (windSystem != null)
                return;

            Transform parent = waveField != null ? waveField.transform : null;
            var go = new GameObject("OceanWindSystem");
            if (parent != null)
                go.transform.SetParent(parent, false);

            windSystem = go.AddComponent<OceanWindSystem>();
        }

        private void ResetTweaks()
        {
            if (waveField != null)
                waveField.ResetRuntimeTuning();

            if (windSystem != null)
            {
                windSystem.WindEnabled = true;
                windSystem.DirectionDegrees = 35f;
                windSystem.BaseStrength = 12f;
                windSystem.GustStrength = 4f;
                windSystem.GustFrequency = 0.12f;
                windSystem.TurbulenceStrength = 1.5f;
                windSystem.TurbulenceScale = 0.05f;
            }
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

