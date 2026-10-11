using RumOverboard.Gameplay.Ocean;
using UnityEngine;

namespace RumOverboard.Gameplay.World
{
    /// <summary>
    /// Procedural voyage: a chain of ocean clusters joined by checkpoint islands (harbors), each
    /// cluster harder than the previous (narrower safe channel, more rocks / reefs, cliff narrows,
    /// guarded harbor approaches, rougher sea). Authored asset:
    /// Assets/Source/Configs/FeatureScenes/WorldGenConfig.asset — edited live from the World
    /// debug window (F6) and saved back with "Save to config".
    /// The world is rebuilt deterministically from the replicated seed on every peer.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldGenConfig", menuName = "RumOverboard/Feature Configs/World Generation")]
    public sealed class WorldGenConfig : ScriptableObject
    {
        [Header("Voyage")]
        [Tooltip("World seed. 0 = a new random seed every session (host picks, replicated).")]
        public int Seed;
        [Tooltip("Number of ocean clusters (legs). Checkpoints = clusters + 1 (start harbor included).")]
        [Range(1, 12)] public int ClusterCount = 6;
        [Tooltip("Difficulty over the voyage: x = cluster / (count - 1), y = difficulty 0..1.")]
        public AnimationCurve DifficultyCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Route")]
        [Tooltip("Angle (deg) between the voyage heading and the wind — ~90 = a beam reach.")]
        [Range(0f, 180f)] public float RouteAngleToWind = 80f;
        [Tooltip("Max random turn (deg) of each leg relative to the previous one.")]
        [Range(0f, 90f)] public float LegTurnMax = 35f;
        [Tooltip("Max total deviation (deg) of the route from the base heading.")]
        [Range(0f, 120f)] public float RouteDriftMax = 65f;
        [Tooltip("Harbor-to-harbor distance (m) in the first cluster.")]
        public float LegLengthEasy = 650f;
        [Tooltip("Harbor-to-harbor distance (m) in the last cluster.")]
        public float LegLengthHard = 1000f;
        [Range(0f, 0.5f)] public float LegLengthJitter = 0.15f;

        [Header("Checkpoint islands")]
        public float IslandRadiusMin = 85f;
        public float IslandRadiusMax = 130f;
        public float IslandPeakMin = 18f;
        public float IslandPeakMax = 40f;
        [Tooltip("Pier length (m) from the shoreline out into the basin.")]
        public float PierLength = 38f;
        public float PierWidth = 4f;
        [Tooltip("Pier deck height (m) above sea level.")]
        public float PierDeckHeight = 1.45f;
        [Tooltip("Guaranteed depth (m) of the harbor basin around the berth.")]
        public float HarborBasinDepth = 7f;
        [Tooltip("Radius (m) of the dredged harbor basin.")]
        public float HarborBasinRadius = 42f;
        [Tooltip("Distance (m) of the harbor approach point off the berth.")]
        public float ApproachDistance = 150f;
        [Tooltip("The ship 'arrives' at the next checkpoint within this distance (m) of its berth.")]
        public float HarborReachRadius = 75f;
        [Tooltip("Lighthouse / beacon tower height (m) — the landmark to steer for.")]
        public float BeaconHeight = 16f;

        [Header("Safe channel (always navigable)")]
        [Tooltip("Half-width (m) of the guaranteed clear channel in the easiest cluster.")]
        public float ChannelHalfWidthEasy = 110f;
        [Tooltip("Half-width (m) of the guaranteed clear channel in the hardest cluster.")]
        public float ChannelHalfWidthHard = 26f;
        [Tooltip("Sideways meander (m) of the channel, easy → hard.")]
        public float ChannelWiggleEasy = 40f;
        public float ChannelWiggleHard = 170f;
        [Tooltip("Number of channel bends, easy → hard.")]
        public int ChannelBendsEasy = 1;
        public int ChannelBendsHard = 4;
        [Tooltip("Guaranteed water depth (m) along the channel centerline.")]
        public float ChannelMinDepth = 5f;
        [Tooltip("Half-width (m) of a cluster's ocean area: obstacles and the map reveal stay within it.")]
        public float ClusterHalfWidth = 340f;
        [Tooltip("Open-sea floor depth (m).")]
        public float SeaFloorDepth = 45f;

