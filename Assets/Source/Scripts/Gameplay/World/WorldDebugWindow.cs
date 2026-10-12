#if FUSION2
using System.IO;
using RumOverboard.Gameplay.Ocean.FeatureScenes;
using RumOverboard.Gameplay.World.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.World
{
    /// <summary>
    /// World generation debug window (F6; frees the cursor while open): status, live chart preview,
    /// host controls (regenerate / progress / teleport / mooring), generation tuning,
    /// reset-from-config and save-to-config, chart PNG export.
    /// Generation sliders are local: press "Rebuild locally" (or regenerate as host) to apply.
    /// Every peer must run the same config, otherwise clients log a checksum mismatch.
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class WorldDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 520f, 780f);

        private const int WindowId = 0x0BEEF2A6;
        private Vector2 _scroll;
        private WorldGenConfig _snapshot;
        private string _seedText = "0";
        private int _teleportTarget = 1;
        private string _status;

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f6Key.wasPressedThisFrame)
                visible = !visible;
            RumOverboard.Gameplay.UiInputCapture.SetDebugWindow(this, visible);
        }

        private void OnDisable() => RumOverboard.Gameplay.UiInputCapture.SetDebugWindow(this, false);

        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;
            if (!visible)
            {
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 68f, 180f, 22f), "F6 — World debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "World generation — F6");
        }

        private void Draw(int id)
        {
            NetworkWorld world = NetworkWorld.Instance;
            WorldGenConfig cfg = world != null ? world.Config : WorldGenConfig.Active;
            if (cfg == null)
            {
                GUILayout.Label("No WorldGenConfig.");
                GUI.DragWindow();
                return;
            }
            if (_snapshot == null)
            {
                _snapshot = ScriptableObject.CreateInstance<WorldGenConfig>();
                _snapshot.CopyFrom(cfg);
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (world == null || !world.IsReady)
            {
                GUILayout.Label(world == null ? "(no NetworkWorld in scene)" : "(world not built yet — start a session)");
            }
            else
            {
                DrawStatus(world);
                DrawHostControls(world, cfg);
            }

            DrawTuning(cfg);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild locally") && world != null)
                world.LocalRebuild();
            if (GUILayout.Button("Reset from config"))
                cfg.CopyFrom(_snapshot);
            if (GUILayout.Button("Save to config"))
            {
                _snapshot.CopyFrom(cfg);
                FeatureSceneConfigPersistence.SaveAsset(cfg);
                _status = "Config saved.";
            }
            if (GUILayout.Button("Hide (F6)"))
                visible = false;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private void DrawStatus(NetworkWorld world)
        {
            WorldLayout layout = world.Layout;
            int cluster = Mathf.Clamp(world.ShipCluster, 0, layout.ClusterCount - 1);
            string moored = world.MooredAt >= 0 ? $"moored at {world.MooredAt}" : "under way";
            GUILayout.Label($"seed {world.Seed}   gen {world.Generation}   wind {world.WindDegrees:F0}°   {(world.IsHost ? "HOST" : "client")}" +
                            (world.ChecksumMismatch ? "   <color=red>CHECKSUM MISMATCH</color>" : ""));
            GUILayout.Label($"reached {world.Reached}/{layout.Checkpoints.Count - 1}   ship cluster {cluster} " +
                            $"(difficulty {layout.Clusters[cluster].Difficulty:F2})   {moored}");
            GUILayout.Label($"{layout.Features.Count} features, {layout.Carves.Count} carves, " +
                            $"{world.BuildResult?.Chunks ?? 0} chunks / {world.BuildResult?.Colliders ?? 0} colliders");
            GUILayout.Label($"generate {world.GenerateMilliseconds:F0} ms, build {world.BuildMilliseconds:F0} ms, " +
                            $"chart {world.MapMilliseconds:F0} ms, revealed {world.Map.RevealedFraction:P0}");
            WorldCluster c = layout.Clusters[cluster];
            GUILayout.Label($"cluster {cluster}: channel ±{c.ChannelHalfWidth:F0} m, islands {c.Islands}, rocks {c.Rocks}, " +
                            $"reefs {c.Reefs}, narrows {c.Narrows}{(c.ApproachReef ? ", reef ring" : "")}{(c.ApproachCliffs ? ", cliff gate" : "")}");

            if (world.Map != null && world.Map.Texture != null)
            {
                float aspect = world.Map.Width / (float)world.Map.Height;
                float h = 300f, w = h * aspect;
                if (w > 480f)
                {
                    w = 480f;
                    h = w / aspect;
                }
                Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(r, world.Map.Texture, ScaleMode.ScaleToFit, false);
                // Ship marker (debug only — the real chart has none).
                NetworkShipMarker(world, r);
            }
            GUILayout.BeginHorizontal();
            world.RevealAllLocal = GUILayout.Toggle(world.RevealAllLocal, "Reveal all (local)");
            world.DrawChannelsLocal = GUILayout.Toggle(world.DrawChannelsLocal, "Draw channels (local)");
            if (GUILayout.Button("Export PNG"))
                ExportPng(world);
            GUILayout.EndHorizontal();
        }

        private static void NetworkShipMarker(NetworkWorld world, Rect r)
        {
            var ship = world.FindShip();
            if (ship == null)
                return;
            Vector3 p = ship.transform.position;
            Vector2 uv = world.Map.WorldToUv(new Vector2(p.x, p.z));
            var c = new Vector2(r.x + uv.x * r.width, r.y + (1f - uv.y) * r.height);
            Color old = GUI.color;
            GUI.color = Color.magenta;
            GUI.DrawTexture(new Rect(c.x - 3f, c.y - 3f, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawHostControls(NetworkWorld world, WorldGenConfig cfg)
        {
            bool host = world.IsHost;
            GUILayout.Label(host ? "<b>Host controls</b>" : "<b>Host controls</b> (host only)");
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && host;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed (0 = random)", GUILayout.Width(120f));
            _seedText = GUILayout.TextField(_seedText, GUILayout.Width(110f));
            if (GUILayout.Button("Regenerate"))
            {
                int.TryParse(_seedText, out int seed);
                world.HostRegenerate(seed);
            }
            if (GUILayout.Button("Same seed"))
                world.HostRegenerate(world.Seed);
            GUILayout.EndHorizontal();

            int last = world.Layout.Checkpoints.Count - 1;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Checkpoint {_teleportTarget}", GUILayout.Width(100f));
            _teleportTarget = Mathf.RoundToInt(GUILayout.HorizontalSlider(_teleportTarget, 0, last, GUILayout.Width(140f)));
            if (GUILayout.Button("→ approach"))
                world.HostTeleport(_teleportTarget, true);
            if (GUILayout.Button("→ berth"))
                world.HostTeleport(_teleportTarget, false);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reached −1"))
                world.HostSetReached(world.Reached - 1);
            if (GUILayout.Button("Reached +1"))
                world.HostSetReached(world.Reached + 1);
            if (GUILayout.Button("Reset progress"))
                world.HostResetProgress();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cast off"))
                world.HostCastOff();
            if (GUILayout.Button("Moor at nearest berth"))
                _status = world.HostMoorNearest() ? "Moored." : $"No berth within {cfg.MooringReach:F0} m.";
            GUILayout.EndHorizontal();
            GUI.enabled = wasEnabled;
        }

        private static void DrawTuning(WorldGenConfig cfg)
        {
            GUILayout.Label("<b>Route</b>");
            cfg.Seed = IntSlider("Config seed (0 = random)", cfg.Seed, 0, 9999);
            cfg.ClusterCount = IntSlider("Clusters", cfg.ClusterCount, 1, 12);
            cfg.LegLengthEasy = Slider("Leg length easy m", cfg.LegLengthEasy, 300f, 2000f);
            cfg.LegLengthHard = Slider("Leg length hard m", cfg.LegLengthHard, 300f, 2500f);
            cfg.RouteAngleToWind = Slider("Route ∠ wind °", cfg.RouteAngleToWind, 0f, 180f);
            cfg.LegTurnMax = Slider("Leg turn max °", cfg.LegTurnMax, 0f, 90f);

            GUILayout.Label("<b>Channels</b>");
            cfg.ChannelHalfWidthEasy = Slider("Half-width easy m", cfg.ChannelHalfWidthEasy, 20f, 250f);
            cfg.ChannelHalfWidthHard = Slider("Half-width hard m", cfg.ChannelHalfWidthHard, 12f, 150f);
            cfg.ChannelWiggleEasy = Slider("Wiggle easy m", cfg.ChannelWiggleEasy, 0f, 300f);
            cfg.ChannelWiggleHard = Slider("Wiggle hard m", cfg.ChannelWiggleHard, 0f, 400f);
            cfg.ChannelMinDepth = Slider("Min depth m", cfg.ChannelMinDepth, 2f, 15f);
            cfg.ClusterHalfWidth = Slider("Cluster half-width m", cfg.ClusterHalfWidth, 120f, 800f);

            GUILayout.Label("<b>Hazards (easy → hard)</b>");
            cfg.IslandsEasy = IntSlider("Islands easy", cfg.IslandsEasy, 0, 12);
            cfg.IslandsHard = IntSlider("Islands hard", cfg.IslandsHard, 0, 12);
            cfg.RocksEasy = IntSlider("Rocks easy", cfg.RocksEasy, 0, 40);
            cfg.RocksHard = IntSlider("Rocks hard", cfg.RocksHard, 0, 40);
            cfg.ReefsEasy = IntSlider("Reefs easy", cfg.ReefsEasy, 0, 30);
            cfg.ReefsHard = IntSlider("Reefs hard", cfg.ReefsHard, 0, 30);
            cfg.NarrowsFrom = Slider("Narrows from difficulty", cfg.NarrowsFrom, 0f, 1f);
            cfg.ApproachReefFrom = Slider("Approach reef from", cfg.ApproachReefFrom, 0f, 1f);
            cfg.ApproachCliffsFrom = Slider("Approach cliffs from", cfg.ApproachCliffsFrom, 0f, 1f);

            GUILayout.Label("<b>Chart</b>");
            cfg.FogEdge = Slider("Fog edge m", cfg.FogEdge, 5f, 200f);
            cfg.CheckpointRevealRadius = Slider("Checkpoint reveal m", cfg.CheckpointRevealRadius, 60f, 600f);

            GUILayout.Label("<b>Mooring / progress (live)</b>");
            cfg.MooringStiffness = Slider("Stiffness 1/s²", cfg.MooringStiffness, 0.05f, 4f);
            cfg.MooringDamping = Slider("Damping 1/s", cfg.MooringDamping, 0f, 5f);
            cfg.MooringMaxAccel = Slider("Max accel m/s²", cfg.MooringMaxAccel, 0.2f, 10f);
            cfg.MooringSlack = Slider("Slack m", cfg.MooringSlack, 0f, 3f);
            cfg.MooringReach = Slider("Mooring reach m", cfg.MooringReach, 4f, 40f);
            cfg.HarborReachRadius = Slider("Arrival radius m", cfg.HarborReachRadius, 20f, 200f);
            cfg.WindScaleHard = Slider("Wind × at hardest", cfg.WindScaleHard, 0.5f, 3f);
        }

        private void ExportPng(NetworkWorld world)
        {
            if (world.Map == null || world.Map.Texture == null)
                return;
            string dir = Path.Combine(Application.temporaryCachePath, "WorldCharts");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"chart_seed{world.Seed}_r{world.Reached}.png");
            File.WriteAllBytes(path, world.Map.Texture.EncodeToPNG());
            _status = $"Saved {path}";
            Debug.Log($"[World] Chart exported: {path}");
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:F2}", GUILayout.Width(230f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }

        private static int IntSlider(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value}", GUILayout.Width(230f));
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max));
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
#endif
