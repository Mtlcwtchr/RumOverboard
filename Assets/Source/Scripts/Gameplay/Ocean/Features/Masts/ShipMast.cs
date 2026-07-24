using System;
using System.Collections.Generic;
using RumOverboard.Gameplay.Ocean.Simulation;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Masts
{
    /// <summary>
    /// Independent mast component: owns its MastDefinition, its SailPanels,
    /// cloth simulation, aerodynamic forces and mast stress.
    /// Place one per mast on child GameObjects of the ship.
    /// The ship-level <see cref="ShipSailsAggregator"/> discovers and drives these.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipMast : MonoBehaviour
    {
        #region Data Structures

        [Serializable]
        public class MastDefinition
        {
            public string name = "Mast";
            public Vector3 baseLocal;
            public Vector3 topLocal;
            [Min(0.05f)] public float radius = 0.22f;
            [Min(1000f)] public float yieldStrength = 40_000_000f;
            [Min(100000f)] public float elasticModulus = 12_000_000_000f;

            [NonSerialized] public float stressRatio;
            [NonSerialized] public float currentBendingMoment;
            [NonSerialized] public float maxBendingMoment;
            [NonSerialized] public Vector3 totalForceOnMast;
        }

        [Serializable]
        public class SailPanel
        {
            public string name = "Sail";
            public bool enabled = true;

            [Header("Visual sync")]
            public string visualSailName = string.Empty;
            public bool pinBoundaryToRig = true;
            public bool triangularHint;

            [Header("Mast attachment")]
            [Range(0f, 1f)] public float mastAttachHeight = 0.5f;
            [Range(4, 32)] public int ropeAttachmentPoints = 10;

            [Header("Layout (ship-local) - synced with yards")]
            public Vector3 topLeftLocal;
            public Vector3 topRightLocal;
            public Vector3 bottomLeftLocal;
            public Vector3 bottomRightLocal;

            [Header("Grid resolution")]
            [Range(2, 16)] public int gridWidth = 8;
            [Range(2, 16)] public int gridHeight = 6;

            [Header("Control")]
            [Min(0.1f)] public float baseArea = 12f;
            [Range(0f, 1f)] public float hoist01 = 1f;
            [Range(0f, 1f)] public float extension01 = 1f;
            [Range(-85f, 85f)] public float sheetAngleDeg;
            [Range(0f, 1f)] public float damage01;

            [Header("Cloth physics")]
            [Min(0.01f)] public float particleMass = 0.22f;
            [Min(0f)] public float structuralStiffness = 360f;
            [Min(0f)] public float shearStiffness = 180f;
            [Min(0f)] public float bendStiffness = 28f;
            [Min(0f)] public float damping = 4.8f;
            [Min(0f)] public float gravityScale = 0.6f;
            [Min(0f)] public float windDragCoeff = 1.2f;
            [Min(0f)] public float windLiftCoeff = 0.4f;
            [Min(0f)] public float windFlutterStrength = 0.42f;
            [Min(0f)] public float windFlutterFrequency = 3.5f;
            [Range(1, 12)] public int solverIterations = 4;

            [Header("Visual")]
            public Color clothColor = new Color(0.95f, 0.92f, 0.85f, 0.92f);

            // Runtime data
            [NonSerialized] public Vector3[] positions;
            [NonSerialized] public Vector3[] prevPositions;
            [NonSerialized] public Vector3[] restPositions;
            [NonSerialized] public bool[] pinned;
            [NonSerialized] public bool initialized;

            [NonSerialized] public Transform visualRoot;
            [NonSerialized] public MeshFilter meshFilter;
            [NonSerialized] public MeshRenderer meshRenderer;
            [NonSerialized] public Mesh mesh;

            [NonSerialized] public Vector3 lastForce;
            [NonSerialized] public Vector3 lastForcePoint;
            [NonSerialized] public float lastMastMoment;

            public int ParticleCount => gridWidth * gridHeight;
            public int GetIndex(int x, int y) => y * gridWidth + x;
        }

        #endregion

        #region Serialized Fields

        [SerializeField] private MastDefinition mast = new MastDefinition();
        [SerializeField] private SailPanel[] sails = Array.Empty<SailPanel>();

        [Header("Standalone mode (runs when no aggregator drives this mast)")]
        [SerializeField] private bool standalone = true;
        [SerializeField] private float airDensity = 1.225f;
        [SerializeField] private float sailForceScale = 1f;
        [SerializeField] private float maxForcePerSail = 6000f;
        [SerializeField] private float liftFactor = 1.1f;
        [SerializeField] private float dragFactor = 1f;
        [SerializeField] private float sideForceFactor = 0.6f;
        [SerializeField] private float reverseDriveFactor = 0.12f;
        [SerializeField] private float verticalForceFactor = 0.08f;
        [SerializeField] private bool blockReverseDriveFromHeadwind = true;
        [SerializeField] [Range(1, 8)] private int substeps = 3;

        [Header("Runtime visuals")]
        [SerializeField] private bool createVisuals = true;
        [SerializeField] private Material sailMaterial;

        [Header("Debug")]
        [SerializeField] private bool drawDebugGizmos = true;

        [Header("Editor")]
        [SerializeField] private bool editorShowAttachmentHandles = true;
        [SerializeField] private bool editorShowPointLabels = true;
        [SerializeField] private bool editorDrawAttachmentPointsGizmo = true;
        [SerializeField] private bool editorDrawMeshPreviewGizmo = true;
        [SerializeField] [Min(0.01f)] private float editorPointHandleSize = 0.08f;

        #endregion

        #region Private state

        private Material _runtimeSailMaterial;
        /// <summary>Cached ship transform — set by aggregator or resolved on Awake.</summary>
        private Transform _shipTransform;
        private Rigidbody _shipBody;
        private OceanWindSystem _windSystem;
        private bool _drivenByAggregator;

        #endregion

        #region Public API

        public MastDefinition Mast => mast;
        public int SailCount => sails != null ? sails.Length : 0;
        public SailPanel[] Sails => sails;

        public float StressRatio => mast.stressRatio;
        public float CurrentBendingMoment => mast.currentBendingMoment;
        public float MaxBendingMoment => mast.maxBendingMoment;
        public Vector3 TotalForceOnMast => mast.totalForceOnMast;

        /// <summary>Sum of drive forces from all sails on this mast.</summary>
        public Vector3 TotalDriveForce
        {
            get
            {
                Vector3 sum = Vector3.zero;
                if (sails != null)
                    for (int i = 0; i < sails.Length; i++)
                        if (sails[i] != null && sails[i].enabled)
                            sum += sails[i].lastForce;
                return sum;
            }
        }

        public void SetSails(SailPanel[] panels) => sails = panels ?? Array.Empty<SailPanel>();

        public void SetMast(MastDefinition def)
        {
            mast = def ?? new MastDefinition();
            ComputeMastStrength();
        }

        public void SetHoist(int sailIndex, float value) { if (TryGetSail(sailIndex, out var s)) s.hoist01 = Mathf.Clamp01(value); }
        public void SetExtension(int sailIndex, float value) { if (TryGetSail(sailIndex, out var s)) s.extension01 = Mathf.Clamp01(value); }
        public void SetSheetAngle(int sailIndex, float value) { if (TryGetSail(sailIndex, out var s)) s.sheetAngleDeg = Mathf.Clamp(value, -85f, 85f); }
        public void SetDamage(int sailIndex, float value) { if (TryGetSail(sailIndex, out var s)) s.damage01 = Mathf.Clamp01(value); }

        public string GetSailName(int i) => TryGetSail(i, out var s) ? (string.IsNullOrWhiteSpace(s.name) ? $"Sail {i}" : s.name) : $"Sail {i}";
        public bool GetSailEnabled(int i) => TryGetSail(i, out var s) && s.enabled;
        public void SetSailEnabled(int i, bool v) { if (TryGetSail(i, out var s)) s.enabled = v; }
        public float GetHoist(int i) => TryGetSail(i, out var s) ? s.hoist01 : 0f;
        public float GetExtension(int i) => TryGetSail(i, out var s) ? s.extension01 : 0f;
        public float GetSheetAngle(int i) => TryGetSail(i, out var s) ? s.sheetAngleDeg : 0f;
        public float GetDamage(int i) => TryGetSail(i, out var s) ? s.damage01 : 0f;

        public float GetEffectiveArea(int i)
        {
            if (!TryGetSail(i, out var s)) return 0f;
            float deployment = s.hoist01 * Mathf.Lerp(0.15f, 1f, s.extension01);
            return s.baseArea * deployment * (1f - s.damage01);
        }

        public Vector3 GetSailForce(int i) => TryGetSail(i, out var s) ? s.lastForce : Vector3.zero;
        public Vector3 GetSailForcePoint(int i) => TryGetSail(i, out var s) ? s.lastForcePoint : ShipTransform.position;

        /// <summary>
        /// Called by the aggregator to inject shared dependencies.
        /// Disables standalone self-driving.
        /// </summary>
        public void Bind(Rigidbody body, Transform shipTransform)
        {
            _shipBody = body;
            _shipTransform = shipTransform;
            _drivenByAggregator = true;
        }

        #endregion

        #region Properties

        private Transform ShipTransform => _shipTransform != null ? _shipTransform : transform;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            if (_shipTransform == null) _shipTransform = transform.parent != null ? transform.parent : transform;
            if (_shipBody == null) _shipBody = GetComponentInParent<Rigidbody>();
            _windSystem = FindAnyObjectByType<OceanWindSystem>();
            ComputeMastStrength();
            EnsureRuntimeObjects();
        }

        private void OnValidate()
        {
            ComputeMastStrength();
        }

        private void FixedUpdate()
        {
            // Standalone mode: self-drive when no aggregator is calling Step()
            if (_drivenByAggregator || !standalone)
                return;

            Vector3 wind = Vector3.zero;
            if (_windSystem != null)
                wind = _windSystem.EvaluateWind(transform.position, Time.time);

            float dt = Time.fixedDeltaTime;
            Step(dt, Time.time, wind, _shipBody != null, airDensity, sailForceScale,
                maxForcePerSail, liftFactor, dragFactor, sideForceFactor,
                reverseDriveFactor, verticalForceFactor, blockReverseDriveFromHeadwind,
                substeps, dt / Mathf.Max(1, substeps));
        }

        #endregion

        #region Simulation (called by aggregator)

        /// <summary>
        /// Run one simulation step for this mast: cloth substeps + aerodynamic forces + stress.
        /// </summary>
        public void Step(
            float deltaTime,
            float simulationTime,
            Vector3 trueWind,
            bool applyForces,
            float airDensity,
            float sailForceScale,
            float maxForcePerSail,
            float liftFactor,
            float dragFactor,
            float sideForceFactor,
            float reverseDriveFactor,
            float verticalForceFactor,
            bool blockReverseDriveFromHeadwind,
            int substeps,
            float timeStep)
        {
            if (sails == null || sails.Length == 0)
                return;

            // Reset mast forces
            mast.totalForceOnMast = Vector3.zero;
            mast.currentBendingMoment = 0f;

            float dt = Mathf.Max(0.0001f, deltaTime);
            float subDt = dt / Mathf.Max(1, substeps);

            // Cloth sub-steps
            for (int sub = 0; sub < substeps; sub++)
            {
                float subTime = simulationTime + sub * subDt;
                for (int i = 0; i < sails.Length; i++)
                {
                    SailPanel sail = sails[i];
                    if (sail == null || !sail.enabled) continue;
                    SimulateSailCloth(sail, subDt, trueWind, subTime, airDensity);
                }
            }

            // Aerodynamic forces
            for (int i = 0; i < sails.Length; i++)
            {
                SailPanel sail = sails[i];
                if (sail == null || !sail.enabled) continue;
                ApplySailForces(sail, trueWind, applyForces,
                    airDensity, sailForceScale, maxForcePerSail,
                    liftFactor, dragFactor, sideForceFactor,
                    reverseDriveFactor, verticalForceFactor,
                    blockReverseDriveFromHeadwind);
                UpdateSailVisuals(sail);
            }

            // Mast stress
            mast.stressRatio = MastStressSolver.ComputeStressRatio(mast.currentBendingMoment, mast.maxBendingMoment);
        }

        #endregion

        #region Cloth Simulation

        private void SimulateSailCloth(SailPanel sail, float dt, Vector3 trueWind, float time, float airDensity)
        {
            EnsureParticles(sail);

            int count = sail.ParticleCount;
            float invMass = 1f / Mathf.Max(0.01f, sail.particleMass);
            Vector3 gravity = Physics.gravity * sail.gravityScale;
            Vector3 apparentWind = trueWind - _shipBody.linearVelocity;
            float windSpeed = apparentWind.magnitude;
            Vector3 windDir = windSpeed > 0.01f ? apparentWind / windSpeed : Vector3.zero;

            float deployment = sail.hoist01 * Mathf.Lerp(0.15f, 1f, sail.extension01);

            for (int i = 0; i < count; i++)
            {
                if (sail.pinned[i])
                {
                    int x = i % sail.gridWidth;
                    int y = i / sail.gridWidth;
                    sail.positions[i] = ComputeAnchorPosition(sail, x, y, deployment);
                    sail.prevPositions[i] = sail.positions[i];
                    continue;
                }

                Vector3 pos = sail.positions[i];
                Vector3 prev = sail.prevPositions[i];
                Vector3 velocity = (pos - prev) / Mathf.Max(0.0001f, dt);

                Vector3 force = gravity * sail.particleMass;

                Vector3 localNormal = ComputeLocalNormal(sail, i);
                if (windSpeed > 0.01f && localNormal.sqrMagnitude > 0.01f)
                {
                    float exposure = Vector3.Dot(-windDir, localNormal);
                    float absExposure = Mathf.Abs(exposure);

                    float dragMag = 0.5f * airDensity * windSpeed * windSpeed * sail.windDragCoeff * absExposure;
                    force += -localNormal * (Mathf.Sign(exposure) * dragMag * sail.baseArea / count);

                    Vector3 liftDir = Vector3.Cross(windDir, Vector3.Cross(localNormal, windDir));
                    if (liftDir.sqrMagnitude > 0.001f)
                    {
                        liftDir.Normalize();
                        float liftMag = 0.5f * airDensity * windSpeed * windSpeed * sail.windLiftCoeff *
                                        absExposure * (1f - absExposure) * 2f;
                        force += liftDir * (liftMag * sail.baseArea / count);
                    }

                    float flutter = Mathf.Sin(time * sail.windFlutterFrequency * 6.28f + i * 1.37f) *
                                    sail.windFlutterStrength * windSpeed;
                    force += localNormal * flutter;
                }

                force -= velocity * sail.damping;

                Vector3 newPos = 2f * pos - prev + force * invMass * dt * dt;
                sail.prevPositions[i] = pos;
                sail.positions[i] = newPos;
            }

            for (int iter = 0; iter < sail.solverIterations; iter++)
                SolveConstraints(sail, deployment);
        }

        private void SolveConstraints(SailPanel sail, float deployment)
        {
            int w = sail.gridWidth;
            int h = sail.gridHeight;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = sail.GetIndex(x, y);
                if (x < w - 1) SolveDistanceConstraint(sail, idx, sail.GetIndex(x + 1, y), sail.structuralStiffness);
                if (y < h - 1) SolveDistanceConstraint(sail, idx, sail.GetIndex(x, y + 1), sail.structuralStiffness);
            }

            for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                int idx = sail.GetIndex(x, y);
                SolveDistanceConstraint(sail, idx, sail.GetIndex(x + 1, y + 1), sail.shearStiffness);
                SolveDistanceConstraint(sail, sail.GetIndex(x + 1, y), sail.GetIndex(x, y + 1), sail.shearStiffness);
            }

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w - 2; x++)
                SolveDistanceConstraint(sail, sail.GetIndex(x, y), sail.GetIndex(x + 2, y), sail.bendStiffness);

            for (int y = 0; y < h - 2; y++)
            for (int x = 0; x < w; x++)
                SolveDistanceConstraint(sail, sail.GetIndex(x, y), sail.GetIndex(x, y + 2), sail.bendStiffness);

            for (int i = 0; i < sail.ParticleCount; i++)
            {
                if (sail.pinned[i])
                {
                    int x = i % w;
                    int y = i / w;
                    sail.positions[i] = ComputeAnchorPosition(sail, x, y, deployment);
                }
            }
        }

        private static void SolveDistanceConstraint(SailPanel sail, int a, int b, float stiffness)
        {
            Vector3 delta = sail.positions[b] - sail.positions[a];
            float dist = delta.magnitude;
            if (dist < 0.0001f) return;

            float restDist = (sail.restPositions[b] - sail.restPositions[a]).magnitude;
            float diff = (dist - restDist) / dist;
            float stiffFactor = Mathf.Clamp01(stiffness * 0.001f);
            Vector3 correction = delta * (0.5f * diff * stiffFactor);

            if (!sail.pinned[a]) sail.positions[a] += correction;
            if (!sail.pinned[b]) sail.positions[b] -= correction;
        }

        private Vector3 ComputeAnchorPosition(SailPanel sail, int x, int y, float deployment)
        {
            float u = (float)x / (sail.gridWidth - 1);
            float v = (float)y / (sail.gridHeight - 1);

            Vector3 topLeft = sail.topLeftLocal;
            Vector3 topRight = sail.topRightLocal;
            Vector3 bottomLeft = sail.bottomLeftLocal;
            Vector3 bottomRight = sail.bottomRightLocal;

            Vector3 bl = Vector3.Lerp(topLeft, bottomLeft, deployment);
            Vector3 br = Vector3.Lerp(topRight, bottomRight, deployment);

            Vector3 bottomMid = (bl + br) * 0.5f;
            float spread = Mathf.Lerp(0.3f, 1f, sail.extension01);
            bl = bottomMid + (bl - bottomMid) * spread;
            br = bottomMid + (br - bottomMid) * spread;

            Vector3 top = Vector3.Lerp(topLeft, topRight, u);
            Vector3 bottom = Vector3.Lerp(bl, br, u);
            Vector3 localPos = Vector3.Lerp(top, bottom, v);

            if (Mathf.Abs(sail.sheetAngleDeg) > 0.01f)
            {
                Vector3 pivot = (topLeft + topRight) * 0.5f;
                Quaternion rot = Quaternion.AngleAxis(sail.sheetAngleDeg, Vector3.up);
                localPos = pivot + rot * (localPos - pivot);
            }

            return ShipTransform.TransformPoint(localPos);
        }

        private static Vector3 ComputeLocalNormal(SailPanel sail, int idx)
        {
            int w = sail.gridWidth;
            int h = sail.gridHeight;
            int x = idx % w;
            int y = idx / w;

            Vector3 pos = sail.positions[idx];
            Vector3 dx = Vector3.zero;
            Vector3 dy = Vector3.zero;

            if (x < w - 1) dx = sail.positions[sail.GetIndex(x + 1, y)] - pos;
            else if (x > 0) dx = pos - sail.positions[sail.GetIndex(x - 1, y)];

            if (y < h - 1) dy = sail.positions[sail.GetIndex(x, y + 1)] - pos;
            else if (y > 0) dy = pos - sail.positions[sail.GetIndex(x, y - 1)];

            Vector3 normal = Vector3.Cross(dx, dy);
            if (normal.sqrMagnitude < 0.0001f) return Vector3.up;
            return normal.normalized;
        }

        #endregion

        #region Aerodynamic Forces

        private void ApplySailForces(
            SailPanel sail, Vector3 trueWind, bool applyForces,
            float airDensity, float sailForceScale, float maxForcePerSail,
            float liftFactor, float dragFactor, float sideForceFactor,
            float reverseDriveFactor, float verticalForceFactor,
            bool blockReverseDriveFromHeadwind)
        {
            Vector3 apparentWind = trueWind - _shipBody.linearVelocity;
            if (apparentWind.sqrMagnitude < 0.01f)
            {
                sail.lastForce = Vector3.zero;
                return;
            }

            float deployment = sail.hoist01 * Mathf.Lerp(0.15f, 1f, sail.extension01);
            float effectiveArea = sail.baseArea * deployment * (1f - sail.damage01);
            if (effectiveArea <= 0.001f)
            {
                sail.lastForce = Vector3.zero;
                return;
            }

            Vector3 sailNormal = ComputeAggregateNormal(sail);
            SailAerodynamicsResult aero = SailAerodynamicsSolver.Solve(new SailAerodynamicsInput(
                apparentWind, sailNormal, ShipTransform.forward, effectiveArea,
                airDensity, sailForceScale, maxForcePerSail,
                liftFactor, dragFactor, sideForceFactor,
                reverseDriveFactor, verticalForceFactor,
                blockReverseDriveFromHeadwind));

            Vector3 rigForce = aero.RigForce;
            Vector3 driveForce = aero.DriveForce;
            Vector3 forcePoint = ComputeSailCenter(sail);

            sail.lastForce = driveForce;
            sail.lastForcePoint = forcePoint;

            if (applyForces && driveForce.sqrMagnitude > 0.0001f)
                _shipBody.AddForceAtPosition(driveForce, forcePoint, ForceMode.Force);

            if (applyForces && rigForce.sqrMagnitude > 0.0001f)
            {
                mast.totalForceOnMast += rigForce;
                sail.lastMastMoment = ComputeMastMomentContribution(sail, rigForce);
                mast.currentBendingMoment += sail.lastMastMoment;
            }
        }

        private Vector3 ComputeAggregateNormal(SailPanel sail)
        {
            Vector3 normal = Vector3.zero;
            int w = sail.gridWidth;
            int h = sail.gridHeight;

            for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                Vector3 p00 = sail.positions[sail.GetIndex(x, y)];
                Vector3 p10 = sail.positions[sail.GetIndex(x + 1, y)];
                Vector3 p01 = sail.positions[sail.GetIndex(x, y + 1)];
                normal += Vector3.Cross(p10 - p00, p01 - p00);
            }

            if (normal.sqrMagnitude < 0.0001f)
                return ShipTransform.forward;
            return normal.normalized;
        }

        private Vector3 ComputeSailCenter(SailPanel sail)
        {
            Vector3 center = Vector3.zero;
            int count = sail.ParticleCount;
            for (int i = 0; i < count; i++)
                center += sail.positions[i];
            return center / Mathf.Max(1, count);
        }

        private float ComputeMastMomentContribution(SailPanel sail, Vector3 force)
        {
            Vector3 mastBase = ShipTransform.TransformPoint(mast.baseLocal);
            Vector3 mastTop = ShipTransform.TransformPoint(mast.topLocal);
            return MastStressSolver.ComputeMomentContribution(mastBase, mastTop, sail.mastAttachHeight, force);
        }

        #endregion

        #region Initialization

        private void ComputeMastStrength()
        {
            mast.maxBendingMoment = MastStressSolver.ComputeMaxBendingMoment(mast.radius, mast.yieldStrength);
        }

        private void EnsureParticles(SailPanel sail)
        {
            if (sail.initialized && sail.positions != null && sail.positions.Length == sail.ParticleCount)
                return;

            int count = sail.ParticleCount;
            sail.positions = new Vector3[count];
            sail.prevPositions = new Vector3[count];
            sail.restPositions = new Vector3[count];
            sail.pinned = new bool[count];

            int w = sail.gridWidth;
            int h = sail.gridHeight;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = sail.GetIndex(x, y);
                Vector3 worldPos = ComputeAnchorPosition(sail, x, y, 1f);
                sail.positions[idx] = worldPos;
                sail.prevPositions[idx] = worldPos;
                sail.restPositions[idx] = worldPos;

                bool pinAsRopeAnchor = sail.pinBoundaryToRig && ShouldPinPerimeterPoint(x, y, w, h, sail.ropeAttachmentPoints);
                sail.pinned[idx] = pinAsRopeAnchor || (!sail.pinBoundaryToRig && y == 0);
            }

            sail.initialized = true;
        }

        private void EnsureRuntimeObjects()
        {
            if (!createVisuals) return;
            if (sails == null) return;
            for (int i = 0; i < sails.Length; i++)
                CreateSailVisual(sails[i]);
        }

        private void CreateSailVisual(SailPanel sail)
        {
            if (sail == null || sail.visualRoot != null) return;

            GameObject root = new GameObject($"Sail_{sail.name}");
            root.transform.SetParent(ShipTransform, false);
            sail.visualRoot = root.transform;

            GameObject clothObj = new GameObject("ClothMesh");
            clothObj.transform.SetParent(root.transform, false);
            sail.meshFilter = clothObj.AddComponent<MeshFilter>();
            sail.meshRenderer = clothObj.AddComponent<MeshRenderer>();
            sail.mesh = new Mesh { name = $"SailMesh_{sail.name}" };
            sail.mesh.MarkDynamic();

            BuildSailMesh(sail);
            sail.meshFilter.sharedMesh = sail.mesh;
            sail.meshRenderer.sharedMaterial = ResolveSailMaterial(sail.clothColor);
        }

        private void BuildSailMesh(SailPanel sail)
        {
            int w = sail.gridWidth;
            int h = sail.gridHeight;
            int vertCount = w * h;
            int triCount = (w - 1) * (h - 1) * 6;

            Vector3[] verts = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];
            int[] tris = new int[triCount];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = sail.GetIndex(x, y);
                float u = (float)x / (w - 1);
                float v = 1f - (float)y / (h - 1);

                verts[idx] = Vector3.Lerp(
                    Vector3.Lerp(sail.topLeftLocal, sail.topRightLocal, u),
                    Vector3.Lerp(sail.bottomLeftLocal, sail.bottomRightLocal, u),
                    (float)y / (h - 1));
                uvs[idx] = new Vector2(u, v);
            }

            int triIdx = 0;
            for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                int i00 = sail.GetIndex(x, y);
                int i10 = sail.GetIndex(x + 1, y);
                int i01 = sail.GetIndex(x, y + 1);
                int i11 = sail.GetIndex(x + 1, y + 1);

                tris[triIdx++] = i00; tris[triIdx++] = i10; tris[triIdx++] = i01;
                tris[triIdx++] = i01; tris[triIdx++] = i10; tris[triIdx++] = i11;
            }

            sail.mesh.vertices = verts;
            sail.mesh.uv = uvs;
            sail.mesh.triangles = tris;
            sail.mesh.RecalculateNormals();
            sail.mesh.RecalculateBounds();
        }

        private void UpdateSailVisuals(SailPanel sail)
        {
            if (!createVisuals || sail.mesh == null || sail.visualRoot == null) return;

            int count = sail.ParticleCount;
            Vector3[] verts = new Vector3[count];
            for (int i = 0; i < count; i++)
                verts[i] = ShipTransform.InverseTransformPoint(sail.positions[i]);

            sail.mesh.vertices = verts;
            sail.mesh.RecalculateNormals();
            sail.mesh.RecalculateBounds();

            if (sail.meshRenderer != null)
            {
                Color tint = Color.Lerp(sail.clothColor, new Color(0.3f, 0.28f, 0.25f, sail.clothColor.a), sail.damage01);
                Material mat = sail.meshRenderer.sharedMaterial;
                if (mat != null)
                {
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
                }
            }
        }

        private Material ResolveSailMaterial(Color defaultColor)
        {
            if (sailMaterial != null) return sailMaterial;
            if (_runtimeSailMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                                ?? Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Standard")
                                ?? Shader.Find("Sprites/Default");

                _runtimeSailMaterial = shader != null ? new Material(shader) : null;
                if (_runtimeSailMaterial != null)
                {
                    if (_runtimeSailMaterial.HasProperty("_BaseColor")) _runtimeSailMaterial.SetColor("_BaseColor", defaultColor);
                    if (_runtimeSailMaterial.HasProperty("_Color")) _runtimeSailMaterial.SetColor("_Color", defaultColor);
                    if (_runtimeSailMaterial.HasProperty("_Cull")) _runtimeSailMaterial.SetFloat("_Cull", 0f);
                }
            }
            return _runtimeSailMaterial;
        }

        /// <summary>Invalidate particle state for all sails (e.g. after editing anchors).</summary>
        public void ResetParticles()
        {
            if (sails == null) return;
            for (int i = 0; i < sails.Length; i++)
            {
                if (sails[i] == null) continue;
                sails[i].initialized = false;
                sails[i].positions = null;
                sails[i].prevPositions = null;
                sails[i].restPositions = null;
                sails[i].pinned = null;
            }
        }

        #endregion

        #region Helpers

        private bool TryGetSail(int index, out SailPanel sail)
        {
            sail = null;
            if (sails == null || index < 0 || index >= sails.Length) return false;
            sail = sails[index];
            return sail != null;
        }

        private static bool ShouldPinPerimeterPoint(int x, int y, int width, int height, int requestedAnchors)
        {
            if (width < 2 || height < 2) return false;
            bool isBoundary = x == 0 || y == 0 || x == width - 1 || y == height - 1;
            if (!isBoundary) return false;

            bool isCorner = (x == 0 || x == width - 1) && (y == 0 || y == height - 1);
            if (isCorner) return true;

            int perimeterCount = (width * 2) + (height * 2) - 4;
            int anchorCount = Mathf.Clamp(requestedAnchors, 4, perimeterCount);
            if (anchorCount >= perimeterCount) return true;

            int index = GetPerimeterIndex(x, y, width, height);
            if (index < 0) return false;

            float step = (perimeterCount - 1f) / Mathf.Max(1f, anchorCount - 1f);
            for (int i = 0; i < anchorCount; i++)
            {
                int targetIndex = Mathf.RoundToInt(i * step);
                if (targetIndex == index) return true;
            }
            return false;
        }

        private static int GetPerimeterIndex(int x, int y, int width, int height)
        {
            if (y == 0) return x;
            if (x == width - 1) return (width - 1) + y;
            if (y == height - 1) return (width - 1) + (height - 1) + ((width - 1) - x);
            if (x == 0) return (width - 1) + (height - 1) + (width - 1) + ((height - 1) - y);
            return -1;
        }

        #endregion

        #region Debug

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (sails == null) return;
            Transform tr = ShipTransform;

            // Mast axis
            Vector3 baseW = tr.TransformPoint(mast.baseLocal);
            Vector3 topW = tr.TransformPoint(mast.topLocal);
            float stress = Mathf.Clamp01(mast.stressRatio);
            Gizmos.color = Color.Lerp(Color.green, Color.red, stress);
            Gizmos.DrawLine(baseW, topW);
            Gizmos.DrawWireSphere(topW, mast.radius * 2f);

            if (mast.totalForceOnMast.sqrMagnitude > 0.01f)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
                Vector3 forcePoint = Vector3.Lerp(baseW, topW, 0.6f);
                Gizmos.DrawLine(forcePoint, forcePoint + mast.totalForceOnMast * 0.001f);
            }

            // Sail anchor points
            if (editorDrawAttachmentPointsGizmo)
            {
                for (int i = 0; i < sails.Length; i++)
                {
                    SailPanel sail = sails[i];
                    if (sail == null) continue;

                    Vector3 tl = tr.TransformPoint(sail.topLeftLocal);
                    Vector3 trr = tr.TransformPoint(sail.topRightLocal);
                    Vector3 bl = tr.TransformPoint(sail.bottomLeftLocal);
                    Vector3 br = tr.TransformPoint(sail.bottomRightLocal);

                    float size = Mathf.Max(0.03f, editorPointHandleSize);
                    Gizmos.color = new Color(0.25f, 0.95f, 1f, 0.95f);
                    Gizmos.DrawSphere(tl, size);
                    Gizmos.DrawSphere(trr, size);
                    Gizmos.DrawSphere(bl, size);
                    Gizmos.DrawSphere(br, size);

                    Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
                    Gizmos.DrawLine(tl, trr);
                    Gizmos.DrawLine(trr, br);
                    Gizmos.DrawLine(br, bl);
                    Gizmos.DrawLine(bl, tl);
                }
            }
        }
#endif

        #endregion
    }
}

