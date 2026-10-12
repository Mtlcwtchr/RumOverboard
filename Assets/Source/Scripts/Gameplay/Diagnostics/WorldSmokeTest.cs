#if FUSION2
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fusion;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.World;
using RumOverboard.Gameplay.World.Generation;
using RumOverboard.Gameplay.World.Map;
using RumOverboard.Gameplay.World.Mooring;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Diagnostics
{
    /// <summary>
    /// Play-mode scenario for the world map scene (flag <c>-worldSmoke</c>; runner
    /// WorldMapSceneSetup.RunSmoke). Offline host: world built, ship spawned moored at the start berth
    /// and holding there, chart table on deck, bollard cast-off / make-fast, arrival at checkpoint 1
    /// lifts the fog, regenerate. Exits the editor with 0 (all passed) or 1.
    /// </summary>
    public sealed class WorldSmokeTest : MonoBehaviour
    {
        private const string Flag = "-worldSmoke";

        private readonly StringBuilder _report = new();
        private readonly List<string> _failures = new();
        private NetworkInputData _input;
        private NetworkPlayer _player;
        private NetworkShip _ship;
        private NetworkWorld _world;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains(Flag))
                return;
            var cm = FindAnyObjectByType<ConnectionManager>();
            cm?.OverrideAutoStart(GameMode.Single);
            new GameObject("WorldSmokeTest").AddComponent<WorldSmokeTest>();
        }

        private IEnumerator Start()
        {
            Log("== World map smoke test ==");
            float deadline = Time.realtimeSinceStartup + 60f;
            while ((_player == null || _ship == null || _world == null || !_world.IsReady) && Time.realtimeSinceStartup < deadline)
            {
                _player = NetworkPlayer.All.FirstOrDefault(p => p != null && p.HasInputAuthority);
                _ship = NetworkShip.All.FirstOrDefault();
                _world = NetworkWorld.Instance;
                yield return null;
            }
            if (_player == null || _ship == null || _world == null || !_world.IsReady)
            {
                Fail($"spawn: player={_player != null} ship={_ship != null} world={_world != null} ready={_world != null && _world.IsReady}");
                Finish();
                yield break;
            }
            _player.InputOverride = () => _input;
            Log($"world seed {_world.Seed}: {_world.Layout.ClusterCount} clusters, {_world.Layout.Features.Count} features, " +
                $"gen {_world.GenerateMilliseconds:F0} ms, build {_world.BuildMilliseconds:F0} ms, chart {_world.MapMilliseconds:F0} ms");

            yield return Run(Built());
            yield return Run(MooredAtStart());
            yield return Run(Shots());
            yield return Run(ChartTableOnDeck());
            yield return Run(Bollard());
            yield return Run(ArriveAtCheckpoint());
            yield return Run(Regenerate());
            Finish();
        }

        private IEnumerator Built()
        {
            WorldLayout layout = _world.Layout;
            Check(layout.Checkpoints.Count == layout.ClusterCount + 1, "world: checkpoints = clusters + 1");
            Check(_world.Config != null && _world.Config.TerrainMaterial != null && _world.Config.LampMaterial != null,
                "world: WorldGenConfig asset linked (materials survive player builds)");
            Check(_world.BuildResult != null && _world.BuildResult.Chunks > 0 && _world.BuildResult.Colliders > 0,
                $"world: geometry built ({_world.BuildResult?.Chunks} chunks, {_world.BuildResult?.Colliders} colliders)");
            Check(_world.Map != null && _world.Map.Texture != null, "world: chart texture painted");
            float r0 = _world.Map != null ? _world.Map.RevealedFraction : 0f;
            Log($"world: chart {_world.Map?.Width}×{_world.Map?.Height}, revealed {r0:P0}");
            Check(r0 > 0.02f && r0 < 0.6f, "world: chart starts mostly fogged");
            Check(_world.BuildResult != null && _world.BuildResult.Bollards.Count == layout.Checkpoints.Count * 2, "world: two bollards per harbor");
            yield break;
        }

        private IEnumerator MooredAtStart()
        {
            yield return Wait(1f);
            WorldCheckpoint start = _world.Layout.Checkpoints[0];
            Vector2 xz = Xz(_ship.Body.position);
            Log($"start: ship {Vector2.Distance(xz, start.Berth):F2} m from berth, moored at {_world.MooredAt}");
            Check(Vector2.Distance(xz, start.Berth) < 3f, "start: ship spawned at the start berth");
            Check(_world.MooredAt == 0, "start: ship is moored at the start harbor");

            float maxDrift = 0f, minUp = 1f;
            for (float t = 0f; t < 10f; t += Time.deltaTime)
            {
                maxDrift = Mathf.Max(maxDrift, Vector2.Distance(Xz(_ship.Body.position), start.Berth));
                minUp = Mathf.Min(minUp, _ship.transform.up.y);
                yield return null;
            }
            float depth = _world.Layout.SampleHeight(start.Berth.x, start.Berth.y);
            Log($"start: drift over 10 s max {maxDrift:F2} m, min up.y {minUp:F3}, seabed {depth:F1} m");
            Check(maxDrift < 3f, "start: mooring lines hold the ship at the berth");
            Check(minUp > 0.9f, "start: ship stays upright alongside");
            Check(depth < -3f, "start: deep water under the berth");
        }

        /// <summary>Optional (<c>-worldShots dir</c>): renders a few vantage points of the built world to PNG.</summary>
        private IEnumerator Shots()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-worldShots");
            if (at < 0 || at + 1 >= args.Length)
                yield break;
            string dir = args[at + 1];
            System.IO.Directory.CreateDirectory(dir);

            WorldLayout layout = _world.Layout;
            float sea = _world.SeaLevel;
            var shots = new List<(string name, Vector3 eye, Vector3 target)>();
            WorldCheckpoint start = layout.Checkpoints[0];
            Vector3 berth = new(start.Berth.x, sea, start.Berth.y);
            Vector3 harbor = new(start.HarborDir.x, 0f, start.HarborDir.y);
            Vector3 side = Vector3.Cross(Vector3.up, harbor);
            shots.Add(("berth", berth + harbor * 45f + side * 25f + Vector3.up * 18f, berth));
            WorldCheckpoint next = layout.Checkpoints[1];
            shots.Add(("deck_view", berth + Vector3.up * 7f, new Vector3(next.IslandCenter.x, sea + 10f, next.IslandCenter.y)));
            shots.Add(("start_island", berth + harbor * 260f + side * 120f + Vector3.up * 140f,
                new Vector3(start.IslandCenter.x, sea, start.IslandCenter.y)));
            for (int i = 1; i < layout.Checkpoints.Count; i += Mathf.Max(1, layout.Checkpoints.Count - 2))
            {
                WorldCheckpoint cp = layout.Checkpoints[i];
                Vector3 b = new(cp.Berth.x, sea, cp.Berth.y);
                Vector3 h = new(cp.HarborDir.x, 0f, cp.HarborDir.y);
                shots.Add(($"approach_{i}", b + h * 320f + Vector3.up * 70f, b));
            }

            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            // The real game camera (URP data, depth texture for shore foam); pose restored after each render.
            Camera cam = Camera.main;
            if (cam == null)
                yield break;
            var surface = FindAnyObjectByType<RumOverboard.Gameplay.Ocean.OceanSurfaceRenderer>();
            foreach ((string name, Vector3 eye, Vector3 target) in shots)
            {
                yield return null;
                Transform t = cam.transform;
                t.GetPositionAndRotation(out Vector3 oldPos, out Quaternion oldRot);
                RenderTexture oldTarget = cam.targetTexture;
                t.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                if (surface != null)
                    surface.transform.position = new Vector3(eye.x, surface.transform.position.y, eye.z);
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = oldTarget;
                t.SetPositionAndRotation(oldPos, oldRot);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"world_{name}.png"), tex.EncodeToPNG());
            }
            Log($"shots: {shots.Count} PNGs in {dir}");
            Destroy(rt);
            Destroy(tex);
        }

        private IEnumerator ChartTableOnDeck()
        {
            ChartTable table = _ship.GetComponentInChildren<ChartTable>();
            Check(table != null, "chart: table attached to the ship");
            if (table == null) yield break;
            Check(table.Ref.IsValid, "chart: table resolves as an interactable");
            Check(table.IsAvailable(_player.LocalInteractor), "chart: table usable");
            ChartViewer viewer = _world.Viewer;
            Check(viewer != null, "chart: viewer present");
            if (viewer == null) yield break;
            viewer.Open(table);
            Check(viewer.IsOpen && UiInputCapture.Active, "chart: opening captures input");
            Check(viewer.Ship == _ship, "chart: the viewer reads the table's ship");
            // The player isn't at the table, so the viewer closes next frame: check the log read on open.
            Vector3 f = _ship.transform.forward;
            float heading = Mathf.Repeat(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 360f);
            Check(Mathf.Abs(Mathf.DeltaAngle(viewer.Snapshot.HeadingDeg, heading)) < 0.5f,
                $"chart: log shows the ship's course ({viewer.Snapshot.HeadingDeg:F0}° vs {heading:F0}°)");
            yield return Wait(0.3f);
            f = _ship.transform.forward;
            heading = Mathf.Repeat(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 360f);
            Texture2D icon = ChartViewer.ShipIcon.Texture;
            Check(icon != null && icon.GetPixel(0, 0).a < 0.01f && icon.GetPixel(32, 32).a > 0.99f && icon.GetPixel(32, 58).a > 0.5f && icon.GetPixel(32, 63).a < 0.01f,
                "chart: ship icon is a hull, bow up, on a clear background");

            Transform marker = table.Marker;
            Check(marker != null && marker.GetComponent<Renderer>().enabled, "chart: ship marker on the table's chart");
            if (marker != null)
            {
                Vector3 p = _ship.transform.position;
                Vector2 uv = _world.Map.WorldToUv(new Vector2(p.x, p.z));
                Transform chart = table.transform.Find("Chart");
                Vector3 local = marker.localPosition - chart.localPosition;
                Vector2 got = new(local.x / chart.localScale.x + 0.5f, local.z / chart.localScale.y + 0.5f);
                Check(Vector2.Distance(got, uv) < 0.01f, $"chart: marker sits at the ship's spot on the chart ({got} vs {uv})");
                Check(Mathf.Abs(Mathf.DeltaAngle(marker.localEulerAngles.y, heading)) < 1f, "chart: marker points along the ship's heading");
            }
            viewer.Close();
            Check(!viewer.IsOpen && !UiInputCapture.Active, "chart: closing releases input");
        }

        private IEnumerator Bollard()
        {
            WorldCheckpoint start = _world.Layout.Checkpoints[0];
            MooringBollard bollard = _world.BuildResult.Bollards[1].GetComponent<MooringBollard>();
            Check(bollard != null, "bollard: has the interaction");
            if (bollard == null) yield break;
            Check(bollard.Ref.IsValid, "bollard: resolves as an interactable");

            // Stand on the pier next to the stern bollard, facing it.
            Vector3 b = bollard.transform.position;
            Vector2 inward = -start.Right;
            float deck = _world.SeaLevel + _world.Config.PierDeckHeight;
            var stand = new Vector3(b.x + inward.x * 1.1f, deck + 0.15f, b.z + inward.y * 1.1f);
            yield return PlaceAt(stand, Quaternion.LookRotation(new Vector3(-inward.x, 0f, -inward.y), Vector3.up));
            Log($"bollard: player at {_player.transform.position} (pier deck {deck:F2}), prompt '{bollard.GetPrompt(_player.LocalInteractor)}'");

            yield return Press(bollard.Ref);
            Check(_world.MooredAt == -1, "bollard: E casts off");
            yield return Wait(0.5f);
            yield return Press(bollard.Ref);
            Check(_world.MooredAt == 0, "bollard: E makes fast again");
        }

        private IEnumerator ArriveAtCheckpoint()
        {
            float before = _world.Map.RevealedFraction;
            _world.HostTeleport(1, true);
            yield return Wait(1.5f);
            WorldCheckpoint c1 = _world.Layout.Checkpoints[1];
            Check(Vector2.Distance(Xz(_ship.Body.position), c1.Approach) < 6f, "arrive: teleported to checkpoint 1's approach");
            Check(_world.Reached == 0 && _world.MooredAt == -1, "arrive: approach is outside the arrival radius, under way");
            float depth = _world.Layout.SampleHeight(c1.Approach.x, c1.Approach.y);
            Check(depth < -3f, $"arrive: deep water at the approach ({depth:F1} m)");

            _world.HostTeleport(1, false);
            yield return Wait(1f);
            float after = _world.Map.RevealedFraction;
            Log($"arrive: reached {_world.Reached}, moored at {_world.MooredAt}, chart revealed {before:P0} → {after:P0}, ship cluster {_world.ShipCluster}");
            Check(_world.Reached == 1, "arrive: reaching checkpoint 1's harbor counts");
            Check(_world.MooredAt == 1, "arrive: made fast at checkpoint 1");
            Check(_world.Map.ComposedReached == 1 && after > before + 0.03f, "arrive: the chart opens the next leg");
        }

        private IEnumerator Regenerate()
        {
            int seed = _world.Seed == 4242 ? 4243 : 4242;
            _world.HostRegenerate(seed);
            yield return Wait(2f);
            WorldCheckpoint start = _world.Layout.Checkpoints[0];
            Log($"regenerate: seed {_world.Seed}, gen {_world.Generation}, ship {Vector2.Distance(Xz(_ship.Body.position), start.Berth):F2} m from new berth");
            Check(_world.Seed == seed && _world.IsReady, "regenerate: new world built");
            Check(_world.Reached == 0 && _world.Map.ComposedReached == 0, "regenerate: progress and chart reset");
            Check(Vector2.Distance(Xz(_ship.Body.position), start.Berth) < 3f && _world.MooredAt == 0,
                "regenerate: ship moored at the new start berth");
        }

        // ---------------------------------------------------------------------------------------
        private static Vector2 Xz(Vector3 p) => new(p.x, p.z);

        private IEnumerator PlaceAt(Vector3 position, Quaternion rotation)
        {
            _input = default;
            var rb = _player.GetComponent<Rigidbody>();
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = Vector3.zero;
            _player.transform.SetPositionAndRotation(position, rotation);
            yield return Wait(0.8f);
        }

        private IEnumerator Press(InteractableRef target)
        {
            _input = default;
            _input.Target = target;
            _input.Buttons.Set(NetworkInputData.ButtonInteract, true);
            yield return Wait(0.15f);
            _input.Buttons.Set(NetworkInputData.ButtonInteract, false);
            yield return Wait(0.25f);
            _input = default;
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
                yield return null;
        }

        private IEnumerator Run(IEnumerator step)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!step.MoveNext()) yield break;
                    current = step.Current;
                }
                catch (Exception e)
                {
                    Fail($"exception: {e}");
                    yield break;
                }
                yield return current;
            }
        }

        private void Check(bool ok, string what)
        {
            Log((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) _failures.Add(what);
        }

        private void Fail(string what) => Check(false, what);
        private void Log(string line) => _report.AppendLine(line);

        private void Finish()
        {
            _report.AppendLine(_failures.Count == 0 ? "RESULT: ALL PASSED" : $"RESULT: {_failures.Count} FAILED");
            Debug.Log("[WorldSmoke]\n" + _report);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(_failures.Count == 0 ? 0 : 1);
#endif
        }
    }
}
#endif
