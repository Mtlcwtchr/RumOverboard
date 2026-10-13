using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using RumOverboard.Gameplay.World;
using RumOverboard.Gameplay.World.Generation;
using RumOverboard.Gameplay.World.Map;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RumOverboard.EditorTools.World
{
    /// <summary>
    /// Edit-mode self test of the world generator (no play mode, no networking):
    ///   Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.World.WorldGenSelfTest.Run -logFile -
    /// For seeds 1..N: route + harbor sanity, navigable channels, berth depth, determinism, chart reveal
    /// monotonic with progress. Writes chart previews to Logs/WorldPreview. Exit code 0 = all passed.
    /// </summary>
    public static class WorldGenSelfTest
    {
        private const int Seeds = 20;
        private const string PreviewDir = "Logs/WorldPreview";

        [MenuItem("RumOverboard/World/Run Generator Self-Test", priority = 30)]
        public static void RunFromMenu() => Execute(false);

        public static void Run() => Execute(true);

        private static void Execute(bool exit)
        {
            WorldGenConfig cfg = AssetDatabase.LoadAssetAtPath<WorldGenConfig>("Assets/Source/Configs/FeatureScenes/WorldGenConfig.asset");
            if (cfg == null)
                cfg = ScriptableObject.CreateInstance<WorldGenConfig>();

            var report = new StringBuilder();
            var failures = new List<string>();
            void Check(bool ok, string what)
            {
                if (!ok)
                {
                    failures.Add(what);
                    report.AppendLine("  FAIL  " + what);
                }
            }

            Directory.CreateDirectory(PreviewDir);
            Debug.Log("[WorldGenSelfTest] start");
            int seeds = Seeds;
            string[] cmd = Environment.GetCommandLineArgs();
            int argAt = Array.IndexOf(cmd, "-worldSeeds");
            if (argAt >= 0 && argAt + 1 < cmd.Length && int.TryParse(cmd[argAt + 1], out int n))
                seeds = Mathf.Max(1, n);
            double genTotal = 0, mapTotal = 0;
            for (int seed = 1; seed <= seeds; seed++)
            {
                float wind = (seed * 47) % 360;
                var sw = Stopwatch.StartNew();
                WorldLayout layout = WorldLayoutGenerator.Generate(cfg, seed, wind, 0f);
                double gen = sw.Elapsed.TotalMilliseconds;
                genTotal += gen;
                Debug.Log($"[WorldGenSelfTest] seed {seed} generated in {gen:F0} ms ({layout.Features.Count} features)");

                Check(layout.Clusters.Count == cfg.ClusterCount, $"seed {seed}: {layout.Clusters.Count} clusters, expected {cfg.ClusterCount}");
                Check(layout.Checkpoints.Count == layout.Clusters.Count + 1, $"seed {seed}: checkpoints = clusters + 1");

                // Harbors: pier rooted on land, berth footprint in deep water, island far from the next.
                foreach (WorldCheckpoint cp in layout.Checkpoints)
                {
                    float root = layout.SampleHeight(cp.PierStart.x, cp.PierStart.y);
                    Check(root > -0.5f, $"seed {seed} cp {cp.Index}: pier root at {root:F1} m (should be ashore)");
                    Vector2 fwd = cp.HarborDir, right = cp.Right;
                    float shallowest = float.MinValue;
                    for (float a = -10.5f; a <= 10.5f; a += 1.5f)
                        for (float l = -2.7f; l <= 2.7f; l += 0.9f)
                        {
                            Vector2 p = cp.Berth + fwd * a + right * l;
                            shallowest = Mathf.Max(shallowest, layout.SampleHeight(p.x, p.y));
                        }
                    Check(shallowest < -3f, $"seed {seed} cp {cp.Index}: berth too shallow ({shallowest:F1} m)");
                    float approach = layout.SampleHeight(cp.Approach.x, cp.Approach.y);
                    Check(approach < -cfg.ChannelMinDepth * 0.8f, $"seed {seed} cp {cp.Index}: harbor approach depth {approach:F1} m");
                }

                // Every cluster has a navigable channel from berth to berth.
                foreach (WorldCluster cluster in layout.Clusters)
                {
                    float total = WorldGeometry.PolylineLength(cluster.Channel);
                    float worst = float.MinValue;
                    Vector2 worstAt = default;
                    for (float s = 0f; s <= total; s += 4f)
                    {
                        Vector2 p = WorldGeometry.PointAt(cluster.Channel, s / total, out _);
                        float h = layout.SampleHeight(p.x, p.y);
                        if (h > worst)
                        {
                            worst = h;
                            worstAt = p;
                        }
                    }
                    Check(worst <= -cfg.ChannelMinDepth * 0.85f,
                        $"seed {seed} cluster {cluster.Index}: channel shoals to {worst:F1} m at {worstAt} — {Culprit(layout, worstAt)}");
                    if (worst > -cfg.ChannelMinDepth * 0.85f)
                        report.AppendLine("        channel: " + string.Join(" ", cluster.Channel.ConvertAll(v => $"({v.x:F0},{v.y:F0})")) +
                                          $"  from island ({layout.Checkpoints[cluster.Index].IslandCenter.x:F0},{layout.Checkpoints[cluster.Index].IslandCenter.y:F0})");
                }

                // Hazards scale with difficulty.
                WorldCluster first = layout.Clusters[0], last = layout.Clusters[layout.Clusters.Count - 1];
                int hazardsFirst = first.Rocks + first.Reefs + first.Islands + first.Narrows;
                int hazardsLast = last.Rocks + last.Reefs + last.Islands + last.Narrows;
                if (layout.Clusters.Count > 2)
                    Check(hazardsLast > hazardsFirst, $"seed {seed}: last cluster ({hazardsLast} hazards) not harder than first ({hazardsFirst})");
                if (layout.Clusters.Count > 2)
                    Check(last.ChannelHalfWidth < first.ChannelHalfWidth, $"seed {seed}: channel doesn't narrow with difficulty");

                // Determinism.
                WorldLayout again = WorldLayoutGenerator.Generate(cfg, seed, wind, 0f);
                bool same = again.Features.Count == layout.Features.Count && again.Carves.Count == layout.Carves.Count;
                for (int i = 0; same && i < layout.Features.Count; i++)
                    same = again.Features[i].Center == layout.Features[i].Center && again.Features[i].Radius == layout.Features[i].Radius;
                for (int i = 0; same && i < layout.Checkpoints.Count; i++)
                    same = again.Checkpoints[i].Berth == layout.Checkpoints[i].Berth;
                for (int i = 0; same && i < 200; i++)
                {
                    float x = Mathf.Lerp(layout.Bounds.xMin, layout.Bounds.xMax, (i * 0.618f) % 1f);
                    float z = Mathf.Lerp(layout.Bounds.yMin, layout.Bounds.yMax, (i * 0.381f) % 1f);
                    same = again.SampleHeight(x, z) == layout.SampleHeight(x, z);
                }
                Check(same, $"seed {seed}: generation is not deterministic");

                // Chart: fog lifts monotonically with progress, and fully at the end.
                sw.Restart();
                var painter = new WorldMapPainter(layout, cfg, false);
                double baseMs = sw.Elapsed.TotalMilliseconds;
                painter.Compose(0);
                Debug.Log($"[WorldGenSelfTest] seed {seed} chart {painter.Width}×{painter.Height}: base {baseMs:F0} ms, compose {sw.Elapsed.TotalMilliseconds - baseMs:F0} ms");
                float prev = -1f;
                float initial = 0f;
                for (int r = 0; r <= layout.ClusterCount; r++)
                {
                    painter.Compose(r);
                    if (r == 0)
                    {
                        initial = painter.RevealedFraction;
                        if (seed <= 4)
                            SavePng(painter, $"{PreviewDir}/seed{seed}_start.png");
                    }
                    Check(painter.RevealedFraction >= prev - 1e-4f, $"seed {seed}: reveal shrank at progress {r}");
                    prev = painter.RevealedFraction;
                }
                Check(initial > 0.02f && initial < 0.6f, $"seed {seed}: initial reveal {initial:P0} (expected only the first leg)");
                Check(prev > initial + 0.2f, $"seed {seed}: final reveal {prev:P0} barely above initial {initial:P0}");
                if (seed <= 4)
                {
                    painter.Compose(1);
                    SavePng(painter, $"{PreviewDir}/seed{seed}_reached1.png");
                    painter.Compose(0, true, true);
                    SavePng(painter, $"{PreviewDir}/seed{seed}_all.png");
                }
                painter.Dispose();
                double map = sw.Elapsed.TotalMilliseconds;
                mapTotal += map;

                int land = 0, hidden = 0, reefs = 0;
                foreach (WorldFeature f in layout.Features)
                {
                    if (f.Kind == WorldFeatureKind.Reef) reefs++;
                    else if (f.IsHidden) hidden++;
                    else if (f.IsLand) land++;
                }
                report.AppendLine($"seed {seed,2}: {layout.Bounds.width:F0}×{layout.Bounds.height:F0} m, {layout.Features.Count} features " +
                                  $"({land} land, {hidden} hidden rocks, {reefs} reef patches), gen {gen:F0} ms, chart {map:F0} ms, " +
                                  $"reveal {initial:P0}→{prev:P0}");
                Debug.Log($"[WorldGenSelfTest] seed {seed} done: gen {gen:F0} ms, chart {map:F0} ms, {failures.Count} failures so far");
            }

            report.AppendLine($"avg generate {genTotal / seeds:F0} ms, avg chart (all progress steps) {mapTotal / seeds:F0} ms");
            report.AppendLine(failures.Count == 0 ? "RESULT: ALL PASSED" : $"RESULT: {failures.Count} FAILED");
            Debug.Log("[WorldGenSelfTest]\n" + report);
            if (exit)
                EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static string Culprit(WorldLayout layout, Vector2 p)
        {
            float best = float.MinValue;
            int bestIndex = -1;
            for (int i = 0; i < layout.Features.Count; i++)
                if (layout.Features[i].TryEvaluate(p.x, p.y, layout.SeaFloorDepth, out float h) && h > best)
                {
                    best = h;
                    bestIndex = i;
                }
            if (bestIndex < 0)
                return "seabed";
            WorldFeature f = layout.Features[bestIndex];
            return $"feature #{bestIndex} {f.Kind} (cluster {f.Cluster}, r {f.Radius:F0}, stretch {f.Stretch:F1}, " +
                   $"outline {f.OutlineRadius:F0}, hazard {f.HazardRadius:F0}, dist {Vector2.Distance(p, f.Center):F0}, h {best:F1})";
        }

        private static void SavePng(WorldMapPainter painter, string path)
        {
            var tex = new Texture2D(painter.Width, painter.Height, TextureFormat.RGBA32, false);
            tex.SetPixels32(painter.Pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
