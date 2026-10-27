using System.Collections.Generic;
using RumOverboard.Gameplay.Ocean;
using UnityEngine;

namespace RumOverboard.Gameplay.World.Generation
{
    public enum WorldFeatureKind : byte
    {
        Seabed,
        CheckpointIsland,
        Island,
        Cliff,
        Rock,
        Reef,
    }

    /// <summary>
    /// One landform: a lobed, optionally stretched mound. Inside its (noisy) outline the height is
    /// the "top" profile (peak → shoreline for land, a flat shallow plateau for reefs); outside it
    /// shelves down to the sea floor over <see cref="Shelf"/> meters. Heights are relative to sea level.
    /// </summary>
    public struct WorldFeature
    {
        public WorldFeatureKind Kind;
        public int Cluster;            // -1 = checkpoint island
        public Vector2 Center;
        public Vector2 Axis;           // unit; stretch direction
        public float Stretch;          // ≥ 1, elongation along Axis
        public float Radius;
        public float Peak;             // summit height (reef: plateau top, negative)
        public float Base;             // outline height: 0 for land (shoreline), < 0 for reefs / hidden rocks
        public float Steepness;        // profile exponent: low = cliff walls, high = gentle dome, 0 = flat plateau
        public float Shelf;            // underwater slope length (m)
        public float LobeAmp;
        public float LobePhase;
        public float CoastNoise;       // relative outline wobble
        public float NoiseAmp;         // surface relief (m)
        public float NoiseScale;
        public int NoiseSeed;

        /// <summary>Max reach of the land / plateau (m).</summary>
        public float OutlineRadius => Radius * (1f + LobeAmp) * (1f + CoastNoise) * Stretch;

        /// <summary>Max reach of any influence (outline + shelf).</summary>
        public float BoundRadius => (Radius * (1f + LobeAmp) * (1f + CoastNoise) + Shelf) * Stretch;

        /// <summary>Where it's dangerous for a hull: the outline plus the shallow start of the shelf.</summary>
        public float HazardRadius => (Radius * (1f + LobeAmp) * (1f + CoastNoise) + Shelf * 0.35f) * Stretch;

        public bool IsLand => Kind != WorldFeatureKind.Reef && Peak > 0f;

        /// <summary>A rock whose top stays below the surface — invisible from deck, marked on the chart.</summary>
        public bool IsHidden => Kind == WorldFeatureKind.Rock && Peak < 0f;

        /// <summary>Height at (x, z), or false when outside the feature's influence.</summary>
        public bool TryEvaluate(float x, float z, float seaFloor, out float height)
        {
            float dx = x - Center.x, dz = z - Center.y;
            float bound = BoundRadius;
            if (dx * dx + dz * dz >= bound * bound)
            {
                height = 0f;
                return false;
            }

            float u = (dx * Axis.x + dz * Axis.y) / Stretch;
            float v = -dx * Axis.y + dz * Axis.x;
            float d = Mathf.Sqrt(u * u + v * v);
            float theta = Mathf.Atan2(v, u) + LobePhase;
            float lobe = 1f + LobeAmp * (0.5f * Mathf.Sin(2f * theta) + 0.3f * Mathf.Sin(3f * theta + 1.3f) +
                                         0.2f * Mathf.Sin(5f * theta + 2.1f));
            float r = Radius * lobe;
            if (CoastNoise > 0f)
                r *= 1f + CoastNoise * WorldNoise.Fbm(x * 0.018f, z * 0.018f, NoiseSeed + 77, 3);
            r = Mathf.Max(r, 0.5f);

            float s = d / r;
            if (s < 1f)
            {
                float profile = Base + (Peak - Base) * Mathf.Pow(1f - s * s, Steepness);
                if (NoiseAmp > 0f)
                    profile += NoiseAmp * WorldNoise.Fbm(x * NoiseScale, z * NoiseScale, NoiseSeed, 4) * (1f - s);
                if (Kind == WorldFeatureKind.Reef)
                    height = Mathf.Min(profile, -0.12f);
                else
                    height = Base >= 0f ? Mathf.Max(profile, Base + 0.12f * (1f - s)) : profile;
                return true;
            }

            float t = (d - r) / Mathf.Max(Shelf, 0.5f);
            if (t >= 1f)
            {
                height = -seaFloor;
                return true;
            }
            t = t * t * (3f - 2f * t);
            height = Mathf.Lerp(Base, -seaFloor, t);
            return true;
        }
    }

    /// <summary>
    /// Guaranteed navigable water: a capsule (segment + radius) that only lowers the floor, and only
    /// where it is already underwater — beaches and quays keep their shoreline.
    /// </summary>
    public struct WorldCarve
    {
        public Vector2 A, B;
        public float Radius;
        public float Falloff;
        public float Depth;

