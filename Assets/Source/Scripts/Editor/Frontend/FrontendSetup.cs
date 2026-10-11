#if FUSION2
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RumOverboard.Core;
using RumOverboard.EditorTools.World;
using RumOverboard.Frontend;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.EditorTools.Frontend
{
    /// <summary>
    /// Builds the frontend scenes (RumOverboard ▸ Frontend): MainMenu (persistent GameSession + UI)
    /// and Lobby (scene NetworkObject with <see cref="NetworkLobby"/>), points Boot at the menu and
    /// orders Build Settings: Boot, MainMenu, Lobby, WorldMapScene, then the feature scenes.
    /// </summary>
    public static class FrontendSetup
    {
        public const string BootPath = "Assets/Scenes/Boot.unity";
        public const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
        public const string LobbyPath = "Assets/Scenes/Lobby.unity";
        public const string GameConfigPath = "Assets/Source/Configs/GameConfig.asset";

        [MenuItem("RumOverboard/Frontend/Build Menu Scenes", priority = 1)]
        public static void BuildScenes()
        {
            if (!File.Exists(WorldMapSceneSetup.ScenePath))
                WorldMapSceneSetup.BuildScene();

            // MainMenu: camera + entry that creates the persistent session.
            Scene menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Load after NewScene(Single): it unloads unused assets and would null an earlier reference.
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath);
            if (config == null)
                Debug.LogWarning($"[Frontend] {GameConfigPath} not found — the menu will use GameConfig defaults.");
            CreateCamera();
            var entryGo = new GameObject("MainMenu");
            var entry = entryGo.AddComponent<MainMenuEntry>();
            var so = new SerializedObject(entry);
            so.FindProperty("config").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(menu, MainMenuPath);

            // Lobby: networked room state, nothing else (the UI lives on the session).
            Scene lobby = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera();
            var lobbyGo = new GameObject("Lobby");
            lobbyGo.AddComponent<Fusion.NetworkObject>();
            lobbyGo.AddComponent<NetworkLobby>();
            EditorSceneManager.MarkSceneDirty(lobby);
            EditorSceneManager.SaveScene(lobby, LobbyPath);
            // Fusion bakes scene objects on save, but an untitled scene has no path yet → SortKey 0.
            lobby = EditorSceneManager.OpenScene(LobbyPath, OpenSceneMode.Single);
            Fusion.Editor.NetworkObjectPostprocessor.BakeScene(lobby);
            EditorSceneManager.MarkSceneDirty(lobby);
            EditorSceneManager.SaveScene(lobby);

            // Boot hands off to the new menu.
            Scene boot = EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Single);
            foreach (GameObject root in boot.GetRootGameObjects())
            foreach (GameBootstrap bootstrap in root.GetComponentsInChildren<GameBootstrap>(true))
            {
                var bso = new SerializedObject(bootstrap);
                bso.FindProperty("menuScene").stringValue = "MainMenu";
                bso.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(boot);
            }
            EditorSceneManager.SaveScene(boot);

            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Frontend] Scenes saved: {MainMenuPath}, {LobbyPath}; Boot → MainMenu; build settings ordered.");
        }

        private static void CreateCamera()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = UiKit.Backdrop;
            camGo.AddComponent<AudioListener>();
        }

        private static void UpdateBuildSettings()
        {
            string[] head = { BootPath, MainMenuPath, LobbyPath, WorldMapSceneSetup.ScenePath };
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (string path in head)
                scenes.Add(new EditorBuildSettingsScene(path, true));
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (!head.Contains(s.path) && File.Exists(s.path))
                    scenes.Add(s);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Batch: play from Boot; MenuSmokeTest (-menuSmoke / -menuSmokeHost / -menuSmokeClient) runs the checks.</summary>
        public static void RunMenuSmoke()
        {
            if (!File.Exists(MainMenuPath) || !File.Exists(LobbyPath))
                BuildScenes();
            EditorSceneManager.OpenScene(BootPath);
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Batch: Windows development player of the enabled scenes (for multi-process menu tests).</summary>
        public static void BuildTestPlayer()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-menuOut");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Builds/MenuSmoke";
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(dir, "RumOverboard.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Frontend] Player build {report.summary.result} → {options.locationPathName} ({report.summary.totalTime})");
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
#endif
