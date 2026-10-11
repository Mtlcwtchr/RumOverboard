using System;
using System.Threading.Tasks;
using RumOverboard.Gameplay.World.Generation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RumOverboard.Gameplay.World.Map
{
    /// <summary>
    /// Paints the voyage chart — an aged paper sea chart, north up — from the same
    /// <see cref="WorldLayout"/> the geometry is built from, so the map is always truthful.
    ///
    ///   • Base layer (once per world): water tint by depth, water-lining contours, reef stipple,
    ///     coastline ink, hill-shaded land with contour lines, cliff hatching, grid, hidden-rock marks.
    ///   • <see cref="Compose"/> (on every progress change): fog of war over everything outside the
    ///     revealed clusters (legs 0..reached) and checkpoint discs (0..reached+1), harbor markers,
    ///     a red X on the next destination, then chart furniture (frame, compass rose, scale bar).
    /// There's deliberately no ship marker: the crew has to work out where they are.
    /// </summary>
    public sealed class WorldMapPainter : IDisposable
    {
        private static readonly Color Ink = new(0.2f, 0.15f, 0.1f);
        private static readonly Color RedInk = new(0.72f, 0.12f, 0.08f);
        private static readonly Color PaperColor = new(0.93f, 0.87f, 0.72f);
        private static readonly Color FogColor = new(0.66f, 0.6f, 0.48f);

        private const int FogCell = 4; // px per fog-grid cell

        private readonly WorldLayout _layout;
        private readonly WorldGenConfig _cfg;
        private Color32[] _base;
        private Color32[] _pixels;
        private float[] _fogNoise;
        private float[] _legDistance;
        private int _gridW, _gridH;

        public int Width { get; }
        public int Height { get; }
        public Rect WorldRect { get; }
        public float MetersPerPixel { get; }
        public Texture2D Texture { get; private set; }
        public int ComposedReached { get; private set; } = int.MinValue;
        public bool ComposedRevealAll { get; private set; }
        /// <summary>Share of chart pixels not under fog after the last compose (0..1).</summary>
        public float RevealedFraction { get; private set; }
        public Color32[] Pixels => _pixels;

        public WorldMapPainter(WorldLayout layout, WorldGenConfig cfg, bool createTexture = true)
        {
            _layout = layout;
            _cfg = cfg;
            Rect b = layout.Bounds;
            float margin = Mathf.Max(0f, cfg.MapMargin);
            var rect = Rect.MinMaxRect(b.xMin - margin, b.yMin - margin, b.xMax + margin, b.yMax + margin);
            int res = Mathf.Clamp(cfg.MapResolution, 256, 4096);
            float mpp = Mathf.Max(rect.width, rect.height) / res;
            MetersPerPixel = mpp;
            Width = Mathf.Max(64, Mathf.CeilToInt(rect.width / mpp));
            Height = Mathf.Max(64, Mathf.CeilToInt(rect.height / mpp));
            WorldRect = new Rect(rect.xMin, rect.yMin, Width * mpp, Height * mpp);

            PaintBase();
            BuildLegDistances();
            _pixels = new Color32[_base.Length];
            if (createTexture)
            {
                Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true)
                {
                    name = $"VoyageChart_{layout.Seed}",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                };
            }
        }

        public Vector2 WorldToPixel(Vector2 world) =>
            new((world.x - WorldRect.xMin) / MetersPerPixel - 0.5f, (world.y - WorldRect.yMin) / MetersPerPixel - 0.5f);

        public Vector2 WorldToUv(Vector2 world) =>
            new((world.x - WorldRect.xMin) / WorldRect.width, (world.y - WorldRect.yMin) / WorldRect.height);

        private Vector2 PixelToWorld(int i, int j) =>
            new(WorldRect.xMin + (i + 0.5f) * MetersPerPixel, WorldRect.yMin + (j + 0.5f) * MetersPerPixel);

        // ---- Base layer ---------------------------------------------------------------------------
        private void BuildLegDistances()
        {
            int clusters = _layout.ClusterCount;
            _gridW = Width / FogCell + 2;
            _gridH = Height / FogCell + 2;
            int gw = _gridW;
            _legDistance = new float[_gridW * _gridH * clusters];
            Parallel.For(0, _gridH, gj =>
            {
                for (int gi = 0; gi < gw; gi++)
                {
                    var p = new Vector2(WorldRect.xMin + (gi * FogCell + 0.5f) * MetersPerPixel,
                        WorldRect.yMin + (gj * FogCell + 0.5f) * MetersPerPixel);
                    int g = gj * gw + gi;
                    for (int k = 0; k < clusters; k++)
                        _legDistance[g * clusters + k] = _layout.DistanceToLeg(p, k);
                }
            });
        }

        private void PaintBase()
        {
            int w = Width, h = Height;
            var height = new float[w * h];
            var kind = new byte[w * h];
            _fogNoise = new float[w * h];
            int seed = _layout.Seed;

            Parallel.For(0, h, j =>
            {
                for (int i = 0; i < w; i++)
                {
                    Vector2 p = PixelToWorld(i, j);
                    int idx = j * w + i;
                    height[idx] = _layout.Sample(p.x, p.y, out WorldFeatureKind k);
                    kind[idx] = (byte)k;
                    _fogNoise[idx] = WorldNoise.Fbm(p.x * 0.0045f, p.y * 0.0045f, seed + 501, 4) +
                                     0.35f * WorldNoise.Fbm(p.x * 0.03f, p.y * 0.03f, seed + 502, 2);
                }
            });

            _base = new Color32[w * h];
            float mpp = MetersPerPixel;
            float contour = Mathf.Max(1f, _cfg.MapContourStep);
            float grid = Mathf.Max(50f, _cfg.MapGridStep);
            var light = new Vector3(-0.55f, 0.7f, 0.45f).normalized;

            Parallel.For(0, h, j =>
            {
                for (int i = 0; i < w; i++)
                {
                    int idx = j * w + i;
                    float hv = height[idx];
                    float hl = height[j * w + Mathf.Max(i - 1, 0)];
                    float hr = height[j * w + Mathf.Min(i + 1, w - 1)];
                    float hd = height[Mathf.Max(j - 1, 0) * w + i];
                    float hu = height[Mathf.Min(j + 1, h - 1) * w + i];
                    var k = (WorldFeatureKind)kind[idx];
                    Color paper = Paper(i, j, seed);
                    Color c;

                    bool land = hv >= 0f;
                    bool coast = land != (hl >= 0f) || land != (hr >= 0f) || land != (hd >= 0f) || land != (hu >= 0f);

                    if (land)
                    {
                        var n = new Vector3(hl - hr, 2f * mpp, hd - hu).normalized;
                        float shade = Mathf.Clamp01(Vector3.Dot(n, light));
                        Color low = new(0.87f, 0.79f, 0.58f), high = new(0.68f, 0.58f, 0.4f);
                        c = Color.Lerp(low, high, Mathf.Clamp01(hv / 40f)) * (0.72f + 0.38f * shade);
                        float slope = Mathf.Sqrt((hr - hl) * (hr - hl) + (hu - hd) * (hu - hd)) / (2f * mpp);
                        if (k == WorldFeatureKind.Cliff || slope > 1.3f)
                        {
                            c *= 0.84f;
                            if (((i + j) & 3) == 0) c = Color.Lerp(c, Ink, 0.45f);
                        }
                        if (k == WorldFeatureKind.Rock)
                            c = Color.Lerp(c, Ink, 0.55f);
                        if (Crosses(hv, hl, hr, hd, hu, contour))
                            c = Color.Lerp(c, new Color(0.45f, 0.33f, 0.2f), 0.45f);
                        c = Color.Lerp(paper, c, 0.85f);
                    }
                    else
                    {
                        float depth = -hv;
                        Color shallow = paper * new Color(0.88f, 0.97f, 0.98f);
                        Color deep = paper * new Color(0.74f, 0.84f, 0.89f);
                        c = Color.Lerp(shallow, deep, Mathf.Clamp01((depth - 1f) / 14f));
                        // Water lining: soundings at 2 m and 5 m.
                        if (CrossesLevel(hv, hl, hr, hd, hu, -2f) || CrossesLevel(hv, hl, hr, hd, hu, -5f))
                            c = Color.Lerp(c, new Color(0.3f, 0.42f, 0.5f), 0.35f);
                        // Reef / hidden rock stipple.
                        if ((k == WorldFeatureKind.Reef || k == WorldFeatureKind.Rock) && depth < 3.5f)
                        {
                            float dots = WorldNoise.White(i, j, seed + 900);
                            if (dots > 0.62f) c = Color.Lerp(c, new Color(0.62f, 0.3f, 0.22f), 0.75f);
                            else c = Color.Lerp(c, new Color(0.85f, 0.7f, 0.6f), 0.25f);
                        }
                    }

                    if (coast)
                        c = Color.Lerp(c, Ink, 0.9f);

                    Vector2 p = PixelToWorld(i, j);
                    if (OnGrid(p.x, grid, mpp) || OnGrid(p.y, grid, mpp))
                        c = Color.Lerp(c, new Color(0.35f, 0.3f, 0.25f), 0.16f);

                    c.a = 1f;
                    _base[idx] = c;
                }
            });

            // Hidden rocks: the classic "+" danger mark.
            foreach (WorldFeature f in _layout.Features)
            {
                if (!f.IsHidden) continue;
                Vector2 px = WorldToPixel(f.Center);
                DrawLine(_base, px + new Vector2(-4f, 0f), px + new Vector2(4f, 0f), 1.6f, Ink, 0.9f);
                DrawLine(_base, px + new Vector2(0f, -4f), px + new Vector2(0f, 4f), 1.6f, Ink, 0.9f);
            }
        }

        private static bool OnGrid(float v, float step, float mpp)
        {
            float m = Mathf.Repeat(v, step);
            return m < mpp * 0.5f || step - m < mpp * 0.5f;
        }

        private static bool Crosses(float h, float l, float r, float d, float u, float step)
        {
            float b = Mathf.Floor(h / step);
            return b != Mathf.Floor(l / step) || b != Mathf.Floor(r / step) || b != Mathf.Floor(d / step) || b != Mathf.Floor(u / step);
        }

        private static bool CrossesLevel(float h, float l, float r, float d, float u, float level)
        {
            bool above = h >= level;
            return above != (l >= level) || above != (r >= level) || above != (d >= level) || above != (u >= level);
        }

        private Color Paper(int i, int j, int seed)
        {
            float grain = WorldNoise.White(i, j, seed + 7) * 0.05f;
            float blotch = WorldNoise.Fbm(i * 0.012f, j * 0.012f, seed + 8, 3) * 0.06f;
            float u = (i + 0.5f) / Width - 0.5f, v = (j + 0.5f) / Height - 0.5f;
            float vignette = Mathf.Clamp01((u * u + v * v) * 1.4f);
            float k = 1f - grain + blotch - vignette * 0.22f;
            return new Color(PaperColor.r * k, PaperColor.g * k, PaperColor.b * (k - vignette * 0.05f), 1f);
        }

        // ---- Fog of war + markers -----------------------------------------------------------------
        /// <summary>
        /// Recomposes the chart for progress <paramref name="reached"/> (index of the last checkpoint
        /// the ship has arrived at; 0 = only the start harbor). Revealed: legs 0..reached and
        /// checkpoints 0..reached+1.
        /// </summary>
        public void Compose(int reached, bool revealAll = false, bool drawChannels = false)
        {
            int clusters = _layout.ClusterCount;
            reached = Mathf.Clamp(reached, 0, clusters);
            int w = Width, h = Height;
            float halfWidth = Mathf.Max(50f, _cfg.ClusterHalfWidth);
            float edge = Mathf.Max(1f, _cfg.FogEdge);
            float cpRadius = Mathf.Max(30f, _cfg.CheckpointRevealRadius);
            int lastCp = Mathf.Min(reached + 1, _layout.Checkpoints.Count - 1);
            int revealedCount = 0;
            object gate = new();

            // Leg term of the fog score on the coarse grid (distance fields are smooth → bilinear is exact enough).
            int gw = _gridW, gh = _gridH;
            var legScore = new float[gw * gh];
            if (!revealAll)
                Parallel.For(0, gh, gj =>
                {
                    for (int gi = 0; gi < gw; gi++)
                    {
                        int g = gj * gw + gi;
                        float dRev = float.MaxValue, dUnrev = float.MaxValue;
                        for (int k = 0; k < clusters; k++)
                        {
                            float d = _legDistance[g * clusters + k];
                            if (k <= reached) dRev = Mathf.Min(dRev, d);
                            else dUnrev = Mathf.Min(dUnrev, d);
                        }
                        float score = halfWidth - dRev;
                        if (dUnrev < float.MaxValue)
                            score = Mathf.Min(score, (dUnrev - dRev) * 0.5f);
                        legScore[g] = score;
                    }
                });

            Parallel.For(0, h, j =>
            {
                int local = 0;
                float gy = j / (float)FogCell;
                int gj0 = Mathf.Min((int)gy, gh - 2);
                float ty = gy - gj0;
                for (int i = 0; i < w; i++)
                {
                    int idx = j * w + i;
                    float fog = 0f;
                    if (!revealAll)
                    {
                        Vector2 p = PixelToWorld(i, j);
                        float gx = i / (float)FogCell;
                        int gi0 = Mathf.Min((int)gx, gw - 2);
                        float tx = gx - gi0;
                        int g00 = gj0 * gw + gi0;
                        float score = Mathf.Lerp(Mathf.Lerp(legScore[g00], legScore[g00 + 1], tx),
                            Mathf.Lerp(legScore[g00 + gw], legScore[g00 + gw + 1], tx), ty);
                        for (int ci = 0; ci <= lastCp; ci++)
                            score = Mathf.Max(score, cpRadius - Vector2.Distance(p, _layout.Checkpoints[ci].IslandCenter));
                        score += _fogNoise[idx] * edge * 0.9f;
                        fog = 1f - Smooth(-edge * 0.5f, edge * 0.5f, score);

                        Color fogC = FogColor * (0.92f + 0.12f * _fogNoise[idx]);
                        Color baseC = _base[idx];
                        Color c = Color.Lerp(baseC, fogC, fog);
                        // Inked edge of the known waters.
                        if (Mathf.Abs(score) < MetersPerPixel * 1.1f)
                            c = Color.Lerp(c, Ink, 0.35f);
                        c.a = 1f;
                        _pixels[idx] = c;
                    }
                    else
                    {
                        _pixels[idx] = _base[idx];
                    }
                    if (fog < 0.5f) local++;
                }
                lock (gate) revealedCount += local;
            });
            RevealedFraction = revealedCount / (float)(w * h);

            if (drawChannels)
                foreach (WorldCluster c in _layout.Clusters)
                    for (int s = 0; s + 1 < c.Channel.Count; s++)
                        DrawLine(_pixels, WorldToPixel(c.Channel[s]), WorldToPixel(c.Channel[s + 1]), 1.5f,
                            new Color(0.1f, 0.35f, 0.75f), 0.8f);

            // Harbors: known ones get an anchorage ring, visited ones are filled, the next one gets a red X.
            int top = revealAll ? _layout.Checkpoints.Count - 1 : lastCp;
            for (int c = 0; c <= top; c++)
            {
                WorldCheckpoint cp = _layout.Checkpoints[c];
                Vector2 berth = WorldToPixel(cp.Berth);
                DrawRing(_pixels, berth, 6f, 1.8f, Ink, 0.95f);
                if (c <= reached)
                    DrawDisc(_pixels, berth, 3.5f, Ink, 0.95f);
                if (c == 0)
                    DrawRing(_pixels, berth, 10f, 1.2f, Ink, 0.8f);
                if (c == reached + 1)
                {
                    Vector2 x = WorldToPixel(cp.IslandCenter);
                    float s = 11f;
                    DrawLine(_pixels, x + new Vector2(-s, -s), x + new Vector2(s, s), 3.4f, RedInk, 0.95f);
                    DrawLine(_pixels, x + new Vector2(-s, s), x + new Vector2(s, -s), 3.4f, RedInk, 0.95f);
                }
            }

            DrawFurniture(_pixels);

            if (Texture != null)
            {
                Texture.SetPixels32(_pixels);
                Texture.Apply(true);
            }
            ComposedReached = reached;
            ComposedRevealAll = revealAll;
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ---- Chart furniture ----------------------------------------------------------------------
        private void DrawFurniture(Color32[] buf)
        {
            int w = Width, h = Height;
            // Frame: thick outer + thin inner line.
            DrawRect(buf, new Vector2(4f, 4f), new Vector2(w - 5f, h - 5f), 3f, Ink, 0.9f);
            DrawRect(buf, new Vector2(11f, 11f), new Vector2(w - 12f, h - 12f), 1.2f, Ink, 0.7f);

            // Compass rose (top-right).
            float r = Mathf.Clamp(Mathf.Min(w, h) * 0.06f, 28f, 70f);
            var c = new Vector2(w - r - 30f, h - r - 30f);
            DrawRing(buf, c, r, 1.5f, Ink, 0.8f);
            DrawRing(buf, c, r * 0.82f, 1f, Ink, 0.6f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                var side = new Vector2(dir.y, -dir.x);
                float len = i % 2 == 0 ? r * 1.05f : r * 0.6f;
                Color col = i == 0 ? RedInk : Ink;
                DrawLine(buf, c + side * (r * 0.12f), c + dir * len, 1.4f, col, 0.9f);
                DrawLine(buf, c - side * (r * 0.12f), c + dir * len, 1.4f, col, 0.9f);
            }
            // "N"
            Vector2 n0 = c + new Vector2(-r * 0.12f, r * 1.18f), n1 = n0 + new Vector2(0f, r * 0.3f);
            Vector2 n2 = n0 + new Vector2(r * 0.24f, 0f), n3 = n2 + new Vector2(0f, r * 0.3f);
            DrawLine(buf, n0, n1, 2f, Ink, 0.95f);
            DrawLine(buf, n1, n2, 2f, Ink, 0.95f);
            DrawLine(buf, n2, n3, 2f, Ink, 0.95f);

            // Scale bar (bottom-left): 500 m in 100 m blocks.
            float block = 100f / MetersPerPixel;
            var o = new Vector2(28f, 26f);
            for (int i = 0; i < 5; i++)
            {
                Vector2 a = o + new Vector2(i * block, 0f), b = o + new Vector2((i + 1) * block, 0f);
                DrawLine(buf, a, b, 6f, i % 2 == 0 ? Ink : new Color(0.95f, 0.9f, 0.78f), 0.95f);
            }
            DrawRect(buf, o - new Vector2(0f, 3.5f), o + new Vector2(5f * block, 3.5f), 1f, Ink, 0.9f);
        }

        // ---- Raster helpers (pixel space, anti-aliased by distance) ---------------------------------
        private void Blend(Color32[] buf, int i, int j, Color c, float a)
        {
            if (i < 0 || j < 0 || i >= Width || j >= Height || a <= 0f) return;
            int idx = j * Width + i;
            Color dst = buf[idx];
            Color r = Color.Lerp(dst, c, Mathf.Clamp01(a));
            r.a = 1f;
            buf[idx] = r;
        }

        private void DrawLine(Color32[] buf, Vector2 a, Vector2 b, float thickness, Color c, float alpha)
        {
            float half = thickness * 0.5f;
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - half - 1f), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + half + 1f);
            int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - half - 1f), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + half + 1f);
            x0 = Mathf.Max(x0, 0); y0 = Mathf.Max(y0, 0); x1 = Mathf.Min(x1, Width - 1); y1 = Mathf.Min(y1, Height - 1);
            for (int j = y0; j <= y1; j++)
            for (int i = x0; i <= x1; i++)
            {
                float d = WorldGeometry.DistanceToSegment(new Vector2(i, j), a, b);
                float cover = Mathf.Clamp01(half - d + 0.5f);
                if (cover > 0f) Blend(buf, i, j, c, cover * alpha);
            }
        }

        private void DrawDisc(Color32[] buf, Vector2 center, float radius, Color c, float alpha)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - 1f)), x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(center.x + radius + 1f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - 1f)), y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(center.y + radius + 1f));
            for (int j = y0; j <= y1; j++)
            for (int i = x0; i <= x1; i++)
            {
                float d = Vector2.Distance(new Vector2(i, j), center);
                float cover = Mathf.Clamp01(radius - d + 0.5f);
                if (cover > 0f) Blend(buf, i, j, c, cover * alpha);
            }
        }

        private void DrawRing(Color32[] buf, Vector2 center, float radius, float thickness, Color c, float alpha)
        {
            float outer = radius + thickness * 0.5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - outer - 1f)), x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(center.x + outer + 1f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - outer - 1f)), y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(center.y + outer + 1f));
            for (int j = y0; j <= y1; j++)
            for (int i = x0; i <= x1; i++)
            {
                float d = Mathf.Abs(Vector2.Distance(new Vector2(i, j), center) - radius);
                float cover = Mathf.Clamp01(thickness * 0.5f - d + 0.5f);
                if (cover > 0f) Blend(buf, i, j, c, cover * alpha);
            }
        }

        private void DrawRect(Color32[] buf, Vector2 min, Vector2 max, float thickness, Color c, float alpha)
        {
            DrawLine(buf, new Vector2(min.x, min.y), new Vector2(max.x, min.y), thickness, c, alpha);
            DrawLine(buf, new Vector2(max.x, min.y), new Vector2(max.x, max.y), thickness, c, alpha);
            DrawLine(buf, new Vector2(max.x, max.y), new Vector2(min.x, max.y), thickness, c, alpha);
            DrawLine(buf, new Vector2(min.x, max.y), new Vector2(min.x, min.y), thickness, c, alpha);
        }

        public void Dispose()
        {
            if (Texture != null)
            {
                if (Application.isPlaying) Object.Destroy(Texture);
                else Object.DestroyImmediate(Texture);
            }
            Texture = null;
        }
    }
}
