using RumOverboard.EditorTools.Attributes;
using RumOverboard.EditorTools.ShipSandbox;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Crew;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.EditorTools.Stamina
{
    /// <summary>
    /// Crew stamina feature setup:
    ///   • <see cref="EnsurePlayerPrefab"/> — NetworkPlayer.prefab gets AttributesComponent (Player profile) +
    ///     NetworkAttributes (idempotent; also run by Ship Sandbox ▸ Prepare Player Prefab).
    ///   • ★ Build Stamina Scene — sample/tuning attribute assets, registry, prefab, then
    ///     Assets/Scenes/StaminaScene.unity: the ship sandbox (brig in a calm sea — deck to sprint and jump,
    ///     masts to climb, lines to haul, the sea to swim) with the F7 stamina window open (+ F6 attributes).
    /// Menu: RumOverboard ▸ Stamina.
    /// </summary>
    public static class StaminaSetup
    {
        public const string ScenePath = "Assets/Scenes/StaminaScene.unity";
        private const string PlayerPrefabPath = "Assets/Source/Prefabs/NetworkPlayer.prefab";
        private const string PlayerProfilePath = AttributeRegistryBuilder.ConfigFolder + "/Profiles/Player.asset";

        [MenuItem("RumOverboard/Stamina/★ Build Stamina Scene", priority = 0)]
        public static void BuildStaminaScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            AttributesSandboxSetup.CreateSampleContent();
            AttributeRegistryBuilder.Rebuild(log: true);
            ShipSandboxSetup.EnsureLayers();
            ShipSandboxSetup.PreparePlayerPrefab(); // also calls EnsurePlayerPrefab

            ShipSandboxSetup.BuildScene(ScenePath, "Calm", null);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var debug = new GameObject("StaminaDebug");
            SetVisible(debug.AddComponent<StaminaDebugWindow>(), true);
            SetVisible(debug.AddComponent<AttributesDebugWindow>(), false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Stamina] Scene saved: {ScenePath}. Play → sprint (Shift), jump, climb a mast, haul a line, swim; F7 window.");
        }

        [MenuItem("RumOverboard/Stamina/Add Stamina to Player Prefab", priority = 1)]
        public static void EnsurePlayerPrefabMenu() => EnsurePlayerPrefab();

        /// <summary>NetworkPlayer.prefab: AttributesComponent with the Player profile + NetworkAttributes.</summary>
        public static void EnsurePlayerPrefab()
        {
            var profile = AssetDatabase.LoadAssetAtPath<AttributeProfile>(PlayerProfilePath);
            if (profile == null)
            {
                Debug.LogWarning($"[Stamina] {PlayerProfilePath} missing — run RumOverboard ▸ Stamina ▸ ★ Build Stamina Scene " +
                                 "(or Attributes ▸ Create Sample Content). Player prefab left without attributes.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                bool changed = false;
                var attributes = root.GetComponent<AttributesComponent>();
                if (attributes == null)
                {
                    attributes = root.AddComponent<AttributesComponent>();
                    changed = true;
                }
                var so = new SerializedObject(attributes);
                SerializedProperty profileProp = so.FindProperty("profile");
                if (profileProp.objectReferenceValue == null)
                {
                    profileProp.objectReferenceValue = profile;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
                if (root.GetComponent<NetworkAttributes>() == null)
                {
                    root.AddComponent<NetworkAttributes>();
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                    Debug.Log("[Stamina] NetworkPlayer prefab: AttributesComponent (Player) + NetworkAttributes added.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SetVisible(MonoBehaviour window, bool visible)
        {
            var so = new SerializedObject(window);
            SerializedProperty prop = so.FindProperty("visible");
            if (prop == null) return;
            prop.boolValue = visible;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