        public float BoundRadius => Radius + Falloff;

        public float Apply(float x, float z, float h)
        {
            float target = -Depth;
            if (h <= target || h >= 0f)
                return h;
            float dist = WorldGeometry.DistanceToSegment(new Vector2(x, z), A, B);
            if (dist >= Radius + Falloff)
                return h;
            float w = dist <= Radius ? 1f : 1f - Smooth((dist - Radius) / Falloff);
            w *= Mathf.Clamp01(-h / 1.5f);
            return Mathf.Lerp(h, target, w);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }

    public sealed class WorldCheckpoint
    {
        public int Index;
        public Vector2 IslandCenter;
        public float IslandRadius;
        public float IslandPeak;
        /// <summary>Unit direction from the island out through the harbor mouth.</summary>
        public Vector2 HarborDir;
        public Vector2 Shore;
        public Vector2 PierStart;
        public Vector2 PierEnd;
        public float PierWidth;
        /// <summary>Ship center when moored (XZ). The ship lies along the pier, bow seaward.</summary>
        public Vector2 Berth;
        /// <summary>Ship heading (deg, Unity yaw) when moored.</summary>
        public float BerthYaw;
        public Vector2 BollardBow;
        public Vector2 BollardStern;
        public Vector2 Approach;
        public Vector2 Beacon;
        public float BeaconGround;

        public Vector2 Right => new(HarborDir.y, -HarborDir.x);
    }

    public sealed class WorldCluster
    {
        public int Index;
        public float Difficulty;
        public float ChannelHalfWidth;
        /// <summary>Safe channel centerline: berth(k) → approach(k) → … → approach(k+1) → berth(k+1).</summary>
        public readonly List<Vector2> Channel = new();
        public int Islands, Rocks, Reefs, Narrows;
        public bool ApproachReef, ApproachCliffs;
    }

    /// <summary>
    /// The generated voyage: checkpoints, clusters (legs) with their safe channels, landforms and
    /// carves, plus a spatial grid for fast height queries. Pure data — no scene objects — so it can
    /// be built and validated anywhere (editor self-test, every network peer).
    /// </summary>
    public sealed class WorldLayout : IOceanFloorSampler
    {
        public const float GridCell = 128f;

        public int Seed;
        public float WindDegrees;
        public float SeaFloorDepth;
        public float SeaLevel;
        public int FloorNoiseSeed;
        public readonly List<WorldCheckpoint> Checkpoints = new();
        public readonly List<WorldCluster> Clusters = new();
        public readonly List<WorldFeature> Features = new();
        public readonly List<WorldCarve> Carves = new();
        public Rect Bounds;

        private Dictionary<long, List<int>> _featureGrid;
        private Dictionary<long, List<int>> _carveGrid;
        private static readonly List<int> Empty = new();

        public int ClusterCount => Clusters.Count;

        // ---- Spatial index ---------------------------------------------------------------------
        public void BuildIndex()
        {
            _featureGrid = new Dictionary<long, List<int>>();
            _carveGrid = new Dictionary<long, List<int>>();
            for (int i = 0; i < Features.Count; i++)
            {
                WorldFeature f = Features[i];
                Insert(_featureGrid, i, f.Center, f.Center, f.BoundRadius);
            }
            for (int i = 0; i < Carves.Count; i++)
            {
                WorldCarve c = Carves[i];
                Insert(_carveGrid, i, c.A, c.B, c.BoundRadius);
            }

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (WorldFeature f in Features)
            {
                float r = f.BoundRadius;
                minX = Mathf.Min(minX, f.Center.x - r);
                maxX = Mathf.Max(maxX, f.Center.x + r);
                minZ = Mathf.Min(minZ, f.Center.y - r);
                maxZ = Mathf.Max(maxZ, f.Center.y + r);
            }
            foreach (WorldCluster c in Clusters)
            foreach (Vector2 p in c.Channel)
            {
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.y);
                maxZ = Mathf.Max(maxZ, p.y);
            }
            if (minX > maxX)
                minX = maxX = minZ = maxZ = 0f;
            Bounds = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        }

        private static void Insert(Dictionary<long, List<int>> grid, int index, Vector2 a, Vector2 b, float radius)
        {
            int x0 = Cell(Mathf.Min(a.x, b.x) - radius), x1 = Cell(Mathf.Max(a.x, b.x) + radius);
            int z0 = Cell(Mathf.Min(a.y, b.y) - radius), z1 = Cell(Mathf.Max(a.y, b.y) + radius);
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                long key = Key(x, z);
                if (!grid.TryGetValue(key, out List<int> list))
                    grid[key] = list = new List<int>(4);
                list.Add(index);
            }
        }

