using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.EditorTools.ShipSandbox
{
    /// <summary>
    /// Batch runners for the sandbox play-mode scenarios.
    ///
    ///   Offline (host-side logic):
    ///     Unity -batchmode -projectPath . -executeMethod ...ShipSandboxSmokeTest.Run -sandboxSmoke
    ///   Real host + client over Photon:
    ///     1. Unity -batchmode -projectPath . -executeMethod ...ShipSandboxSmokeTest.BuildPlayer -smokeOut &lt;dir&gt;
    ///     2. &lt;dir&gt;/SandboxSmoke.app/Contents/MacOS/* -batchmode -sandboxSmokeHost &lt;session&gt;
    ///     3. Unity -batchmode -projectPath . -executeMethod ...ShipSandboxSmokeTest.Run -sandboxSmokeClient &lt;session&gt;
    /// The editor exits with 0 when every check passes.
    /// </summary>
    public static class ShipSandboxSmokeTest
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(ShipSandboxSetup.ScenePath);
            EditorApplication.EnterPlaymode();
        }

        public static void BuildPlayer()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-smokeOut");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Builds/SandboxSmoke";

            var options = new BuildPlayerOptions
            {
                // Same enabled-scene list/order as the editor so build indices (what Fusion
                // replicates as SceneRef) match between the player host and the editor client.
                scenes = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(
                    System.Linq.Enumerable.Where(EditorBuildSettings.scenes, s => s.enabled), s => s.path)),
                locationPathName = System.IO.Path.Combine(dir, "SandboxSmoke.app"),
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[SmokeBuild] {report.summary.result} → {options.locationPathName} ({report.summary.totalTime})");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
