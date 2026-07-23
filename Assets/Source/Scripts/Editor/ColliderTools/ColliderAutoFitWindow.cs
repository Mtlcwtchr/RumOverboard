using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.ColliderTools
{
    /// <summary>
    /// Editor window for <see cref="PrimitiveColliderBaker"/>. Also owns the menu entries
    /// (top "RumOverboard/Colliders" menu and the right-click hierarchy context menu).
    /// </summary>
    public class ColliderAutoFitWindow : EditorWindow
    {
        private const string PrefsKey = "RumOverboard.ColliderAutoFit.Settings";

        [SerializeField] private GameObject _target;
        [SerializeField] private BakeSettings _settings = new BakeSettings();
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private bool _showRules = true;
        [SerializeField] private string _lastReport;

        // ------------------------------------------------------------------
        // Menu entries
        // ------------------------------------------------------------------

        [MenuItem("RumOverboard/Colliders/Auto-Fit Primitive Colliders...", false, 0)]
        public static void OpenWindow()
        {
            var window = GetWindow<ColliderAutoFitWindow>(false, "Collider Auto-Fit", true);
            window.minSize = new Vector2(360f, 480f);
            if (window._target == null && Selection.activeGameObject != null)
                window._target = Selection.activeGameObject;
            window.Show();
        }

        // Right-click a GameObject in the Hierarchy -> RumOverboard submenu.
        [MenuItem("GameObject/RumOverboard/Auto-Fit Primitive Colliders (Window)", false, 30)]
        public static void ContextOpenWindow(MenuCommand command)
        {
            OpenWindow();
            var window = GetWindow<ColliderAutoFitWindow>();
            window._target = command.context as GameObject ?? Selection.activeGameObject;
            window.Repaint();
        }

        [MenuItem("GameObject/RumOverboard/Auto-Fit Primitive Colliders (Defaults)", false, 31)]
        public static void ContextRunDefaults(MenuCommand command)
        {
            var target = command.context as GameObject ?? Selection.activeGameObject;
            if (target == null)
                return;

            var settings = LoadSettings();
            settings.EnsureRules();
            BakeReport report = PrimitiveColliderBaker.Bake(target, settings);
            EditorUtility.DisplayDialog("Collider Auto-Fit", report.ToString(), "OK");
        }

        // The GameObject context items only enable when a GameObject is selected.
        [MenuItem("GameObject/RumOverboard/Auto-Fit Primitive Colliders (Window)", true)]
        [MenuItem("GameObject/RumOverboard/Auto-Fit Primitive Colliders (Defaults)", true)]
        private static bool ValidateContext()
        {
            return Selection.activeGameObject != null;
        }

        // ------------------------------------------------------------------
        // Window lifecycle
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            _settings = LoadSettings();
        }

        private void OnDisable()
        {
            SaveSettings(_settings);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _target = (GameObject)EditorGUILayout.ObjectField(_target, typeof(GameObject), true);
                if (GUILayout.Button("Use Selection", GUILayout.Width(110f)))
                    _target = Selection.activeGameObject;
            }

            EditorGUILayout.Space();
            DrawGeneralSettings();
            EditorGUILayout.Space();
            DrawStrategySettings();
            EditorGUILayout.Space();
            DrawRules();
            EditorGUILayout.Space();
            DrawActions();

            if (!string.IsNullOrEmpty(_lastReport))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(_lastReport, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawGeneralSettings()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _settings.GroupName = EditorGUILayout.TextField("Group Name", _settings.GroupName);
            _settings.GroupByCategory = EditorGUILayout.Toggle("Group By Category", _settings.GroupByCategory);
            _settings.ClearPrevious = EditorGUILayout.Toggle(
                new GUIContent("Clear Previous", "Delete the existing generated group before rebuilding."),
                _settings.ClearPrevious);
            _settings.IncludeInactive = EditorGUILayout.Toggle("Include Inactive", _settings.IncludeInactive);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Source Colliders", EditorStyles.boldLabel);
            _settings.RemoveSourceMeshColliders = EditorGUILayout.Toggle(
                new GUIContent("Remove Mesh Colliders", "Delete MeshCollider components on the source meshes (the overlapping convex colliders)."),
                _settings.RemoveSourceMeshColliders);
            _settings.DisableSourceColliders = EditorGUILayout.Toggle(
                new GUIContent("Disable Other Colliders", "Disable any non-mesh colliders already on the source parts."),
                _settings.DisableSourceColliders);
            _settings.AutoEnableReadWrite = EditorGUILayout.Toggle(
                new GUIContent("Auto Enable Read/Write", "Flip Read/Write on imported models so their vertices can be read."),
                _settings.AutoEnableReadWrite);
        }

        private void DrawStrategySettings()
        {
            // This window now covers only ropes and masts; everything else is drawn by hand
            // with the Collider Painter (RumOverboard/Colliders/Collider Painter).
            EditorGUILayout.LabelField("Masts (Limbs)", EditorStyles.boldLabel);
            _settings.LimbElongation = EditorGUILayout.Slider(
                new GUIContent("Limb Elongation", "Limbs: min length/thickness ratio for a component to become a single capsule."),
                _settings.LimbElongation, 1.5f, 6f);
            _settings.CapsuleRadiusPercentile = EditorGUILayout.Slider(
                new GUIContent("Capsule Radius %", "Capsule radius from this percentile of vertex distance to the axis."),
                _settings.CapsuleRadiusPercentile, 0.5f, 1f);
            _settings.LimbMinSizeFraction = EditorGUILayout.Slider(
                new GUIContent("Limb Min Size %", "Limbs: drop side pieces shorter than this fraction of the largest limb (rings/cleats)."),
                _settings.LimbMinSizeFraction, 0f, 0.5f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ropes", EditorStyles.boldLabel);
            _settings.RopeMinRadius = Mathf.Max(0.005f, EditorGUILayout.FloatField(
                new GUIContent("Rope Min Radius", "Minimum capsule radius per rope/cable strand so thin ropes stay grab-able."),
                _settings.RopeMinRadius));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Common", EditorStyles.boldLabel);
            _settings.MinPartSize = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Min Part Size", "Parts smaller than this (longest edge) are ignored."),
                _settings.MinPartSize));
            _settings.MarkTriggers = EditorGUILayout.Toggle("Mark As Triggers", _settings.MarkTriggers);
            _settings.AssignLayer = EditorGUILayout.Toggle("Assign Layer", _settings.AssignLayer);
            using (new EditorGUI.DisabledScope(!_settings.AssignLayer))
                _settings.Layer = EditorGUILayout.LayerField("Layer", _settings.Layer);
        }

        private void DrawRules()
        {
            _settings.EnsureRules();
            _showRules = EditorGUILayout.Foldout(_showRules, $"Category Rules ({_settings.Rules.Count})", true);
            if (!_showRules)
                return;

            EditorGUI.indentLevel++;
            int removeAt = -1;
            for (int i = 0; i < _settings.Rules.Count; i++)
            {
                CategoryRule rule = _settings.Rules[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(string.IsNullOrEmpty(rule.NameContains)
                            ? "(catch-all)"
                            : $"contains \"{rule.NameContains}\"", EditorStyles.miniBoldLabel);
                        if (GUILayout.Button("▲", GUILayout.Width(24f)) && i > 0)
                            (_settings.Rules[i], _settings.Rules[i - 1]) = (_settings.Rules[i - 1], _settings.Rules[i]);
                        if (GUILayout.Button("▼", GUILayout.Width(24f)) && i < _settings.Rules.Count - 1)
                            (_settings.Rules[i], _settings.Rules[i + 1]) = (_settings.Rules[i + 1], _settings.Rules[i]);
                        if (GUILayout.Button("✕", GUILayout.Width(24f)))
                            removeAt = i;
                    }

                    rule.NameContains = EditorGUILayout.TextField("Name Contains", rule.NameContains);
                    rule.Category = EditorGUILayout.TextField("Category", rule.Category);
                    rule.Strategy = (FitStrategy)EditorGUILayout.EnumPopup("Strategy", rule.Strategy);
                    rule.Trigger = (ColliderTrigger)EditorGUILayout.EnumPopup(
                        new GUIContent("Trigger", "Default = follow global Mark As Triggers; Trigger/Solid override it for this category."),
                        rule.Trigger);
                }
            }

            if (removeAt >= 0)
                _settings.Rules.RemoveAt(removeAt);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Rule"))
                    _settings.Rules.Insert(0, new CategoryRule("Name", "Misc", FitStrategy.Auto));
                if (GUILayout.Button("Reset To Ship Defaults"))
                    _settings.Rules = BakeSettings.CreateShipDefaultRules();
            }

            EditorGUI.indentLevel--;
        }

        private void DrawActions()
        {
            using (new EditorGUI.DisabledScope(_target == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Bake Colliders", GUILayout.Height(32f)))
                        RunBake();
                    if (GUILayout.Button("Validate Colliders", GUILayout.Height(32f)))
                        RunValidate();
                }
            }

            if (_target == null)
                EditorGUILayout.HelpBox("Assign a target GameObject (or select one in the hierarchy).", MessageType.Warning);
        }

        private void RunBake()
        {
            SaveSettings(_settings);
            BakeReport report = PrimitiveColliderBaker.Bake(_target, _settings);
            _lastReport = report.ToString();
            Repaint();
        }

        private void RunValidate()
        {
            SaveSettings(_settings);
            ColliderFitValidator.ValidationReport report = ColliderFitValidator.Validate(_target, _settings);
            _lastReport = report.ToString();
            Repaint();
        }

        // ------------------------------------------------------------------
        // Persistence
        // ------------------------------------------------------------------

        private static BakeSettings LoadSettings()
        {
            string json = EditorPrefs.GetString(PrefsKey, string.Empty);
            BakeSettings settings = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { settings = JsonUtility.FromJson<BakeSettings>(json); }
                catch { settings = null; }
            }

            settings ??= new BakeSettings();
            settings.EnsureRules();
            return settings;
        }

        private static void SaveSettings(BakeSettings settings)
        {
            if (settings == null)
                return;
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
        }
    }
}
