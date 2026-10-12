#if UNITY_EDITOR && FUSION2
using System;
using RumOverboard.EditorTools.ShipSandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing.Editor
{
    public static class RowingFeatureSetup
    {
        [MenuItem("RumOverboard/Feature Scenes/Build Rowing Scene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ValidateRhythm();
            ShipSandboxSetup.EnsureLayers();
            ShipSandboxSetup.PrepareShipPrefab();
            ShipSandboxSetup.BuildScene(ShipSandboxSetup.RowingScenePath, "Calm", ShipSandboxSetup.DebugRowing);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("RumOverboard/Feature Scenes/Open Rowing Scene")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ShipSandboxSetup.RowingScenePath);
        }

        // Batch entry creates the concrete assets, then runs the isolated play-mode scenario.
        public static void BuildAndTest()
        {
            try { Build(); EditorApplication.EnterPlaymode(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void RunTests()
        {
            ValidateRhythm();
            EditorSceneManager.OpenScene(ShipSandboxSetup.RowingScenePath);
            EditorApplication.EnterPlaymode();
        }

        public static void RunShipRegression()
        {
            RumOverboard.EditorTools.World.WorldGenSelfTest.RunFromMenu();
            ShipSandboxSmokeTest.Run();
        }

        public static void BuildAndCheckDrift()
        {
            ValidateRhythm();
            ShipSandboxSetup.PrepareShipPrefab();
            AssetDatabase.SaveAssets();
            ShipSandboxSmokeTest.Run();
        }

        [MenuItem("RumOverboard/Feature Scenes/Validate Rowing Rhythm")]
        public static void ValidateRhythm()
        {
            var cfg = ScriptableObject.CreateInstance<RowingConfig>();
            try
            {
                var t = new RowTiming(cfg, 1f / 60f);
                OarState s = default;
                Require(RowingRhythm.Press(ref s, 1, 100, t, cfg) == RowFeedback.Start, "first stroke");
                byte startPower = s.Power;
                Require(RowingRhythm.Press(ref s, 1, 101, t, cfg) == RowFeedback.Early && s.Power < startPower, "spam penalty");
                Require(RowingRhythm.Press(ref s, -1, 110, t, cfg) == RowFeedback.Backing && s.Sign == -1 && s.Streak == 0, "reverse");
                Require(RowingRhythm.Press(ref s, -1, 110 + t.Period - t.Early, t, cfg) == RowFeedback.InTime && s.Streak == 1, "early window");
                int queued = s.StrokeTick;
                Require(RowingRhythm.Press(ref s, -1, queued - 1, t, cfg) == RowFeedback.None && s.StrokeTick == queued, "one queued stroke");
                Require(RowingRhythm.InCycle(s, queued - 1, t), "queued recovery stays active");
                Require(RowingRhythm.Press(ref s, -1, queued + t.Period + t.Late + 1, t, cfg) == RowFeedback.Late && s.Streak == 0, "late restart");
                for (int i = 0; i < 10; i++) RowingRhythm.Press(ref s, -1, s.StrokeTick + t.Period, t, cfg);
                Require(s.Power == 100, "streak capped at full power");
                // Config permits a wide early window during the drive: it must not truncate the old force.
                cfg.DriveFraction = 0.9f; cfg.EarlyWindow = 0.8f;
                t = new RowTiming(cfg, 1f / 60f);
                s = default;
                RowingRhythm.Press(ref s, 1, 100, t, cfg);
                int press = 100 + t.Period - t.Early;
                float force = RowingRhythm.DriveShape(s, press, t) * RowingRhythm.ActivePower(s, press);
                RowingRhythm.Press(ref s, 1, press, t, cfg);
                Require(force > 0f && Mathf.Abs(force - RowingRhythm.DriveShape(s, press, t) * RowingRhythm.ActivePower(s, press)) < 0.0001f,
                    "queue preserves current thrust and power");
                Debug.Log("[RowingTest] Rhythm: ALL PASSED (start, spam, reverse, timing, queue, cap, continuous drive)");
            }
            finally { UnityEngine.Object.DestroyImmediate(cfg); }
        }

        private static void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Rowing rhythm failed: " + name);
        }
    }
}
#endif
