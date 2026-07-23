using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.ColliderTools
{
    /// <summary>How a single mesh part should be turned into primitive colliders.</summary>
    public enum FitStrategy
    {
        Auto,        // pick the best single primitive from the fitted OBB proportions
        Box,         // one oriented box
        Sphere,      // one bounding sphere
        Capsule,     // one capsule along the longest axis
        Decompose,   // voxel-shell decomposition into a set of boxes (hull/walls/floors)
        Limbs,       // per-connected-component fit: capsule/box/voxel per limb (masts, rigging frames)
        Ropes,       // one climbable capsule per rope/cable strand (rigging, shrouds, stays)
        Stairs,      // one inclined ramp slab over the treads + one panel box per railing
        FloorPlan,   // flat slabs following the floor outline, preserving holes / hatches
        Fence,       // one oriented box per railing run (merges balusters/gaps; splits at gaps)
        Rounded,     // faceted shell of rotated boxes following a rounded volume (hull, nests)
        Skip,        // do not generate colliders for this part
    }

    /// <summary>Whether a category's colliders are solid or triggers.</summary>
    public enum ColliderTrigger
    {
        Default, // follow the global "Mark As Triggers" toggle
        Trigger, // force isTrigger = true
        Solid,   // force isTrigger = false
    }

    /// <summary>
    /// Maps a mesh part (matched by a substring of its GameObject name) to a target
    /// hierarchy sub-group and a fitting strategy. The first matching rule wins; an
    /// empty <see cref="NameContains"/> acts as a catch-all.
    /// </summary>
    [Serializable]
    public class CategoryRule
    {
        public string NameContains = string.Empty;
        public string Category = "Misc";
        public FitStrategy Strategy = FitStrategy.Auto;
        public ColliderTrigger Trigger = ColliderTrigger.Default;

        public CategoryRule() { }

        public CategoryRule(string nameContains, string category, FitStrategy strategy, ColliderTrigger trigger = ColliderTrigger.Default)
        {
            NameContains = nameContains;
            Category = category;
            Strategy = strategy;
            Trigger = trigger;
        }
    }

    /// <summary>Serializable configuration for a bake run.</summary>
    [Serializable]
    public class BakeSettings
    {
        public string GroupName = "AutoColliders";
        public bool GroupByCategory = true;
        public bool ClearPrevious = true;
        public bool IncludeInactive = true;

        [Tooltip("Voxel edge length (working/root units) used when decomposing complex parts. Smaller = more precise, more colliders.")]
        public float VoxelSize = 0.30f;
        public int MaxBoxesPerPart = 48;

        [Tooltip("Parts whose OBB longest edge is below this are skipped entirely.")]
        public float MinPartSize = 0.06f;

        public bool RemoveSourceMeshColliders = true;
        public bool DisableSourceColliders = false;

        [Tooltip("Automatically enable Read/Write on imported models whose meshes are not readable.")]
        public bool AutoEnableReadWrite = true;

        [Tooltip("Global default for isTrigger. Per-category rules can override this to force trigger or solid.")]
        public bool MarkTriggers = false;
        public bool AssignLayer = false;
        public int Layer;

        // Resolved isTrigger for the part currently being baked (set per part from its rule).
        // Runtime-only: not persisted.
        [NonSerialized] public bool CurrentTrigger;

        [Tooltip("Auto: treat the part as a sphere when longest/shortest edge ratio is below this.")]
        public float SphereTolerance = 1.35f;

        [Tooltip("Auto: treat the part as a capsule when it is elongated and its cross-section edges differ by less than this ratio.")]
        public float CapsuleTolerance = 1.6f;

        [Tooltip("Limbs: a component becomes a single capsule when its length / thickness ratio is at least this (lower = capsules for stubbier limbs).")]
        public float LimbElongation = 2.5f;

        [Tooltip("Limbs: capsule radius comes from this percentile of vertex distance to the limb axis (0.9 = snug but ignores stray verts).")]
        public float CapsuleRadiusPercentile = 0.9f;

        [Tooltip("Limbs: components shorter than this fraction of the largest limb are dropped (rings/bands/cleats). Higher = fewer, simpler colliders.")]
        public float LimbMinSizeFraction = 0.15f;

        [Tooltip("Ropes: minimum capsule radius for a rope/cable strand so thin ropes stay grab-able for climbing/hanging.")]
        public float RopeMinRadius = 0.06f;

        [Tooltip("Stairs: thickness of the inclined ramp slab placed over the step treads.")]
        public float StairRampThickness = 0.3f;

        [Tooltip("Stairs: also add one panel box per side railing (off = just the single ramp).")]
        public bool StairRailings = true;

        [Tooltip("Stairs: max gap between step islands that still counts as the same flight. Separate staircases sit farther apart and get their own ramp.")]
        public float StairClusterGap = 0.35f;

        [Tooltip("FloorPlan: grid cell size for tracing the floor outline. Larger = fewer slabs, coarser holes.")]
        public float FloorCellSize = 0.5f;

        [Tooltip("FloorPlan: fill enclosed holes (hatches) so the floor is solid there.")]
        public bool FloorFillHoles = true;

        [Tooltip("Fence: components within this distance merge into one railing run (one box). Corners with a gap split into separate boxes.")]
        public float RailingMergeDistance = 0.6f;

        [Tooltip("Fence: max deviation before a curved railing run is split into another box. Lower = follows curves tighter (more boxes); higher = straighter/fewer.")]
        public float FenceCurveTolerance = 0.2f;

        [Range(0f, 1f)]
        [Tooltip("Global simplification. Higher coarsens voxel/floor grids -> far fewer colliders (use to cut hull collider count / fix perf).")]
        public float Simplify = 0f;

        [Tooltip("Rounded precision: number of angular sectors used to facet a rounded volume. Higher = tighter to the curve, more rotated boxes (reduced by Simplify).")]
        public int Precise = 8;

        [Tooltip("Rounded: minimum wall thickness of each facet box.")]
        public float RoundedThickness = 0.2f;

        [Range(0.01f, 0.95f)]
        [Tooltip("Rounded clusters: keep a cluster only when estimated mesh intersection in that cluster is at least this fraction.")]
        public float RoundedClusterFill = 0.08f;

        // Multiplier applied to voxel/cell sizes from Simplify (1x .. 4x coarser).
        public float SizeScale => Mathf.Lerp(1f, 4f, Mathf.Clamp01(Simplify));

        // Per-part box budget after simplification (coarser + fewer).
        public int EffectiveMaxBoxes => Mathf.Max(4, Mathf.RoundToInt(MaxBoxesPerPart / SizeScale));

        public List<CategoryRule> Rules = new List<CategoryRule>();

        public static List<CategoryRule> CreateShipDefaultRules()
        {
            // This auto tool now only handles ropes and masts; everything else is drawn by hand
            // with the Collider Painter. All other parts are skipped by default.
            return new List<CategoryRule>
            {
                new CategoryRule("Wire", "Rigging", FitStrategy.Ropes),
                new CategoryRule("Rope", "Rigging", FitStrategy.Ropes),

                new CategoryRule("Mast", "Masts", FitStrategy.Limbs),
                new CategoryRule("Bracing", "Masts", FitStrategy.Limbs),

                // Catch-all: leave everything else alone (draw it with the Collider Painter).
                new CategoryRule(string.Empty, "Misc", FitStrategy.Skip),
            };
        }

        public void EnsureRules()
        {
            if (Rules == null || Rules.Count == 0)
                Rules = CreateShipDefaultRules();
        }
    }

    /// <summary>Result of a bake run for reporting back to the user.</summary>
    public class BakeReport
    {
        public int PartsProcessed;
        public int PartsSkipped;
        public int PartsUnreadable;
        public int CollidersCreated;
        public int MeshCollidersRemoved;
        public readonly Dictionary<string, int> PerCategory = new Dictionary<string, int>();
        public readonly List<string> Warnings = new List<string>();

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Colliders created: {CollidersCreated}  |  parts: {PartsProcessed} processed, {PartsSkipped} skipped, {PartsUnreadable} unreadable");
            if (MeshCollidersRemoved > 0)
                sb.AppendLine($"Removed {MeshCollidersRemoved} source MeshColliders.");
            foreach (var kvp in PerCategory)
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            foreach (var w in Warnings)
                sb.AppendLine($"  ! {w}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Analyses every mesh under a target GameObject and rebuilds a clean hierarchy of
    /// primitive colliders (box / sphere / capsule), grouped by category, replacing
    /// overlapping convex mesh colliders.
    /// </summary>
    public static class PrimitiveColliderBaker
    {
        private const string LogPrefix = "[PrimitiveColliderBaker]";

        public static BakeReport Bake(GameObject target, BakeSettings settings)
        {
            var report = new BakeReport();
            if (target == null)
            {
                report.Warnings.Add("No target GameObject.");
                return report;
            }

            settings.EnsureRules();
            Transform root = target.transform;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Auto-Fit Primitive Colliders");

            // 1. Remove / clear an existing generated group so re-runs are idempotent.
            Transform existingGroup = root.Find(settings.GroupName);
            if (existingGroup != null && settings.ClearPrevious)
            {
                Undo.DestroyObjectImmediate(existingGroup.gameObject);
                existingGroup = null;
            }

            var groupRoot = existingGroup != null
                ? existingGroup.gameObject
                : CreateChild(root, settings.GroupName).gameObject;
            if (existingGroup == null)
                Undo.RegisterCreatedObjectUndo(groupRoot, "Create AutoColliders");

            var categoryGroups = new Dictionary<string, Transform>();
            Matrix4x4 rootWorldToLocal = root.worldToLocalMatrix;

            // 2. Collect candidate mesh parts (skip anything under the generated group).
            var parts = CollectParts(target, groupRoot.transform, settings.IncludeInactive);

            // 3. Imported FBX meshes are non-readable by default; flip Read/Write on their
            //    importers so we can access vertex data (reimports may invalidate cached
            //    Mesh references, so all mesh access below is re-resolved from the Transform).
            if (settings.AutoEnableReadWrite)
                EnableReadWriteForParts(parts, report);

            // 4. Optionally strip source mesh colliders that we are replacing.
            if (settings.RemoveSourceMeshColliders || settings.DisableSourceColliders)
                report.MeshCollidersRemoved += HandleSourceColliders(parts, settings);

            try
            {
                for (int p = 0; p < parts.Count; p++)
                {
                    Transform part = parts[p];
                    EditorUtility.DisplayProgressBar(
                        "Auto-Fit Primitive Colliders",
                        $"{part.name} ({p + 1}/{parts.Count})",
                        (p + 1f) / parts.Count);

                    CategoryRule rule = MatchRule(part.name, settings.Rules);
                    if (rule.Strategy == FitStrategy.Skip)
                    {
                        report.PartsSkipped++;
                        continue;
                    }

                    if (!TryReadMesh(part, rootWorldToLocal, out List<Vector3> verts, out List<int> tris))
                    {
                        report.PartsUnreadable++;
                        report.Warnings.Add($"'{part.name}' mesh is not readable; skipped. Enable Read/Write on the model or it will be ignored.");
                        continue;
                    }

                    if (!ColliderGeometry.TryComputeObb(verts, out ColliderGeometry.Obb obb))
                    {
                        report.PartsSkipped++;
                        continue;
                    }

                    float longest = Mathf.Max(obb.Size.x, Mathf.Max(obb.Size.y, obb.Size.z));
                    if (longest < settings.MinPartSize)
                    {
                        report.PartsSkipped++;
                        continue;
                    }

                    FitStrategy strategy = ResolveStrategy(rule.Strategy, obb, settings);
                    Transform categoryGroup = ResolveCategoryGroup(groupRoot.transform, rule.Category, settings, categoryGroups);

                    settings.CurrentTrigger = rule.Trigger switch
                    {
                        ColliderTrigger.Trigger => true,
                        ColliderTrigger.Solid => false,
                        _ => settings.MarkTriggers,
                    };

                    int created = BuildColliders(strategy, part.name, categoryGroup, obb, verts, tris, settings, report);
                    report.CollidersCreated += created;
                    report.PartsProcessed++;

                    string catKey = settings.GroupByCategory ? rule.Category : settings.GroupName;
                    report.PerCategory.TryGetValue(catKey, out int c);
                    report.PerCategory[catKey] = c + created;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(target);

            Debug.Log($"{LogPrefix} Bake complete for '{target.name}'.\n{report}");
            return report;
        }

        // ------------------------------------------------------------------
        // Collider construction per strategy
        // ------------------------------------------------------------------

        private static int BuildColliders(
            FitStrategy strategy,
            string partName,
            Transform categoryGroup,
            in ColliderGeometry.Obb obb,
            List<Vector3> verts,
            List<int> tris,
            BakeSettings settings,
            BakeReport report)
        {
            switch (strategy)
            {
                case FitStrategy.Sphere:
                    return BuildSphere(partName, categoryGroup, verts, settings);
                case FitStrategy.Capsule:
                    return BuildCapsule(partName, categoryGroup, obb, settings);
                case FitStrategy.Limbs:
                    return BuildLimbs(partName, categoryGroup, obb, verts, tris, settings, report);
                case FitStrategy.Ropes:
                    return BuildRopes(partName, categoryGroup, obb, verts, tris, settings, report);
                case FitStrategy.Stairs:
                    return BuildStairs(partName, categoryGroup, obb, verts, tris, settings, report);
                case FitStrategy.FloorPlan:
                    return BuildFloorPlan(partName, categoryGroup, obb, verts, tris, settings, report);
                case FitStrategy.Fence:
                    return BuildFence(partName, categoryGroup, obb, verts, tris, settings, report);
                case FitStrategy.Rounded:
                    return BuildRounded(partName, categoryGroup, obb, verts, tris, settings, report);
                case FitStrategy.Decompose:
                    return BuildDecomposed(partName, categoryGroup, obb, verts, tris, settings, report);
                case FitStrategy.Box:
                default:
                    return BuildBox(partName, categoryGroup, obb, settings);
            }
        }

        private static int BuildBox(string partName, Transform parent, in ColliderGeometry.Obb obb, BakeSettings settings)
        {
            var go = CreateColliderObject($"{partName}_Box", parent, obb.Center, obb.Rotation, settings);
            var box = Undo.AddComponent<BoxCollider>(go);
            box.center = Vector3.zero;
            box.size = obb.Size;
            box.isTrigger = settings.CurrentTrigger;
            return 1;
        }

        private static int BuildSphere(string partName, Transform parent, List<Vector3> verts, BakeSettings settings)
        {
            ColliderGeometry.ComputeBoundingSphere(verts, out Vector3 center, out float radius);
            var go = CreateColliderObject($"{partName}_Sphere", parent, center, Quaternion.identity, settings);
            var sphere = Undo.AddComponent<SphereCollider>(go);
            sphere.center = Vector3.zero;
            sphere.radius = Mathf.Max(radius, 0.01f);
            sphere.isTrigger = settings.CurrentTrigger;
            return 1;
        }

        private static int BuildCapsule(string partName, Transform parent, in ColliderGeometry.Obb obb, BakeSettings settings)
        {
            var go = CreateColliderObject($"{partName}_Capsule", parent, obb.Center, obb.Rotation, settings);
            var capsule = Undo.AddComponent<CapsuleCollider>(go);

            // Longest OBB axis becomes the capsule axis; radius from the two minor axes.
            Vector3 s = obb.Size;
            int dir = 0;
            float major = s.x;
            float minorA = s.y, minorB = s.z;
            if (s.y >= s.x && s.y >= s.z) { dir = 1; major = s.y; minorA = s.x; minorB = s.z; }
            else if (s.z >= s.x && s.z >= s.y) { dir = 2; major = s.z; minorA = s.x; minorB = s.y; }

            float radius = Mathf.Max(Mathf.Max(minorA, minorB) * 0.5f, 0.01f);
            capsule.direction = dir;
            capsule.center = Vector3.zero;
            capsule.radius = radius;
            capsule.height = Mathf.Max(major, radius * 2f);
            capsule.isTrigger = settings.CurrentTrigger;
            return 1;
        }

        private static int BuildLimbs(
            string partName,
            Transform parent,
            in ColliderGeometry.Obb obb,
            List<Vector3> verts,
            List<int> tris,
            BakeSettings settings,
            BakeReport report)
        {
            List<ColliderGeometry.FittedPrimitive> prims = ColliderGeometry.DecomposeToLimbs(
                verts, tris, settings.LimbElongation, settings.CapsuleRadiusPercentile,
                settings.MinPartSize, settings.LimbMinSizeFraction, settings.VoxelSize * settings.SizeScale, settings.EffectiveMaxBoxes);

            if (prims.Count == 0)
            {
                report.Warnings.Add($"'{partName}' limb fit produced nothing; used a single oriented box.");
                return BuildBox(partName, parent, obb, settings);
            }

            // A single-limb part (a plain pole) stays flat under the category group.
            if (prims.Count == 1)
                return CreatePrimitiveObject($"{partName}_{Suffix(prims[0])}", parent, prims[0], settings);

            Transform partGroup = CreateChild(parent, $"{partName}_Parts");
            Undo.RegisterCreatedObjectUndo(partGroup.gameObject, "Create part group");

            for (int i = 0; i < prims.Count; i++)
                CreatePrimitiveObject($"{partName}_{Suffix(prims[i])}_{i}", partGroup, prims[i], settings);

            return prims.Count;
        }

        private static int BuildRopes(
            string partName,
            Transform parent,
            in ColliderGeometry.Obb obb,
            List<Vector3> verts,
            List<int> tris,
            BakeSettings settings,
            BakeReport report)
        {
            List<ColliderGeometry.FittedPrimitive> prims = ColliderGeometry.DecomposeToRopeCapsules(
                verts, tris, settings.CapsuleRadiusPercentile, settings.MinPartSize, settings.RopeMinRadius);

            if (prims.Count == 0)
            {
                // Rope modelled as a single strand the welder could not split -> one capsule.
                report.Warnings.Add($"'{partName}' produced no rope strands; used a single capsule.");
                return BuildCapsule(partName, parent, obb, settings);
            }

            if (prims.Count == 1)
                return CreatePrimitiveObject($"{partName}_Rope", parent, prims[0], settings);

            Transform partGroup = CreateChild(parent, $"{partName}_Ropes");
            Undo.RegisterCreatedObjectUndo(partGroup.gameObject, "Create part group");

            for (int i = 0; i < prims.Count; i++)
                CreatePrimitiveObject($"{partName}_Rope_{i}", partGroup, prims[i], settings);

            return prims.Count;
        }

        private static int BuildStairs(
            string partName,
            Transform parent,
            in ColliderGeometry.Obb obb,
            List<Vector3> verts,
            List<int> tris,
            BakeSettings settings,
            BakeReport report)
        {
            List<ColliderGeometry.FittedPrimitive> prims = ColliderGeometry.DecomposeStairs(
                verts, tris, Vector3.up, settings.MinPartSize, settings.StairRampThickness,
                settings.StairRailings, settings.StairClusterGap);

            if (prims.Count == 0)
            {
                report.Warnings.Add($"'{partName}' stair fit produced nothing; used a single box.");
                return BuildBox(partName, parent, obb, settings);
            }

            if (prims.Count == 1)
                return CreatePrimitiveObject($"{partName}_Ramp", parent, prims[0], settings);

            Transform partGroup = CreateChild(parent, $"{partName}_Parts");
            Undo.RegisterCreatedObjectUndo(partGroup.gameObject, "Create part group");

            for (int i = 0; i < prims.Count; i++)
                CreatePrimitiveObject($"{partName}_Part_{i}", partGroup, prims[i], settings);

            return prims.Count;
        }

        private static int BuildFloorPlan(
            string partName,
            Transform parent,
            in ColliderGeometry.Obb obb,
            List<Vector3> verts,
            List<int> tris,
            BakeSettings settings,
            BakeReport report)
        {
            List<ColliderGeometry.FittedPrimitive> prims = ColliderGeometry.DecomposeFloor(
                verts, tris, settings.FloorCellSize * settings.SizeScale, settings.EffectiveMaxBoxes, settings.FloorFillHoles);

            if (prims.Count == 0)
            {
                report.Warnings.Add($"'{partName}' floor plan produced nothing; used a single box.");
                return BuildBox(partName, parent, obb, settings);
            }

            if (prims.Count == 1)
                return CreatePrimitiveObject($"{partName}_Floor", parent, prims[0], settings);

            Transform partGroup = CreateChild(parent, $"{partName}_Floor");
            Undo.RegisterCreatedObjectUndo(partGroup.gameObject, "Create part group");

            for (int i = 0; i < prims.Count; i++)
                CreatePrimitiveObject($"{partName}_Floor_{i}", partGroup, prims[i], settings);

            return prims.Count;
        }

        private static int BuildFence(
            string partName,
            Transform parent,
            in ColliderGeometry.Obb obb,
            List<Vector3> verts,
            List<int> tris,
            BakeSettings settings,
            BakeReport report)
        {
            List<ColliderGeometry.FittedPrimitive> prims = ColliderGeometry.DecomposeFence(
                verts, tris, Vector3.up, settings.MinPartSize, settings.RailingMergeDistance, settings.FenceCurveTolerance);

            if (prims.Count == 0)
            {
                report.Warnings.Add($"'{partName}' fence fit produced nothing; used a single box.");
                return BuildBox(partName, parent, obb, settings);
            }

            if (prims.Count == 1)
                return CreatePrimitiveObject($"{partName}_Rail", parent, prims[0], settings);

            Transform partGroup = CreateChild(parent, $"{partName}_Rails");
            Undo.RegisterCreatedObjectUndo(partGroup.gameObject, "Create part group");

            for (int i = 0; i < prims.Count; i++)
                CreatePrimitiveObject($"{partName}_Rail_{i}", partGroup, prims[i], settings);

            return prims.Count;
        }

        private static int BuildRounded(
            string partName,
            Transform parent,
            in ColliderGeometry.Obb obb,
            List<Vector3> verts,
            List<int> tris,
            BakeSettings settings,
            BakeReport report)
        {
            int precise = Mathf.Clamp(Mathf.RoundToInt(settings.Precise / settings.SizeScale), 3, 256);
            List<ColliderGeometry.FittedPrimitive> prims = ColliderGeometry.DecomposeRoundedFacets(
                verts, tris, Vector3.up, precise, settings.MinPartSize, settings.RoundedThickness, settings.RoundedClusterFill);

            if (prims.Count == 0)
            {
                report.Warnings.Add($"'{partName}' rounded fit produced nothing; used a single box.");
                return BuildBox(partName, parent, obb, settings);
            }

            if (prims.Count == 1)
                return CreatePrimitiveObject($"{partName}_Facet", parent, prims[0], settings);

            Transform partGroup = CreateChild(parent, $"{partName}_Facets");
            Undo.RegisterCreatedObjectUndo(partGroup.gameObject, "Create part group");

            for (int i = 0; i < prims.Count; i++)
                CreatePrimitiveObject($"{partName}_Facet_{i}", partGroup, prims[i], settings);

            return prims.Count;
        }

        private static string Suffix(ColliderGeometry.FittedPrimitive p) => p.IsCapsule ? "Capsule" : "Box";

        private static int CreatePrimitiveObject(
            string name,
            Transform parent,
            ColliderGeometry.FittedPrimitive prim,
            BakeSettings settings)
        {
            var go = CreateColliderObject(name, parent, prim.Center, prim.Rotation, settings);
            if (prim.IsCapsule)
            {
                var capsule = Undo.AddComponent<CapsuleCollider>(go);
                capsule.center = Vector3.zero;
                capsule.direction = prim.Direction;
                capsule.radius = Mathf.Max(prim.Radius, 0.01f);
                capsule.height = Mathf.Max(prim.Height, prim.Radius * 2f);
                capsule.isTrigger = settings.CurrentTrigger;
            }
            else
            {
                var box = Undo.AddComponent<BoxCollider>(go);
                box.center = Vector3.zero;
                box.size = prim.Size;
                box.isTrigger = settings.CurrentTrigger;
            }

            return 1;
        }

        private static int BuildDecomposed(
            string partName,
            Transform parent,
            in ColliderGeometry.Obb obb,
            List<Vector3> verts,
            List<int> tris,
            BakeSettings settings,
            BakeReport report)
        {
            float voxel = settings.VoxelSize * settings.SizeScale;
            List<ColliderGeometry.LocalBox> boxes = ColliderGeometry.DecomposeShellToBoxes(
                verts, tris, obb, voxel, settings.EffectiveMaxBoxes,
                out float usedVoxel, out int cells);

            if (boxes.Count == 0)
            {
                // Fall back to a single oriented box rather than leaving the part solid-less.
                report.Warnings.Add($"'{partName}' produced no voxels ({cells} cells); used a single box instead.");
                return BuildBox(partName, parent, obb, settings);
            }

            if (usedVoxel > voxel + 1e-4f)
                report.Warnings.Add($"'{partName}' voxel size grown to {usedVoxel:0.###} to stay under {settings.MaxBoxesPerPart} boxes.");

            Transform partGroup = CreateChild(parent, $"{partName}_Boxes");
            Undo.RegisterCreatedObjectUndo(partGroup.gameObject, "Create part group");

            for (int i = 0; i < boxes.Count; i++)
            {
                ColliderGeometry.LocalBox b = boxes[i];
                Vector3 worldLocalCenter = obb.Center + obb.Rotation * b.Center;
                var go = CreateColliderObject($"{partName}_Box_{i}", partGroup, worldLocalCenter, obb.Rotation, settings);
                var box = Undo.AddComponent<BoxCollider>(go);
                box.center = Vector3.zero;
                box.size = b.Size;
                box.isTrigger = settings.CurrentTrigger;
            }

            return boxes.Count;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static FitStrategy ResolveStrategy(FitStrategy requested, in ColliderGeometry.Obb obb, BakeSettings settings)
        {
            if (requested != FitStrategy.Auto)
                return requested;

            Vector3 s = obb.Size;
            float major = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            float minor = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            float mid = s.x + s.y + s.z - major - minor;
            minor = Mathf.Max(minor, 1e-4f);
            mid = Mathf.Max(mid, 1e-4f);

            // Roughly cube-like -> sphere.
            if (major / minor <= settings.SphereTolerance)
                return FitStrategy.Sphere;

            // Elongated with a round cross section -> capsule.
            if (major / mid >= 1.6f && mid / minor <= settings.CapsuleTolerance)
                return FitStrategy.Capsule;

            return FitStrategy.Box;
        }

        private static CategoryRule MatchRule(string name, List<CategoryRule> rules)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                CategoryRule rule = rules[i];
                if (string.IsNullOrEmpty(rule.NameContains))
                    return rule;
                if (name.IndexOf(rule.NameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return rule;
            }

            return new CategoryRule(string.Empty, "Misc", FitStrategy.Auto);
        }

        private static Transform ResolveCategoryGroup(
            Transform groupRoot,
            string category,
            BakeSettings settings,
            Dictionary<string, Transform> cache)
        {
            if (!settings.GroupByCategory)
                return groupRoot;

            if (cache.TryGetValue(category, out Transform t) && t != null)
                return t;

            Transform existing = groupRoot.Find(category);
            if (existing == null)
            {
                existing = CreateChild(groupRoot, category);
                Undo.RegisterCreatedObjectUndo(existing.gameObject, "Create category group");
            }

            cache[category] = existing;
            return existing;
        }

        private static GameObject CreateColliderObject(
            string name,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            BakeSettings settings)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create collider");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = Vector3.one;
            if (settings.AssignLayer)
                go.layer = settings.Layer;
            return go;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static List<Transform> CollectParts(GameObject target, Transform generatedGroup, bool includeInactive)
        {
            var result = new List<Transform>();
            var seen = new HashSet<Transform>();

            foreach (var mf in target.GetComponentsInChildren<MeshFilter>(includeInactive))
            {
                if (mf.sharedMesh == null || IsUnder(mf.transform, generatedGroup) || !seen.Add(mf.transform))
                    continue;
                result.Add(mf.transform);
            }

            foreach (var smr in target.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive))
            {
                if (smr.sharedMesh == null || IsUnder(smr.transform, generatedGroup) || !seen.Add(smr.transform))
                    continue;
                result.Add(smr.transform);
            }

            return result;
        }

        private static void EnableReadWriteForParts(List<Transform> parts, BakeReport report)
        {
            var pathsToFix = new HashSet<string>();
            foreach (Transform part in parts)
            {
                Mesh mesh = ResolveMesh(part);
                if (mesh == null || mesh.isReadable)
                    continue;

                string path = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(path))
                    pathsToFix.Add(path);
            }

            if (pathsToFix.Count == 0)
                return;

            foreach (string path in pathsToFix)
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    report.Warnings.Add($"Enabled Read/Write on '{path}'.");
                }
            }
        }

        private static int HandleSourceColliders(List<Transform> parts, BakeSettings settings)
        {
            int removed = 0;
            foreach (Transform part in parts)
            {
                var colliders = part.GetComponents<Collider>();
                foreach (Collider col in colliders)
                {
                    if (col == null)
                        continue;

                    if (settings.RemoveSourceMeshColliders && col is MeshCollider)
                    {
                        Undo.DestroyObjectImmediate(col);
                        removed++;
                    }
                    else if (settings.DisableSourceColliders)
                    {
                        Undo.RecordObject(col, "Disable source collider");
                        col.enabled = false;
                    }
                }
            }

            return removed;
        }

        private static bool TryReadMesh(Transform part, Matrix4x4 rootWorldToLocal, out List<Vector3> verts, out List<int> tris)
        {
            verts = null;
            tris = null;

            Mesh mesh = ResolveMesh(part);
            if (mesh == null || !mesh.isReadable)
                return false;

            Vector3[] rawVerts = mesh.vertices;
            int[] rawTris = mesh.triangles;
            if (rawVerts == null || rawVerts.Length == 0 || rawTris == null || rawTris.Length == 0)
                return false;

            Matrix4x4 toRoot = rootWorldToLocal * part.localToWorldMatrix;

            verts = new List<Vector3>(rawVerts.Length);
            for (int i = 0; i < rawVerts.Length; i++)
                verts.Add(toRoot.MultiplyPoint3x4(rawVerts[i]));

            tris = new List<int>(rawTris.Length);
            tris.AddRange(rawTris);
            return true;
        }

        private static Mesh ResolveMesh(Transform part)
        {
            if (part.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                return mf.sharedMesh;
            if (part.TryGetComponent(out SkinnedMeshRenderer smr) && smr.sharedMesh != null)
                return smr.sharedMesh;
            return null;
        }

        private static bool IsUnder(Transform t, Transform ancestor)
        {
            if (ancestor == null)
                return false;
            for (Transform c = t; c != null; c = c.parent)
                if (c == ancestor)
                    return true;
            return false;
        }
    }
}