        private static int Cell(float v) => Mathf.FloorToInt(v / GridCell);
        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private List<int> FeaturesAt(float x, float z) =>
            _featureGrid != null && _featureGrid.TryGetValue(Key(Cell(x), Cell(z)), out List<int> l) ? l : Empty;

        private List<int> CarvesAt(float x, float z) =>
            _carveGrid != null && _carveGrid.TryGetValue(Key(Cell(x), Cell(z)), out List<int> l) ? l : Empty;

        /// <summary>True when the 128 m grid cell around (x, z) contains any landform.</summary>
        public bool HasFeaturesNear(float x, float z) => FeaturesAt(x, z).Count > 0;

        // ---- Height queries (thread-safe after BuildIndex) ----------------------------------------
        public float SeabedHeight(float x, float z) =>
            -SeaFloorDepth + 3f * WorldNoise.Fbm(x * 0.006f, z * 0.006f, FloorNoiseSeed, 3);

        /// <summary>Height relative to sea level (negative = underwater).</summary>
        public float SampleHeight(float x, float z) => Sample(x, z, out _);

        public float Sample(float x, float z, out WorldFeatureKind kind)
        {
            float h = SeabedHeight(x, z);
            kind = WorldFeatureKind.Seabed;
            List<int> features = FeaturesAt(x, z);
            for (int i = 0; i < features.Count; i++)
            {
                WorldFeature f = Features[features[i]];
                if (f.TryEvaluate(x, z, SeaFloorDepth, out float fh) && fh > h)
                {
                    h = fh;
                    kind = f.Kind;
                }
            }

            List<int> carves = CarvesAt(x, z);
            for (int i = 0; i < carves.Count; i++)
                h = Carves[carves[i]].Apply(x, z, h);
            return h;
        }

        /// <summary>Height from the landforms only (no carves) — used while placing harbors.</summary>
        public float SampleFeaturesOnly(float x, float z, IReadOnlyList<WorldFeature> features)
        {
            float h = SeabedHeight(x, z);
            for (int i = 0; i < features.Count; i++)
                if (features[i].TryEvaluate(x, z, SeaFloorDepth, out float fh) && fh > h)
                    h = fh;
            return h;
        }

        public float SampleFloorHeight(Vector3 worldPos) => SeaLevel + SampleHeight(worldPos.x, worldPos.z);

        // ---- Route queries -------------------------------------------------------------------------
        /// <summary>Cluster (leg) whose checkpoint-to-checkpoint segment is nearest to p.</summary>
        public int ClusterAt(Vector2 p)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int k = 0; k < Clusters.Count; k++)
            {
                float d = WorldGeometry.DistanceToSegment(p, Checkpoints[k].IslandCenter, Checkpoints[k + 1].IslandCenter);
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }
            return best;
        }

        /// <summary>Distance from p to the cluster's leg (channel polyline and island-to-island segment).</summary>
        public float DistanceToLeg(Vector2 p, int k)
        {
            float d = WorldGeometry.DistanceToSegment(p, Checkpoints[k].IslandCenter, Checkpoints[k + 1].IslandCenter);
            return Mathf.Min(d, WorldGeometry.DistanceToPolyline(p, Clusters[k].Channel));
        }
    }

    public static class WorldGeometry
    {
        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        public static float DistanceToPolyline(Vector2 p, List<Vector2> line)
        {
            if (line == null || line.Count == 0) return float.MaxValue;
            if (line.Count == 1) return Vector2.Distance(p, line[0]);
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
                best = Mathf.Min(best, DistanceToSegment(p, line[i], line[i + 1]));
            return best;
        }

        public static float PolylineLength(List<Vector2> line)
        {
            float len = 0f;
            for (int i = 0; i + 1 < line.Count; i++)
                len += Vector2.Distance(line[i], line[i + 1]);
            return len;
        }

        /// <summary>Point + unit tangent at arc-length fraction t along the polyline.</summary>
        public static Vector2 PointAt(List<Vector2> line, float t, out Vector2 tangent)
        {
            float target = Mathf.Clamp01(t) * PolylineLength(line);
            for (int i = 0; i + 1 < line.Count; i++)
            {
                float seg = Vector2.Distance(line[i], line[i + 1]);
                if (target <= seg || i + 2 == line.Count)
                {
                    tangent = seg > 1e-4f ? (line[i + 1] - line[i]) / seg : Vector2.up;
                    return Vector2.Lerp(line[i], line[i + 1], seg > 1e-4f ? Mathf.Clamp01(target / seg) : 0f);
                }
                target -= seg;
            }
            tangent = Vector2.up;
            return line.Count > 0 ? line[0] : Vector2.zero;
        }

        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Unity yaw (deg) of an XZ direction.</summary>
        public static float Yaw(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

        /// <summary>XZ direction of a Unity yaw (deg).</summary>
        public static Vector2 FromYaw(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }
    }
}
