using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using RumOverboard.Gameplay.World.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace RumOverboard.Gameplay.World.Building
{
    /// <summary>Everything one build created; <see cref="Dispose"/> removes it (objects + runtime assets).</summary>
    public sealed class WorldBuildResult
    {
        public GameObject Root;
        public int Chunks;
        public int Colliders;
        public double Milliseconds;
        /// <summary>Bollard per checkpoint: [cp * 2] = bow line, [cp * 2 + 1] = stern line.</summary>
        public readonly List<Transform> Bollards = new();
        public readonly List<Object> OwnedAssets = new();

        public Transform Bollard(int checkpoint, bool stern) =>
            checkpoint * 2 + (stern ? 1 : 0) < Bollards.Count ? Bollards[checkpoint * 2 + (stern ? 1 : 0)] : null;

        public void Dispose()
        {
            if (Root != null)
            {
                Root.SetActive(false);
                Root.transform.SetParent(null, false);
                WorldGeometryBuilder.DestroySafe(Root);
            }
            foreach (Object o in OwnedAssets)
                WorldGeometryBuilder.DestroySafe(o);
            OwnedAssets.Clear();
            Bollards.Clear();
            Root = null;
        }
    }

    /// <summary>
    /// Turns a <see cref="WorldLayout"/> into scene geometry under a root placed at sea level:
    /// terrain chunks (heights + per-chunk color maps computed in parallel, colliders baked in
    /// parallel), harbor piers with posts and bollards, and beacon towers. Built identically on
    /// every peer; only the host's colliders matter for physics (ship grounding), everyone's for
    /// walking ashore.
    /// </summary>
    public static class WorldGeometryBuilder
    {
        private const MeshColliderCookingOptions Cooking =
            MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private sealed class Chunk
        {
            public int Cx, Cz;
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public Color32[] Colors;
            public float MaxHeight;
            public Mesh Mesh;
        }

        public static WorldBuildResult Build(WorldLayout layout, WorldGenConfig cfg, Transform parent)
        {
            var timer = Stopwatch.StartNew();
            var result = new WorldBuildResult();
            var root = new GameObject("GeneratedWorld");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, layout.SeaLevel, 0f);
            root.transform.rotation = Quaternion.identity;
            result.Root = root;

            BuildTerrain(layout, cfg, root.transform, result);
            BuildHarbors(layout, cfg, root.transform, result);

            timer.Stop();
            result.Milliseconds = timer.Elapsed.TotalMilliseconds;
            return result;
        }

        // ---- Terrain ------------------------------------------------------------------------------
        private static void BuildTerrain(WorldLayout layout, WorldGenConfig cfg, Transform root, WorldBuildResult result)
        {
            float size = Mathf.Max(16f, cfg.ChunkSize);
            int cells = Mathf.Clamp(Mathf.RoundToInt(size / Mathf.Max(0.5f, cfg.CellSize)), 4, 250);
            int texRes = Mathf.Clamp(cfg.ColorMapResolution, 8, 512);
            Rect b = layout.Bounds;
            int cx0 = Mathf.FloorToInt(b.xMin / size), cx1 = Mathf.FloorToInt(b.xMax / size);
            int cz0 = Mathf.FloorToInt(b.yMin / size), cz1 = Mathf.FloorToInt(b.yMax / size);

            var chunks = new List<Chunk>();
            for (int cx = cx0; cx <= cx1; cx++)
            for (int cz = cz0; cz <= cz1; cz++)
                if (ChunkTouchesFeatures(layout, cx * size, cz * size, size))
                    chunks.Add(new Chunk { Cx = cx, Cz = cz });

            float floor = -layout.SeaFloorDepth;
            Parallel.For(0, chunks.Count, i => ComputeChunk(layout, cfg, chunks[i], size, cells, texRes, floor));

            int[] triangles = BuildTriangles(cells);
            Material template = cfg.TerrainMaterial != null ? cfg.TerrainMaterial : FallbackMaterial(Color.white);
            var terrainRoot = new GameObject("Terrain").transform;
            terrainRoot.SetParent(root, false);

            var colliderChunks = new List<Chunk>();
            foreach (Chunk c in chunks)
            {
                if (c.Vertices == null)
                    continue; // open sea floor: nothing worth drawing

                var mesh = new Mesh { name = $"WorldChunk_{c.Cx}_{c.Cz}" };
                mesh.indexFormat = c.Vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(c.Vertices);
                mesh.SetNormals(c.Normals);
                mesh.SetUVs(0, c.Uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                c.Mesh = mesh;
                result.OwnedAssets.Add(mesh);

                var tex = new Texture2D(texRes, texRes, TextureFormat.RGBA32, false)
                {
                    name = mesh.name,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                tex.SetPixels32(c.Colors);
                tex.Apply(false, true);
                result.OwnedAssets.Add(tex);

                var mat = new Material(template) { name = mesh.name };
                if (mat.HasProperty(BaseMapId)) mat.SetTexture(BaseMapId, tex);
                if (mat.HasProperty(MainTexId)) mat.SetTexture(MainTexId, tex);
                if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, Color.white);
                result.OwnedAssets.Add(mat);

                var go = new GameObject(mesh.name);
                go.transform.SetParent(terrainRoot, false);
                go.transform.localPosition = new Vector3(c.Cx * size, 0f, c.Cz * size);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = c.MaxHeight > 0f ? ShadowCastingMode.On : ShadowCastingMode.Off;
                go.isStatic = false;
                result.Chunks++;

                // Hull-relevant geometry only (deep floor never touches a keel).
                if (c.MaxHeight > -8f)
                    colliderChunks.Add(c);
            }

            var ids = new EntityId[colliderChunks.Count];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = colliderChunks[i].Mesh.GetEntityId();
            Parallel.For(0, ids.Length, i => Physics.BakeMesh(ids[i], false, Cooking));

            foreach (Chunk c in colliderChunks)
            {
                Transform t = terrainRoot.Find(c.Mesh.name);
                var mc = t.gameObject.AddComponent<MeshCollider>();
                mc.cookingOptions = Cooking;
                mc.sharedMesh = c.Mesh;
                result.Colliders++;
            }
        }

        private static bool ChunkTouchesFeatures(WorldLayout layout, float x0, float z0, float size)
        {
            float step = Mathf.Min(size, WorldLayout.GridCell * 0.5f);
            for (float x = x0; x <= x0 + size + 0.01f; x += step)
            for (float z = z0; z <= z0 + size + 0.01f; z += step)
                if (layout.HasFeaturesNear(x, z))
                    return true;
            return false;
        }

        private static void ComputeChunk(WorldLayout layout, WorldGenConfig cfg, Chunk c, float size, int cells,
            int texRes, float floor)
        {
            float x0 = c.Cx * size, z0 = c.Cz * size;
            float cell = size / cells;
            int n = cells + 1;
            int a = n + 2; // apron for normals
            var heights = new float[a * a];
            float max = float.MinValue;
            for (int j = 0; j < a; j++)
            for (int i = 0; i < a; i++)
            {
                float h = layout.SampleHeight(x0 + (i - 1) * cell, z0 + (j - 1) * cell);
                heights[j * a + i] = h;
                if (i > 0 && j > 0 && i <= n && j <= n)
                    max = Mathf.Max(max, h);
            }

            c.MaxHeight = max;
            if (max < floor + 4f)
                return;

            c.Vertices = new Vector3[n * n];
            c.Normals = new Vector3[n * n];
            c.Uvs = new Vector2[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int ai = (j + 1) * a + (i + 1);
                float h = heights[ai];
                float hl = heights[ai - 1], hr = heights[ai + 1], hd = heights[ai - a], hu = heights[ai + a];
                int v = j * n + i;
                c.Vertices[v] = new Vector3(i * cell, h, j * cell);
                c.Normals[v] = new Vector3(hl - hr, 2f * cell, hd - hu).normalized;
                c.Uvs[v] = new Vector2(i / (float)cells, j / (float)cells);
            }

            c.Colors = new Color32[texRes * texRes];
            float texel = size / texRes;
            int seed = layout.Seed;
            for (int j = 0; j < texRes; j++)
            for (int i = 0; i < texRes; i++)
            {
                float x = x0 + (i + 0.5f) * texel, z = z0 + (j + 0.5f) * texel;
                float h = layout.Sample(x, z, out WorldFeatureKind kind);
                float hx = layout.SampleHeight(x + 1f, z), hz = layout.SampleHeight(x, z + 1f);
                float slope = Mathf.Sqrt((hx - h) * (hx - h) + (hz - h) * (hz - h));
                c.Colors[j * texRes + i] = TerrainColor(cfg, h, slope, kind, x, z, seed);
            }
        }

        private static Color32 TerrainColor(WorldGenConfig cfg, float h, float slope, WorldFeatureKind kind,
            float x, float z, int seed)
        {
            float n = WorldNoise.Fbm(x * 0.08f, z * 0.08f, seed + 11, 2);
            Color c;
            if (h < 0f)
            {
                Color bed = kind switch
                {
                    WorldFeatureKind.Reef => cfg.ReefColor,
                    WorldFeatureKind.Rock or WorldFeatureKind.Cliff => cfg.RockColor,
                    _ => cfg.SeabedColor,
                };
                if (h > -1.2f && kind != WorldFeatureKind.Reef)
                    bed = cfg.SandColor;
                c = Color.Lerp(bed, cfg.SeabedColor * 0.45f, Mathf.Clamp01(-h / 14f));
            }
            else if (kind == WorldFeatureKind.Cliff || kind == WorldFeatureKind.Rock || slope > 1.1f)
            {
                c = Color.Lerp(cfg.RockColor, cfg.CliffColor, Mathf.Clamp01(slope * 0.6f + n * 0.5f));
            }
            else if (h < 1.3f + n * 0.8f)
            {
                c = cfg.SandColor;
            }
            else
            {
                float forest = Mathf.Clamp01(0.5f + n * 1.6f + (h - 6f) * 0.04f);
                c = Color.Lerp(cfg.GrassColor, cfg.ForestColor, forest);
                if (slope > 0.75f)
                    c = Color.Lerp(c, cfg.RockColor, Mathf.Clamp01((slope - 0.75f) * 2.5f));
            }

            float grain = 1f + n * 0.08f;
            return new Color(c.r * grain, c.g * grain, c.b * grain, 1f);
        }

        private static int[] BuildTriangles(int cells)
        {
            int n = cells + 1;
            var tris = new int[cells * cells * 6];
            int t = 0;
            for (int j = 0; j < cells; j++)
            for (int i = 0; i < cells; i++)
            {
                int v = j * n + i;
                tris[t++] = v;
                tris[t++] = v + n;
                tris[t++] = v + 1;
                tris[t++] = v + 1;
                tris[t++] = v + n;
                tris[t++] = v + n + 1;
            }
            return tris;
        }

        // ---- Harbors -------------------------------------------------------------------------------
        private static void BuildHarbors(WorldLayout layout, WorldGenConfig cfg, Transform root, WorldBuildResult result)
        {
            Material wood = cfg.WoodMaterial != null ? cfg.WoodMaterial : Owned(result, FallbackMaterial(new Color(0.45f, 0.32f, 0.2f)));
            Material stone = cfg.StoneMaterial != null ? cfg.StoneMaterial : Owned(result, FallbackMaterial(new Color(0.78f, 0.76f, 0.7f)));
            Material lamp = cfg.LampMaterial != null ? cfg.LampMaterial : Owned(result, UnlitMaterial(new Color(1f, 0.82f, 0.35f)));
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Mesh cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

            foreach (WorldCheckpoint cp in layout.Checkpoints)
            {
                var harbor = new GameObject($"Harbor_{cp.Index}").transform;
                harbor.SetParent(root, false);
                Quaternion yaw = Quaternion.Euler(0f, cp.BerthYaw, 0f);
                float deck = cfg.PierDeckHeight;

                // Deck.
                Vector2 mid = (cp.PierStart + cp.PierEnd) * 0.5f;
                float length = Vector2.Distance(cp.PierStart, cp.PierEnd);
                Transform deckT = Part(harbor, root, "PierDeck", cube, wood, new Vector3(mid.x, deck - 0.15f, mid.y), yaw,
                    new Vector3(cp.PierWidth, 0.3f, length));
                deckT.gameObject.AddComponent<BoxCollider>();

                // Posts down to the seabed.
                Vector2 right = cp.Right;
                for (float along = 0f; along <= length - 6f + 0.01f; along += 4f)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 p = cp.Shore + cp.HarborDir * along + right * (side * (cp.PierWidth * 0.5f - 0.15f));
                        float bed = Mathf.Max(layout.SampleHeight(p.x, p.y), -14f) - 0.5f;
                        float top = deck - 0.3f;
                        if (top - bed < 0.2f) continue;
                        Part(harbor, root, "Post", cylinder, wood, new Vector3(p.x, (top + bed) * 0.5f, p.y), Quaternion.identity,
                            new Vector3(0.32f, (top - bed) * 0.5f, 0.32f));
                    }
                }

                // Fender: keeps a berthed hull from sliding under the deck (no visuals).
                float waterLen = Vector2.Distance(cp.Shore, cp.PierEnd);
                Vector2 fenderMid = cp.Shore + cp.HarborDir * (waterLen * 0.5f) + right * (cp.PierWidth * 0.5f + 0.2f);
                var fender = new GameObject("Fender");
                fender.transform.SetParent(harbor, false);
                fender.transform.SetPositionAndRotation(
                    root.TransformPoint(new Vector3(fenderMid.x, (deck - 2.2f) * 0.5f, fenderMid.y)), yaw);
                fender.AddComponent<BoxCollider>().size = new Vector3(0.4f, deck + 2.2f, waterLen);

                // Bollards (bow line, stern line).
                result.Bollards.Add(Bollard(harbor, root, "BollardBow", cp.BollardBow, deck, cylinder, stone));
                result.Bollards.Add(Bollard(harbor, root, "BollardStern", cp.BollardStern, deck, cylinder, stone));

                // Beacon tower with a lantern: the landmark to steer for.
                float ground = cp.BeaconGround;
                float height = Mathf.Max(4f, cfg.BeaconHeight);
                Transform tower = Part(harbor, root, "Beacon", cylinder, stone,
                    new Vector3(cp.Beacon.x, ground + height * 0.5f - 0.5f, cp.Beacon.y), Quaternion.identity,
                    new Vector3(3f, height * 0.5f, 3f));
                tower.gameObject.AddComponent<CapsuleCollider>();
                Part(harbor, root, "Lantern", cube, lamp, new Vector3(cp.Beacon.x, ground + height + 0.3f, cp.Beacon.y),
                    Quaternion.Euler(0f, 45f, 0f), new Vector3(1.8f, 1.6f, 1.8f));
                Part(harbor, root, "Roof", cylinder, wood, new Vector3(cp.Beacon.x, ground + height + 1.35f, cp.Beacon.y),
                    Quaternion.identity, new Vector3(2.6f, 0.25f, 2.6f));
            }
        }

        private static Transform Bollard(Transform harbor, Transform root, string name, Vector2 p, float deck, Mesh cylinder, Material stone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(harbor, false);
            go.transform.position = root.TransformPoint(new Vector3(p.x, deck, p.y));
            Part(go.transform, root, "Post", cylinder, stone, new Vector3(p.x, deck + 0.3f, p.y), Quaternion.identity,
                new Vector3(0.34f, 0.3f, 0.34f));
            Part(go.transform, root, "Cap", cylinder, stone, new Vector3(p.x, deck + 0.62f, p.y), Quaternion.identity,
                new Vector3(0.5f, 0.04f, 0.5f));
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.24f;
            col.height = 0.9f;
            col.center = new Vector3(0f, 0.4f, 0f);
            return go.transform;
        }

        /// <summary>Visual-only mesh part; <paramref name="position"/> / rotation are in root (sea-level) space.</summary>
        private static Transform Part(Transform parent, Transform root, string name, Mesh mesh, Material mat, Vector3 position,
            Quaternion rotation, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(root.TransformPoint(position), root.rotation * rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // ---- Materials / cleanup -------------------------------------------------------------------
        private static Material Owned(WorldBuildResult result, Material m)
        {
            result.OwnedAssets.Add(m);
            return m;
        }

        public static Material FallbackMaterial(Color color)
        {
            Material m = CreateMaterial("WorldFallback", "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard");
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            return m;
        }

        public static Material UnlitMaterial(Color color)
        {
            Material m = CreateMaterial("WorldUnlit", "Universal Render Pipeline/Unlit", "Unlit/Color", "Universal Render Pipeline/Lit");
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, color);
            return m;
        }

        // Shader.Find only sees shaders that made it into the build; fall back to cloning a config material.
        private static Material CreateMaterial(string name, params string[] shaders)
        {
            foreach (string shaderName in shaders)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader != null)
                    return new Material(shader) { name = name };
            }
            WorldGenConfig cfg = WorldGenConfig.Active;
            Material template = cfg.LampMaterial != null ? cfg.LampMaterial
                : cfg.RopeMaterial != null ? cfg.RopeMaterial
                : cfg.StoneMaterial != null ? cfg.StoneMaterial
                : cfg.WoodMaterial != null ? cfg.WoodMaterial
                : cfg.TerrainMaterial;
            if (template != null)
                return new Material(template) { name = name };
            Debug.LogError($"[World] No shader for '{name}' in this build — assign the WorldGenConfig materials.");
            return new Material(Shader.Find("Hidden/InternalErrorShader")) { name = name };
        }

        public static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
