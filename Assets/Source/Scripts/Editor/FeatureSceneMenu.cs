using Source.Scripts.Editor.ShipFeatures;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Source.Scripts.Editor
{
    public static class FeatureSceneMenu
    {
        private const string HullScenePath = "Assets/Scenes/HullScene.unity";
        private const string SailScenePath = "Assets/Scenes/SailScene.unity";
        private const string WheelScenePath = "Assets/Scenes/WheelScene.unity";

        // ─────────────────────────────────────────────────────────────
        //  Full pipeline: rebuild modules → setup all scenes
        // ─────────────────────────────────────────────────────────────

        [MenuItem("RumOverboard/Feature Scenes/★ Rebuild All (Modules + Scenes)")]
        public static void RebuildAll()
        {
            // 1. Extract all module prefabs from NetworkShip_Reference
            MastModulePrefabSetup.ExtractMastModulePrefabs();
            MastModulePrefabSetup.ExtractHullModulePrefab();
            MastModulePrefabSetup.ExtractWheelModulePrefab();

            // 2. Setup each feature scene
            EditorSceneManager.OpenScene(HullScenePath, OpenSceneMode.Single);
            FeatureSceneSetupEditor.SetupHullScene();
            EditorSceneManager.SaveOpenScenes();

            EditorSceneManager.OpenScene(WheelScenePath, OpenSceneMode.Single);
            FeatureSceneSetupEditor.SetupWheelScene();
            EditorSceneManager.SaveOpenScenes();

            EditorSceneManager.OpenScene(SailScenePath, OpenSceneMode.Single);
            FeatureSceneSetupEditor.SetupSailScene();
            EditorSceneManager.SaveOpenScenes();

            UnityEngine.Debug.Log("[FeatureSceneMenu] ★ Full rebuild complete: modules extracted, all 3 scenes set up and saved.");
        }

        // ─────────────────────────────────────────────────────────────
        //  Open scenes
        // ─────────────────────────────────────────────────────────────

        [MenuItem("RumOverboard/Feature Scenes/Open HullScene")]
        public static void OpenHullScene()
        {
            EditorSceneManager.OpenScene(HullScenePath, OpenSceneMode.Single);
        }

        [MenuItem("RumOverboard/Feature Scenes/Open WheelScene")]
        public static void OpenWheelScene()
        {
            EditorSceneManager.OpenScene(WheelScenePath, OpenSceneMode.Single);
        }

        [MenuItem("RumOverboard/Feature Scenes/Open SailScene")]
        public static void OpenSailScene()
        {
            EditorSceneManager.OpenScene(SailScenePath, OpenSceneMode.Single);
        }

        // ─────────────────────────────────────────────────────────────
        //  Open + Setup (individual)
        // ─────────────────────────────────────────────────────────────

        [MenuItem("RumOverboard/Feature Scenes/Open + Setup HullScene")]
        public static void OpenAndSetupHullScene()
        {
            EditorSceneManager.OpenScene(HullScenePath, OpenSceneMode.Single);
            FeatureSceneSetupEditor.SetupHullScene();
        }

        [MenuItem("RumOverboard/Feature Scenes/Open + Setup WheelScene")]
        public static void OpenAndSetupWheelScene()
        {
            EditorSceneManager.OpenScene(WheelScenePath, OpenSceneMode.Single);
            FeatureSceneSetupEditor.SetupWheelScene();
        }

        [MenuItem("RumOverboard/Feature Scenes/Open + Setup SailScene")]
        public static void OpenAndSetupSailScene()
        {
            EditorSceneManager.OpenScene(SailScenePath, OpenSceneMode.Single);
            FeatureSceneSetupEditor.SetupSailScene();
        }
    }
}
