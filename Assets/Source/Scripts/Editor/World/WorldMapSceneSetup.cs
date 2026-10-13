#if FUSION2
using System.IO;
using RumOverboard.EditorTools.ShipSandbox;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.EditorTools.World
{
    /// <summary>
    /// Isolated feature scene for procedural world generation (RumOverboard ▸ World):
    /// the sandbox scene (ocean, ship, crew, HUD) + a scene NetworkObject "World" with
    /// <see cref="NetworkWorld"/> (generation, chart, progress, mooring) and the F6 debug window.
    /// </summary>
    public static class WorldMapSceneSetup
    {
        public const string ScenePath = "Assets/Scenes/WorldMapScene.unity";
        public const string ConfigPath = "Assets/Source/Configs/FeatureScenes/WorldGenConfig.asset";
        private const string MaterialDir = "Assets/Source/Environment/Materials/World";
        private const string SeaStateDir = "Assets/Source/Environment/OceanData";
        private const float OceanRadius = 2600f;

        [MenuItem("RumOverboard/World/Build World Map Scene", priority = 10)]
        public static void BuildScene()
        {
            EnsureConfig();
            ShipSandboxSetup.BuildScene(ScenePath, "Calm", null);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // OpenScene(Single) unloads unused assets — load the config only after it, or the reference is lost.
            WorldGenConfig cfg = EnsureConfig();
            GameObject ocean = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "OceanSystem")
                    ocean = root;

            var worldGo = new GameObject("World");
            SceneManager.MoveGameObjectToScene(worldGo, scene);
            worldGo.AddComponent<Fusion.NetworkObject>();
            var world = worldGo.AddComponent<NetworkWorld>();
            var so = new SerializedObject(world);
            so.FindProperty("config").objectReferenceValue = cfg;
            if (ocean != null)
            {
                so.FindProperty("waveField").objectReferenceValue = ocean.GetComponentInChildren<OceanWaveField>(true);
                so.FindProperty("depthProvider").objectReferenceValue = ocean.GetComponentInChildren<OceanDepthProvider>(true);
                so.FindProperty("wind").objectReferenceValue = Object.FindAnyObjectByType<OceanWindSystem>();
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject debug = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "FeatureDebug")
                    debug = root;
            if (debug == null)
            {
                debug = new GameObject("FeatureDebug");
                SceneManager.MoveGameObjectToScene(debug, scene);
            }
            var window = debug.AddComponent<WorldDebugWindow>();
            var wso = new SerializedObject(window);
            wso.FindProperty("visible").boolValue = false;
            wso.ApplyModifiedPropertiesWithoutUndo();

            // The world spans kilometres: widen the ocean disk so distant islands sit in water rather than
            // over bare seabed. 250 rings keep the near-ship ring spacing of the default (112 rings × 520 m).
            var surface = ocean != null ? ocean.GetComponentInChildren<OceanSurfaceRenderer>(true) : null;
            if (surface != null)
            {
                var sso = new SerializedObject(surface);
                sso.FindProperty("meshRadius").floatValue = OceanRadius;
                sso.FindProperty("radialRings").intValue = 250;
                sso.FindProperty("radialSectors").intValue = 256;
                sso.ApplyModifiedPropertiesWithoutUndo();
            }

            Camera cam = Camera.main;
            if (cam != null)
                cam.farClipPlane = OceanRadius - 200f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (world.Config == null || world.Config.TerrainMaterial == null)
                Debug.LogError($"[World] NetworkWorld.config is not linked to {ConfigPath}.");
            Debug.Log($"[World] Scene saved: {ScenePath} (config {ConfigPath})");
        }

        /// <summary>Re-links <see cref="NetworkWorld"/>'s config (and materials) in the existing scene without rebuilding it.</summary>
        [MenuItem("RumOverboard/World/Relink World Config", priority = 12)]
        public static void RelinkConfig()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            WorldGenConfig cfg = EnsureConfig();
            int linked = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (NetworkWorld world in root.GetComponentsInChildren<NetworkWorld>(true))
            {
                var so = new SerializedObject(world);
                so.FindProperty("config").objectReferenceValue = cfg;
                so.ApplyModifiedPropertiesWithoutUndo();
                linked++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            Debug.Log($"[World] Linked {ConfigPath} to {linked} NetworkWorld(s) in {ScenePath} (saved={saved}).");
        }

        [MenuItem("RumOverboard/World/Select World Gen Config", priority = 11)]
        public static void SelectConfig() => Selection.activeObject = EnsureConfig();

        /// <summary>Batch: open the world scene and enter play mode; WorldSmokeTest (-worldSmoke) runs the checks.</summary>
        public static void RunSmoke()
        {
            if (!File.Exists(ScenePath))
                BuildScene();
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.EnterPlaymode();
        }

        public static WorldGenConfig EnsureConfig()
        {
            EnsureFolder("Assets/Source/Configs/FeatureScenes");
            var cfg = AssetDatabase.LoadAssetAtPath<WorldGenConfig>(ConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<WorldGenConfig>();
                AssetDatabase.CreateAsset(cfg, ConfigPath);
            }

            EnsureFolder(MaterialDir);
            if (cfg.TerrainMaterial == null) cfg.TerrainMaterial = LitMaterial("WorldTerrain", Color.white, 0.08f);
            if (cfg.WoodMaterial == null) cfg.WoodMaterial = LitMaterial("WorldWood", new Color(0.45f, 0.31f, 0.18f), 0.15f);
            if (cfg.StoneMaterial == null) cfg.StoneMaterial = LitMaterial("WorldStone", new Color(0.55f, 0.53f, 0.5f), 0.1f);
            if (cfg.ChartMaterial == null) cfg.ChartMaterial = LitMaterial("WorldChart", Color.white, 0.05f);
            if (cfg.RopeMaterial == null) cfg.RopeMaterial = UnlitMaterial("WorldRope", new Color(0.52f, 0.42f, 0.28f));
            if (cfg.LampMaterial == null) cfg.LampMaterial = UnlitMaterial("WorldLamp", new Color(1f, 0.82f, 0.35f));

            if (cfg.SeaStates == null || cfg.SeaStates.Length == 0)
            {
                cfg.SeaStates = new[]
                {
                    AssetDatabase.LoadAssetAtPath<OceanSeaStateProfile>($"{SeaStateDir}/SeaState_Calm.asset"),
                    AssetDatabase.LoadAssetAtPath<OceanSeaStateProfile>($"{SeaStateDir}/SeaState_Moderate.asset"),
                    AssetDatabase.LoadAssetAtPath<OceanSeaStateProfile>($"{SeaStateDir}/SeaState_Storm.asset"),
                };
                if (cfg.SeaStates[0] == null)
                    Debug.LogWarning($"[World] Sea state profiles not found in {SeaStateDir} — the sea won't roughen with difficulty.");
            }

            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            return cfg;
        }

        private static Material LitMaterial(string name, Color color, float smoothness)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Material UnlitMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.color = color;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
