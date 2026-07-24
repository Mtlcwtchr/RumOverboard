using RumOverboard.Gameplay.Ocean;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools
{
    public class SailRuntimeDebugWindowEditor : EditorWindow
    {
        private const string TargetNetworkShipPrefabPath = "Assets/Source/Prefabs/NetworkShip.prefab";
        private const string SourceNetworkShipReferencePrefabPath = "Assets/Source/Prefabs/NetworkShip_Reference.prefab";

        private ShipSailsAggregator _sailsAggregator;
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
            if (_sailsAggregator == null)
                _sailsAggregator = FindAggregator();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Sail Runtime Tweaks", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Управление физическими парусами и аэродинамикой без поиска объектов на сцене.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                _sailsAggregator = (ShipSailsAggregator)EditorGUILayout.ObjectField("Sails Aggregator", _sailsAggregator, typeof(ShipSailsAggregator), true);
                if (GUILayout.Button("Find", GUILayout.Width(70f)))
                    _sailsAggregator = FindAggregator();
            }

            if (_sailsAggregator == null)
            {
                EditorGUILayout.HelpBox("ShipSailsAggregator не найден. Добавь компонент на корень сетевого корабля или запусти сцену.", MessageType.Warning);
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
            _sailsAggregator.AirDensity = EditorGUILayout.Slider("Air Density", _sailsAggregator.AirDensity, 0f, 2f);
            _sailsAggregator.SailForceScale = EditorGUILayout.Slider("Force Scale", _sailsAggregator.SailForceScale, 0f, 3f);
            _sailsAggregator.MaxForcePerSail = EditorGUILayout.Slider("Max Force / Sail", _sailsAggregator.MaxForcePerSail, 0f, 8000f);
            _sailsAggregator.LiftFactor = EditorGUILayout.Slider("Lift Factor", _sailsAggregator.LiftFactor, 0f, 2f);
            _sailsAggregator.DragFactor = EditorGUILayout.Slider("Drag Factor", _sailsAggregator.DragFactor, 0f, 2f);
            _sailsAggregator.SideForceFactor = EditorGUILayout.Slider("Side Force Factor", _sailsAggregator.SideForceFactor, 0f, 1f);
            _sailsAggregator.ReverseDriveFactor = EditorGUILayout.Slider("Reverse Drive Factor", _sailsAggregator.ReverseDriveFactor, 0f, 1f);
            _sailsAggregator.VerticalForceFactor = EditorGUILayout.Slider("Vertical Force Factor", _sailsAggregator.VerticalForceFactor, 0f, 1f);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            _sailsAggregator.DebugDrawRopes = EditorGUILayout.Toggle("Debug Ropes", _sailsAggregator.DebugDrawRopes);
            _sailsAggregator.DebugDrawForces = EditorGUILayout.Toggle("Debug Forces", _sailsAggregator.DebugDrawForces);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Mast Stress", EditorStyles.boldLabel);
            int mastCount = _sailsAggregator.MastCount;
            for (int i = 0; i < mastCount; i++)
            {
                var mast = _sailsAggregator.GetMast(i);
                if (mast == null) continue;

                float stress = mast.StressRatio;
                float moment = mast.CurrentBendingMoment;
                float maxMoment = mast.MaxBendingMoment;
                Vector3 force = mast.TotalForceOnMast;

                Color prevColor = GUI.color;
                GUI.color = Color.Lerp(Color.white, Color.red, Mathf.Clamp01(stress));
                EditorGUILayout.BeginVertical(GUI.skin.box);
                GUI.color = prevColor;

                string mastName = mast.Mast != null ? mast.Mast.name : $"Mast {i}";
                EditorGUILayout.LabelField($"{mastName}: Stress {stress * 100f:0.0}%");
                EditorGUILayout.LabelField($"  Moment: {moment:0.0} / {maxMoment:0.0} N·m");
                EditorGUILayout.LabelField($"  Force: {force.magnitude:0.0} N ({force.x:0.0}, {force.y:0.0}, {force.z:0.0})");

                // Per-sail controls inside this mast
                for (int s = 0; s < mast.SailCount; s++)
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    string sailName = mast.GetSailName(s);
                    EditorGUILayout.LabelField($"  {sailName}", EditorStyles.boldLabel);

                    bool enabled = EditorGUILayout.Toggle("Enabled", mast.GetSailEnabled(s));
                    mast.SetSailEnabled(s, enabled);

                    float hoist = EditorGUILayout.Slider("Hoist", mast.GetHoist(s), 0f, 1f);
                    mast.SetHoist(s, hoist);

                    float extension = EditorGUILayout.Slider("Extension", mast.GetExtension(s), 0f, 1f);
                    mast.SetExtension(s, extension);

                    float sheet = EditorGUILayout.Slider("Sheet Angle", mast.GetSheetAngle(s), -85f, 85f);
                    mast.SetSheetAngle(s, sheet);

                    float damage = EditorGUILayout.Slider("Damage", mast.GetDamage(s), 0f, 1f);
                    mast.SetDamage(s, damage);

                    float area = mast.GetEffectiveArea(s);
                    EditorGUILayout.LabelField($"Effective Area: {area:0.00} m2");

                    EditorGUILayout.EndVertical();
                }

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
                EditorUtility.SetDirty(_sailsAggregator);
        }

        private void DrawThrustMap()
        {
            _map.Clear();
            _map.hasShip = true;
            _map.shipHeadingDeg = _sailsAggregator.transform.eulerAngles.y;

            float maxF = Mathf.Max(1f, _sailsAggregator.MaxForcePerSail);
            int count = _sailsAggregator.SailCount;
            for (int i = 0; i < count; i++)
            {
                if (!_sailsAggregator.GetSailEnabled(i))
                    continue;
                Vector3 f = _sailsAggregator.GetSailForce(i);
                if (f.sqrMagnitude < 1e-4f)
                    continue;
                _map.Add(null, new Vector2(f.x, f.z), Mathf.Clamp01(f.magnitude / maxF), new Color(0.4f, 1f, 0.45f, 0.55f));
            }

            Vector3 total = _sailsAggregator.TotalThrust;
            if (total.sqrMagnitude > 1e-4f)
                _map.Add($"Thrust {total.magnitude:0} N", new Vector2(total.x, total.z),
                    Mathf.Clamp01(total.magnitude / (maxF * Mathf.Max(1, count))), OceanDebugGizmos.ThrustColor);

            var wind = _sailsAggregator.WindSystem;
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
            if (_sailsAggregator == null) return;

            Undo.RecordObject(_sailsAggregator, "Apply Sail Preset");
            int count = _sailsAggregator.SailCount;
            for (int i = 0; i < count; i++)
            {
                _sailsAggregator.SetSailEnabled(i, true);
                _sailsAggregator.SetHoist(i, hoist);
                _sailsAggregator.SetExtension(i, extension);
                float sign = i % 2 == 0 ? -1f : 1f;
                _sailsAggregator.SetSheetAngle(i, absSheetAngle * sign);
                _sailsAggregator.SetDamage(i, damage);
            }

            EditorUtility.SetDirty(_sailsAggregator);
        }

        private static ShipSailsAggregator FindAggregator()
        {
            ShipSailsAggregator inScene = FindAnyObjectByType<ShipSailsAggregator>();
            if (inScene != null) return inScene;

            GameObject targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetNetworkShipPrefabPath);
            if (targetPrefab != null)
            {
                ShipSailsAggregator a = targetPrefab.GetComponent<ShipSailsAggregator>();
                if (a != null) return a;
            }

            GameObject refPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourceNetworkShipReferencePrefabPath);
            return refPrefab != null ? refPrefab.GetComponent<ShipSailsAggregator>() : null;
        }
    }
}
