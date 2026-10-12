using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.World.Generation
{
    /// <summary>
    /// Builds a <see cref="WorldLayout"/> from a seed:
    ///   1. route — checkpoint islands chained across the wind (a sailable reach), legs longer and
    ///      twistier with difficulty;
    ///   2. harbors — each island gets a pier facing its arriving leg (start island: facing the first
    ///      leg), berth, mooring bollards, approach point and a beacon tower;
    ///   3. safe channels — berth → approach → meanders → approach → berth, detouring around islands,
    ///      narrower and bendier as difficulty grows; carved to a guaranteed depth;
    ///   4. hazards — islands / rocks (some hidden) / reefs placed outside every channel, the hard
    ///      clusters hugging it; cliff narrows pinching the channel; reef rings and cliff gates
    ///      guarding the harbor approaches.
    /// Deterministic: same config + seed + wind → identical layout on every peer.
    /// </summary>
    public static class WorldLayoutGenerator
    {
        private const float ShipHalfLength = 15.8f; // brig x1.5
        private const float ShipHalfBeam = 4.0f;

        public static WorldLayout Generate(WorldGenConfig cfg, int seed, float windDegrees, float seaLevel)
        {
            var rng = new WorldRandom(seed, 1);
            var layout = new WorldLayout
            {
                Seed = seed,
                WindDegrees = windDegrees,
                SeaFloorDepth = Mathf.Max(5f, cfg.SeaFloorDepth),
                SeaLevel = seaLevel,
                FloorNoiseSeed = rng.NextSeed(),
            };
            int clusters = Mathf.Max(1, cfg.ClusterCount);

            PlaceCheckpoints(cfg, layout, ref rng, clusters);
            for (int i = 0; i < layout.Checkpoints.Count; i++)
                PlaceHarbor(cfg, layout, ref rng, i);
            for (int k = 0; k < clusters; k++)
                BuildChannel(cfg, layout, ref rng, k);
            for (int k = 0; k < clusters; k++)
                PlaceHazards(cfg, layout, ref rng, k);
            AddChannelCarves(cfg, layout);

            layout.BuildIndex();
            return layout;
        }

        // ---- 1. Route ------------------------------------------------------------------------------
        private static void PlaceCheckpoints(WorldGenConfig cfg, WorldLayout layout, ref WorldRandom rng, int clusters)
        {
            float side = rng.Value() < 0.5f ? -1f : 1f;
            float baseYaw = layout.WindDegrees + side * cfg.RouteAngleToWind;
            float drift = 0f;
            Vector2 pos = Vector2.zero;

            for (int i = 0; i <= clusters; i++)
            {
                float radius = rng.Range(cfg.IslandRadiusMin, Mathf.Max(cfg.IslandRadiusMin, cfg.IslandRadiusMax));
                float peak = rng.Range(cfg.IslandPeakMin, Mathf.Max(cfg.IslandPeakMin, cfg.IslandPeakMax));
                var island = new WorldFeature
                {
                    Kind = WorldFeatureKind.CheckpointIsland,
                    Cluster = -1,
                    Center = pos,
                    Axis = rng.OnUnitCircle(),
                    Stretch = rng.Range(1f, 1.25f),
                    Radius = radius,
                    Peak = peak,
                    Base = 0f,
                    Steepness = rng.Range(1.15f, 1.6f),
                    Shelf = rng.Range(55f, 75f),
                    LobeAmp = rng.Range(0.08f, 0.16f),
                    LobePhase = rng.Range(0f, 6.283f),
                    CoastNoise = 0.07f,
                    NoiseAmp = Mathf.Min(7f, peak * 0.22f),
                    NoiseScale = 0.022f,
                    NoiseSeed = rng.NextSeed(),
                };
                layout.Features.Add(island);
                layout.Checkpoints.Add(new WorldCheckpoint
                {
                    Index = i,
                    IslandCenter = pos,
                    IslandRadius = radius,
                    IslandPeak = peak,
                });

                if (i == clusters)
                    break;

                float d = cfg.Difficulty(i);
                drift = Mathf.Clamp(drift + rng.Signed() * cfg.LegTurnMax, -cfg.RouteDriftMax, cfg.RouteDriftMax);
                Vector2 dir = WorldGeometry.FromYaw(baseYaw + drift);
                float len = Mathf.Lerp(cfg.LegLengthEasy, cfg.LegLengthHard, d) * (1f + rng.Signed() * cfg.LegLengthJitter);
                pos += dir * Mathf.Max(len, radius * 2f + 260f);

                layout.Clusters.Add(new WorldCluster
                {
                    Index = i,
                    Difficulty = d,
                    ChannelHalfWidth = Mathf.Lerp(cfg.ChannelHalfWidthEasy, cfg.ChannelHalfWidthHard, d),
                });
            }
        }

        // ---- 2. Harbors ----------------------------------------------------------------------------
        private static void PlaceHarbor(WorldGenConfig cfg, WorldLayout layout, ref WorldRandom rng, int i)
        {
            WorldCheckpoint cp = layout.Checkpoints[i];
            WorldFeature island = layout.Features[i];
            var only = new[] { island };

            // The harbor faces the leg the crew arrives by (the start harbor faces the first leg).
            Vector2 toward = i == 0
                ? layout.Checkpoints[1].IslandCenter - cp.IslandCenter
                : layout.Checkpoints[i - 1].IslandCenter - cp.IslandCenter;
            float spread = i == 0 ? 25f : 55f;
            Vector2 dir = WorldGeometry.Rotate(toward.normalized, rng.Signed() * spread).normalized;
            Vector2 right = new(dir.y, -dir.x);

            // Shoreline along the harbor ray.
            float shoreDist = 0f;
            for (float t = 0f; t < island.BoundRadius; t += 0.5f)
            {
                Vector2 p = cp.IslandCenter + dir * t;
                if (layout.SampleFeaturesOnly(p.x, p.y, only) < 0f)
                {
                    shoreDist = t;
                    break;
                }
            }
            Vector2 shore = cp.IslandCenter + dir * shoreDist;

            // Pier long enough that the berthed hull clears the island's own slope.
            float lateral = cfg.PierWidth * 0.5f + ShipHalfBeam + 0.8f;
            float berthAlong = Mathf.Max(cfg.PierLength - 12f, ShipHalfLength + 6f);
            for (int guard = 0; guard < 20 && !FootprintClear(layout, only, shore, dir, right, berthAlong, lateral); guard++)
                berthAlong += 3f;

            float pierLength = berthAlong + 12f;
            cp.HarborDir = dir;
            cp.Shore = shore;
            cp.PierStart = shore - dir * 6f;
            cp.PierEnd = shore + dir * pierLength;
            cp.PierWidth = cfg.PierWidth;
            cp.Berth = shore + dir * berthAlong + right * lateral;
            cp.BerthYaw = WorldGeometry.Yaw(dir);
            float edge = cfg.PierWidth * 0.5f - 0.45f;
            cp.BollardBow = shore + dir * (berthAlong + 11f) + right * edge;
            cp.BollardStern = shore + dir * (berthAlong - 10f) + right * edge;
            cp.Approach = cp.Berth + dir * cfg.ApproachDistance;

            // Beacon on the high ground beside the harbor: the landmark to steer for.
            float side = rng.Value() < 0.5f ? -1f : 1f;
            Vector2 beaconDir = WorldGeometry.Rotate(dir, side * rng.Range(30f, 50f));
            float beaconDist = Mathf.Max(8f, shoreDist * 0.55f);
            cp.Beacon = cp.IslandCenter + beaconDir * beaconDist;
            cp.BeaconGround = Mathf.Max(0.5f, layout.SampleFeaturesOnly(cp.Beacon.x, cp.Beacon.y, only));

            // Dredged basin around the berth + the harbor fairway out to the approach point.
            layout.Carves.Add(new WorldCarve
            {
                A = cp.Berth + dir * 6f,
                B = cp.Berth + dir * 6f,
                Radius = cfg.HarborBasinRadius * 0.6f,
                Falloff = cfg.HarborBasinRadius * 0.4f,
                Depth = cfg.HarborBasinDepth,
            });
            layout.Carves.Add(new WorldCarve
            {
                A = cp.Berth,
                B = cp.Approach,
                Radius = 16f,
                Falloff = 14f,
                Depth = Mathf.Max(cfg.ChannelMinDepth, cfg.HarborBasinDepth - 1f),
            });
        }

        private static bool FootprintClear(WorldLayout layout, IReadOnlyList<WorldFeature> only, Vector2 shore,
            Vector2 dir, Vector2 right, float berthAlong, float lateral)
        {
            for (float a = -ShipHalfLength - 3f; a <= ShipHalfLength + 3f; a += 2f)
            for (float l = -ShipHalfBeam - 3f; l <= ShipHalfBeam + 3f; l += 1.5f)
            {
                Vector2 p = shore + dir * (berthAlong + a) + right * (lateral + l);
                if (layout.SampleFeaturesOnly(p.x, p.y, only) > -0.6f)
                    return false;
            }
            return true;
        }

        // ---- 3. Safe channels ---------------------------------------------------------------------
        private static void BuildChannel(WorldGenConfig cfg, WorldLayout layout, ref WorldRandom rng, int k)
        {
            WorldCluster cluster = layout.Clusters[k];
            WorldCheckpoint from = layout.Checkpoints[k];
            WorldCheckpoint to = layout.Checkpoints[k + 1];
            float d = cluster.Difficulty;

            // Leave the harbor radially until clear of the island's hazard ring, and enter the same way;
            // only the open-water part in between is bent around islands.
            Vector2 exit = RingPoint(layout, k, from);
            Vector2 entry = RingPoint(layout, k + 1, to);
            var middle = new List<Vector2> { exit };
            Vector2 along = (entry - exit).normalized;
            Vector2 perp = new(-along.y, along.x);
            int bends = Mathf.RoundToInt(Mathf.Lerp(cfg.ChannelBendsEasy, cfg.ChannelBendsHard, d));
            float wiggle = Mathf.Lerp(cfg.ChannelWiggleEasy, cfg.ChannelWiggleHard, d);
            float sign = rng.Value() < 0.5f ? -1f : 1f;
            for (int i = 1; i <= bends; i++)
            {
                float t = i / (float)(bends + 1);
                float offset = sign * wiggle * rng.Range(0.55f, 1f) * Mathf.Sin(Mathf.PI * t);
                middle.Add(PushOutOfRings(layout, Vector2.Lerp(exit, entry, t) + perp * offset));
                sign = -sign;
            }
            middle.Add(entry);

            Detour(layout, middle);

            cluster.Channel.Clear();
            cluster.Channel.Add(from.Berth);
            cluster.Channel.Add(from.Approach);
            foreach (Vector2 p in middle)
                if (Vector2.Distance(p, from.Approach) > 1f && Vector2.Distance(p, to.Approach) > 1f)
                    cluster.Channel.Add(p);
            cluster.Channel.Add(to.Approach);
            cluster.Channel.Add(to.Berth);
        }

        private static float KeepRadius(WorldFeature island) => island.HazardRadius + 30f;
        private const float RingFactor = 1.12f;

        /// <summary>Point straight out of the harbor on the island's clearance ring (or the approach if already outside).</summary>
        private static Vector2 RingPoint(WorldLayout layout, int index, WorldCheckpoint cp)
        {
            WorldFeature island = layout.Features[index];
            float ring = KeepRadius(island) * RingFactor;
            Vector2 radial = cp.Approach - island.Center;
            if (radial.magnitude >= ring)
                return cp.Approach;
            Vector2 dir = (radial.normalized + cp.HarborDir).normalized;
            return island.Center + dir * ring;
        }

        private static Vector2 PushOutOfRings(WorldLayout layout, Vector2 p)
        {
            for (int c = 0; c < layout.Checkpoints.Count; c++)
            {
                WorldFeature island = layout.Features[c];
                float ring = KeepRadius(island) * RingFactor;
                Vector2 off = p - island.Center;
                if (off.magnitude < ring)
                    p = island.Center + (off.sqrMagnitude > 1f ? off.normalized : Vector2.right) * ring;
            }
            return p;
        }

        /// <summary>Bends the open-water part of a channel around every checkpoint island.</summary>
        private static void Detour(WorldLayout layout, List<Vector2> line)
        {
            for (int guard = 0; guard < 96; guard++)
            {
                bool changed = false;
                for (int j = 0; j + 1 < line.Count && !changed; j++)
                {
                    for (int c = 0; c < layout.Checkpoints.Count && !changed; c++)
                    {
                        WorldFeature island = layout.Features[c];
                        float keep = KeepRadius(island);
                        Vector2 center = island.Center;
                        Vector2 p = line[j], q = line[j + 1];
                        if (WorldGeometry.DistanceToSegment(center, p, q) >= keep - 0.5f)
                            continue;
                        // An endpoint inside the ring can't be fixed by bending (shouldn't happen past RingPoint).
                        if (Vector2.Distance(p, center) < keep || Vector2.Distance(q, center) < keep)
                            continue;

                        Vector2 pq = q - p;
                        float t = pq.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(center - p, pq) / pq.sqrMagnitude) : 0.5f;
                        Vector2 closest = p + pq * t;
                        Vector2 outDir = closest - center;
                        if (outDir.sqrMagnitude < 1f)
                            outDir = new Vector2(-pq.y, pq.x);
                        outDir.Normalize();
                        line.Insert(j + 1, center + outDir * keep * RingFactor);
                        changed = true;
                    }
                }
                if (!changed)
                    return;
            }
        }

        private static void AddChannelCarves(WorldGenConfig cfg, WorldLayout layout)
        {
            foreach (WorldCluster cluster in layout.Clusters)
            {
                float radius = Mathf.Clamp(cluster.ChannelHalfWidth * 0.4f, 10f, 24f);
                for (int i = 0; i + 1 < cluster.Channel.Count; i++)
                    layout.Carves.Add(new WorldCarve
                    {
                        A = cluster.Channel[i],
                        B = cluster.Channel[i + 1],
                        Radius = radius,
                        Falloff = 12f,
                        Depth = cfg.ChannelMinDepth,
                    });
            }
        }

        // ---- 4. Hazards ----------------------------------------------------------------------------
        private static void PlaceHazards(WorldGenConfig cfg, WorldLayout layout, ref WorldRandom rng, int k)
        {
            WorldCluster cluster = layout.Clusters[k];
            float d = cluster.Difficulty;
            WorldCheckpoint to = layout.Checkpoints[k + 1];

            // Set pieces first: they define the cluster's character.
            if (d >= cfg.NarrowsFrom - 1e-4f)
                cluster.Narrows += PlaceNarrows(layout, ref rng, k, 0.42f + rng.Signed() * 0.06f) ? 1 : 0;
            if (d >= cfg.SecondNarrowsFrom - 1e-4f)
                cluster.Narrows += PlaceNarrows(layout, ref rng, k, 0.7f + rng.Signed() * 0.05f) ? 1 : 0;
            if (d >= cfg.ApproachReefFrom - 1e-4f)
                cluster.ApproachReef = PlaceApproachReef(cfg, layout, ref rng, k, to);
            if (d >= cfg.ApproachCliffsFrom - 1e-4f)
                cluster.ApproachCliffs = PlaceApproachCliffs(cfg, layout, ref rng, k, to);

            int islands = Mathf.RoundToInt(Mathf.Lerp(cfg.IslandsEasy, cfg.IslandsHard, d));
            int rocks = Mathf.RoundToInt(Mathf.Lerp(cfg.RocksEasy, cfg.RocksHard, d));
            int reefs = Mathf.RoundToInt(Mathf.Lerp(cfg.ReefsEasy, cfg.ReefsHard, d));
            float hug = 0.2f + 0.55f * d; // share of rocks / reefs placed right beside the channel

            for (int i = 0; i < islands; i++)
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    WorldFeature f = MakeIsland(ref rng, k);
                    f.Center = RandomInCluster(cfg, layout, ref rng, k);
                    if (!IsClear(layout, f, 0f, true)) continue;
                    layout.Features.Add(f);
                    cluster.Islands++;
                    break;
                }

            for (int i = 0; i < rocks; i++)
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    WorldFeature f = MakeRock(ref rng, k, d);
                    f.Center = rng.Value() < hug
                        ? BesideChannel(layout, ref rng, k, f.HazardRadius)
                        : RandomInCluster(cfg, layout, ref rng, k);
                    if (!IsClear(layout, f, 0f, false)) continue;
                    layout.Features.Add(f);
                    cluster.Rocks++;
                    break;
                }

            for (int i = 0; i < reefs; i++)
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    WorldFeature f = MakeReef(cfg, ref rng, k);
                    f.Center = rng.Value() < hug
                        ? BesideChannel(layout, ref rng, k, f.HazardRadius)
                        : RandomInCluster(cfg, layout, ref rng, k);
                    if (!IsClear(layout, f, 0f, false)) continue;
                    layout.Features.Add(f);
                    AddCoralHeads(layout, ref rng, f, k);
                    cluster.Reefs++;
                    break;
                }
        }

        private static Vector2 RandomInCluster(WorldGenConfig cfg, WorldLayout layout, ref WorldRandom rng, int k)
        {
            Vector2 a = layout.Checkpoints[k].IslandCenter, b = layout.Checkpoints[k + 1].IslandCenter;
            Vector2 along = (b - a).normalized;
            Vector2 perp = new(-along.y, along.x);
            return Vector2.Lerp(a, b, rng.Range(0.1f, 0.9f)) + perp * (rng.Signed() * cfg.ClusterHalfWidth * 0.9f);
        }

        private static Vector2 BesideChannel(WorldLayout layout, ref WorldRandom rng, int k, float hazard)
        {
            WorldCluster cluster = layout.Clusters[k];
            Vector2 p = WorldGeometry.PointAt(cluster.Channel, rng.Range(0.12f, 0.88f), out Vector2 tangent);
            Vector2 normal = new(-tangent.y, tangent.x);
            float side = rng.Value() < 0.5f ? -1f : 1f;
            return p + normal * side * (cluster.ChannelHalfWidth + hazard + rng.Range(2f, 30f));
        }

        /// <summary>
        /// Rejects a feature that would block any channel (within its half-width), crowd a harbor
        /// island, or (for islands) overlap another island.
        /// </summary>
        private static bool IsClear(WorldLayout layout, in WorldFeature f, float channelScale, bool island)
        {
            float hazard = f.HazardRadius;
            foreach (WorldCluster c in layout.Clusters)
            {
                float clearance = Mathf.Max(c.ChannelHalfWidth * (channelScale > 0f ? channelScale : 1f), 14f);
                if (c.Channel.Count > 1 && WorldGeometry.DistanceToPolyline(f.Center, c.Channel) < clearance + hazard)
                    return false;
            }

            for (int i = 0; i < layout.Features.Count; i++)
            {
                WorldFeature other = layout.Features[i];
                float dist = Vector2.Distance(f.Center, other.Center);
                if (other.Kind == WorldFeatureKind.CheckpointIsland)
                {
                    if (dist < other.OutlineRadius + hazard + 35f) return false;
                }
                else if (island && (other.Kind == WorldFeatureKind.Island || other.Kind == WorldFeatureKind.Cliff))
                {
                    if (dist < other.OutlineRadius + f.OutlineRadius + 15f) return false;
                }
            }
            return true;
        }

        private static WorldFeature MakeIsland(ref WorldRandom rng, int k)
        {
            float peak = rng.Range(6f, 28f);
            return new WorldFeature
            {
                Kind = WorldFeatureKind.Island,
                Cluster = k,
                Axis = rng.OnUnitCircle(),
                Stretch = rng.Range(1f, 1.7f),
                Radius = rng.Range(26f, 70f),
                Peak = peak,
                Base = 0f,
                Steepness = rng.Range(0.9f, 1.6f),
                Shelf = rng.Range(28f, 48f),
                LobeAmp = rng.Range(0.15f, 0.32f),
                LobePhase = rng.Range(0f, 6.283f),
                CoastNoise = 0.1f,
                NoiseAmp = Mathf.Min(4f, peak * 0.25f),
                NoiseScale = 0.03f,
                NoiseSeed = rng.NextSeed(),
            };
        }

        private static WorldFeature MakeRock(ref WorldRandom rng, int k, float difficulty)
        {
            bool hidden = rng.Value() < 0.12f + 0.38f * difficulty;
            float peak = hidden ? -rng.Range(0.3f, 1.1f) : rng.Range(2.5f, 9f);
            return new WorldFeature
            {
                Kind = WorldFeatureKind.Rock,
                Cluster = k,
                Axis = rng.OnUnitCircle(),
                Stretch = rng.Range(1f, 1.8f),
                Radius = rng.Range(3.5f, 11f),
                Peak = peak,
                Base = hidden ? peak - 2.5f : 0f,
                Steepness = rng.Range(0.45f, 0.8f),
                Shelf = rng.Range(8f, 16f),
                LobeAmp = rng.Range(0.2f, 0.35f),
                LobePhase = rng.Range(0f, 6.283f),
                CoastNoise = 0.08f,
                NoiseAmp = hidden ? 0.2f : 1f,
                NoiseScale = 0.15f,
                NoiseSeed = rng.NextSeed(),
            };
        }

        private static WorldFeature MakeReef(WorldGenConfig cfg, ref WorldRandom rng, int k)
        {
            float top = -rng.Range(cfg.ReefTopDepthMin, Mathf.Max(cfg.ReefTopDepthMin, cfg.ReefTopDepthMax));
            return new WorldFeature
            {
                Kind = WorldFeatureKind.Reef,
                Cluster = k,
                Axis = rng.OnUnitCircle(),
                Stretch = rng.Range(1.2f, 2.5f),
                Radius = rng.Range(16f, 40f),
                Peak = top,
                Base = top,
                Steepness = 0f,
                Shelf = rng.Range(16f, 28f),
                LobeAmp = rng.Range(0.3f, 0.45f),
                LobePhase = rng.Range(0f, 6.283f),
                CoastNoise = 0.18f,
                NoiseAmp = 0.3f,
                NoiseScale = 0.09f,
                NoiseSeed = rng.NextSeed(),
            };
        }

        /// <summary>A few coral heads breaking the surface: the only visible sign of a reef from deck.</summary>
        private static void AddCoralHeads(WorldLayout layout, ref WorldRandom rng, in WorldFeature reef, int k)
        {
            int count = rng.Range(1, 4);
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = rng.OnUnitCircle() * reef.Radius * rng.Range(0f, 0.55f);
                offset = reef.Axis * (Vector2.Dot(offset, reef.Axis) * reef.Stretch) +
                         new Vector2(-reef.Axis.y, reef.Axis.x) * Vector2.Dot(offset, new Vector2(-reef.Axis.y, reef.Axis.x));
                layout.Features.Add(new WorldFeature
                {
                    Kind = WorldFeatureKind.Rock,
                    Cluster = k,
                    Center = reef.Center + offset,
                    Axis = rng.OnUnitCircle(),
                    Stretch = rng.Range(1f, 1.5f),
                    Radius = rng.Range(1.4f, 3f),
                    Peak = rng.Range(0.5f, 1.5f),
                    Base = 0f,
                    Steepness = 0.6f,
                    Shelf = 3f,
                    LobeAmp = 0.3f,
                    LobePhase = rng.Range(0f, 6.283f),
                    CoastNoise = 0f,
                    NoiseAmp = 0.2f,
                    NoiseScale = 0.4f,
                    NoiseSeed = rng.NextSeed(),
                });
            }
        }

        // ---- Set pieces ----------------------------------------------------------------------------
        /// <summary>Two cliffs flanking the open-water part of the channel: the passage narrows to its width.</summary>
        private static bool PlaceNarrows(WorldLayout layout, ref WorldRandom rng, int k, float t)
        {
            WorldCluster cluster = layout.Clusters[k];
            var middle = cluster.Channel.GetRange(1, cluster.Channel.Count - 2);
            Vector2 p = WorldGeometry.PointAt(middle, t, out Vector2 tangent);
            Vector2 normal = new(-tangent.y, tangent.x);

            var pair = new WorldFeature[2];
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                var cliff = new WorldFeature
                {
                    Kind = WorldFeatureKind.Cliff,
                    Cluster = k,
                    Axis = tangent,
                    Stretch = rng.Range(2.2f, 3f),
                    Radius = rng.Range(22f, 34f),
                    Peak = rng.Range(22f, 40f),
                    Base = 0f,
                    Steepness = 0.28f,
                    Shelf = 10f,
                    LobeAmp = 0.08f,
                    LobePhase = rng.Range(0f, 6.283f),
                    CoastNoise = 0.06f,
                    NoiseAmp = 4f,
                    NoiseScale = 0.045f,
                    NoiseSeed = rng.NextSeed(),
                };
                float across = cliff.Radius * (1f + cliff.LobeAmp) * (1f + cliff.CoastNoise);
                cliff.Center = p + normal * side * (cluster.ChannelHalfWidth + across + 3f);
                if (!ClearOfOtherChannels(layout, cliff, k) || !ClearOfHarborIslands(layout, cliff, 30f) ||
                    Blocks(layout, cliff, cluster.Channel, cluster.ChannelHalfWidth * 0.85f, 4f))
                    return false;
                pair[s] = cliff;
            }
            layout.Features.Add(pair[0]);
            layout.Features.Add(pair[1]);
            return true;
        }

        /// <summary>An arc of reef off the harbor mouth; the fairway (and any departing channel) cut the gaps.</summary>
        private static bool PlaceApproachReef(WorldGenConfig cfg, WorldLayout layout, ref WorldRandom rng, int k, WorldCheckpoint cp)
        {
            float d = layout.Clusters[k].Difficulty;
            float ringR = cfg.ApproachDistance * 0.75f;
            float arc = (70f + 50f * d) * Mathf.Deg2Rad;
            int placed = 0;
            float angle = -arc;
            while (angle <= arc)
            {
                WorldFeature reef = MakeReef(cfg, ref rng, k);
                reef.Radius = rng.Range(11f, 18f);
                reef.Stretch = 1.6f;
                reef.Shelf = 12f;
                Vector2 radial = WorldGeometry.Rotate(cp.HarborDir, angle * Mathf.Rad2Deg);
                reef.Center = cp.Berth + radial * ringR;
                reef.Axis = new Vector2(-radial.y, radial.x);
                angle += reef.Radius * reef.Stretch * 1.7f / ringR;

                bool clear = true;
                foreach (WorldCluster c in layout.Clusters)
                {
                    float gap = Mathf.Max(16f, c.ChannelHalfWidth * 0.6f);
                    if (Blocks(layout, reef, c.Channel, gap, cfg.ChannelMinDepth + 1f))
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear || !ClearOfHarborIslands(layout, reef, 6f))
                    continue;
                layout.Features.Add(reef);
                placed++;
                if (rng.Value() < 0.5f)
                    AddCoralHeads(layout, ref rng, reef, k);
            }
            return placed > 0;
        }

        /// <summary>A cliff gate halfway down the harbor fairway.</summary>
        private static bool PlaceApproachCliffs(WorldGenConfig cfg, WorldLayout layout, ref WorldRandom rng, int k, WorldCheckpoint cp)
        {
            float halfGap = Mathf.Max(22f, layout.Clusters[k].ChannelHalfWidth * 0.8f);
            Vector2 p = cp.Berth + cp.HarborDir * (cfg.ApproachDistance * 0.5f);
            Vector2 normal = cp.Right;
            var pair = new WorldFeature[2];
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                var cliff = new WorldFeature
                {
                    Kind = WorldFeatureKind.Cliff,
                    Cluster = k,
                    Axis = cp.HarborDir,
                    Stretch = rng.Range(1.6f, 2.2f),
                    Radius = rng.Range(14f, 22f),
                    Peak = rng.Range(15f, 28f),
                    Base = 0f,
                    Steepness = 0.3f,
                    Shelf = 9f,
                    LobeAmp = 0.1f,
                    LobePhase = rng.Range(0f, 6.283f),
                    CoastNoise = 0.06f,
                    NoiseAmp = 3f,
                    NoiseScale = 0.05f,
                    NoiseSeed = rng.NextSeed(),
                };
                float across = cliff.Radius * (1f + cliff.LobeAmp) * (1f + cliff.CoastNoise);
                cliff.Center = p + normal * side * (halfGap + across + 2f);
                if (!ClearOfOtherChannels(layout, cliff, k) || !ClearOfHarborIslands(layout, cliff, 8f) ||
                    Blocks(layout, cliff, layout.Clusters[k].Channel, halfGap * 0.85f, 4f))
                    return false;
                pair[s] = cliff;
            }
            layout.Features.Add(pair[0]);
            layout.Features.Add(pair[1]);
            return true;
        }

        private static bool ClearOfOtherChannels(WorldLayout layout, in WorldFeature f, int own)
        {
            foreach (WorldCluster c in layout.Clusters)
            {
                if (c.Index == own) continue;
                if (WorldGeometry.DistanceToPolyline(f.Center, c.Channel) < Mathf.Max(c.ChannelHalfWidth, 14f) + f.HazardRadius)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// True when the feature alone raises the floor above −<paramref name="minDepth"/> anywhere
        /// within <paramref name="halfWidth"/> of the channel centerline.
        /// </summary>
        private static bool Blocks(WorldLayout layout, in WorldFeature f, List<Vector2> line, float halfWidth, float minDepth)
        {
            float reach = f.BoundRadius + halfWidth;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                if (WorldGeometry.DistanceToSegment(f.Center, a, b) > reach)
                    continue;
                float len = Vector2.Distance(a, b);
                if (len < 1e-3f) continue;
                Vector2 dir = (b - a) / len;
                Vector2 n = new(-dir.y, dir.x);
                int steps = Mathf.CeilToInt(len / 4f);
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                    for (int l = -2; l <= 2; l++)
                    {
                        Vector2 q = p + n * (halfWidth * l * 0.5f);
                        if (f.TryEvaluate(q.x, q.y, layout.SeaFloorDepth, out float h) && h > -minDepth)
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool ClearOfHarborIslands(WorldLayout layout, in WorldFeature f, float margin)
        {
            foreach (WorldCheckpoint cp in layout.Checkpoints)
            {
                WorldFeature island = layout.Features[cp.Index];
                if (Vector2.Distance(f.Center, island.Center) < island.OutlineRadius + f.OutlineRadius + margin)
                    return false;
                // Never crowd the berth itself.
                if (Vector2.Distance(f.Center, cp.Berth) < f.HazardRadius + 30f)
                    return false;
            }
            return true;
        }
    }
}
