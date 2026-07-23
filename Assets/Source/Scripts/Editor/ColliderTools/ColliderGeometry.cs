using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.EditorTools.ColliderTools
{
    /// <summary>
    /// Pure geometry helpers used by <see cref="PrimitiveColliderBaker"/>:
    /// principal-axis oriented bounding boxes, bounding spheres, capsule fitting and
    /// voxel-based decomposition of a mesh shell into a small set of axis boxes.
    /// All math works in a single "working space" (the collider root's local space);
    /// the caller is responsible for feeding vertices already transformed into it.
    /// </summary>
    internal static class ColliderGeometry
    {
        /// <summary>An oriented bounding box expressed in the working space.</summary>
        public struct Obb
        {
            public Vector3 Center;      // OBB centre in working space
            public Quaternion Rotation; // maps OBB local axes -> working space
            public Vector3 Size;        // full extents along the OBB's x/y/z axes

            public Vector3 HalfSize => Size * 0.5f;
        }

        /// <summary>A single decomposed box, expressed in the parent OBB's local frame.</summary>
        public struct LocalBox
        {
            public Vector3 Center; // centre in OBB-local coordinates
            public Vector3 Size;   // full size in OBB-local coordinates
        }

        // ---------------------------------------------------------------------
        // Oriented bounding box (PCA)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Fits an oriented bounding box to a point cloud using PCA. The OBB axes are
        /// sorted so that Size.x is the longest extent and Size.z the shortest.
        /// </summary>
        public static bool TryComputeObb(IReadOnlyList<Vector3> points, out Obb obb)
        {
            obb = default;
            int n = points?.Count ?? 0;
            if (n == 0)
                return false;

            Vector3 mean = Vector3.zero;
            for (int i = 0; i < n; i++)
                mean += points[i];
            mean /= n;

            double cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = points[i] - mean;
                cxx += (double)d.x * d.x;
                cxy += (double)d.x * d.y;
                cxz += (double)d.x * d.z;
                cyy += (double)d.y * d.y;
                cyz += (double)d.y * d.z;
                czz += (double)d.z * d.z;
            }

            double inv = 1.0 / n;
            var cov = new double[3, 3]
            {
                { cxx * inv, cxy * inv, cxz * inv },
                { cxy * inv, cyy * inv, cyz * inv },
                { cxz * inv, cyz * inv, czz * inv },
            };

            JacobiEigen(cov, out Vector3 a0, out Vector3 a1, out Vector3 a2);

            // Re-orthonormalise to fight accumulated floating point error and force a
            // right-handed basis so the resulting rotation is well defined.
            a0 = a0.sqrMagnitude > 1e-12f ? a0.normalized : Vector3.right;
            a1 = a1 - Vector3.Dot(a1, a0) * a0;
            a1 = a1.sqrMagnitude > 1e-12f ? a1.normalized : Vector3.Cross(a0, Vector3.up).normalized;
            a2 = Vector3.Cross(a0, a1);

            ProjectExtent(points, a0, out float min0, out float max0);
            ProjectExtent(points, a1, out float min1, out float max1);
            ProjectExtent(points, a2, out float min2, out float max2);

            float c0 = (min0 + max0) * 0.5f;
            float c1 = (min1 + max1) * 0.5f;
            float c2 = (min2 + max2) * 0.5f;

            Vector3 center = a0 * c0 + a1 * c1 + a2 * c2;
            Vector3 size = new Vector3(
                Mathf.Max(max0 - min0, 1e-4f),
                Mathf.Max(max1 - min1, 1e-4f),
                Mathf.Max(max2 - min2, 1e-4f));

            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(a0.x, a0.y, a0.z, 0f));
            m.SetColumn(1, new Vector4(a1.x, a1.y, a1.z, 0f));
            m.SetColumn(2, new Vector4(a2.x, a2.y, a2.z, 0f));
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

            obb = new Obb { Center = center, Rotation = m.rotation, Size = size };
            return true;
        }

        private static void ProjectExtent(IReadOnlyList<Vector3> points, Vector3 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                float d = Vector3.Dot(points[i], axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        /// <summary>Tightest bounding sphere approximation (centroid + farthest point).</summary>
        public static void ComputeBoundingSphere(IReadOnlyList<Vector3> points, out Vector3 center, out float radius)
        {
            center = Vector3.zero;
            radius = 0f;
            int n = points?.Count ?? 0;
            if (n == 0)
                return;

            for (int i = 0; i < n; i++)
                center += points[i];
            center /= n;

            // Ritter-style expansion for a tighter fit than a pure centroid sphere.
            float sqr = 0f;
            for (int i = 0; i < n; i++)
            {
                float d = (points[i] - center).sqrMagnitude;
                if (d > sqr) sqr = d;
            }
            radius = Mathf.Sqrt(sqr);
        }

        // ---------------------------------------------------------------------
        // Voxel shell decomposition
        // ---------------------------------------------------------------------

        /// <summary>
        /// Voxelises the mesh surface inside the OBB frame and greedily merges the
        /// occupied cells into a compact set of axis-aligned (in OBB space) boxes.
        /// The <paramref name="voxelSize"/> is grown automatically if the result would
        /// exceed <paramref name="maxBoxes"/> or the working grid would be too large.
        /// </summary>
        public static List<LocalBox> DecomposeShellToBoxes(
            IReadOnlyList<Vector3> verts,
            IReadOnlyList<int> tris,
            in Obb obb,
            float voxelSize,
            int maxBoxes,
            out float usedVoxelSize,
            out int occupiedCells)
        {
            const int cellCap = 262144; // ~64^3, keeps the grid allocation sane
            usedVoxelSize = Mathf.Max(voxelSize, 1e-3f);
            occupiedCells = 0;

            Quaternion invRot = Quaternion.Inverse(obb.Rotation);
            Vector3 half = obb.HalfSize;

            // Pre-transform vertices into OBB-local space once.
            var local = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
                local[i] = invRot * (verts[i] - obb.Center);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                int nx = Mathf.Max(1, Mathf.CeilToInt(obb.Size.x / usedVoxelSize));
                int ny = Mathf.Max(1, Mathf.CeilToInt(obb.Size.y / usedVoxelSize));
                int nz = Mathf.Max(1, Mathf.CeilToInt(obb.Size.z / usedVoxelSize));

                if ((long)nx * ny * nz > cellCap)
                {
                    usedVoxelSize *= 1.6f;
                    continue;
                }

                var occ = new bool[nx, ny, nz];
                float voxHalf = usedVoxelSize * 0.5f;
                var boxHalf = new Vector3(voxHalf, voxHalf, voxHalf);
                int marked = 0;

                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    Vector3 a = local[tris[t]];
                    Vector3 b = local[tris[t + 1]];
                    Vector3 c = local[tris[t + 2]];

                    Vector3 triMin = Vector3.Min(Vector3.Min(a, b), c);
                    Vector3 triMax = Vector3.Max(Vector3.Max(a, b), c);

                    int i0 = Mathf.Clamp(Mathf.FloorToInt((triMin.x + half.x) / usedVoxelSize), 0, nx - 1);
                    int i1 = Mathf.Clamp(Mathf.FloorToInt((triMax.x + half.x) / usedVoxelSize), 0, nx - 1);
                    int j0 = Mathf.Clamp(Mathf.FloorToInt((triMin.y + half.y) / usedVoxelSize), 0, ny - 1);
                    int j1 = Mathf.Clamp(Mathf.FloorToInt((triMax.y + half.y) / usedVoxelSize), 0, ny - 1);
                    int k0 = Mathf.Clamp(Mathf.FloorToInt((triMin.z + half.z) / usedVoxelSize), 0, nz - 1);
                    int k1 = Mathf.Clamp(Mathf.FloorToInt((triMax.z + half.z) / usedVoxelSize), 0, nz - 1);

                    for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    for (int k = k0; k <= k1; k++)
                    {
                        if (occ[i, j, k])
                            continue;

                        Vector3 cell = new Vector3(
                            -half.x + (i + 0.5f) * usedVoxelSize,
                            -half.y + (j + 0.5f) * usedVoxelSize,
                            -half.z + (k + 0.5f) * usedVoxelSize);

                        if (TriBoxOverlap(cell, boxHalf, a, b, c))
                        {
                            occ[i, j, k] = true;
                            marked++;
                        }
                    }
                }

                occupiedCells = marked;
                List<LocalBox> boxes = GreedyMerge(occ, nx, ny, nz, half, usedVoxelSize);

                if (boxes.Count <= maxBoxes || attempt == 5)
                    return boxes;

                usedVoxelSize *= 1.5f;
            }

            return new List<LocalBox>();
        }

        private static List<LocalBox> GreedyMerge(bool[,,] occ, int nx, int ny, int nz, Vector3 half, float voxel)
        {
            var used = new bool[nx, ny, nz];
            var result = new List<LocalBox>();

            for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                if (!occ[i, j, k] || used[i, j, k])
                    continue;

                // Grow along x.
                int i1 = i;
                while (i1 + 1 < nx && occ[i1 + 1, j, k] && !used[i1 + 1, j, k])
                    i1++;

                // Grow along y while the whole [i..i1] row is available.
                int j1 = j;
                while (j1 + 1 < ny && RowFree(occ, used, i, i1, j1 + 1, k))
                    j1++;

                // Grow along z while the whole [i..i1]x[j..j1] slab is available.
                int k1 = k;
                while (k1 + 1 < nz && SlabFree(occ, used, i, i1, j, j1, k1 + 1))
                    k1++;

                for (int z = k; z <= k1; z++)
                for (int y = j; y <= j1; y++)
                for (int x = i; x <= i1; x++)
                    used[x, y, z] = true;

                Vector3 min = new Vector3(-half.x + i * voxel, -half.y + j * voxel, -half.z + k * voxel);
                Vector3 max = new Vector3(-half.x + (i1 + 1) * voxel, -half.y + (j1 + 1) * voxel, -half.z + (k1 + 1) * voxel);

                result.Add(new LocalBox
                {
                    Center = (min + max) * 0.5f,
                    Size = max - min,
                });
            }

            return result;
        }

        private static bool RowFree(bool[,,] occ, bool[,,] used, int i0, int i1, int j, int k)
        {
            for (int i = i0; i <= i1; i++)
                if (!occ[i, j, k] || used[i, j, k])
                    return false;
            return true;
        }

        private static bool SlabFree(bool[,,] occ, bool[,,] used, int i0, int i1, int j0, int j1, int k)
        {
            for (int j = j0; j <= j1; j++)
                if (!RowFree(occ, used, i0, i1, j, k))
                    return false;
            return true;
        }

        // ---------------------------------------------------------------------
        // Limb decomposition (for masts and other multi-part / oriented shapes)
        // ---------------------------------------------------------------------

        /// <summary>A fitted collider primitive expressed in the working (root-local) space.</summary>
        public struct FittedPrimitive
        {
            public bool IsCapsule;      // true = capsule, false = box
            public Vector3 Center;      // world/root-local centre
            public Quaternion Rotation; // orientation of this primitive
            public Vector3 Size;        // box full size (box only)
            public float Radius;        // capsule radius
            public float Height;        // capsule total height along Direction
            public int Direction;       // capsule axis in local frame (0 = x)
        }

        /// <summary>
        /// Splits a mesh into connected components (welded islands) and fits each one
        /// independently, so a compound part like a mast (vertical pole + horizontal yards)
        /// becomes a set of individually-oriented primitives instead of one bounding shape:
        /// <list type="bullet">
        /// <item>a straight or tapered pole/spar -> a single capsule aligned to it;</item>
        /// <item>a flat plate -> a single oriented box;</item>
        /// <item>a welded cross / irregular chunk -> voxel boxes covering it.</item>
        /// </list>
        /// This gives per-limb rotation, keeps uniform beams as one collider, and never
        /// emits stray spheres.
        /// </summary>
        public static List<FittedPrimitive> DecomposeToLimbs(
            IReadOnlyList<Vector3> verts,
            IReadOnlyList<int> tris,
            float elongation,
            float radiusPercentile,
            float minComponentSize,
            float keepFraction,
            float voxelSize,
            int maxBoxes)
        {
            var result = new List<FittedPrimitive>();
            List<LimbComponent> comps = BuildComponents(verts, tris, minComponentSize);
            if (comps.Count == 0)
                return result;

            // --- Drop small side pieces so a stick + its fittings collapses to one collider.
            //     A component is dropped when it is either much smaller than the biggest limb
            //     (decorative rings/bands/cleats) or fully swallowed by a larger one. The
            //     largest limb and any substantial feature (e.g. the crow's nest) survive. ---
            comps.Sort((a, b) => b.Obb.Size.x.CompareTo(a.Obb.Size.x));
            var keep = new bool[comps.Count];
            for (int i = 0; i < comps.Count; i++)
                keep[i] = true;

            float maxLen = comps.Count > 0 ? comps[0].Obb.Size.x : 0f; // largest, after sort
            float minKeepLen = maxLen * Mathf.Clamp01(keepFraction);

            for (int i = 1; i < comps.Count; i++)
            {
                if (comps[i].Obb.Size.x < minKeepLen)
                    keep[i] = false;
            }

            for (int i = 0; i < comps.Count; i++)
            {
                if (!keep[i])
                    continue;
                for (int j = i + 1; j < comps.Count; j++)
                {
                    if (keep[j] && ObbContains(comps[i].Obb, comps[j].Obb))
                        keep[j] = false;
                }
            }

            // --- Fit the survivors independently. ---
            for (int i = 0; i < comps.Count; i++)
            {
                if (keep[i])
                    FitComponent(comps[i].Verts, comps[i].Tris, comps[i].Obb, elongation, radiusPercentile, voxelSize, maxBoxes, result);
            }

            return result;
        }

        /// <summary>
        /// Fits every rope/cable strand with a single capsule aligned to it. Each connected
        /// strand becomes one climbable capsule (radius floored to <paramref name="minRadius"/>
        /// so thin ropes are still grab-able). Unlike limb decomposition it keeps every strand
        /// (no size dropping) — rigging is made of many similar cords that all need colliders.
        /// </summary>
        public static List<FittedPrimitive> DecomposeToRopeCapsules(
            IReadOnlyList<Vector3> verts,
            IReadOnlyList<int> tris,
            float radiusPercentile,
            float minComponentSize,
            float minRadius)
        {
            var result = new List<FittedPrimitive>();
            List<LimbComponent> comps = BuildComponents(verts, tris, minComponentSize);

            foreach (LimbComponent comp in comps)
            {
                Obb obb = comp.Obb;
                Quaternion invRot = Quaternion.Inverse(obb.Rotation);

                var dists = new List<float>(comp.Verts.Count);
                for (int i = 0; i < comp.Verts.Count; i++)
                {
                    Vector3 p = invRot * (comp.Verts[i] - obb.Center);
                    dists.Add(Mathf.Sqrt(p.y * p.y + p.z * p.z)); // distance to the strand axis
                }
                dists.Sort();
                int idx = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(radiusPercentile) * (dists.Count - 1)), 0, dists.Count - 1);
                float radius = Mathf.Max(dists.Count > 0 ? dists[idx] : 0f, minRadius);

                result.Add(new FittedPrimitive
                {
                    IsCapsule = true,
                    Center = obb.Center,
                    Rotation = obb.Rotation,
                    Radius = radius,
                    Height = Mathf.Max(obb.Size.x + radius * 2f, radius * 2f),
                    Direction = 0,
                });
            }

            return result;
        }

        /// <summary>
        /// Simplifies a staircase to at most three colliders: ONE thin inclined slab (a ramp)
        /// spanning the whole run of treads, plus (when <paramref name="includeRailings"/>) one
        /// panel box per side railing. The slope/height are measured from the central width band
        /// so side railings never raise the ramp, and it does not depend on how the steps are
        /// split into sub-meshes — a stair modelled as N separate steps still yields one ramp.
        /// <paramref name="up"/> is the deck-up direction in working space.
        /// </summary>
        public static List<FittedPrimitive> DecomposeStairs(
            IReadOnlyList<Vector3> verts,
            IReadOnlyList<int> tris,
            Vector3 up,
            float minSize,
            float rampThickness,
            bool includeRailings,
            float clusterGap)
        {
            var result = new List<FittedPrimitive>();

            // A single mesh often bundles several separate flights. Split into connected
            // components (steps may be individual islands), then group nearby islands into one
            // flight each, and fit ONE ramp per flight.
            List<LimbComponent> comps = BuildComponents(verts, tris, minSize);
            List<List<Vector3>> flights = ClusterFlights(comps, clusterGap);
            if (flights.Count == 0)
                flights.Add(new List<Vector3>(verts)); // fallback: treat everything as one flight

            foreach (List<Vector3> flightVerts in flights)
            {
                if (!TryComputeRamp(flightVerts, up, rampThickness, out FittedPrimitive ramp, out RampFrame frame))
                    continue;

                result.Add(ramp);
                if (includeRailings)
                    AddRailings(flightVerts, frame, minSize, result);
            }

            return result;
        }

        // Groups connected components into flights: two components join the same flight when
        // their axis-aligned bounds are within clusterGap of each other (adjacent steps touch;
        // separate staircases sit apart), via union-find over the proximity graph.
        private static List<List<Vector3>> ClusterFlights(List<LimbComponent> comps, float clusterGap)
        {
            var flights = new List<List<Vector3>>();
            int c = comps.Count;
            if (c == 0)
                return flights;

            var min = new Vector3[c];
            var max = new Vector3[c];
            for (int i = 0; i < c; i++)
            {
                Vector3 lo = comps[i].Verts[0], hi = comps[i].Verts[0];
                List<Vector3> vs = comps[i].Verts;
                for (int k = 1; k < vs.Count; k++)
                {
                    lo = Vector3.Min(lo, vs[k]);
                    hi = Vector3.Max(hi, vs[k]);
                }
                min[i] = lo;
                max[i] = hi;
            }

            var parent = new int[c];
            for (int i = 0; i < c; i++)
                parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }

            float g = Mathf.Max(clusterGap, 0f);
            for (int i = 0; i < c; i++)
            for (int j = i + 1; j < c; j++)
            {
                bool overlap =
                    min[i].x - g <= max[j].x && max[i].x + g >= min[j].x &&
                    min[i].y - g <= max[j].y && max[i].y + g >= min[j].y &&
                    min[i].z - g <= max[j].z && max[i].z + g >= min[j].z;
                if (overlap)
                {
                    int ra = Find(i), rb = Find(j);
                    if (ra != rb) parent[ra] = rb;
                }
            }

            var byRoot = new Dictionary<int, List<Vector3>>();
            for (int i = 0; i < c; i++)
            {
                int r = Find(i);
                if (!byRoot.TryGetValue(r, out List<Vector3> list))
                    byRoot[r] = list = new List<Vector3>();
                list.AddRange(comps[i].Verts);
            }

            foreach (var kvp in byRoot)
                flights.Add(kvp.Value);
            return flights;
        }

        private struct RampFrame
        {
            public Vector3 RunAxis, WidthAxis, Up;
            public float RMin, Run, YLow, YHigh, WMid, Thickness;
        }

        // Everything sitting above the tread (ramp) surface at the left / right of centre is a
        // railing; each side collapses to a single oriented panel box.
        private static void AddRailings(IReadOnlyList<Vector3> verts, in RampFrame f, float minSize, List<FittedPrimitive> result)
        {
            float margin = Mathf.Max(0.1f, f.Thickness * 0.5f);
            var left = new List<Vector3>();
            var right = new List<Vector3>();

            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                float r = Vector3.Dot(v, f.RunAxis);
                float w = Vector3.Dot(v, f.WidthAxis);
                float y = Vector3.Dot(v, f.Up);
                float t = f.Run > 1e-4f ? Mathf.Clamp01((r - f.RMin) / f.Run) : 0f;
                float yRamp = Mathf.Lerp(f.YLow, f.YHigh, t);
                if (y <= yRamp + margin)
                    continue; // on/under the walkable surface -> not a railing

                if (w < f.WMid) left.Add(v);
                else right.Add(v);
            }

            AddPanel(left, minSize, result);
            AddPanel(right, minSize, result);
        }

        private static void AddPanel(List<Vector3> pts, float minSize, List<FittedPrimitive> result)
        {
            if (pts.Count < 6 || !TryComputeObb(pts, out Obb o) || o.Size.x < minSize)
                return;
            result.Add(new FittedPrimitive { IsCapsule = false, Center = o.Center, Rotation = o.Rotation, Size = o.Size });
        }

        // Fits a thin slab tilted to the staircase slope, its top face along the tread-nosing
        // line. Slope/height come from the central width band so railings do not raise the ramp.
        private static bool TryComputeRamp(IReadOnlyList<Vector3> verts, Vector3 up, float thickness, out FittedPrimitive ramp, out RampFrame frame)
        {
            ramp = default;
            frame = default;
            int n = verts?.Count ?? 0;
            if (n < 3)
                return false;

            up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;

            // Horizontal basis.
            Vector3 e1 = Vector3.Cross(up, Vector3.forward);
            if (e1.sqrMagnitude < 1e-6f)
                e1 = Vector3.Cross(up, Vector3.right);
            e1.Normalize();
            Vector3 e2 = Vector3.Cross(up, e1).normalized;

            Vector3 mean = Vector3.zero;
            for (int i = 0; i < n; i++)
                mean += verts[i];
            mean /= n;

            // The climb (run) direction is the steepest-ascent direction of height over the
            // horizontal plane: fit y = p*a + q*b (centred) and take the gradient (p, q). This
            // captures the true slope even when the staircase is wider than it is long, where a
            // PCA/extent-based axis would pick the width and yield a flat, horizontal ramp.
            double saa = 0, sab = 0, sbb = 0, say = 0, sby = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = verts[i] - mean;
                double a = Vector3.Dot(d, e1);
                double b = Vector3.Dot(d, e2);
                double y = Vector3.Dot(d, up);
                saa += a * a;
                sab += a * b;
                sbb += b * b;
                say += a * y;
                sby += b * y;
            }

            double det = saa * sbb - sab * sab;
            Vector3 runAxis;
            if (System.Math.Abs(det) > 1e-9)
            {
                double p = (sbb * say - sab * sby) / det;
                double q = (saa * sby - sab * say) / det;
                Vector3 grad = e1 * (float)p + e2 * (float)q;
                runAxis = grad.sqrMagnitude > 1e-8f ? grad.normalized : e1;
            }
            else
            {
                runAxis = e1;
            }
            Vector3 widthAxis = Vector3.Cross(up, runAxis).normalized;

            float rMin = float.MaxValue, rMax = float.MinValue;
            float wMin = float.MaxValue, wMax = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float r = Vector3.Dot(verts[i], runAxis);
                float w = Vector3.Dot(verts[i], widthAxis);
                if (r < rMin) rMin = r; if (r > rMax) rMax = r;
                if (w < wMin) wMin = w; if (w > wMax) wMax = w;
            }

            float run = rMax - rMin;
            float width = wMax - wMin;
            float wMid = (wMin + wMax) * 0.5f;
            if (run < 1e-3f)
                return false;

            // Tread height at each end, measured only in the central width band so the two
            // side railings (which run the whole length) do not lift the sampled height.
            float centralHalf = 0.35f * width;
            float loCut = rMin + run * 0.2f;
            float hiCut = rMax - run * 0.2f;
            float yLow = SampleTreadTop(verts, runAxis, widthAxis, up, wMid, centralHalf, rMin, loCut, true, run);
            float yHigh = SampleTreadTop(verts, runAxis, widthAxis, up, wMid, centralHalf, hiCut, rMax, false, run);
            if (float.IsNaN(yLow) || float.IsNaN(yHigh))
                return false;

            Vector3 a3 = runAxis * rMin + widthAxis * wMid + up * yLow;
            Vector3 b3 = runAxis * rMax + widthAxis * wMid + up * yHigh;

            Vector3 xAxis = b3 - a3;
            float length = xAxis.magnitude;
            if (length < 1e-3f)
                return false;
            xAxis /= length;

            Vector3 zAxis = widthAxis;
            Vector3 yAxis = Vector3.Cross(zAxis, xAxis).normalized;
            if (Vector3.Dot(yAxis, up) < 0f)
            {
                zAxis = -zAxis;
                yAxis = Vector3.Cross(zAxis, xAxis).normalized;
            }

            thickness = Mathf.Max(thickness, 0.05f);
            Vector3 topCenter = (a3 + b3) * 0.5f;
            Vector3 center = topCenter - yAxis * (thickness * 0.5f);

            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(xAxis.x, xAxis.y, xAxis.z, 0f));
            m.SetColumn(1, new Vector4(yAxis.x, yAxis.y, yAxis.z, 0f));
            m.SetColumn(2, new Vector4(zAxis.x, zAxis.y, zAxis.z, 0f));
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

            ramp = new FittedPrimitive
            {
                IsCapsule = false,
                Center = center,
                Rotation = m.rotation,
                Size = new Vector3(length, thickness, Mathf.Max(width, 0.05f)),
            };

            frame = new RampFrame
            {
                RunAxis = runAxis,
                WidthAxis = widthAxis,
                Up = up,
                RMin = rMin,
                Run = run,
                YLow = yLow,
                YHigh = yHigh,
                WMid = wMid,
                Thickness = thickness,
            };
            return true;
        }

        // Max height inside [rLo, rHi] within the central width band; falls back to the whole
        // width if the central band is empty at that end. Returns NaN when no vertices qualify.
        private static float SampleTreadTop(
            IReadOnlyList<Vector3> verts, Vector3 runAxis, Vector3 widthAxis, Vector3 up,
            float wMid, float centralHalf, float rLo, float rHi, bool low, float run)
        {
            float best = float.NaN;
            for (int pass = 0; pass < 2; pass++)
            {
                float half = pass == 0 ? centralHalf : float.MaxValue; // widen on the fallback pass
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 v = verts[i];
                    float r = Vector3.Dot(v, runAxis);
                    if (r < rLo || r > rHi)
                        continue;
                    if (Mathf.Abs(Vector3.Dot(v, widthAxis) - wMid) > half)
                        continue;
                    float y = Vector3.Dot(v, up);
                    if (float.IsNaN(best) || y > best)
                        best = y;
                }
                if (!float.IsNaN(best))
                    break;
            }
            return best;
        }

        // ---------------------------------------------------------------------
        // Floor plan decomposition (flat slabs that preserve holes / hatches)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Rasterises a flat floor onto a 2D grid in its own plane (occupied where the mesh has
        /// surface, empty at holes/hatches) and greedily merges the occupied cells into a few
        /// thin slabs. Openings stay open — so a hatch you drop through is not sealed by one big
        /// floor box. <paramref name="cellSize"/> controls how finely the outline is followed.
        /// </summary>
        public static List<FittedPrimitive> DecomposeFloor(
            IReadOnlyList<Vector3> verts,
            IReadOnlyList<int> tris,
            float cellSize,
            int maxBoxes,
            bool fillHoles)
        {
            var result = new List<FittedPrimitive>();
            if (!TryComputeObb(verts, out Obb obb))
                return result;

            Quaternion invRot = Quaternion.Inverse(obb.Rotation);
            Vector3 half = obb.HalfSize;
            float sizeX = obb.Size.x, sizeY = obb.Size.y, sizeZ = obb.Size.z;

            // Project every vertex into the floor plane (OBB x/y; z is the thickness axis).
            int n = verts.Count;
            var lx = new float[n];
            var ly = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = invRot * (verts[i] - obb.Center);
                lx[i] = p.x;
                ly[i] = p.y;
            }

            const int cellCap = 65536; // 256x256
            float used = Mathf.Max(cellSize, 0.05f);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                int nx = Mathf.Max(1, Mathf.CeilToInt(sizeX / used));
                int ny = Mathf.Max(1, Mathf.CeilToInt(sizeY / used));
                if ((long)nx * ny > cellCap)
                {
                    used *= 1.6f;
                    continue;
                }

                var occ = new bool[nx, ny];
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    var a = new Vector2(lx[i0], ly[i0]);
                    var b = new Vector2(lx[i1], ly[i1]);
                    var c = new Vector2(lx[i2], ly[i2]);

                    float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
                    float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                    float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                    float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

                    int ci0 = Mathf.Clamp(Mathf.FloorToInt((minX + half.x) / used), 0, nx - 1);
                    int ci1 = Mathf.Clamp(Mathf.FloorToInt((maxX + half.x) / used), 0, nx - 1);
                    int cj0 = Mathf.Clamp(Mathf.FloorToInt((minY + half.y) / used), 0, ny - 1);
                    int cj1 = Mathf.Clamp(Mathf.FloorToInt((maxY + half.y) / used), 0, ny - 1);

                    for (int ci = ci0; ci <= ci1; ci++)
                    for (int cj = cj0; cj <= cj1; cj++)
                    {
                        if (occ[ci, cj])
                            continue;
                        float px = -half.x + (ci + 0.5f) * used;
                        float py = -half.y + (cj + 0.5f) * used;
                        if (PointInTri(px, py, a, b, c))
                            occ[ci, cj] = true;
                    }
                }

                if (fillHoles)
                    FillInteriorHoles(occ, nx, ny);

                List<RectI> rects = GreedyMerge2D(occ, nx, ny);
                if (rects.Count <= maxBoxes || attempt == 5)
                {
                    float thickness = Mathf.Max(sizeZ, 0.05f);
                    for (int i = 0; i < rects.Count; i++)
                    {
                        RectI r = rects[i];
                        float x0 = -half.x + r.I0 * used, x1 = -half.x + (r.I1 + 1) * used;
                        float y0 = -half.y + r.J0 * used, y1 = -half.y + (r.J1 + 1) * used;
                        Vector3 localCenter = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0f);
                        result.Add(new FittedPrimitive
                        {
                            IsCapsule = false,
                            Center = obb.Center + obb.Rotation * localCenter,
                            Rotation = obb.Rotation,
                            Size = new Vector3(x1 - x0, y1 - y0, thickness),
                        });
                    }
                    return result;
                }

                used *= 1.5f;
            }

            return result;
        }

        private static bool PointInTri(float px, float py, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (px - c.x) * (a.y - c.y) - (a.x - c.x) * (py - c.y);
            float d2 = (px - a.x) * (b.y - a.y) - (b.x - a.x) * (py - a.y);
            float d3 = (px - b.x) * (c.y - b.y) - (c.x - b.x) * (py - b.y);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        private static List<RectI> GreedyMerge2D(bool[,] occ, int nx, int ny)
        {
            var used = new bool[nx, ny];
            var result = new List<RectI>();

            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                if (!occ[i, j] || used[i, j])
                    continue;

                int i1 = i;
                while (i1 + 1 < nx && occ[i1 + 1, j] && !used[i1 + 1, j])
                    i1++;

                int j1 = j;
                while (j1 + 1 < ny && RowFree2D(occ, used, i, i1, j1 + 1))
                    j1++;

                for (int y = j; y <= j1; y++)
                for (int x = i; x <= i1; x++)
                    used[x, y] = true;

                result.Add(new RectI { I0 = i, I1 = i1, J0 = j, J1 = j1 });
            }

            return result;
        }

        private static bool RowFree2D(bool[,] occ, bool[,] used, int i0, int i1, int j)
        {
            for (int i = i0; i <= i1; i++)
                if (!occ[i, j] || used[i, j])
                    return false;
            return true;
        }

        // Flood-fills empty cells reachable from the grid border; any empty cell left unvisited
        // is an enclosed hole (a hatch) and gets marked occupied, so the floor reads as solid.
        private static void FillInteriorHoles(bool[,] occ, int nx, int ny)
        {
            var visited = new bool[nx, ny];
            var stack = new Stack<int>();

            void Push(int i, int j)
            {
                if (i < 0 || i >= nx || j < 0 || j >= ny) return;
                if (occ[i, j] || visited[i, j]) return;
                visited[i, j] = true;
                stack.Push(i * ny + j);
            }

            for (int i = 0; i < nx; i++) { Push(i, 0); Push(i, ny - 1); }
            for (int j = 0; j < ny; j++) { Push(0, j); Push(nx - 1, j); }

            while (stack.Count > 0)
            {
                int c = stack.Pop();
                int i = c / ny, j = c % ny;
                Push(i + 1, j); Push(i - 1, j); Push(i, j + 1); Push(i, j - 1);
            }

            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                if (!occ[i, j] && !visited[i, j])
                    occ[i, j] = true;
        }

        /// <summary>
        /// Fits railings/fences with shape-preserving oriented boxes. Connected components are
        /// grouped by proximity into runs (balusters + gaps + rail of one railing = one run;
        /// separate railings stay separate). Each run's centreline is traced and simplified
        /// (Douglas–Peucker), so a straight run becomes one box while a curved run (bow / stern)
        /// becomes a short chain of boxes that follows the curve. Baluster detail is dropped;
        /// the overall shape is kept. <paramref name="curveTolerance"/> is the max deviation (in
        /// working units) allowed before a run is split into another box.
        /// </summary>
        public static List<FittedPrimitive> DecomposeFence(
            IReadOnlyList<Vector3> verts,
            IReadOnlyList<int> tris,
            Vector3 up,
            float minComponentSize,
            float clusterGap,
            float curveTolerance)
        {
            var result = new List<FittedPrimitive>();
            List<LimbComponent> comps = BuildComponents(verts, tris, minComponentSize);
            List<List<Vector3>> clusters = ClusterFlights(comps, clusterGap);
            up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;

            foreach (List<Vector3> run in clusters)
                SplitRunIntoBoxes(run, up, curveTolerance, result);

            return result;
        }

        private static void SplitRunIntoBoxes(List<Vector3> verts, Vector3 up, float tol, List<FittedPrimitive> result)
        {
            int n = verts.Count;
            if (n < 6)
            {
                AddObbBox(verts, result);
                return;
            }

            Vector3 e1 = Vector3.Cross(up, Vector3.forward);
            if (e1.sqrMagnitude < 1e-6f)
                e1 = Vector3.Cross(up, Vector3.right);
            e1.Normalize();
            Vector3 e2 = Vector3.Cross(up, e1).normalized;

            Vector3 mean = Vector3.zero;
            for (int i = 0; i < n; i++)
                mean += verts[i];
            mean /= n;

            double caa = 0, cab = 0, cbb = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = verts[i] - mean;
                double a = Vector3.Dot(d, e1), b = Vector3.Dot(d, e2);
                caa += a * a; cab += a * b; cbb += b * b;
            }
            double angle = 0.5 * System.Math.Atan2(2 * cab, caa - cbb);
            Vector3 uAxis = (e1 * (float)System.Math.Cos(angle) + e2 * (float)System.Math.Sin(angle)).normalized;

            float uMin = float.MaxValue, uMax = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float u = Vector3.Dot(verts[i], uAxis);
                if (u < uMin) uMin = u;
                if (u > uMax) uMax = u;
            }
            float span = uMax - uMin;
            if (span < 1e-3f)
            {
                AddObbBox(verts, result);
                return;
            }

            const int k = 16;
            var binSum = new Vector3[k];
            var binCnt = new int[k];
            for (int i = 0; i < n; i++)
            {
                int idx = Mathf.Clamp((int)((Vector3.Dot(verts[i], uAxis) - uMin) / span * k), 0, k - 1);
                binSum[idx] += verts[i];
                binCnt[idx]++;
            }

            var poly = new List<Vector3>();
            var polyU = new List<float>();
            for (int b = 0; b < k; b++)
            {
                if (binCnt[b] == 0) continue;
                poly.Add(binSum[b] / binCnt[b]);
                polyU.Add(uMin + (b + 0.5f) / k * span);
            }
            if (poly.Count < 2)
            {
                AddObbBox(verts, result);
                return;
            }

            List<int> kept = DouglasPeucker(poly, tol);
            float overlap = span / k;
            for (int s = 0; s + 1 < kept.Count; s++)
            {
                float lo = s == 0 ? uMin - 1e-3f : polyU[kept[s]] - overlap;
                float hi = s == kept.Count - 2 ? uMax + 1e-3f : polyU[kept[s + 1]] + overlap;
                var spanVerts = new List<Vector3>();
                for (int i = 0; i < n; i++)
                {
                    float u = Vector3.Dot(verts[i], uAxis);
                    if (u >= lo && u <= hi)
                        spanVerts.Add(verts[i]);
                }
                AddObbBox(spanVerts, result);
            }
        }

        private static void AddObbBox(List<Vector3> verts, List<FittedPrimitive> result)
        {
            if (verts.Count >= 3 && TryComputeObb(verts, out Obb o))
                result.Add(new FittedPrimitive { IsCapsule = false, Center = o.Center, Rotation = o.Rotation, Size = o.Size });
        }

        private static List<int> DouglasPeucker(List<Vector3> pts, float tol)
        {
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            DouglasPeuckerRec(pts, 0, pts.Count - 1, Mathf.Max(tol, 1e-3f), keep);

            var idx = new List<int>();
            for (int i = 0; i < pts.Count; i++)
                if (keep[i]) idx.Add(i);
            return idx;
        }

        private static void DouglasPeuckerRec(List<Vector3> pts, int a, int b, float tol, bool[] keep)
        {
            if (b <= a + 1)
                return;

            float maxD = -1f;
            int split = -1;
            for (int i = a + 1; i < b; i++)
            {
                float d = PointLineDistance(pts[i], pts[a], pts[b]);
                if (d > maxD) { maxD = d; split = i; }
            }

            if (maxD > tol && split > 0)
            {
                keep[split] = true;
                DouglasPeuckerRec(pts, a, split, tol, keep);
                DouglasPeuckerRec(pts, split, b, tol, keep);
            }
        }

        private static float PointLineDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-6f)
                return (p - a).magnitude;
            Vector3 n = ab / len;
            float t = Vector3.Dot(p - a, n);
            return (p - (a + n * t)).magnitude;
        }

        private struct RectI
        {
            public int I0, I1, J0, J1;
        }

        // ---------------------------------------------------------------------
        // Rounded facet decomposition (hull / cornered fences / nests)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Approximates rounded volumes by clustering the component inside an enclosing cube,
        /// then fitting one oriented box per occupied cluster.
        /// </summary>
        public static List<FittedPrimitive> DecomposeRoundedFacets(
            IReadOnlyList<Vector3> verts,
            IReadOnlyList<int> tris,
            Vector3 up,
            int precise,
            float minComponentSize,
            float minThickness,
            float clusterFillThreshold)
        {
            var result = new List<FittedPrimitive>();
            up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
            List<LimbComponent> comps = BuildComponents(verts, tris, minComponentSize);

            foreach (LimbComponent comp in comps)
                FacetComponent(comp.Verts, comp.Tris, comp.Obb, up, precise, minThickness, clusterFillThreshold, result);

            return result;
        }

        private sealed class RoundedClusterData
        {
            public readonly List<Vector3> Points = new List<Vector3>(32);
            public ulong OccupancyBits;
        }

        private static void FacetComponent(
            List<Vector3> verts,
            List<int> tris,
            in Obb obb,
            Vector3 up,
            int precise,
            float minThickness,
            float clusterFillThreshold,
            List<FittedPrimitive> result)
        {
            int n = verts.Count;
            if (n < 4)
            {
                AddObbBox(verts, result);
                return;
            }

            int startCount = result.Count;
            int precision = Mathf.Clamp(precise, 3, 256);
            float precision01 = Mathf.InverseLerp(3f, 256f, precision);
            float fillThreshold = Mathf.Clamp(clusterFillThreshold, 0.01f, 0.95f);

            Quaternion invRoot = Quaternion.Inverse(obb.Rotation);
            var localPts = new Vector3[n];
            Vector3 lo = invRoot * (verts[0] - obb.Center);
            Vector3 hi = lo;
            for (int i = 0; i < n; i++)
            {
                Vector3 lp = invRoot * (verts[i] - obb.Center);
                localPts[i] = lp;
                lo = Vector3.Min(lo, lp);
                hi = Vector3.Max(hi, lp);
            }

            Vector3 span = hi - lo;
            float rootSize = Mathf.Max(span.x, Mathf.Max(span.y, span.z));
            if (rootSize < 1e-4f)
            {
                AddObbBox(verts, result);
                return;
            }

            Vector3 cubeCenter = (lo + hi) * 0.5f;
            float rootHalf = rootSize * 0.5f + 1e-4f;
            Vector3 cubeMin = cubeCenter - Vector3.one * rootHalf;

            int subdivisions = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(2f, 24f, precision01)), 2, 24);
            float clusterSize = (rootHalf * 2f) / subdivisions;
            float sampleSpacing = Mathf.Max(clusterSize * 0.55f, 0.015f);
            float padding = Mathf.Max(minThickness * 0.5f, clusterSize * 0.08f, 0.005f);

            int CellIndex(int x, int y, int z) => x + y * subdivisions + z * subdivisions * subdivisions;

            bool TryClusterOfPoint(Vector3 lp, out int ix, out int iy, out int iz)
            {
                Vector3 rel = (lp - cubeMin) / clusterSize;
                ix = Mathf.FloorToInt(rel.x);
                iy = Mathf.FloorToInt(rel.y);
                iz = Mathf.FloorToInt(rel.z);
                return ix >= 0 && iy >= 0 && iz >= 0 && ix < subdivisions && iy < subdivisions && iz < subdivisions;
            }

            var clusters = new Dictionary<int, RoundedClusterData>();

            void AddSample(Vector3 lp)
            {
                if (!TryClusterOfPoint(lp, out int ix, out int iy, out int iz))
                    return;

                int key = CellIndex(ix, iy, iz);
                if (!clusters.TryGetValue(key, out RoundedClusterData data))
                    clusters[key] = data = new RoundedClusterData();

                data.Points.Add(lp);

                Vector3 cmin = cubeMin + new Vector3(ix, iy, iz) * clusterSize;
                Vector3 local01 = (lp - cmin) / clusterSize;
                int bx = Mathf.Clamp(Mathf.FloorToInt(local01.x * 4f), 0, 3);
                int by = Mathf.Clamp(Mathf.FloorToInt(local01.y * 4f), 0, 3);
                int bz = Mathf.Clamp(Mathf.FloorToInt(local01.z * 4f), 0, 3);
                int bit = bx + by * 4 + bz * 16;
                data.OccupancyBits |= 1UL << bit;
            }

            if (tris != null && tris.Count >= 3)
            {
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    if ((uint)i0 >= (uint)n || (uint)i1 >= (uint)n || (uint)i2 >= (uint)n)
                        continue;

                    Vector3 a = localPts[i0];
                    Vector3 b = localPts[i1];
                    Vector3 c0 = localPts[i2];
                    float edge = Mathf.Max((a - b).magnitude, Mathf.Max((b - c0).magnitude, (c0 - a).magnitude));
                    int subdiv = Mathf.Clamp(Mathf.CeilToInt(edge / sampleSpacing), 1, 6);
                    float inv = 1f / subdiv;

                    for (int iu = 0; iu <= subdiv; iu++)
                    {
                        float u = iu * inv;
                        for (int iv = 0; iv <= subdiv - iu; iv++)
                        {
                            float v = iv * inv;
                            float w = 1f - u - v;
                            AddSample(a * u + b * v + c0 * w);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                    AddSample(localPts[i]);
            }

            Vector3 upLocal = invRoot * up;
            if (upLocal.sqrMagnitude < 1e-6f)
                upLocal = Vector3.up;
            upLocal.Normalize();

            foreach (var kvp in clusters)
            {
                RoundedClusterData data = kvp.Value;
                if (data.Points.Count < 3)
                    continue;

                float fill = CountBits(data.OccupancyBits) / 64f;
                if (fill < fillThreshold)
                    continue;

                if (!TryBuildClusterBox(data.Points, upLocal, padding, out Vector3 centerLocal, out Quaternion rotLocal, out Vector3 sizeLocal))
                    continue;

                result.Add(new FittedPrimitive
                {
                    IsCapsule = false,
                    Center = obb.Center + obb.Rotation * centerLocal,
                    Rotation = obb.Rotation * rotLocal,
                    Size = sizeLocal,
                });
            }

            if (result.Count == startCount)
                AddObbBox(verts, result);
        }

        private static bool TryBuildClusterBox(
            List<Vector3> points,
            Vector3 upLocal,
            float padding,
            out Vector3 center,
            out Quaternion rotation,
            out Vector3 size)
        {
            center = Vector3.zero;
            rotation = Quaternion.identity;
            size = Vector3.one * 0.01f;

            int count = points != null ? points.Count : 0;
            if (count < 2)
                return false;

            Vector3 a = points[0];
            int aIdx = 0;
            float best = -1f;
            for (int i = 0; i < count; i++)
            {
                float d = (points[i] - a).sqrMagnitude;
                if (d > best)
                {
                    best = d;
                    aIdx = i;
                }
            }

            Vector3 b = points[aIdx];
            int bIdx = aIdx;
            best = -1f;
            for (int i = 0; i < count; i++)
            {
                float d = (points[i] - b).sqrMagnitude;
                if (d > best)
                {
                    best = d;
                    bIdx = i;
                }
            }

            Vector3 dir = points[bIdx] - points[aIdx];
            if (dir.sqrMagnitude < 1e-8f)
                return false;

            Vector3 xAxis = dir.normalized;
            Vector3 zAxis = Vector3.Cross(xAxis, upLocal);
            if (zAxis.sqrMagnitude < 1e-8f)
                zAxis = Vector3.Cross(xAxis, Vector3.up);
            if (zAxis.sqrMagnitude < 1e-8f)
                zAxis = Vector3.Cross(xAxis, Vector3.right);
            zAxis.Normalize();
            Vector3 yAxis = Vector3.Cross(zAxis, xAxis).normalized;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float sumAbsY = 0f, sumAbsZ = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = points[i];
                float px = Vector3.Dot(p, xAxis);
                float py = Vector3.Dot(p, yAxis);
                float pz = Vector3.Dot(p, zAxis);

                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
                if (pz < minZ) minZ = pz;
                if (pz > maxZ) maxZ = pz;

                sumAbsY += Mathf.Abs(py);
                sumAbsZ += Mathf.Abs(pz);
            }

            float avgHalfY = sumAbsY / count;
            float avgHalfZ = sumAbsZ / count;

            float sx = Mathf.Max(maxX - minX, 0.01f) + padding * 2f;
            float sy = Mathf.Max(maxY - minY, avgHalfY * 2f, 0.01f) + padding * 2f;
            float sz = Mathf.Max(maxZ - minZ, avgHalfZ * 2f, 0.01f) + padding * 2f;

            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            center = xAxis * cx + yAxis * cy + zAxis * cz;

            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(xAxis.x, xAxis.y, xAxis.z, 0f));
            m.SetColumn(1, new Vector4(yAxis.x, yAxis.y, yAxis.z, 0f));
            m.SetColumn(2, new Vector4(zAxis.x, zAxis.y, zAxis.z, 0f));
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

            rotation = m.rotation;
            size = new Vector3(sx, sy, sz);
            return true;
        }

        private static int CountBits(ulong bits)
        {
            int count = 0;
            while (bits != 0)
            {
                bits &= bits - 1;
                count++;
            }
            return count;
        }

        // Welds vertices by position, splits the mesh into connected components via triangle
        // connectivity, and returns each component (with its OBB) that is at least minSize long.
        private static List<LimbComponent> BuildComponents(
            IReadOnlyList<Vector3> verts,
            IReadOnlyList<int> tris,
            float minSize)
        {
            var comps = new List<LimbComponent>();
            int n = verts?.Count ?? 0;
            if (n == 0)
                return comps;

            Vector3 lo = verts[0], hi = verts[0];
            for (int i = 1; i < n; i++)
            {
                lo = Vector3.Min(lo, verts[i]);
                hi = Vector3.Max(hi, verts[i]);
            }
            float eps = Mathf.Max(1e-4f, (hi - lo).magnitude * 1e-4f);

            var cellMap = new Dictionary<Vector3Int, int>(n);
            var canon = new int[n];
            var canonPos = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                var key = new Vector3Int(
                    Mathf.RoundToInt(verts[i].x / eps),
                    Mathf.RoundToInt(verts[i].y / eps),
                    Mathf.RoundToInt(verts[i].z / eps));
                if (cellMap.TryGetValue(key, out int idx))
                {
                    canon[i] = idx;
                }
                else
                {
                    idx = canonPos.Count;
                    cellMap[key] = idx;
                    canonPos.Add(verts[i]);
                    canon[i] = idx;
                }
            }

            int m = canonPos.Count;
            var parent = new int[m];
            for (int i = 0; i < m; i++)
                parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb)
                    parent[ra] = rb;
            }

            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = canon[tris[t]], b = canon[tris[t + 1]], c = canon[tris[t + 2]];
                Union(a, b);
                Union(b, c);
            }

            var compVerts = new Dictionary<int, List<int>>();
            for (int c = 0; c < m; c++)
            {
                int r = Find(c);
                if (!compVerts.TryGetValue(r, out List<int> list))
                    compVerts[r] = list = new List<int>();
                list.Add(c);
            }

            var compTris = new Dictionary<int, List<int>>();
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int r = Find(canon[tris[t]]);
                if (!compTris.TryGetValue(r, out List<int> list))
                    compTris[r] = list = new List<int>();
                list.Add(canon[tris[t]]);
                list.Add(canon[tris[t + 1]]);
                list.Add(canon[tris[t + 2]]);
            }

            foreach (var kvp in compVerts)
            {
                int root = kvp.Key;
                List<int> cIdx = kvp.Value;

                var localMap = new Dictionary<int, int>(cIdx.Count);
                var lverts = new List<Vector3>(cIdx.Count);
                for (int i = 0; i < cIdx.Count; i++)
                {
                    localMap[cIdx[i]] = lverts.Count;
                    lverts.Add(canonPos[cIdx[i]]);
                }

                var ltris = new List<int>();
                if (compTris.TryGetValue(root, out List<int> ct))
                    for (int i = 0; i < ct.Count; i++)
                        ltris.Add(localMap[ct[i]]);

                if (TryComputeObb(lverts, out Obb cObb) && cObb.Size.x >= minSize)
                    comps.Add(new LimbComponent { Verts = lverts, Tris = ltris, Obb = cObb });
            }

            return comps;
        }

        private struct LimbComponent
        {
            public List<Vector3> Verts;
            public List<int> Tris;
            public Obb Obb;
        }

        // True when every corner of <paramref name="small"/> lies inside <paramref name="large"/>
        // expanded by its own smallest half-extent (so a band slightly wider than the pole still
        // counts as contained, while a spar sticking out clearly does not).
        private static bool ObbContains(in Obb large, in Obb small)
        {
            Quaternion inv = Quaternion.Inverse(large.Rotation);
            Vector3 h = large.HalfSize;
            float margin = Mathf.Min(h.x, Mathf.Min(h.y, h.z));
            Vector3 sh = small.HalfSize;

            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 corner = small.Center + small.Rotation * new Vector3(sx * sh.x, sy * sh.y, sz * sh.z);
                Vector3 p = inv * (corner - large.Center);
                if (Mathf.Abs(p.x) > h.x + margin || Mathf.Abs(p.y) > h.y + margin || Mathf.Abs(p.z) > h.z + margin)
                    return false;
            }

            return true;
        }

        private static void FitComponent(
            List<Vector3> verts,
            List<int> tris,
            in Obb obb,
            float elongation,
            float radiusPercentile,
            float voxelSize,
            int maxBoxes,
            List<FittedPrimitive> outList)
        {
            float s0 = obb.Size.x, s1 = obb.Size.y, s2 = obb.Size.z; // sorted x >= y >= z
            Quaternion invRot = Quaternion.Inverse(obb.Rotation);

            // 1. Elongated limb (a pole/spar) -> ONE capsule along its own axis.
            //    The radius is a percentile of vertex distance to the axis, so small
            //    attachments on the stick (rings, cleats, bands) are absorbed rather than
            //    turned into separate colliders.
            if (s0 / Mathf.Max(s1, 1e-4f) >= elongation)
            {
                var dists = new List<float>(verts.Count);
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 p = invRot * (verts[i] - obb.Center);
                    dists.Add(Mathf.Sqrt(p.y * p.y + p.z * p.z)); // distance to the OBB x axis
                }
                dists.Sort();
                int idx = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(radiusPercentile) * (dists.Count - 1)), 0, dists.Count - 1);
                float radius = Mathf.Max(dists[idx], 0.02f);

                outList.Add(new FittedPrimitive
                {
                    IsCapsule = true,
                    Center = obb.Center,
                    Rotation = obb.Rotation,
                    Radius = radius,
                    Height = Mathf.Max(s0 + radius * 2f, radius * 2f),
                    Direction = 0,
                });
                return;
            }

            // 2. Solidly-filled footprint -> single oriented box (plates, planks, panels).
            if (BoxCoverage(verts, obb, invRot) >= 0.6f)
            {
                outList.Add(new FittedPrimitive
                {
                    IsCapsule = false,
                    Center = obb.Center,
                    Rotation = obb.Rotation,
                    Size = obb.Size,
                });
                return;
            }

            // 3. Compound / hollow chunk -> voxel boxes that follow the shape.
            float vsize = Mathf.Clamp(s2 * 0.9f, 0.05f, Mathf.Max(voxelSize, 0.05f));
            List<LocalBox> boxes = DecomposeShellToBoxes(verts, tris, obb, vsize, maxBoxes, out _, out _);
            if (boxes.Count == 0)
            {
                outList.Add(new FittedPrimitive
                {
                    IsCapsule = false,
                    Center = obb.Center,
                    Rotation = obb.Rotation,
                    Size = obb.Size,
                });
                return;
            }

            for (int i = 0; i < boxes.Count; i++)
            {
                outList.Add(new FittedPrimitive
                {
                    IsCapsule = false,
                    Center = obb.Center + obb.Rotation * boxes[i].Center,
                    Rotation = obb.Rotation,
                    Size = boxes[i].Size,
                });
            }
        }

        // Fraction of the OBB's (x, y) footprint cells that contain vertices.
        // High for a solid plate/box, low for a cross or sparse chunk.
        private static float BoxCoverage(List<Vector3> verts, in Obb obb, Quaternion invRot)
        {
            const int res = 12;
            var grid = new bool[res, res];
            Vector3 half = obb.HalfSize;
            float sx = Mathf.Max(obb.Size.x, 1e-4f);
            float sy = Mathf.Max(obb.Size.y, 1e-4f);

            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = invRot * (verts[i] - obb.Center);
                int gx = Mathf.Clamp((int)((p.x + half.x) / sx * res), 0, res - 1);
                int gy = Mathf.Clamp((int)((p.y + half.y) / sy * res), 0, res - 1);
                grid[gx, gy] = true;
            }

            int filled = 0;
            for (int x = 0; x < res; x++)
            for (int y = 0; y < res; y++)
                if (grid[x, y]) filled++;

            return filled / (float)(res * res);
        }

        // ---------------------------------------------------------------------
        // Jacobi eigensolver (symmetric 3x3), Numerical-Recipes style
        // ---------------------------------------------------------------------

        private static void JacobiEigen(double[,] a, out Vector3 e0, out Vector3 e1, out Vector3 e2)
        {
            const int n = 3;
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            var d = new double[3];
            var b = new double[3];
            var z = new double[3];

            for (int ip = 0; ip < n; ip++)
            {
                b[ip] = d[ip] = a[ip, ip];
                z[ip] = 0.0;
            }

            for (int sweep = 0; sweep < 50; sweep++)
            {
                double sm = System.Math.Abs(a[0, 1]) + System.Math.Abs(a[0, 2]) + System.Math.Abs(a[1, 2]);
                if (sm == 0.0)
                    break;

                double tresh = sweep < 3 ? 0.2 * sm / (n * n) : 0.0;

                for (int ip = 0; ip < n - 1; ip++)
                {
                    for (int iq = ip + 1; iq < n; iq++)
                    {
                        double g = 100.0 * System.Math.Abs(a[ip, iq]);
                        if (sweep > 3
                            && System.Math.Abs(d[ip]) + g == System.Math.Abs(d[ip])
                            && System.Math.Abs(d[iq]) + g == System.Math.Abs(d[iq]))
                        {
                            a[ip, iq] = 0.0;
                        }
                        else if (System.Math.Abs(a[ip, iq]) > tresh)
                        {
                            double h = d[iq] - d[ip];
                            double t;
                            if (System.Math.Abs(h) + g == System.Math.Abs(h))
                            {
                                t = a[ip, iq] / h;
                            }
                            else
                            {
                                double theta = 0.5 * h / a[ip, iq];
                                t = 1.0 / (System.Math.Abs(theta) + System.Math.Sqrt(1.0 + theta * theta));
                                if (theta < 0.0) t = -t;
                            }

                            double c = 1.0 / System.Math.Sqrt(1.0 + t * t);
                            double s = t * c;
                            double tau = s / (1.0 + c);
                            h = t * a[ip, iq];
                            z[ip] -= h;
                            z[iq] += h;
                            d[ip] -= h;
                            d[iq] += h;
                            a[ip, iq] = 0.0;

                            for (int j = 0; j <= ip - 1; j++) Rotate(a, s, tau, j, ip, j, iq);
                            for (int j = ip + 1; j <= iq - 1; j++) Rotate(a, s, tau, ip, j, j, iq);
                            for (int j = iq + 1; j < n; j++) Rotate(a, s, tau, ip, j, iq, j);
                            for (int j = 0; j < n; j++) Rotate(v, s, tau, j, ip, j, iq);
                        }
                    }
                }

                for (int ip = 0; ip < n; ip++)
                {
                    b[ip] += z[ip];
                    d[ip] = b[ip];
                    z[ip] = 0.0;
                }
            }

            // Sort eigenvectors by descending eigenvalue so column 0 is the longest axis.
            int c0 = 0, c1 = 1, c2 = 2;
            if (d[c0] < d[c1]) (c0, c1) = (c1, c0);
            if (d[c0] < d[c2]) (c0, c2) = (c2, c0);
            if (d[c1] < d[c2]) (c1, c2) = (c2, c1);

            e0 = Column(v, c0);
            e1 = Column(v, c1);
            e2 = Column(v, c2);
        }

        private static void Rotate(double[,] m, double s, double tau, int i, int j, int k, int l)
        {
            double g = m[i, j];
            double h = m[k, l];
            m[i, j] = g - s * (h + g * tau);
            m[k, l] = h + s * (g - h * tau);
        }

        private static Vector3 Column(double[,] m, int c)
        {
            return new Vector3((float)m[0, c], (float)m[1, c], (float)m[2, c]);
        }

        // ---------------------------------------------------------------------
        // Triangle / AABB overlap (Akenine-Möller separating axis test)
        // ---------------------------------------------------------------------

        public static bool TriBoxOverlap(Vector3 boxCenter, Vector3 boxHalf, Vector3 a, Vector3 b, Vector3 c)
        {
            // Direct port of Tomas Akenine-Möller's triangle/box overlap test.
            Vector3 v0 = a - boxCenter;
            Vector3 v1 = b - boxCenter;
            Vector3 v2 = c - boxCenter;

            Vector3 e0 = v1 - v0;
            Vector3 e1 = v2 - v1;
            Vector3 e2 = v0 - v2;

            float fex = Mathf.Abs(e0.x);
            float fey = Mathf.Abs(e0.y);
            float fez = Mathf.Abs(e0.z);
            if (!AxisTestX01(e0.z, e0.y, fez, fey, v0, v2, boxHalf)) return false;
            if (!AxisTestY02(e0.z, e0.x, fez, fex, v0, v2, boxHalf)) return false;
            if (!AxisTestZ12(e0.y, e0.x, fey, fex, v1, v2, boxHalf)) return false;

            fex = Mathf.Abs(e1.x);
            fey = Mathf.Abs(e1.y);
            fez = Mathf.Abs(e1.z);
            if (!AxisTestX01(e1.z, e1.y, fez, fey, v0, v2, boxHalf)) return false;
            if (!AxisTestY02(e1.z, e1.x, fez, fex, v0, v2, boxHalf)) return false;
            if (!AxisTestZ0(e1.y, e1.x, fey, fex, v0, v1, boxHalf)) return false;

            fex = Mathf.Abs(e2.x);
            fey = Mathf.Abs(e2.y);
            fez = Mathf.Abs(e2.z);
            if (!AxisTestX2(e2.z, e2.y, fez, fey, v0, v1, boxHalf)) return false;
            if (!AxisTestY1(e2.z, e2.x, fez, fex, v0, v1, boxHalf)) return false;
            if (!AxisTestZ12(e2.y, e2.x, fey, fex, v1, v2, boxHalf)) return false;

            // AABB of the triangle vs the box on the 3 principal axes.
            if (Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x)) > boxHalf.x || Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x)) < -boxHalf.x) return false;
            if (Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y)) > boxHalf.y || Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y)) < -boxHalf.y) return false;
            if (Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z)) > boxHalf.z || Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z)) < -boxHalf.z) return false;

            // Triangle plane vs box.
            Vector3 normal = Vector3.Cross(e0, e1);
            return PlaneBoxOverlap(normal, v0, boxHalf);
        }

        private static bool Overlaps(float p0, float p1, float rad)
        {
            float min = Mathf.Min(p0, p1);
            float max = Mathf.Max(p0, p1);
            return !(min > rad || max < -rad);
        }

        // AXISTEST_X01 / AXISTEST_X2: projections use verts' (y,z), radius uses box (y,z).
        private static bool AxisTestX01(float a, float b, float fa, float fb, Vector3 u, Vector3 w, Vector3 h)
            => Overlaps(a * u.y - b * u.z, a * w.y - b * w.z, fa * h.y + fb * h.z);

        private static bool AxisTestX2(float a, float b, float fa, float fb, Vector3 u, Vector3 w, Vector3 h)
            => Overlaps(a * u.y - b * u.z, a * w.y - b * w.z, fa * h.y + fb * h.z);

        // AXISTEST_Y02 / AXISTEST_Y1: projections use verts' (x,z), radius uses box (x,z).
        private static bool AxisTestY02(float a, float b, float fa, float fb, Vector3 u, Vector3 w, Vector3 h)
            => Overlaps(-a * u.x + b * u.z, -a * w.x + b * w.z, fa * h.x + fb * h.z);

        private static bool AxisTestY1(float a, float b, float fa, float fb, Vector3 u, Vector3 w, Vector3 h)
            => Overlaps(-a * u.x + b * u.z, -a * w.x + b * w.z, fa * h.x + fb * h.z);

        // AXISTEST_Z12 / AXISTEST_Z0: projections use verts' (x,y), radius uses box (x,y).
        private static bool AxisTestZ12(float a, float b, float fa, float fb, Vector3 u, Vector3 w, Vector3 h)
            => Overlaps(a * u.x - b * u.y, a * w.x - b * w.y, fa * h.x + fb * h.y);

        private static bool AxisTestZ0(float a, float b, float fa, float fb, Vector3 u, Vector3 w, Vector3 h)
            => Overlaps(a * u.x - b * u.y, a * w.x - b * w.y, fa * h.x + fb * h.y);

        private static bool PlaneBoxOverlap(Vector3 normal, Vector3 vert, Vector3 maxbox)
        {
            Vector3 vmin, vmax;

            vmin.x = normal.x > 0f ? -maxbox.x - vert.x : maxbox.x - vert.x;
            vmax.x = normal.x > 0f ? maxbox.x - vert.x : -maxbox.x - vert.x;
            vmin.y = normal.y > 0f ? -maxbox.y - vert.y : maxbox.y - vert.y;
            vmax.y = normal.y > 0f ? maxbox.y - vert.y : -maxbox.y - vert.y;
            vmin.z = normal.z > 0f ? -maxbox.z - vert.z : maxbox.z - vert.z;
            vmax.z = normal.z > 0f ? maxbox.z - vert.z : -maxbox.z - vert.z;

            if (Vector3.Dot(normal, vmin) > 0f) return false;
            return Vector3.Dot(normal, vmax) >= 0f;
        }
    }
}
