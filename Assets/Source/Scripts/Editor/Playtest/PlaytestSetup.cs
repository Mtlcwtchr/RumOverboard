using System.Collections.Generic;
using System.IO;
using System.Text;
using RumOverboard.EditorTools.Attributes;
using RumOverboard.EditorTools.Fire;
using RumOverboard.EditorTools.Items;
using RumOverboard.EditorTools.ShipSandbox;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Crew;
using RumOverboard.Gameplay.Fire;
using RumOverboard.Gameplay.Items;
using RumOverboard.Gameplay.Playtest;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.EditorTools.Playtest
{
    /// <summary>
    /// Playtest = every finished mechanic in one scene, for hands-on and multiplayer testing.
    ///   ★ Build Playtest Scene — content (attributes, items, fire) + prefabs + Assets/Scenes/PlaytestScene.unity:
    ///     the ship sandbox (brig, calm sea: climbing, helm, rigging, water) + items on deck, chest, loot in the water,
    ///     a fire pile, powder, lantern, buckets, water cask; all debug windows F2–F9 (closed), F1 help, PlaytestBoot.
    ///   Build Windows Player — Builds/Playtest/RumOverboard.exe that starts straight in the playtest scene
    ///     (the only scene of that build) + .bat launchers (host / client / two windows).
    /// </summary>
    public static class PlaytestSetup
    {
        public const string ScenePath = "Assets/Scenes/PlaytestScene.unity";
        public const string BuildFolder = "Builds/Playtest";
        public const string ExeName = "RumOverboard.exe";

        [MenuItem("RumOverboard/Playtest/★ Build Playtest Scene", priority = 0)]
        public static void BuildSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene();
        }

        public static void BuildScene()
        {
            FireSetup.CreateFireContent(); // → items → attribute samples
            AttributeRegistryBuilder.Rebuild(log: true);
            ShipSandboxSetup.EnsureLayers();
            ShipSandboxSetup.PreparePlayerPrefab(); // crew attributes (stamina, drunkenness, carry) on the player prefab
            ItemsSetup.BuildPrefabsAndCatalog(overwrite: false);
            FireSetup.BuildPrefabs(overwrite: false);
            FireSetup.EnsureConfig();
            FireSetup.EnsureMaterials();
            Fusion.Editor.NetworkProjectConfigUtilities.RebuildPrefabTable();

            ShipSandboxSetup.BuildScene(ScenePath, "Calm", null);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // The sandbox build re-imports assets: load what the scene references only now.
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemsSetup.CatalogPath);
            var config = AssetDatabase.LoadAssetAtPath<FireConfig>(FireSetup.ConfigPath);
            var flame = AssetDatabase.LoadAssetAtPath<Material>($"{FireSetup.MaterialFolder}/FireParticles.mat");
            var smoke = AssetDatabase.LoadAssetAtPath<Material>($"{FireSetup.MaterialFolder}/SmokeParticles.mat");

            new GameObject("PlaytestBoot").AddComponent<PlaytestBoot>();
            // The HUD's Leave goes to the Fusion menu, which isn't in the playtest build: come back here instead
            // (the scene reconnects on load — same as F1 ▸ Reconnect).
            var hud = Object.FindAnyObjectByType<RumOverboard.Gameplay.GameplayHud>();
            if (hud != null)
            {
                var hso = new SerializedObject(hud);
                SerializedProperty menu = hso.FindProperty("menuScene");
                if (menu != null) menu.stringValue = Path.GetFileNameWithoutExtension(ScenePath);
                hso.ApplyModifiedPropertiesWithoutUndo();
            }
            ItemsSetup.AddSpawner(catalog, Layout());

            var fire = new GameObject("Fire");
            Set(fire.AddComponent<FireDirector>(), "config", config);
            var visuals = fire.AddComponent<FireVisuals>();
            Set(visuals, "config", config);
            Set(visuals, "flameMaterial", flame);
            Set(visuals, "smokeMaterial", smoke);
            Hidden(fire.AddComponent<FireDebugWindow>());

            var debug = new GameObject("PlaytestDebug");
            var items = debug.AddComponent<ItemsDebugWindow>();
            items.SetCatalog(catalog);
            Set(items, "catalog", catalog);
            Hidden(items);
            Hidden(debug.AddComponent<AttributesDebugWindow>());
            Hidden(debug.AddComponent<StaminaDebugWindow>());
            debug.AddComponent<HeldItemPresenter>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Playtest] Scene saved: {ScenePath}. Play → F1 help; F2–F9 debug windows.");
        }

        /// <summary>Ship axes from the first crew spawn: x starboard, z bow.</summary>
        private static List<ItemSpawner.Placement> Layout() => new()
        {
            // Free deck (ship sandbox brig, from the centreline at crew spawn 0): masts at z +2 and −4 on the
            // centreline, bulwarks at |x| ≈ 1.5 (closing in past z 4), crew spawns at (±1.6, 0) and (±1.6, −1.5).
            // Fire pile on the clear patch at the bow + lantern; powder aft, ≥ 4 m (blast radius) from the spawns.
            new() { ItemId = "Crate", Where = ItemSpawner.Where.Deck, Offset = new(0.6f, 0.5f, 3.6f) },
            new() { ItemId = "Board", Where = ItemSpawner.Where.Deck, Offset = new(0f, 0.3f, 4.5f), Yaw = 90f },
            new() { ItemId = "Barrel", Where = ItemSpawner.Where.Deck, Offset = new(-0.55f, 0.6f, 3.7f) },
            new() { ItemId = "Lantern", Where = ItemSpawner.Where.Deck, Offset = new(0.05f, 0.35f, 3.25f) },
            new() { ItemId = "PowderBarrel", Where = ItemSpawner.Where.Deck, Offset = new(0.8f, 0.6f, -5.5f) },
            // firefighting next to the crew spawn
            new() { ItemId = "Bucket", Where = ItemSpawner.Where.Deck, Offset = new(0.4f, 0.35f, 1.2f) },
            new() { ItemId = "Bucket", Where = ItemSpawner.Where.Deck, Offset = new(-0.4f, 0.35f, 1.2f) },
            new() { ItemId = "WaterCask", Where = ItemSpawner.Where.Deck, Offset = new(1.0f, 0.6f, -2.9f), AnchorToShip = true },
            // rum, navigation, cargo aft; the chest at the stern
            new() { ItemId = "GrogBottle", Where = ItemSpawner.Where.Deck, Offset = new(0.6f, 0.35f, -0.6f) },
            new() { ItemId = "GrogBottle", Where = ItemSpawner.Where.Deck, Offset = new(-0.6f, 0.35f, -0.6f) },
            new() { ItemId = "Spyglass", Where = ItemSpawner.Where.Deck, Offset = new(0.3f, 0.35f, -1.4f) },
            new() { ItemId = "Compass", Where = ItemSpawner.Where.Deck, Offset = new(-0.3f, 0.35f, -1.4f) },
            new() { ItemId = "Barrel", Where = ItemSpawner.Where.Deck, Offset = new(0.8f, 0.6f, 0.7f) },
            new() { ItemId = "Chest", Where = ItemSpawner.Where.Deck, Offset = new(0f, 0.45f, -2.2f), AnchorToShip = true },
            // loot in the water next to the ship
            new() { ItemId = "Barrel", Where = ItemSpawner.Where.Water, Offset = new(6f, 1.5f, 0f) },
            new() { ItemId = "Board", Where = ItemSpawner.Where.Water, Offset = new(5.5f, 1.5f, 4f) },
            new() { ItemId = "GrogBottle", Where = ItemSpawner.Where.Water, Offset = new(6f, 1.5f, -3f) },
            new() { ItemId = "Bucket", Where = ItemSpawner.Where.Water, Offset = new(-6f, 1.5f, 1f) },
        };

        // ───────────────────────── player build ─────────────────────────

        [MenuItem("RumOverboard/Playtest/Build Windows Player", priority = 10)]
        public static void BuildPlayerMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildPlayer();
        }

        /// <summary>Builds the player; returns true on success. Writes Builds/Playtest/build_result.txt.</summary>
        public static bool BuildPlayer()
        {
            if (!File.Exists(ScenePath)) BuildScene();
            Fusion.Editor.NetworkProjectConfigUtilities.RebuildPrefabTable(); // Runner.Spawn resolves prefabs by this table

            Directory.CreateDirectory(BuildFolder);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, // the only scene: the game starts in it (build index 0 on every instance)
                locationPathName = $"{BuildFolder}/{ExeName}",
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            bool ok = s.result == BuildResult.Succeeded;
            if (ok) WriteLaunchers();

            // The report also collects unrelated editor errors logged during the build (e.g. remote-control
            // requests timing out while the main thread is busy): list only the build's own.
            var errors = new StringBuilder();
            int buildErrors = 0;
            foreach (BuildStep step in report.steps)
                foreach (BuildStepMessage m in step.messages)
                    if ((m.type == LogType.Error || m.type == LogType.Exception) && !m.content.Contains("/api/"))
                    {
                        buildErrors++;
                        errors.AppendLine($"  {m.type}: {m.content}");
                    }
            var text = new StringBuilder();
            text.AppendLine($"result {s.result}, {buildErrors} build error(s), {s.totalWarnings} warning(s), " +
                            $"{s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:0} s → {Path.GetFullPath(options.locationPathName)}");
            text.Append(errors);
            File.WriteAllText($"{BuildFolder}/build_result.txt", text.ToString());
            if (ok) Debug.Log($"[Playtest] Build OK: {text}");
            else Debug.LogError($"[Playtest] Build FAILED: {text}");
            return ok;
        }

        internal static void WriteLaunchers()
        {
            const string window = "-screen-fullscreen 0 -screen-width 1280 -screen-height 720";
            File.WriteAllText($"{BuildFolder}/Play (auto host or join).bat",
                $"@echo off\r\nstart \"\" \"%~dp0{ExeName}\" {window}\r\n");
            File.WriteAllText($"{BuildFolder}/Play as Host.bat",
                $"@echo off\r\nstart \"\" \"%~dp0{ExeName}\" -host {window}\r\n");
            File.WriteAllText($"{BuildFolder}/Play as Client.bat",
                $"@echo off\r\nstart \"\" \"%~dp0{ExeName}\" -client {window}\r\n");
            File.WriteAllText($"{BuildFolder}/Two windows on this PC (host + client).bat",
                "@echo off\r\n" +
                $"start \"\" \"%~dp0{ExeName}\" -host -screen-fullscreen 0 -screen-width 960 -screen-height 600 -logFile \"%~dp0host.log\"\r\n" +
                "ping -n 13 127.0.0.1 >nul\r\n" +
                $"start \"\" \"%~dp0{ExeName}\" -client -screen-fullscreen 0 -screen-width 960 -screen-height 600 -logFile \"%~dp0client.log\"\r\n");
            // Automated check: host + client run PlaytestNetProbe (bottle, stamina, rum, walking, fire, powder) and quit.
            File.WriteAllText($"{BuildFolder}/Network self-test (2 windows).bat",
                "@echo off\r\n" +
                "set OUT=%~dp0ProbeRun\r\n" +
                "if exist \"%OUT%\" rmdir /s /q \"%OUT%\"\r\n" +
                "mkdir \"%OUT%\"\r\n" +
                "set S=selftest-%RANDOM%%RANDOM%\r\n" +
                "echo Starting host and client (session %S%) - about 1 minute, the windows close by themselves...\r\n" +
                $"start \"\" \"%~dp0{ExeName}\" -host -session %S% -probe \"%OUT%\" -screen-fullscreen 0 -screen-width 960 -screen-height 600 -logFile \"%OUT%\\host.log\"\r\n" +
                "ping -n 13 127.0.0.1 >nul\r\n" +
                $"start \"\" \"%~dp0{ExeName}\" -client -session %S% -probe \"%OUT%\" -screen-fullscreen 0 -screen-width 960 -screen-height 600 -logFile \"%OUT%\\client.log\"\r\n" +
                "set /a N=0\r\n" +
                ":wait\r\n" +
                "ping -n 4 127.0.0.1 >nul\r\n" +
                "set /a N+=1\r\n" +
                "if %N% GEQ 100 goto done\r\n" +
                "if not exist \"%OUT%\\host.txt\" goto wait\r\n" +
                "if not exist \"%OUT%\\client*.txt\" goto wait\r\n" +
                ":done\r\n" +
                "echo.\r\n" +
                "type \"%OUT%\\host.txt\" 2>nul\r\n" +
                "echo.\r\n" +
                "type \"%OUT%\\client*.txt\" 2>nul\r\n" +
                "echo.\r\n" +
                "echo Screenshots and logs: %OUT%\r\n" +
                "pause\r\n");
            File.WriteAllText($"{BuildFolder}/README.txt",
                "RumOverboard — playtest build\r\n" +
                "==============================\r\n" +
                "Starts straight in the playtest scene (brig + every mechanic). F1 in game: controls, what to test, network, reconnect.\r\n\r\n" +
                "Multiplayer: the first instance becomes the host, later ones join it (session 'rum-playtest', Photon region 'eu').\r\n" +
                "Start the second instance only after the first one is in the game.\r\n" +
                "  Play (auto host or join).bat     — normal start\r\n" +
                "  Play as Host.bat / Play as Client.bat — force the role (the client keeps retrying until a host is up)\r\n" +
                "  Two windows on this PC (host + client).bat — host, then a client 12 s later; logs: host.log / client.log\r\n" +
                "  Network self-test (2 windows).bat — automatic check: host + client, ~1 min, prints PASS/FAIL (+ screenshots)\r\n" +
                "Command line: -host | -client | -auto, -session <name> (own session for your group), -region eu|ru|us|asia|best\r\n" +
                "Friends on other PCs: the same build; everyone runs 'Play (auto ...)' with the same -session (or the default one).\r\n" +
                "Without internet / Photon the game falls back to offline single player (top line says OFFLINE).\r\n");
        }

        private static void Hidden(Object window)
        {
            var so = new SerializedObject(window);
            SerializedProperty p = so.FindProperty("visible");
            if (p != null) p.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