        [Header("Hazards per cluster (easy → hard)")]
        public int IslandsEasy = 2;
        public int IslandsHard = 4;
        public int RocksEasy = 2;
        public int RocksHard = 16;
        public int ReefsEasy;
        public int ReefsHard = 9;
        [Tooltip("Difficulty from which a pair of cliffs pinches the channel into narrows.")]
        [Range(0f, 1f)] public float NarrowsFrom = 0.3f;
        [Tooltip("Difficulty from which a second narrows appears.")]
        [Range(0f, 1f)] public float SecondNarrowsFrom = 0.75f;
        [Tooltip("Difficulty from which a reef ring guards the harbor (one gap = the channel).")]
        [Range(0f, 1f)] public float ApproachReefFrom = 0.2f;
        [Tooltip("Difficulty from which cliffs flank the harbor approach.")]
        [Range(0f, 1f)] public float ApproachCliffsFrom = 0.55f;
        [Tooltip("Depth (m) of reef tops below sea level: shallow enough to ground the brig (draft ~2.4 m).")]
        public float ReefTopDepthMin = 0.35f;
        public float ReefTopDepthMax = 0.9f;

        [Header("Geometry")]
        [Tooltip("Terrain chunk size (m).")]
        public float ChunkSize = 128f;
        [Tooltip("Terrain mesh cell (m).")]
        public float CellSize = 2.5f;
        [Tooltip("Per-chunk color map resolution (texels per side).")]
        public int ColorMapResolution = 64;
        public Material TerrainMaterial;
        public Material WoodMaterial;
        public Material StoneMaterial;
        public Material ChartMaterial;
        public Material RopeMaterial;
        [Tooltip("Unlit material for the harbor lamps (tinted at runtime). Keeps the shader in player builds.")]
        public Material LampMaterial;

        [Header("Terrain colors")]
        public Color SandColor = new(0.86f, 0.78f, 0.56f);
        public Color GrassColor = new(0.33f, 0.52f, 0.25f);
        public Color ForestColor = new(0.2f, 0.36f, 0.18f);
        public Color RockColor = new(0.47f, 0.44f, 0.4f);
        public Color CliffColor = new(0.36f, 0.33f, 0.31f);
        public Color ReefColor = new(0.85f, 0.62f, 0.52f);
        public Color SeabedColor = new(0.55f, 0.52f, 0.4f);

        [Header("Map (paper chart)")]
        [Tooltip("Longest side of the chart texture (px).")]
        public int MapResolution = 1280;
        [Tooltip("Ocean margin (m) around the voyage on the chart.")]
        public float MapMargin = 220f;
        [Tooltip("Chart grid step (m).")]
        public float MapGridStep = 250f;
        [Tooltip("Contour line step (m) on land.")]
        public float MapContourStep = 10f;
        [Tooltip("Width (m) of the soft fog-of-war edge.")]
        public float FogEdge = 70f;
        [Tooltip("Radius (m) revealed around each known checkpoint.")]
        public float CheckpointRevealRadius = 260f;

        [Header("Sea & wind per cluster")]
        [Tooltip("Sea states from the easiest to the hardest cluster (picked by difficulty).")]
        public OceanSeaStateProfile[] SeaStates = new OceanSeaStateProfile[0];
        [Tooltip("Sea state blend time (s) when entering another cluster.")]
        public float SeaStateTransition = 25f;
        [Tooltip("Wind strength multiplier in the easiest / hardest cluster.")]
        public float WindScaleEasy = 1f;
        public float WindScaleHard = 1.5f;

        [Header("Mooring")]
        [Tooltip("Make the ship fast at the start harbor when it spawns.")]
        public bool MoorAtStart = true;
        [Tooltip("Line stiffness per kg of ship (N/m/kg).")]
        public float MooringStiffness = 0.8f;
        [Tooltip("Line damping per kg of ship (N·s/m/kg).")]
        public float MooringDamping = 1.2f;
        [Tooltip("Max line pull per kg (m/s²) — a hard tug, not a teleport.")]
        public float MooringMaxAccel = 3f;
        [Tooltip("Slack (m) the lines settle to; they slowly take up to this length.")]
        public float MooringSlack = 0.4f;
        [Tooltip("Lines can be made fast only with the ship's center within this distance (m) of the berth.")]
        public float MooringReach = 16f;

        private static WorldGenConfig _fallback;
        private static WorldGenConfig _active;

        public static WorldGenConfig Active
        {
            get
            {
                if (_active != null) return _active;
                if (_fallback == null) _fallback = CreateInstance<WorldGenConfig>();
                return _fallback;
            }
            set => _active = value;
        }

        public float Difficulty(int cluster)
        {
            float t = ClusterCount <= 1 ? 1f : Mathf.Clamp01(cluster / (float)(ClusterCount - 1));
            return DifficultyCurve != null && DifficultyCurve.length > 0
                ? Mathf.Clamp01(DifficultyCurve.Evaluate(t))
                : t;
        }

        public void CopyFrom(WorldGenConfig other)
        {
            if (other == null || other == this) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }
}
