using RumOverboard.Gameplay.Ocean;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools
{
    public class SailRuntimeDebugWindowEditor : EditorWindow
    {
        private const string NetworkShipPrefabPath = "Assets/Source/Prefabs/NetworkShip.prefab";

        private ShipSailSystem _sailSystem;
        private Vector2 _scroll;
        private readonly OceanDebugGizmos.MiniMap _map = new OceanDebugGizmos.MiniMap();

        [MenuItem("RumOverboard/Debug/Sail Runtime Window")]
        public static void Open()
        {
            var window = GetWindow<SailRuntimeDebugWindowEditor>("Sail Runtime");
            window.minSize = new Vector2(420f, 480f);
            window.Show();
        }

        private void OnFocus()
        {
            if (_sailSystem == null)
                _sailSystem = FindSailSystem();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Sail Runtime Tweaks", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Управление физическими парусами и аэродинамикой без поиска объектов на сцене.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                _sailSystem = (ShipSailSystem)EditorGUILayout.ObjectField("Sail System", _sailSystem, typeof(ShipSailSystem), true);
                if (GUILayout.Button("Find", GUILayout.Width(70f)))
                    _sailSystem = FindSailSystem();
            }

            if (_sailSystem == null)
            {
                EditorGUILayout.HelpBox("ShipSailSystem не найден. Добавь компонент на корень сетевого корабля или запусти сцену, чтобы NetworkShip добавил его автоматически.", MessageType.Warning);
#if FUSION2
                if (GUILayout.Button("Setup NetworkShip Prefab (Sails)", GUILayout.Height(26f)))
                {
                    if (ShipNetworkingSetup.Setup(out string summary))
                    {
                        Debug.Log($"[SailRuntimeDebugWindowEditor] {summary}");
                        AssetDatabase.Refresh();
                        _sailSystem = FindSailSystem();
                    }
                    else
                    {
                        Debug.LogError($"[SailRuntimeDebugWindowEditor] {summary}");
                    }
                }
#endif
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Thrust map (top-down)", EditorStyles.boldLabel);
            DrawThrustMap();
            Repaint();

            EditorGUI.BeginChangeCheck();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Global Aerodynamics", EditorStyles.boldLabel);
            _sailSystem.AirDensity = EditorGUILayout.Slider("Air Density", _sailSystem.AirDensity, 0f, 2f);
            _sailSystem.SailForceScale = EditorGUILayout.Slider("Force Scale", _sailSystem.SailForceScale, 0f, 3f);
            _sailSystem.MaxForcePerSail = EditorGUILayout.Slider("Max Force / Sail", _sailSystem.MaxForcePerSail, 0f, 8000f);
            _sailSystem.LiftFactor = EditorGUILayout.Slider("Lift Factor", _sailSystem.LiftFactor, 0f, 2f);
            _sailSystem.DragFactor = EditorGUILayout.Slider("Drag Factor", _sailSystem.DragFactor, 0f, 2f);
            _sailSystem.SideForceFactor = EditorGUILayout.Slider("Side Force Factor", _sailSystem.SideForceFactor, 0f, 1f);
            _sailSystem.ReverseDriveFactor = EditorGUILayout.Slider("Reverse Drive Factor", _sailSystem.ReverseDriveFactor, 0f, 1f);
            _sailSystem.VerticalForceFactor = EditorGUILayout.Slider("Vertical Force Factor", _sailSystem.VerticalForceFactor, 0f, 1f);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            _sailSystem.DebugDrawRopes = EditorGUILayout.Toggle("Debug Ropes", _sailSystem.DebugDrawRopes);
            _sailSystem.DebugDrawForces = EditorGUILayout.Toggle("Debug Forces", _sailSystem.DebugDrawForces);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Mast Stress", EditorStyles.boldLabel);
            int mastCount = _sailSystem.MastCount;
            for (int i = 0; i < mastCount; i++)
            {
                float stress = _sailSystem.GetMastStressRatio(i);
                float moment = _sailSystem.GetMastBendingMoment(i);
                float maxMoment = _sailSystem.GetMastMaxBendingMoment(i);
                Vector3 force = _sailSystem.GetMastForce(i);

                Color prevColor = GUI.color;
                GUI.color = Color.Lerp(Color.white, Color.red, Mathf.Clamp01(stress));
                EditorGUILayout.BeginVertical(GUI.skin.box);
                GUI.color = prevColor;

                EditorGUILayout.LabelField($"Mast {i}: Stress {stress * 100f:0.0}%");
                EditorGUILayout.LabelField($"  Moment: {moment:0.0} / {maxMoment:0.0} N·m");
                EditorGUILayout.LabelField($"  Force: {force.magnitude:0.0} N ({force.x:0.0}, {force.y:0.0}, {force.z:0.0})");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Per-sail Control", EditorStyles.boldLabel);

            int count = _sailSystem.SailCount;
            for (int i = 0; i < count; i++)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);

                string sailName = _sailSystem.GetSailName(i);
                EditorGUILayout.LabelField($"{i + 1}. {sailName}", EditorStyles.boldLabel);

                bool enabled = EditorGUILayout.Toggle("Enabled", _sailSystem.GetSailEnabled(i));
                _sailSystem.SetSailEnabled(i, enabled);

                float hoist = EditorGUILayout.Slider("Hoist", _sailSystem.GetHoist(i), 0f, 1f);
                _sailSystem.SetHoist(i, hoist);

                float extension = EditorGUILayout.Slider("Extension", _sailSystem.GetExtension(i), 0f, 1f);
                _sailSystem.SetExtension(i, extension);

                float sheet = EditorGUILayout.Slider("Sheet Angle", _sailSystem.GetSheetAngle(i), -85f, 85f);
                _sailSystem.SetSheetAngle(i, sheet);

                float damage = EditorGUILayout.Slider("Damage", _sailSystem.GetDamage(i), 0f, 1f);
                _sailSystem.SetDamage(i, damage);

                float area = _sailSystem.GetEffectiveArea(i);
                EditorGUILayout.LabelField($"Effective Area: {area:0.00} m2");

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preset: Full Sail", GUILayout.Height(24f)))
                    ApplySailPreset(1f, 1f, 0f, 0f);
                if (GUILayout.Button("Preset: Reefed", GUILayout.Height(24f)))
                    ApplySailPreset(0.55f, 0.65f, 12f, 0f);
                if (GUILayout.Button("Preset: Storm", GUILayout.Height(24f)))
                    ApplySailPreset(0.3f, 0.45f, 20f, 0f);
            }

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_sailSystem);
        }

        // Top-down thrust diagram: per-sail force arrows, net thrust, wind, and ship heading.
        private void DrawThrustMap()
        {
            _map.Clear();
            _map.hasShip = true;
            _map.shipHeadingDeg = _sailSystem.transform.eulerAngles.y;

            float maxF = Mathf.Max(1f, _sailSystem.MaxForcePerSail);
            int count = _sailSystem.SailCount;
            for (int i = 0; i < count; i++)
            {
                if (!_sailSystem.GetSailEnabled(i))
                    continue;
                Vector3 f = _sailSystem.GetSailForce(i);
                if (f.sqrMagnitude < 1e-4f)
                    continue;
                _map.Add(null, new Vector2(f.x, f.z), Mathf.Clamp01(f.magnitude / maxF), new Color(0.4f, 1f, 0.45f, 0.55f));
            }

            Vector3 total = _sailSystem.TotalThrust;
            if (total.sqrMagnitude > 1e-4f)
                _map.Add($"Thrust {total.magnitude:0} N", new Vector2(total.x, total.z),
                    Mathf.Clamp01(total.magnitude / (maxF * Mathf.Max(1, count))), OceanDebugGizmos.ThrustColor);

            var wind = _sailSystem.WindSystem;
            if (wind != null && wind.WindEnabled)
            {
                Vector3 w = wind.DirectionVector;
                _map.Add($"Wind {wind.BaseStrength:0}", new Vector2(w.x, w.z),
                    Mathf.Clamp01(wind.BaseStrength / 20f), OceanDebugGizmos.WindColor);
            }

            OceanDebugGizmos.DrawMiniMapLayout(_map, 200f);
        }

        private void ApplySailPreset(float hoist, float extension, float absSheetAngle, float damage)
        {
            if (_sailSystem == null)
                return;

            Undo.RecordObject(_sailSystem, "Apply Sail Preset");
            int count = _sailSystem.SailCount;
            for (int i = 0; i < count; i++)
            {
                _sailSystem.SetSailEnabled(i, true);
                _sailSystem.SetHoist(i, hoist);
                _sailSystem.SetExtension(i, extension);
                float sign = i % 2 == 0 ? -1f : 1f;
                _sailSystem.SetSheetAngle(i, absSheetAngle * sign);
                _sailSystem.SetDamage(i, damage);
            }

            EditorUtility.SetDirty(_sailSystem);
        }

        private static ShipSailSystem FindSailSystem()
        {
            ShipSailSystem inScene = FindAnyObjectByType<ShipSailSystem>();
            if (inScene != null)
                return inScene;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkShipPrefabPath);
            return prefab != null ? prefab.GetComponent<ShipSailSystem>() : null;
        }
    }
}

