using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RumOverboard.EditorTools.ColliderTools
{
    /// <summary>
    /// Post-bake validator: estimates how well generated colliders cover the source mesh
    /// and how far collider surfaces protrude from that mesh.
    /// </summary>
    internal static class ColliderFitValidator
    {
        private const int MaxMeshSamples = 18000;
        private const int MaxColliderSamples = 16000;

        internal sealed class ValidationReport
        {
            public int SourceParts;
            public int SourceMeshes;
            public int ColliderCount;
            public int MeshSamples;
            public int CoveredMeshSamples;
            public int ColliderSurfaceSamples;
            public int OverflowSamples;
            public float MissingMeanDistance;
            public float MissingMaxDistance;
            public float OverflowMeanDistance;
            public float OverflowMaxDistance;
            public readonly List<string> Warnings = new();

            public float CoveragePercent => MeshSamples > 0 ? 100f * CoveredMeshSamples / MeshSamples : 0f;
            public float OverflowPercent => ColliderSurfaceSamples > 0 ? 100f * OverflowSamples / ColliderSurfaceSamples : 0f;

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Validation: parts={SourceParts}, meshes={SourceMeshes}, colliders={ColliderCount}");
                sb.AppendLine($"Coverage: {CoveredMeshSamples}/{MeshSamples} ({CoveragePercent:0.00}%)");
                sb.AppendLine($"Miss distance: mean={MissingMeanDistance:0.000}  max={MissingMaxDistance:0.000}");
                sb.AppendLine($"Overflow: {OverflowSamples}/{ColliderSurfaceSamples} ({OverflowPercent:0.00}%)");
                sb.AppendLine($"Overflow distance: mean={OverflowMeanDistance:0.000}  max={OverflowMaxDistance:0.000}");
                for (int i = 0; i < Warnings.Count; i++)
                    sb.AppendLine($"  ! {Warnings[i]}");
                return sb.ToString();
            }
        }

        public static ValidationReport Validate(GameObject target, BakeSettings settings)
        {
            var report = new ValidationReport();
            if (target == null)
            {
                report.Warnings.Add("No target GameObject.");
                return report;
            }

            if (settings == null)
            {
                report.Warnings.Add("No bake settings.");
                return report;
            }

            Transform root = target.transform;
            Transform generatedRoot = root.Find(settings.GroupName);
            if (generatedRoot == null)
            {
                report.Warnings.Add($"Generated group '{settings.GroupName}' not found.");
                return report;
            }

            List<Transform> parts = CollectSourceParts(target, generatedRoot, settings.IncludeInactive);
            report.SourceParts = parts.Count;

            int precise = Mathf.Clamp(settings.Precise, 3, 256);
            float precision01 = Mathf.InverseLerp(3f, 256f, precise);
            float sampleSpacing = Mathf.Lerp(0.18f, 0.035f, precision01) * Mathf.Clamp(settings.SizeScale, 1f, 3f);
            sampleSpacing = Mathf.Clamp(sampleSpacing, 0.02f, 0.25f);
            float overflowTolerance = Mathf.Max(settings.RoundedThickness * 0.75f, sampleSpacing * 1.25f);

            var meshSamples = new List<Vector3>(MaxMeshSamples);
            for (int i = 0; i < parts.Count; i++)
            {
                Mesh mesh = ResolveMesh(parts[i]);
                if (mesh == null)
                    continue;

                report.SourceMeshes++;
                if (!mesh.isReadable)
                {
                    report.Warnings.Add($"'{parts[i].name}' mesh is not readable; skipped in validation.");
                    continue;
                }

                SampleMeshSurface(mesh, parts[i].localToWorldMatrix, sampleSpacing, meshSamples, MaxMeshSamples);
                if (meshSamples.Count >= MaxMeshSamples)
                {
                    report.Warnings.Add($"Mesh samples capped at {MaxMeshSamples} for performance.");
                    break;
                }
            }

            report.MeshSamples = meshSamples.Count;
            if (report.MeshSamples == 0)
            {
                report.Warnings.Add("No mesh samples were collected.");
                return report;
            }

            var colliders = new List<Collider>();
            foreach (Collider col in generatedRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col != null && col.enabled)
                    colliders.Add(col);
            }
            report.ColliderCount = colliders.Count;
            if (colliders.Count == 0)
            {
                report.Warnings.Add("No enabled colliders found in generated group.");
                return report;
            }

            float missingSum = 0f;
            float missingMax = 0f;
            int covered = 0;
            for (int i = 0; i < meshSamples.Count; i++)
            {
                if (ContainsAny(colliders, meshSamples[i], out float nearest))
                {
                    covered++;
                }
                else
                {
                    missingSum += nearest;
                    if (nearest > missingMax) missingMax = nearest;
                }
            }

            report.CoveredMeshSamples = covered;
            int missed = Mathf.Max(1, report.MeshSamples - covered);
            report.MissingMeanDistance = covered < report.MeshSamples ? (missingSum / missed) : 0f;
            report.MissingMaxDistance = missingMax;

            var colliderSurfaceSamples = new List<Vector3>(MaxColliderSamples);
            for (int i = 0; i < colliders.Count; i++)
            {
                SampleColliderSurface(colliders[i], sampleSpacing, colliderSurfaceSamples, MaxColliderSamples);
                if (colliderSurfaceSamples.Count >= MaxColliderSamples)
                {
                    report.Warnings.Add($"Collider surface samples capped at {MaxColliderSamples} for performance.");
                    break;
                }
            }

            report.ColliderSurfaceSamples = colliderSurfaceSamples.Count;
            if (report.ColliderSurfaceSamples == 0)
            {
                report.Warnings.Add("No collider surface samples were collected.");
                return report;
            }

            var hash = new PointHash(meshSamples, Mathf.Max(sampleSpacing * 1.35f, 0.03f));

            int overflow = 0;
            float overflowSum = 0f;
            float overflowMax = 0f;
            for (int i = 0; i < colliderSurfaceSamples.Count; i++)
            {
                float d = hash.NearestDistance(colliderSurfaceSamples[i]);
                if (d > overflowTolerance)
                {
                    overflow++;
                    float exceed = d - overflowTolerance;
                    overflowSum += exceed;
                    if (exceed > overflowMax) overflowMax = exceed;
                }
            }

            report.OverflowSamples = overflow;
            report.OverflowMeanDistance = overflow > 0 ? overflowSum / overflow : 0f;
            report.OverflowMaxDistance = overflowMax;

            return report;
        }

        private static List<Transform> CollectSourceParts(GameObject target, Transform generatedGroup, bool includeInactive)
        {
            var result = new List<Transform>();
            var seen = new HashSet<Transform>();

            foreach (MeshFilter mf in target.GetComponentsInChildren<MeshFilter>(includeInactive))
            {
                if (mf.sharedMesh == null || IsUnder(mf.transform, generatedGroup) || !seen.Add(mf.transform))
                    continue;
                result.Add(mf.transform);
            }

            foreach (SkinnedMeshRenderer smr in target.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive))
            {
                if (smr.sharedMesh == null || IsUnder(smr.transform, generatedGroup) || !seen.Add(smr.transform))
                    continue;
                result.Add(smr.transform);
            }

            return result;
        }

        private static bool IsUnder(Transform t, Transform ancestor)
        {
            for (Transform c = t; c != null; c = c.parent)
                if (c == ancestor)
                    return true;
            return false;
        }

        private static Mesh ResolveMesh(Transform part)
        {
            if (part.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                return mf.sharedMesh;
            if (part.TryGetComponent(out SkinnedMeshRenderer smr) && smr.sharedMesh != null)
                return smr.sharedMesh;
            return null;
        }

        private static void SampleMeshSurface(Mesh mesh, Matrix4x4 localToWorld, float spacing, List<Vector3> samples, int cap)
        {
            if (mesh == null || samples.Count >= cap)
                return;

            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            if (verts == null || verts.Length == 0 || tris == null || tris.Length < 3)
                return;

            float safe = Mathf.Max(0.01f, spacing);
            for (int t = 0; t + 2 < tris.Length && samples.Count < cap; t += 3)
            {
                Vector3 a = localToWorld.MultiplyPoint3x4(verts[tris[t]]);
                Vector3 b = localToWorld.MultiplyPoint3x4(verts[tris[t + 1]]);
                Vector3 c = localToWorld.MultiplyPoint3x4(verts[tris[t + 2]]);

                float edge = Mathf.Max((a - b).magnitude, Mathf.Max((b - c).magnitude, (c - a).magnitude));
                int subdiv = Mathf.Clamp(Mathf.CeilToInt(edge / safe), 1, 5);
                float inv = 1f / subdiv;

                for (int iu = 0; iu <= subdiv && samples.Count < cap; iu++)
                {
                    float u = iu * inv;
                    for (int iv = 0; iv <= subdiv - iu && samples.Count < cap; iv++)
                    {
                        float v = iv * inv;
                        float w = 1f - u - v;
                        samples.Add(a * u + b * v + c * w);
                    }
                }
            }
        }

        private static bool ContainsAny(List<Collider> colliders, Vector3 point, out float nearestDistance)
        {
            nearestDistance = float.PositiveInfinity;
            const float epsSqr = 1e-8f;
            bool insideAny = false;

            for (int i = 0; i < colliders.Count; i++)
            {
                Collider c = colliders[i];
                Vector3 cp = c.ClosestPoint(point);
                float sqr = (cp - point).sqrMagnitude;
                if (sqr < nearestDistance * nearestDistance)
                    nearestDistance = Mathf.Sqrt(sqr);
                if (sqr <= epsSqr)
                    insideAny = true;
            }

            if (float.IsInfinity(nearestDistance))
                nearestDistance = 0f;
            return insideAny;
        }

        private static void SampleColliderSurface(Collider collider, float spacing, List<Vector3> points, int cap)
        {
            if (points.Count >= cap || collider == null)
                return;

            if (collider is BoxCollider box)
            {
                SampleBoxSurface(box, spacing, points, cap);
                return;
            }

            if (collider is SphereCollider sphere)
            {
                SampleSphereSurface(sphere, spacing, points, cap);
                return;
            }

            if (collider is CapsuleCollider capsule)
            {
                SampleCapsuleSurface(capsule, spacing, points, cap);
                return;
            }

            Bounds b = collider.bounds;
            points.Add(b.center + new Vector3(b.extents.x, 0f, 0f));
            points.Add(b.center - new Vector3(b.extents.x, 0f, 0f));
            points.Add(b.center + new Vector3(0f, b.extents.y, 0f));
            points.Add(b.center - new Vector3(0f, b.extents.y, 0f));
            points.Add(b.center + new Vector3(0f, 0f, b.extents.z));
            points.Add(b.center - new Vector3(0f, 0f, b.extents.z));
        }

        private static void SampleBoxSurface(BoxCollider box, float spacing, List<Vector3> points, int cap)
        {
            Transform t = box.transform;
            Vector3 c = box.center;
            Vector3 hs = box.size * 0.5f;

            Vector3 scaled = Vector3.Scale(box.size, Abs(t.lossyScale));
            int rx = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0.02f, scaled.x) / Mathf.Max(0.02f, spacing)), 1, 16);
            int ry = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0.02f, scaled.y) / Mathf.Max(0.02f, spacing)), 1, 16);
            int rz = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0.02f, scaled.z) / Mathf.Max(0.02f, spacing)), 1, 16);

            AddBoxFace(c, hs, 0, +1, ry, rz, t, points, cap);
            AddBoxFace(c, hs, 0, -1, ry, rz, t, points, cap);
            AddBoxFace(c, hs, 1, +1, rx, rz, t, points, cap);
            AddBoxFace(c, hs, 1, -1, rx, rz, t, points, cap);
            AddBoxFace(c, hs, 2, +1, rx, ry, t, points, cap);
            AddBoxFace(c, hs, 2, -1, rx, ry, t, points, cap);
        }

        private static void AddBoxFace(Vector3 center, Vector3 hs, int axis, int sign, int ru, int rv, Transform t, List<Vector3> points, int cap)
        {
            for (int iu = 0; iu <= ru && points.Count < cap; iu++)
            {
                float u = ru > 0 ? iu / (float)ru : 0f;
                for (int iv = 0; iv <= rv && points.Count < cap; iv++)
                {
                    float v = rv > 0 ? iv / (float)rv : 0f;
                    Vector3 lp = center;

                    if (axis == 0)
                    {
                        lp.x += sign * hs.x;
                        lp.y += Mathf.Lerp(-hs.y, hs.y, u);
                        lp.z += Mathf.Lerp(-hs.z, hs.z, v);
                    }
                    else if (axis == 1)
                    {
                        lp.y += sign * hs.y;
                        lp.x += Mathf.Lerp(-hs.x, hs.x, u);
                        lp.z += Mathf.Lerp(-hs.z, hs.z, v);
                    }
                    else
                    {
                        lp.z += sign * hs.z;
                        lp.x += Mathf.Lerp(-hs.x, hs.x, u);
                        lp.y += Mathf.Lerp(-hs.y, hs.y, v);
                    }

                    points.Add(t.TransformPoint(lp));
                }
            }
        }

        private static void SampleSphereSurface(SphereCollider sphere, float spacing, List<Vector3> points, int cap)
        {
            Transform t = sphere.transform;
            Vector3 center = t.TransformPoint(sphere.center);
            float scale = Mathf.Max(Abs(t.lossyScale).x, Mathf.Max(Abs(t.lossyScale).y, Abs(t.lossyScale).z));
            float radius = Mathf.Max(0.01f, sphere.radius * scale);

            int lat = Mathf.Clamp(Mathf.CeilToInt(Mathf.PI * radius / Mathf.Max(0.02f, spacing)), 4, 20);
            int lon = Mathf.Clamp(lat * 2, 8, 40);

            for (int i = 0; i <= lat && points.Count < cap; i++)
            {
                float v = i / (float)lat;
                float phi = Mathf.Lerp(0f, Mathf.PI, v);
                float y = Mathf.Cos(phi);
                float rr = Mathf.Sin(phi);
                for (int j = 0; j < lon && points.Count < cap; j++)
                {
                    float u = j / (float)lon;
                    float theta = u * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(rr * Mathf.Cos(theta), y, rr * Mathf.Sin(theta));
                    points.Add(center + dir * radius);
                }
            }
        }

        private static void SampleCapsuleSurface(CapsuleCollider capsule, float spacing, List<Vector3> points, int cap)
        {
            Transform t = capsule.transform;
            Vector3 ls = Abs(t.lossyScale);
            Vector3 localAxis = capsule.direction == 0 ? Vector3.right : (capsule.direction == 1 ? Vector3.up : Vector3.forward);

            float axisScale = capsule.direction == 0 ? ls.x : (capsule.direction == 1 ? ls.y : ls.z);
            float radialScale = capsule.direction == 0 ? Mathf.Max(ls.y, ls.z) : (capsule.direction == 1 ? Mathf.Max(ls.x, ls.z) : Mathf.Max(ls.x, ls.y));

            float radius = Mathf.Max(0.01f, capsule.radius * radialScale);
            float height = Mathf.Max(radius * 2f, capsule.height * axisScale);
            float halfSeg = Mathf.Max(0f, height * 0.5f - radius);

            Vector3 center = t.TransformPoint(capsule.center);
            Vector3 axis = t.TransformDirection(localAxis).normalized;
            MakeBasis(axis, out Vector3 uAxis, out Vector3 vAxis);

            int around = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / Mathf.Max(0.02f, spacing)), 8, 40);
            int rings = Mathf.Clamp(Mathf.CeilToInt((halfSeg * 2f) / Mathf.Max(0.02f, spacing)), 1, 16);

            for (int r = 0; r <= rings && points.Count < cap; r++)
            {
                float t01 = rings > 0 ? r / (float)rings : 0.5f;
                float axial = Mathf.Lerp(-halfSeg, halfSeg, t01);
                Vector3 baseC = center + axis * axial;
                for (int j = 0; j < around && points.Count < cap; j++)
                {
                    float ang = (j / (float)around) * Mathf.PI * 2f;
                    Vector3 radial = uAxis * Mathf.Cos(ang) + vAxis * Mathf.Sin(ang);
                    points.Add(baseC + radial * radius);
                }
            }

            int capRings = Mathf.Clamp(Mathf.CeilToInt((Mathf.PI * radius * 0.5f) / Mathf.Max(0.02f, spacing)), 2, 10);
            for (int h = 1; h <= capRings && points.Count < cap; h++)
            {
                float a = h / (float)(capRings + 1) * (Mathf.PI * 0.5f);
                float y = Mathf.Sin(a);
                float rr = Mathf.Cos(a);
                for (int j = 0; j < around && points.Count < cap; j++)
                {
                    float ang = (j / (float)around) * Mathf.PI * 2f;
                    Vector3 radial = uAxis * Mathf.Cos(ang) + vAxis * Mathf.Sin(ang);
                    points.Add(center + axis * (halfSeg + y * radius) + radial * (rr * radius));
                    if (points.Count >= cap) break;
                    points.Add(center - axis * (halfSeg + y * radius) + radial * (rr * radius));
                }
            }

            if (points.Count < cap) points.Add(center + axis * (halfSeg + radius));
            if (points.Count < cap) points.Add(center - axis * (halfSeg + radius));
        }

        private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static void MakeBasis(Vector3 normal, out Vector3 x, out Vector3 y)
        {
            x = Vector3.Cross(normal, Vector3.up);
            if (x.sqrMagnitude < 1e-6f)
                x = Vector3.Cross(normal, Vector3.right);
            x.Normalize();
            y = Vector3.Cross(normal, x).normalized;
        }

        private sealed class PointHash
        {
            private readonly float _cell;
            private readonly List<Vector3> _points;
            private readonly Dictionary<Vector3Int, List<int>> _buckets;

            public PointHash(List<Vector3> points, float cellSize)
            {
                _points = points;
                _cell = Mathf.Max(0.01f, cellSize);
                _buckets = new Dictionary<Vector3Int, List<int>>(points.Count / 4 + 1);

                for (int i = 0; i < points.Count; i++)
                {
                    Vector3Int k = Key(points[i]);
                    if (!_buckets.TryGetValue(k, out List<int> list))
                        _buckets[k] = list = new List<int>(8);
                    list.Add(i);
                }
            }

            public float NearestDistance(Vector3 p)
            {
                if (_points.Count == 0)
                    return 0f;

                Vector3Int c = Key(p);
                float best = float.PositiveInfinity;

                for (int ring = 0; ring <= 4; ring++)
                {
                    bool any = false;
                    for (int x = -ring; x <= ring; x++)
                    for (int y = -ring; y <= ring; y++)
                    for (int z = -ring; z <= ring; z++)
                    {
                        if (ring > 0 && Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) != ring)
                            continue;

                        var k = new Vector3Int(c.x + x, c.y + y, c.z + z);
                        if (!_buckets.TryGetValue(k, out List<int> list))
                            continue;

                        any = true;
                        for (int i = 0; i < list.Count; i++)
                        {
                            float d = (_points[list[i]] - p).sqrMagnitude;
                            if (d < best)
                                best = d;
                        }
                    }

                    if (any)
                    {
                        float minPossible = (Mathf.Max(0, ring - 1) * _cell);
                        if (best <= minPossible * minPossible)
                            break;
                    }
                }

                if (float.IsInfinity(best))
                {
                    for (int i = 0; i < _points.Count; i++)
                    {
                        float d = (_points[i] - p).sqrMagnitude;
                        if (d < best)
                            best = d;
                    }
                }

                return Mathf.Sqrt(Mathf.Max(0f, best));
            }

            private Vector3Int Key(Vector3 p)
            {
                return new Vector3Int(
                    Mathf.FloorToInt(p.x / _cell),
                    Mathf.FloorToInt(p.y / _cell),
                    Mathf.FloorToInt(p.z / _cell));
            }
        }
    }
}

