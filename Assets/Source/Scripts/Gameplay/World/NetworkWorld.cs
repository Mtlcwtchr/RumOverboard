#if FUSION2
using System;
using System.Diagnostics;
using Fusion;
using Fusion.Addons.Physics;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Gameplay.World.Building;
using RumOverboard.Gameplay.World.Generation;
using RumOverboard.Gameplay.World.Map;
using RumOverboard.Gameplay.World.Mooring;
using RumOverboard.Gameplay.World.Saves;
using RumOverboard.Networking;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RumOverboard.Gameplay.World
{
    /// <summary>
    /// World aggregator (scene NetworkObject). Owns the voyage: generation, geometry, chart, progress,
    /// mooring and per-cluster sea state — the single place the ship, ocean and map are bound together.
    ///
    /// Networking: only the seed, wind, generation counter and progress are replicated. Every peer
    /// generates the identical <see cref="WorldLayout"/> from the seed and builds geometry + chart
    /// locally (deterministic; a host checksum catches config mismatches). Host simulates mooring
    /// springs, checkpoint arrival and the ship's cluster; clients only render.
    ///
    /// Also the ship spawn pose provider: the ship spawns moored at the start harbor's berth.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class NetworkWorld : NetworkBehaviour, IShipSpawnPoseProvider
    {
        // Ship-local cleats for the mooring lines (x is mirrored to the side facing the pier).
        private static readonly Vector3 BowCleat = new Vector3(1.3f, 1.7f, 8.3f) * 1.5f;   // brig x1.5
        private static readonly Vector3 SternCleat = new Vector3(1.9f, 1.8f, -8.4f) * 1.5f;
        private const float BollardTopHeight = 0.55f;
        private const float SpawnHeight = 0.25f;
        private const float RestWarpSpeed = 0.6f;

        [SerializeField] private WorldGenConfig config;
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanDepthProvider depthProvider;
        [SerializeField] private OceanWindSystem wind;

        [Networked] public int Seed { get; set; }
        [Networked] public int Generation { get; set; }
        [Networked] public float WindDegrees { get; set; }
        /// <summary>Index of the last checkpoint the ship has arrived at (0 = start harbor).</summary>
        [Networked] public int Reached { get; set; }
        /// <summary>Checkpoint the ship is moored at, -1 = under way.</summary>
        [Networked] public sbyte MooredAt { get; set; }
        [Networked] public sbyte MoorSide { get; set; }
        [Networked] public NetworkBool MoorSwap { get; set; }
        [Networked] public float BowRest { get; set; }
        [Networked] public float SternRest { get; set; }
        [Networked] public sbyte ShipCluster { get; set; }
        [Networked] public NetworkBool StartMoorDone { get; set; }
        [Networked] public int LayoutChecksum { get; set; }

        public static NetworkWorld Instance { get; private set; }

        public WorldGenConfig Config => config != null ? config : WorldGenConfig.Active;
        public WorldLayout Layout { get; private set; }
        public WorldMapPainter Map { get; private set; }
        public WorldBuildResult BuildResult => _build;
        public double BuildMilliseconds { get; private set; }
        public double GenerateMilliseconds { get; private set; }
        public double MapMilliseconds { get; private set; }
        public bool ChecksumMismatch { get; private set; }
        public float SeaLevel => waveField != null && waveField.Config != null ? waveField.Config.seaLevel : 0f;
        public bool IsReady => Layout != null && Object != null && Object.IsValid && _builtGeneration == Generation;
        public bool VoyageComplete => Layout != null && Reached >= Layout.Checkpoints.Count - 1;
        public bool IsHost => Object != null && Object.IsValid && HasStateAuthority;
        public ChartViewer Viewer => _viewer;

        /// <summary>Local debug: show the whole chart (not replicated).</summary>
        public bool RevealAllLocal { get; set; }
        /// <summary>Local debug: draw cluster channel centerlines on the chart.</summary>
        public bool DrawChannelsLocal { get; set; }

        /// <summary>Raised on every peer after the chart texture was recomposed.</summary>
        public event Action MapChanged;

        private WorldBuildResult _build;
        private int _builtGeneration = -1;
        private bool _composedChannels;
        private MooringLineView _lines;
        private ChartViewer _viewer;
        private NetworkShip _boundShip;
        private int _appliedCluster = -1;
        private OceanSeaStateProfile _appliedProfile;
        private float _windBase = -1f;
        private int _pendingTeleport = -1;
        private bool _pendingTeleportApproach;

        // =========================================================================================
        // Lifecycle
        // =========================================================================================
        private void Awake()
        {
            Instance = this;
            ShipSpawnPose.Provider = this;
            if (config != null)
                WorldGenConfig.Active = config;
            if (waveField == null) waveField = FindAnyObjectByType<OceanWaveField>();
            if (depthProvider == null) depthProvider = FindAnyObjectByType<OceanDepthProvider>();
            if (wind == null) wind = FindAnyObjectByType<OceanWindSystem>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (ReferenceEquals(ShipSpawnPose.Provider, this))
                ShipSpawnPose.Provider = null;
            Teardown();
        }

        public override void Spawned()
        {
            VoyageSave voyage = HasStateAuthority ? VoyageLaunch.Active : null;
            if (HasStateAuthority)
            {
                if (voyage != null && voyage.IsGenerated)
                {
                    Seed = voyage.Seed;
                    WindDegrees = voyage.WindDegrees;
                    Reached = Mathf.Max(0, voyage.Reached);
                }
                else
                {
                    if (Seed == 0)
                        Seed = voyage != null && voyage.Seed != 0 ? voyage.Seed : PickSeed();
                    // New voyages also vary the prevailing wind (route orientation) by seed.
                    WindDegrees = voyage != null
                        ? new System.Random(Seed).Next(0, 360)
                        : wind != null ? wind.DirectionDegrees : 35f;
                }
                if (Generation == 0)
                    Generation = 1;
                MooredAt = -1;
            }
            if (wind != null)
                _windBase = wind.BaseStrength;
            EnsureHelpers();
            if (Seed != 0)
                Rebuild();
            if (voyage != null && Layout != null)
            {
                Reached = Mathf.Clamp(Reached, 0, Mathf.Max(0, Layout.Checkpoints.Count - 1));
                Debug.Log($"[World] Voyage '{voyage.Name}' loaded: seed {Seed}, wind {WindDegrees:0.#}°, checkpoint {Reached}.");
                HostSaveVoyage(force: true);
            }
        }

        /// <summary>Checkpoint the voyage (re)starts from: the last one reached.</summary>
        public int StartCheckpoint => Layout == null || Layout.Checkpoints.Count == 0
            ? 0
            : Mathf.Clamp(Reached, 0, Layout.Checkpoints.Count - 1);

        /// <summary>
        /// Host: write the active voyage (<see cref="VoyageLaunch.Active"/>) if progress changed.
        /// No-op for sandbox launches without a voyage and on clients.
        /// </summary>
        public bool HostSaveVoyage(bool force = false)
        {
            VoyageSave voyage = VoyageLaunch.Active;
            if (voyage == null || !IsHost || Layout == null)
                return false;
            bool completed = VoyageComplete;
            if (!force && voyage.Seed == Seed && voyage.Reached == Reached && voyage.Completed == completed &&
                Mathf.Approximately(voyage.WindDegrees, WindDegrees))
                return false;
            voyage.Seed = Seed;
            voyage.WindDegrees = Mathf.Repeat(WindDegrees, 360f);
            voyage.Reached = Reached;
            voyage.Checkpoints = Layout.Checkpoints.Count;
            voyage.Completed = completed;
            VoyageLaunch.Save();
            return true;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_viewer != null)
                _viewer.Close();
            if (_lines != null)
                _lines.Hide();
            Teardown();
        }

        private void EnsureHelpers()
        {
            if (_lines == null)
            {
                Material rope = Config != null && Config.RopeMaterial != null
                    ? Config.RopeMaterial
                    : WorldGeometryBuilder.UnlitMaterial(new Color(0.5f, 0.4f, 0.27f));
                _lines = MooringLineView.Create(transform, rope);
            }
            if (_viewer == null)
            {
                _viewer = gameObject.GetComponent<ChartViewer>();
                if (_viewer == null)
                    _viewer = gameObject.AddComponent<ChartViewer>();
                _viewer.World = this;
            }
        }

        private int PickSeed()
        {
            WorldGenConfig cfg = Config;
            if (cfg != null && cfg.Seed != 0)
                return cfg.Seed;
            int s = (int)(DateTime.UtcNow.Ticks & 0x7fffffff) ^ Environment.TickCount;
            return s == 0 ? 1 : s & 0x7fffffff;
        }

        // =========================================================================================
        // Generation / build (every peer)
        // =========================================================================================
        private void Rebuild()
        {
            WorldGenConfig cfg = Config;
            if (cfg == null)
            {
                Debug.LogError("[World] No WorldGenConfig — nothing to generate.");
                return;
            }
            Teardown();

            if (wind != null && Mathf.Abs(Mathf.DeltaAngle(wind.DirectionDegrees, WindDegrees)) > 0.01f)
                wind.DirectionDegrees = WindDegrees;

            var sw = Stopwatch.StartNew();
            Layout = WorldLayoutGenerator.Generate(cfg, Seed, WindDegrees, SeaLevel);
            GenerateMilliseconds = sw.Elapsed.TotalMilliseconds;

            _build = WorldGeometryBuilder.Build(Layout, cfg, transform);
            for (int cp = 0; cp < Layout.Checkpoints.Count; cp++)
                for (int s = 0; s < 2; s++)
                {
                    Transform b = _build.Bollard(cp, s == 1);
                    if (b == null)
                        continue;
                    var bollard = b.gameObject.AddComponent<MooringBollard>();
                    bollard.World = this;
                    bollard.Checkpoint = cp;
                    bollard.Stern = s == 1;
                    bollard.MaxDistance = 3.5f;
                    bollard.Prompt = "Швартовы";
                }
            InteractableIndex.Forget(Object);
            if (depthProvider != null)
                depthProvider.SetFloorSampler(Layout);
            BuildMilliseconds = sw.Elapsed.TotalMilliseconds - GenerateMilliseconds;

            sw.Restart();
            Map = new WorldMapPainter(Layout, cfg);
            ComposeMap();
            double paint = sw.Elapsed.TotalMilliseconds;

            _builtGeneration = Generation;
            _appliedCluster = -1;

            int sum = Checksum(Layout);
            ChecksumMismatch = false;
            if (HasStateAuthority)
                LayoutChecksum = sum;
            else if (LayoutChecksum != 0 && LayoutChecksum != sum)
            {
                ChecksumMismatch = true;
                Debug.LogError($"[World] Layout checksum mismatch (host {LayoutChecksum}, local {sum}) — " +
                               "WorldGenConfig differs between peers; the world will not match the host's physics.");
            }

            Debug.Log($"[World] seed {Seed} gen {Generation}: {Layout.ClusterCount} clusters, {Layout.Features.Count} features, " +
                      $"{_build.Chunks} chunks / {_build.Colliders} colliders — generate {GenerateMilliseconds:F0} ms, " +
                      $"build {BuildMilliseconds:F0} ms, chart {paint:F0} ms");
        }

        private void Teardown()
        {
            if (depthProvider != null && Layout != null)
                depthProvider.SetFloorSampler(null);
            _build?.Dispose();
            _build = null;
            Map?.Dispose();
            Map = null;
            Layout = null;
            _builtGeneration = -1;
        }

        private void ComposeMap()
        {
            if (Map == null || Layout == null)
                return;
            var sw = Stopwatch.StartNew();
            Map.Compose(Reached, RevealAllLocal, DrawChannelsLocal);
            _composedChannels = DrawChannelsLocal;
            MapMilliseconds = sw.Elapsed.TotalMilliseconds;
            MapChanged?.Invoke();
        }

        private static int Checksum(WorldLayout layout)
        {
            unchecked
            {
                int h = layout.Features.Count * 397 ^ layout.Carves.Count;
                foreach (WorldCheckpoint cp in layout.Checkpoints)
                {
                    h = h * 31 + Mathf.RoundToInt(cp.Berth.x * 10f);
                    h = h * 31 + Mathf.RoundToInt(cp.Berth.y * 10f);
                    h = h * 31 + Mathf.RoundToInt(cp.BerthYaw * 10f);
                }
                foreach (WorldFeature f in layout.Features)
                    h = h * 31 + Mathf.RoundToInt(f.Center.x + f.Center.y * 7f);
                return h == 0 ? 1 : h;
            }
        }

        // =========================================================================================
        // Spawn pose (ConnectionManager)
        // =========================================================================================
        public bool TryGetShipSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (!IsReady || Layout.Checkpoints.Count == 0)
                return false;
            WorldCheckpoint start = Layout.Checkpoints[StartCheckpoint];
            position = new Vector3(start.Berth.x, SeaLevel + SpawnHeight, start.Berth.y);
            rotation = Quaternion.Euler(0f, start.BerthYaw, 0f);
            return true;
        }

        // =========================================================================================
        // Render (every peer)
        // =========================================================================================
        public override void Render()
        {
            if (Seed == 0 || Config == null)
                return;
            if (_builtGeneration != Generation || Layout == null)
                Rebuild();
            if (Layout == null)
                return;

            int reached = Mathf.Clamp(Reached, 0, Layout.ClusterCount);
            if (Map != null && (Map.ComposedReached != reached || Map.ComposedRevealAll != RevealAllLocal ||
                                _composedChannels != DrawChannelsLocal))
                ComposeMap();

            BindShip();
            ApplyClusterEnvironment();
            UpdateMooringLines();
        }

        private void BindShip()
        {
            NetworkShip ship = FindShip();
            if (ship == _boundShip)
                return;
            _boundShip = ship;
            if (ship != null)
                ChartTable.Attach(ship, this, Config);
        }

        public NetworkShip FindShip()
        {
            foreach (NetworkShip s in NetworkShip.All)
                if (s != null && s.Object != null && s.Object.IsValid)
                    return s;
            return null;
        }

        private void ApplyClusterEnvironment()
        {
            int k = Mathf.Clamp(ShipCluster, 0, Mathf.Max(0, Layout.ClusterCount - 1));
            if (k == _appliedCluster || Layout.ClusterCount == 0)
                return;
            bool first = _appliedCluster < 0;
            _appliedCluster = k;
            float d = Layout.Clusters[k].Difficulty;
            WorldGenConfig cfg = Config;

            OceanSeaStateProfile[] states = cfg.SeaStates;
            if (waveField != null && states != null && states.Length > 0)
            {
                OceanSeaStateProfile p = states[Mathf.Clamp(Mathf.RoundToInt(d * (states.Length - 1)), 0, states.Length - 1)];
                if (p != null && p != _appliedProfile)
                {
                    waveField.SetSeaState(p, first ? 0.01f : cfg.SeaStateTransition);
                    _appliedProfile = p;
                }
            }
            if (wind != null && _windBase >= 0f)
                wind.BaseStrength = _windBase * Mathf.Lerp(cfg.WindScaleEasy, cfg.WindScaleHard, d);
        }

        private void UpdateMooringLines()
        {
            if (_lines == null)
                return;
            NetworkShip ship = _boundShip;
            int cp = MooredAt;
            if (ship == null || cp < 0 || cp >= Layout.Checkpoints.Count)
            {
                _lines.Hide();
                return;
            }
            Transform t = ship.transform;
            float side = MoorSide < 0 ? -1f : 1f;
            _lines.Draw(0, t.TransformPoint(Cleat(BowCleat, side)), Anchor(cp, MoorSwap), BowRest);
            _lines.Draw(1, t.TransformPoint(Cleat(SternCleat, side)), Anchor(cp, !MoorSwap), SternRest);
        }

        // =========================================================================================
        // Host simulation
        // =========================================================================================
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !IsReady)
                return;
            HostSaveVoyage();
            NetworkShip ship = FindShip();
            if (ship == null || ship.Body == null)
                return;

            if (_pendingTeleport >= 0)
            {
                int cp = _pendingTeleport;
                _pendingTeleport = -1;
                DoTeleport(ship, cp, _pendingTeleportApproach);
                return;
            }

            Rigidbody rb = ship.Body;
            var pos = new Vector2(rb.position.x, rb.position.z);
            WorldGenConfig cfg = Config;

            if (!StartMoorDone)
            {
                StartMoorDone = true;
                int start = StartCheckpoint;
                if (cfg.MoorAtStart && Layout.Checkpoints.Count > 0 &&
                    Vector2.Distance(pos, Layout.Checkpoints[start].Berth) < cfg.MooringReach)
                    MakeFast(start, rb);
            }

            ShipCluster = (sbyte)Mathf.Clamp(Layout.ClusterAt(pos), 0, 127);

            int next = Reached + 1;
            if (next < Layout.Checkpoints.Count && Vector2.Distance(pos, Layout.Checkpoints[next].Berth) < cfg.HarborReachRadius)
            {
                Reached = next;
                Debug.Log(next == Layout.Checkpoints.Count - 1
                    ? $"[World] Final checkpoint {next} reached — voyage complete."
                    : $"[World] Checkpoint {next} reached — chart opens leg {next + 1}.");
            }

            if (MooredAt >= 0)
                ApplyMooring(rb);
        }

        private static Vector3 Cleat(Vector3 local, float side) => new(local.x * side, local.y, local.z);

        /// <summary>Top of a pier bollard (world). <paramref name="stern"/> picks the shoreward one.</summary>
        public Vector3 Anchor(int cp, bool stern)
        {
            WorldCheckpoint c = Layout.Checkpoints[cp];
            Vector2 p = stern ? c.BollardStern : c.BollardBow;
            return new Vector3(p.x, SeaLevel + Config.PierDeckHeight + BollardTopHeight, p.y);
        }

        private static Vector3 PointOn(Rigidbody rb, Vector3 local) => rb.position + rb.rotation * local;

        private void MakeFast(int cp, Rigidbody rb)
        {
            Vector3 bow = Anchor(cp, false), stern = Anchor(cp, true);
            Vector3 mid = (bow + stern) * 0.5f;
            float side = (Quaternion.Inverse(rb.rotation) * (mid - rb.position)).x >= 0f ? 1f : -1f;
            Vector3 bowCleat = PointOn(rb, Cleat(BowCleat, side));
            Vector3 sternCleat = PointOn(rb, Cleat(SternCleat, side));
            bool swap = Vector3.Distance(bowCleat, stern) + Vector3.Distance(sternCleat, bow) <
                        Vector3.Distance(bowCleat, bow) + Vector3.Distance(sternCleat, stern);
            MoorSide = (sbyte)side;
            MoorSwap = swap;
            BowRest = Mathf.Max(1f, Vector3.Distance(bowCleat, Anchor(cp, swap)));
            SternRest = Mathf.Max(1f, Vector3.Distance(sternCleat, Anchor(cp, !swap)));
            MooredAt = (sbyte)cp;
            Debug.Log($"[World] Ship made fast at checkpoint {cp} ({(side < 0 ? "port" : "starboard")} side to the pier).");
        }

        /// <summary>Rest length the line settles to with the ship lying at the berth.</summary>
        private float NominalRest(int cp, bool bowLine)
        {
            WorldCheckpoint c = Layout.Checkpoints[cp];
            float side = MoorSide < 0 ? -1f : 1f;
            // The berth is laid out on the pier's right: heading BerthYaw puts the pier to port (side −1).
            bool reversed = side > 0f;
            Quaternion rot = Quaternion.Euler(0f, c.BerthYaw + (reversed ? 180f : 0f), 0f);
            var pos = new Vector3(c.Berth.x, SeaLevel, c.Berth.y);
            Vector3 cleat = pos + rot * Cleat(bowLine ? BowCleat : SternCleat, side);
            Vector3 anchor = Anchor(cp, bowLine ? MoorSwap : !MoorSwap);
            return Mathf.Max(1f, Vector3.Distance(cleat, anchor) + Config.MooringSlack);
        }

        private void ApplyMooring(Rigidbody rb)
        {
            int cp = MooredAt;
            if (cp >= Layout.Checkpoints.Count)
            {
                MooredAt = -1;
                return;
            }
            float dt = Runner.DeltaTime;
            float side = MoorSide < 0 ? -1f : 1f;
            BowRest = Mathf.MoveTowards(BowRest, NominalRest(cp, true), RestWarpSpeed * dt);
            SternRest = Mathf.MoveTowards(SternRest, NominalRest(cp, false), RestWarpSpeed * dt);
            Line(rb, PointOn(rb, Cleat(BowCleat, side)), Anchor(cp, MoorSwap), BowRest);
            Line(rb, PointOn(rb, Cleat(SternCleat, side)), Anchor(cp, !MoorSwap), SternRest);
        }

        private void Line(Rigidbody rb, Vector3 at, Vector3 anchor, float rest)
        {
            Vector3 d = anchor - at;
            float length = d.magnitude;
            float stretch = length - rest;
            if (length < 1e-3f || stretch <= 0f)
                return; // ropes only pull
            Vector3 u = d / length;
            WorldGenConfig cfg = Config;
            float lengthening = -Vector3.Dot(rb.GetPointVelocity(at), u);
            float accel = cfg.MooringStiffness * stretch + cfg.MooringDamping * lengthening;
            accel = Mathf.Clamp(accel, 0f, cfg.MooringMaxAccel);
            rb.AddForceAtPosition(u * (accel * rb.mass), at, ForceMode.Force);
        }

        private void DoTeleport(NetworkShip ship, int cp, bool approach)
        {
            WorldCheckpoint c = Layout.Checkpoints[cp];
            Vector2 xz = approach ? c.Approach : c.Berth;
            float yaw = approach ? WorldGeometry.Yaw(c.Berth - c.Approach) : c.BerthYaw;
            var pos = new Vector3(xz.x, SeaLevel + SpawnHeight, xz.y);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            Rigidbody rb = ship.Body;
            Vector3 oldPos = rb.position;
            Quaternion delta = rot * Quaternion.Inverse(rb.rotation);
            MooredAt = -1;
            Place(ship.gameObject, rb, pos, rot);

            NetworkId shipId = ship.Object.Id;
            foreach (NetworkPlayer p in NetworkPlayer.All)
            {
                if (p == null || p.Object == null || !p.Object.IsValid || p.Platform != shipId)
                    continue;
                Rigidbody prb = p.GetComponent<Rigidbody>();
                Vector3 from = prb != null ? prb.position : p.transform.position;
                Place(p.gameObject, prb, pos + delta * (from - oldPos), null);
            }

            ShipCluster = (sbyte)Mathf.Clamp(Layout.ClusterAt(xz), 0, 127);
            if (!approach)
                MakeFast(cp, rb);
            Debug.Log($"[World] Ship teleported to checkpoint {cp} {(approach ? "approach" : "berth")}.");
        }

        private static void Place(GameObject go, Rigidbody rb, Vector3 pos, Quaternion? rot)
        {
            if (go.TryGetComponent(out NetworkRigidbody3D nrb))
                nrb.Teleport(pos, rot);
            if (rb != null)
            {
                rb.position = pos;
                if (rot.HasValue) rb.rotation = rot.Value;
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            go.transform.position = pos;
            if (rot.HasValue) go.transform.rotation = rot.Value;
        }

        // =========================================================================================
        // Mooring interaction (bollards)
        // =========================================================================================
        public string MooringPrompt(int cp, out bool available)
        {
            available = false;
            if (!IsReady || cp < 0 || cp >= Layout.Checkpoints.Count)
                return "…";
            if (MooredAt == cp)
            {
                available = true;
                return "Отдать швартовы";
            }
            if (MooredAt >= 0)
                return "Корабль ошвартован у другого причала";
            NetworkShip ship = FindShip();
            if (ship == null)
                return "Нет корабля";
            Vector3 p = ship.transform.position;
            float d = Vector2.Distance(new Vector2(p.x, p.z), Layout.Checkpoints[cp].Berth);
            if (d > Config.MooringReach)
                return "Корабль далеко от причала";
            available = true;
            return "Ошвартоваться";
        }

        /// <summary>Host: toggle mooring at <paramref name="cp"/> (bollard E press).</summary>
        public void HostUseMooring(int cp)
        {
            if (!IsHost || !IsReady || cp < 0 || cp >= Layout.Checkpoints.Count)
                return;
            if (MooredAt == cp)
            {
                MooredAt = -1;
                Debug.Log($"[World] Cast off from checkpoint {cp}.");
                return;
            }
            NetworkShip ship = FindShip();
            if (MooredAt >= 0 || ship == null || ship.Body == null)
                return;
            Vector3 p = ship.Body.position;
            if (Vector2.Distance(new Vector2(p.x, p.z), Layout.Checkpoints[cp].Berth) <= Config.MooringReach)
                MakeFast(cp, ship.Body);
        }

        // =========================================================================================
        // Host debug API (WorldDebugWindow, smoke test)
        // =========================================================================================
        /// <summary>Host: new world with <paramref name="seed"/> (0 = config seed or random); ship back to the start berth.</summary>
        public void HostRegenerate(int seed)
        {
            if (!IsHost)
                return;
            Seed = seed != 0 ? seed : PickSeed();
            if (wind != null)
                WindDegrees = wind.DirectionDegrees;
            Generation++;
            Reached = 0;
            MooredAt = -1;
            ShipCluster = 0;
            StartMoorDone = true;
            Rebuild();
            _pendingTeleport = 0;
            _pendingTeleportApproach = false;
        }

        /// <summary>Host: forget progress (chart fogged again), ship back to the start berth.</summary>
        public void HostResetProgress()
        {
            if (!IsHost)
                return;
            Reached = 0;
            MooredAt = -1;
            _pendingTeleport = 0;
            _pendingTeleportApproach = false;
        }

        public void HostSetReached(int reached)
        {
            if (!IsHost || Layout == null)
                return;
            Reached = Mathf.Clamp(reached, 0, Layout.Checkpoints.Count - 1);
        }

        /// <summary>Host: move ship (+ crew aboard) to checkpoint <paramref name="cp"/>'s berth or harbor approach.</summary>
        public void HostTeleport(int cp, bool approach)
        {
            if (!IsHost || Layout == null)
                return;
            _pendingTeleport = Mathf.Clamp(cp, 0, Layout.Checkpoints.Count - 1);
            _pendingTeleportApproach = approach;
        }

        public void HostCastOff()
        {
            if (IsHost)
                MooredAt = -1;
        }

        /// <summary>Host: make fast at the nearest berth in reach. Returns false if none.</summary>
        public bool HostMoorNearest()
        {
            if (!IsHost || !IsReady)
                return false;
            NetworkShip ship = FindShip();
            if (ship == null || ship.Body == null)
                return false;
            Vector3 p = ship.Body.position;
            for (int i = 0; i < Layout.Checkpoints.Count; i++)
                if (Vector2.Distance(new Vector2(p.x, p.z), Layout.Checkpoints[i].Berth) <= Config.MooringReach)
                {
                    MakeFast(i, ship.Body);
                    return true;
                }
            return false;
        }

        /// <summary>Local: re-generate + rebuild from the current (edited) config without changing the seed.</summary>
        public void LocalRebuild()
        {
            if (Seed != 0)
                Rebuild();
        }
    }
}
#endif
