using System;
using System.Collections.Generic;
using RumOverboard.Gameplay.Ocean.Simulation;
using UnityEngine;
#if FUSION2
using RumOverboard.Networking;
#endif

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Runtime sail simulation with high-resolution cloth mesh, wind physics,
    /// mast-synchronized attachment points, and mast stress calculations.
    /// Each sail is a grid of particles simulated with Verlet integration,
    /// attached to yards and masts matching the visual model geometry.
    /// </summary>
    /// <remarks>
    /// DEPRECATED: Use <see cref="ShipSailsAggregator"/> + per-mast <see cref="Features.Masts.ShipMast"/> instead.
    /// This monolithic class is retained for backwards compatibility with existing prefabs.
    /// </remarks>
    [System.Obsolete("Use ShipSailsAggregator + per-mast ShipMast components. This monolithic class is retained for legacy prefabs.")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public partial class ShipSailSystem : MonoBehaviour
    {
        #region Data Structures

        /// <summary>
        /// Defines which mast a sail is attached to, for stress calculations.
        /// </summary>
        [Serializable]
        public class MastDefinition
        {
            public string name = "Mast";
            /// <summary>Local position of mast base on the ship.</summary>
            public Vector3 baseLocal;
            /// <summary>Local position of mast top.</summary>
            public Vector3 topLocal;
            /// <summary>Radius of the mast (meters).</summary>
            [Min(0.05f)] public float radius = 0.22f;
            /// <summary>Material yield strength (Pa). Pine wood ~40 MPa.</summary>
            [Min(1000f)] public float yieldStrength = 40_000_000f;
            /// <summary>Elastic modulus (Pa). Pine ~12 GPa.</summary>
            [Min(100000f)] public float elasticModulus = 12_000_000_000f;
            /// <summary>Current accumulated stress ratio [0..1+]. At 1.0 the mast would break.</summary>
            [NonSerialized] public float stressRatio;
            /// <summary>Current bending moment applied to mast (N·m).</summary>
            [NonSerialized] public float currentBendingMoment;
            /// <summary>Maximum bending moment before failure (N·m).</summary>
            [NonSerialized] public float maxBendingMoment;
            /// <summary>Current total force on this mast from all attached sails (N).</summary>
            [NonSerialized] public Vector3 totalForceOnMast;
        }

        [Serializable]
        private class SailPanel
        {
            public string name = "Sail";
            public bool enabled = true;

            [Header("Visual sync")]
            public string visualSailName = string.Empty;
            public bool pinBoundaryToRig = true;
            public bool triangularHint;

            [Header("Mast attachment")]
            public int mastIndex = -1;
            /// <summary>Height on mast where force is applied (0=base, 1=top).</summary>
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

        [SerializeField] private Rigidbody shipBody;
        [SerializeField] private OceanWindSystem windSystem;

        [Header("Masts (synced with visual model)")]
        [SerializeField] private MastDefinition[] masts = new MastDefinition[3];

        [Header("Visual sync")]
        [SerializeField] private bool syncRigFromVisualSails;
        [SerializeField] private bool syncMastsFromVisual = true;
        [SerializeField] private bool rebuildSailsFromVisual = true;

        [Header("Editor point tools")]
        [SerializeField] private bool editorShowAttachmentHandles = true;
        [SerializeField] private bool editorShowPointLabels = true;
        [SerializeField] private bool editorDrawAttachmentPointsGizmo = true;
        [SerializeField] private bool editorDrawMeshPreviewGizmo = true;
        [SerializeField] [Min(0.01f)] private float editorPointHandleSize = 0.08f;

        [Header("Aerodynamics")]
        [SerializeField] [Min(0f)] private float airDensity = 1.225f;
        [SerializeField] [Min(0f)] private float sailForceScale = 1f;
        [SerializeField] [Min(0f)] private float maxForcePerSail = 5000f;
        [SerializeField] [Range(0f, 2f)] private float liftFactor = 1.1f;
        [SerializeField] [Range(0f, 2f)] private float dragFactor = 1.0f;
        [SerializeField] [Range(0f, 1f)] private float sideForceFactor = 0.6f;
        [SerializeField] [Range(0f, 1f)] private float reverseDriveFactor = 0.12f;
        [SerializeField] [Range(0f, 1f)] private float verticalForceFactor = 0.08f;
        [SerializeField] private bool blockReverseDriveFromHeadwind = true;

        [Header("Physics tuning")]
        [SerializeField] [Min(0.001f)] private float timeStep = 0.016f;
        [SerializeField] [Range(1, 4)] private int substeps = 2;

        [Header("Runtime visuals")]
        [SerializeField] private bool createVisuals = true;
        [SerializeField] private bool simulateVisualsOnProxies = true;
        [SerializeField] private Material sailMaterial;

        [Header("Debug")]
        [SerializeField] private bool debugDrawForces;
        [SerializeField] private bool debugDrawMastStress;
        [SerializeField] private bool debugDrawCloth;
        [SerializeField] private float debugForceScale = 0.001f;

        [SerializeField] private SailPanel[] sails = new SailPanel[7];

        #endregion

        #region Private state

        private Material _runtimeSailMaterial;
        private bool _externallyDriven;

        private static readonly string[] VisualMastNameHints =
        {
            "StylShip_MastFront",
            "StylShip_MastMid",
            "StylShip_MastBack",
            "ForeMast",
            "MainMast",
            "MizzenMast",
            "MastFront",
            "MastMid",
            "MastBack",
        };

        private static readonly string[] VisualSailNameHints =
        {
            "StylShip_SailFront",
            "StylShip_SailMid1",
            "StylShip_SailMid2",
            "StylShip_SailBack",
            "ForeCourse",
            "ForeTopsail",
            "ForeTopgallant",
            "MainCourse",
            "MainTopsail",
            "MainTopgallant",
            "Spanker",
            "InnerJib",
            "OuterJib",
            "FlyingJib",
            "ForeTopmastStaysail",
        };

        #endregion

        #region Public API

        public OceanWindSystem WindSystem
        {
            get => windSystem;
            set => windSystem = value;
        }

        public int SailCount => sails != null ? sails.Length : 0;
        public int MastCount => masts != null ? masts.Length : 0;

        public float AirDensity
        {
            get => airDensity;
            set => airDensity = Mathf.Max(0f, value);
        }

        public float SailForceScale
        {
            get => sailForceScale;
            set => sailForceScale = Mathf.Max(0f, value);
        }

        public float MaxForcePerSail
        {
            get => maxForcePerSail;
            set => maxForcePerSail = Mathf.Max(0f, value);
        }

        public float LiftFactor
        {
            get => liftFactor;
            set => liftFactor = Mathf.Clamp(value, 0f, 2f);
        }

        public float DragFactor
        {
            get => dragFactor;
            set => dragFactor = Mathf.Clamp(value, 0f, 2f);
        }

        public float SideForceFactor
        {
            get => sideForceFactor;
            set => sideForceFactor = Mathf.Clamp01(value);
        }

        public float ReverseDriveFactor
        {
            get => reverseDriveFactor;
            set => reverseDriveFactor = Mathf.Clamp01(value);
        }

        public float VerticalForceFactor
        {
            get => verticalForceFactor;
            set => verticalForceFactor = Mathf.Clamp01(value);
        }

        public bool DebugDrawForces
        {
            get => debugDrawForces;
            set => debugDrawForces = value;
        }

        public bool DebugDrawRopes
        {
            get => debugDrawCloth;
            set => debugDrawCloth = value;
        }

        public void SetHoist(int sailIndex, float value)
        {
            if (!TryGetSail(sailIndex, out SailPanel sail)) return;
            sail.hoist01 = Mathf.Clamp01(value);
        }

        public void SetExtension(int sailIndex, float value)
        {
            if (!TryGetSail(sailIndex, out SailPanel sail)) return;
            sail.extension01 = Mathf.Clamp01(value);
        }

        public void SetSheetAngle(int sailIndex, float value)
        {
            if (!TryGetSail(sailIndex, out SailPanel sail)) return;
            sail.sheetAngleDeg = Mathf.Clamp(value, -85f, 85f);
        }

        public void SetDamage(int sailIndex, float value)
        {
            if (!TryGetSail(sailIndex, out SailPanel sail)) return;
            sail.damage01 = Mathf.Clamp01(value);
        }

        public string GetSailName(int sailIndex)
        {
            if (!TryGetSail(sailIndex, out SailPanel sail))
                return $"Sail {sailIndex}";
            return string.IsNullOrWhiteSpace(sail.name) ? $"Sail {sailIndex}" : sail.name;
        }

        public bool GetSailEnabled(int sailIndex)
        {
            return TryGetSail(sailIndex, out SailPanel sail) && sail.enabled;
        }

        public void SetSailEnabled(int sailIndex, bool enabledValue)
        {
            if (!TryGetSail(sailIndex, out SailPanel sail)) return;
            sail.enabled = enabledValue;
        }

        public float GetHoist(int sailIndex) => TryGetSail(sailIndex, out SailPanel s) ? s.hoist01 : 0f;
        public float GetExtension(int sailIndex) => TryGetSail(sailIndex, out SailPanel s) ? s.extension01 : 0f;
        public float GetSheetAngle(int sailIndex) => TryGetSail(sailIndex, out SailPanel s) ? s.sheetAngleDeg : 0f;
        public float GetDamage(int sailIndex) => TryGetSail(sailIndex, out SailPanel s) ? s.damage01 : 0f;

        public float GetEffectiveArea(int sailIndex)
        {
            if (!TryGetSail(sailIndex, out SailPanel sail)) return 0f;
            float deployment = sail.hoist01 * Mathf.Lerp(0.15f, 1f, sail.extension01);
            return sail.baseArea * deployment * (1f - sail.damage01);
        }

        /// <summary>
        /// Get the current stress ratio for a mast. 0 = no stress, 1 = breaking point.
        /// </summary>
        public float GetMastStressRatio(int mastIndex)
        {
            if (masts == null || mastIndex < 0 || mastIndex >= masts.Length) return 0f;
            return masts[mastIndex].stressRatio;
        }

        /// <summary>
        /// Get the total force currently acting on a mast from its sails (Newtons).
        /// </summary>
        public Vector3 GetMastForce(int mastIndex)
        {
            if (masts == null || mastIndex < 0 || mastIndex >= masts.Length) return Vector3.zero;
            return masts[mastIndex].totalForceOnMast;
        }

        /// <summary>World-space aerodynamic force the sail produced last simulation step (N). For debug thrust arrows.</summary>
        public Vector3 GetSailForce(int sailIndex) => TryGetSail(sailIndex, out SailPanel s) ? s.lastForce : Vector3.zero;

        /// <summary>World-space point where that sail's force is applied (for debug arrows).</summary>
        public Vector3 GetSailForcePoint(int sailIndex) => TryGetSail(sailIndex, out SailPanel s) ? s.lastForcePoint : transform.position;

        /// <summary>Net wind thrust on the hull this step — sum of every enabled sail's force (N).</summary>
        public Vector3 TotalThrust
        {
            get
            {
                Vector3 sum = Vector3.zero;
                if (sails != null)
                {
                    for (int i = 0; i < sails.Length; i++)
                        if (sails[i] != null && sails[i].enabled)
                            sum += sails[i].lastForce;
                }
                return sum;
            }
        }

        /// <summary>
        /// Get the current bending moment on a mast (N·m).
        /// </summary>
        public float GetMastBendingMoment(int mastIndex)
        {
            if (masts == null || mastIndex < 0 || mastIndex >= masts.Length) return 0f;
            return masts[mastIndex].currentBendingMoment;
        }

        /// <summary>
        /// Get the maximum bending moment a mast can withstand before failure (N·m).
        /// </summary>
        public float GetMastMaxBendingMoment(int mastIndex)
        {
            if (masts == null || mastIndex < 0 || mastIndex >= masts.Length) return 0f;
            return masts[mastIndex].maxBendingMoment;
        }

        /// <summary>
        /// Recompute area/mast linkage after editing sail anchor points in Scene view.
        /// </summary>
        public void NotifyRigPointsEditedInEditor()
        {
            if (sails == null)
                return;

            for (int i = 0; i < sails.Length; i++)
            {
                SailPanel sail = sails[i];
                if (sail == null)
                    continue;

                sail.baseArea = Mathf.Max(0.5f, ComputePanelArea(
                    sail.topLeftLocal,
                    sail.topRightLocal,
                    sail.bottomLeftLocal,
                    sail.bottomRightLocal));

                RetuneSailPhysicsFromArea(sail, sail.triangularHint);
                AssignMastForSail(sail);
                sail.initialized = false;
                sail.positions = null;
                sail.prevPositions = null;
                sail.restPositions = null;
                sail.pinned = null;
            }
        }

        /// <summary>
        /// Editor-only utility: sync sail anchor points from this ship's own visual hierarchy.
        /// Use it on NetworkShip so the source is the nested VisualShip instance.
        /// </summary>
        public void SyncAnchorPointsFromCurrentShipVisual(bool syncMasts = true, bool rebuildSails = true)
        {
#if UNITY_EDITOR
            if (syncMasts)
                SyncMastsFromVisualObjects();

            if (rebuildSails)
                SyncSailsFromVisualObjects();
            else
                SyncExistingSailsFromVisualObjects();

            NotifyRigPointsEditedInEditor();
#endif
        }

#if UNITY_EDITOR
        [ContextMenu("Sails/Sync Anchor Points From Visual Meshes")]
        private void SyncAnchorPointsFromVisualMeshesContextMenu()
        {
            SyncAnchorPointsFromCurrentShipVisual(syncMastsFromVisual, rebuildSailsFromVisual);
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        #endregion

        #region Lifecycle

        private void Reset()
        {
            BuildDefaultRig();
        }

        private void Awake()
        {
            if (shipBody == null)
                shipBody = GetComponent<Rigidbody>();
            if (windSystem == null)
                windSystem = FindAnyObjectByType<OceanWindSystem>();

            EnsureRigConfigured();
            ComputeMastStrengths();
            EnsureRuntimeObjects();
        }

        private void OnValidate()
        {
            airDensity = Mathf.Max(0f, airDensity);
            sailForceScale = Mathf.Max(0f, sailForceScale);
            maxForcePerSail = Mathf.Max(0f, maxForcePerSail);
            debugForceScale = Mathf.Max(0f, debugForceScale);
            sideForceFactor = Mathf.Clamp01(sideForceFactor);
            reverseDriveFactor = Mathf.Clamp01(reverseDriveFactor);
            verticalForceFactor = Mathf.Clamp01(verticalForceFactor);
            timeStep = Mathf.Max(0.001f, timeStep);

            EnsureRigConfigured();
            ComputeMastStrengths();
        }

        private void FixedUpdate()
        {
            if (_externallyDriven)
                return;

            Step(Time.fixedDeltaTime, Time.time);
        }

        /// <summary>
        /// External simulation entry point used by a network authority that drives all ship systems
        /// from one deterministic tick.
        /// </summary>
        public void Step(float deltaTime, float simulationTime)
        {
            if (shipBody == null || sails == null || sails.Length == 0)
                return;

            bool applyForces = ShouldApplyForcesForThisPeer();
            if (!applyForces && !simulateVisualsOnProxies)
                return;

            if (windSystem == null)
                windSystem = FindAnyObjectByType<OceanWindSystem>();

            float dt = Mathf.Max(0.0001f, deltaTime);
            float simTime = simulationTime;
            Vector3 shipWorldPos = shipBody.worldCenterOfMass;
            Vector3 trueWind = windSystem != null ? windSystem.EvaluateWind(shipWorldPos, simTime) : Vector3.zero;

            // Reset mast forces for this frame
            ResetMastForces();

            // Sub-step simulation for stability
            float subDt = dt / substeps;
            for (int sub = 0; sub < substeps; sub++)
            {
                float subTime = simTime + sub * subDt;
                for (int i = 0; i < sails.Length; i++)
                {
                    SailPanel sail = sails[i];
                    if (sail == null || !sail.enabled) continue;
                    SimulateSailCloth(sail, subDt, trueWind, subTime);
                }
            }

            // Compute aerodynamic forces and apply
            for (int i = 0; i < sails.Length; i++)
            {
                SailPanel sail = sails[i];
                if (sail == null || !sail.enabled) continue;
                ApplySailForces(sail, trueWind, applyForces);
                UpdateSailVisuals(sail);
            }

            // Compute mast stress
            ComputeMastStress();
        }

        public void SetExternallyDriven(bool externallyDriven)
        {
            _externallyDriven = externallyDriven;
        }

        #endregion

        #region Cloth Simulation (Verlet integration)

        private void SimulateSailCloth(SailPanel sail, float dt, Vector3 trueWind, float time)
        {
            EnsureParticles(sail);

            int count = sail.ParticleCount;
            float invMass = 1f / Mathf.Max(0.01f, sail.particleMass);
            Vector3 gravity = Physics.gravity * sail.gravityScale;
            Vector3 apparentWind = trueWind - shipBody.linearVelocity;
            float windSpeed = apparentWind.magnitude;
            Vector3 windDir = windSpeed > 0.01f ? apparentWind / windSpeed : Vector3.zero;

            // Deployment factor affects rest positions of bottom rows
            float deployment = sail.hoist01 * Mathf.Lerp(0.15f, 1f, sail.extension01);

            // Verlet integration with external forces
            for (int i = 0; i < count; i++)
            {
                if (sail.pinned[i])
                {
                    // Pinned particles follow their anchor (top row)
                    int x = i % sail.gridWidth;
                    int y = i / sail.gridWidth;
                    sail.positions[i] = ComputeAnchorPosition(sail, x, y, deployment);
                    sail.prevPositions[i] = sail.positions[i];
                    continue;
                }

                Vector3 pos = sail.positions[i];
                Vector3 prev = sail.prevPositions[i];
                Vector3 velocity = (pos - prev) / Mathf.Max(0.0001f, dt);

                // External forces
                Vector3 force = gravity * sail.particleMass;

                // Wind force on this particle - compute local normal from neighbors
                Vector3 localNormal = ComputeLocalNormal(sail, i);
                if (windSpeed > 0.01f && localNormal.sqrMagnitude > 0.01f)
                {
                    float exposure = Vector3.Dot(-windDir, localNormal);
                    float absExposure = Mathf.Abs(exposure);

                    // Drag pushes cloth along the wind flow direction.
                    float dragMag = 0.5f * airDensity * windSpeed * windSpeed * sail.windDragCoeff * absExposure;
                    force += -localNormal * (Mathf.Sign(exposure) * dragMag * sail.baseArea / count);

                    // Lift (perpendicular component)
                    Vector3 liftDir = Vector3.Cross(windDir, Vector3.Cross(localNormal, windDir));
                    if (liftDir.sqrMagnitude > 0.001f)
                    {
                        liftDir.Normalize();
                        float liftMag = 0.5f * airDensity * windSpeed * windSpeed * sail.windLiftCoeff *
                                        absExposure * (1f - absExposure) * 2f;
                        force += liftDir * (liftMag * sail.baseArea / count);
                    }

                    // Flutter - small oscillation for realism
                    float flutter = Mathf.Sin(time * sail.windFlutterFrequency * 6.28f + i * 1.37f) *
                                    sail.windFlutterStrength * windSpeed;
                    force += localNormal * flutter;
                }

                // Damping
                force -= velocity * sail.damping;

                // Verlet step
                Vector3 newPos = 2f * pos - prev + force * invMass * dt * dt;
                sail.prevPositions[i] = pos;
                sail.positions[i] = newPos;
            }

            // Constraint solving
            for (int iter = 0; iter < sail.solverIterations; iter++)
            {
                SolveConstraints(sail, deployment);
            }
        }

        private void SolveConstraints(SailPanel sail, float deployment)
        {
            int w = sail.gridWidth;
            int h = sail.gridHeight;

            // Structural constraints (horizontal + vertical neighbors)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = sail.GetIndex(x, y);
                    // Right neighbor
                    if (x < w - 1)
                        SolveDistanceConstraint(sail, idx, sail.GetIndex(x + 1, y), sail.structuralStiffness);
                    // Down neighbor
                    if (y < h - 1)
                        SolveDistanceConstraint(sail, idx, sail.GetIndex(x, y + 1), sail.structuralStiffness);
                }
            }

            // Shear constraints (diagonals)
            for (int y = 0; y < h - 1; y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    int idx = sail.GetIndex(x, y);
                    SolveDistanceConstraint(sail, idx, sail.GetIndex(x + 1, y + 1), sail.shearStiffness);
                    SolveDistanceConstraint(sail, sail.GetIndex(x + 1, y), sail.GetIndex(x, y + 1), sail.shearStiffness);
                }
            }

            // Bend constraints (skip one)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w - 2; x++)
                    SolveDistanceConstraint(sail, sail.GetIndex(x, y), sail.GetIndex(x + 2, y), sail.bendStiffness);
            }
            for (int y = 0; y < h - 2; y++)
            {
                for (int x = 0; x < w; x++)
                    SolveDistanceConstraint(sail, sail.GetIndex(x, y), sail.GetIndex(x, y + 2), sail.bendStiffness);
            }

            // Pin constraint - keep pinned particles at anchors
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

        private void SolveDistanceConstraint(SailPanel sail, int a, int b, float stiffness)
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

            // Bilinear interpolation of corners
            Vector3 topLeft = sail.topLeftLocal;
            Vector3 topRight = sail.topRightLocal;
            Vector3 bottomLeft = sail.bottomLeftLocal;
            Vector3 bottomRight = sail.bottomRightLocal;

            // Apply deployment: bottom moves down based on hoist
            Vector3 bl = Vector3.Lerp(topLeft, bottomLeft, deployment);
            Vector3 br = Vector3.Lerp(topRight, bottomRight, deployment);

            // Apply extension: bottom spreads out
            Vector3 bottomMid = (bl + br) * 0.5f;
            float spread = Mathf.Lerp(0.3f, 1f, sail.extension01);
            bl = bottomMid + (bl - bottomMid) * spread;
            br = bottomMid + (br - bottomMid) * spread;

            Vector3 top = Vector3.Lerp(topLeft, topRight, u);
            Vector3 bottom = Vector3.Lerp(bl, br, u);
            Vector3 localPos = Vector3.Lerp(top, bottom, v);

            // Sheet angle rotation around top center
            if (Mathf.Abs(sail.sheetAngleDeg) > 0.01f)
            {
                Vector3 pivot = (topLeft + topRight) * 0.5f;
                Quaternion rot = Quaternion.AngleAxis(sail.sheetAngleDeg, Vector3.up);
                localPos = pivot + rot * (localPos - pivot);
            }

            return transform.TransformPoint(localPos);
        }

        private Vector3 ComputeLocalNormal(SailPanel sail, int idx)
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

        private void ApplySailForces(SailPanel sail, Vector3 trueWind, bool applyForces)
        {
            ApplySailForcesCore(sail, trueWind, applyForces);
        }

        private Vector3 ComputeAggregateNormal(SailPanel sail)
        {
            return ComputeAggregateNormalCore(sail);
        }

        private Vector3 ComputeSailCenter(SailPanel sail)
        {
            return ComputeSailCenterCore(sail);
        }

        #endregion

        #region Mast Stress Calculations

        private void ComputeMastStrengths()
        {
            ComputeMastStrengthsCore();
        }

        private void ResetMastForces()
        {
            ResetMastForcesCore();
        }

        private float ComputeMastMomentContribution(SailPanel sail, Vector3 force)
        {
            return ComputeMastMomentContributionCore(sail, force);
        }

        private void ComputeMastStress()
        {
            ComputeMastStressCore();
        }

        #endregion

        #region Initialization

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
            {
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
            }

            sail.initialized = true;
        }

        private void EnsureRigConfigured()
        {
            if (sails != null && sails.Length > 0)
            {
                bool hasNull = false;
                for (int i = 0; i < sails.Length; i++)
                    if (sails[i] == null) { hasNull = true; break; }
                if (!hasNull) return;
            }
            BuildDefaultRig();
        }

        private void EnsureRuntimeObjects()
        {
            if (!createVisuals) return;
            for (int i = 0; i < sails.Length; i++)
                CreateSailVisual(sails[i]);
        }

        private void CreateSailVisual(SailPanel sail)
        {
            if (sail == null || sail.visualRoot != null) return;

            GameObject root = new GameObject($"Sail_{sail.name}");
            root.transform.SetParent(transform, false);
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
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = sail.GetIndex(x, y);
                    float u = (float)x / (w - 1);
                    float v = 1f - (float)y / (h - 1);

                    Vector3 localPos = Vector3.Lerp(
                        Vector3.Lerp(sail.topLeftLocal, sail.topRightLocal, u),
                        Vector3.Lerp(sail.bottomLeftLocal, sail.bottomRightLocal, u),
                        (float)y / (h - 1)
                    );

                    verts[idx] = localPos;
                    uvs[idx] = new Vector2(u, v);
                }
            }

            int triIdx = 0;
            for (int y = 0; y < h - 1; y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    int i00 = sail.GetIndex(x, y);
                    int i10 = sail.GetIndex(x + 1, y);
                    int i01 = sail.GetIndex(x, y + 1);
                    int i11 = sail.GetIndex(x + 1, y + 1);

                    tris[triIdx++] = i00;
                    tris[triIdx++] = i10;
                    tris[triIdx++] = i01;

                    tris[triIdx++] = i01;
                    tris[triIdx++] = i10;
                    tris[triIdx++] = i11;
                }
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
                verts[i] = transform.InverseTransformPoint(sail.positions[i]);

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

        #endregion

        #region Materials

        private Material ResolveSailMaterial(Color defaultColor)
        {
            if (sailMaterial != null) return sailMaterial;
            if (_runtimeSailMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Sprites/Default");

                _runtimeSailMaterial = shader != null ? new Material(shader) : null;
                if (_runtimeSailMaterial != null)
                {
                    if (_runtimeSailMaterial.HasProperty("_BaseColor"))
                        _runtimeSailMaterial.SetColor("_BaseColor", defaultColor);
                    if (_runtimeSailMaterial.HasProperty("_Color"))
                        _runtimeSailMaterial.SetColor("_Color", defaultColor);
                    if (_runtimeSailMaterial.HasProperty("_Cull"))
                        _runtimeSailMaterial.SetFloat("_Cull", 0f); // double-sided
                }
            }
            return _runtimeSailMaterial;
        }

        #endregion

        #region Visual Rig Sync

        private void TrySyncRigFromVisual()
        {
            if (!syncRigFromVisualSails)
                return;

            if (syncMastsFromVisual)
                SyncMastsFromVisualObjects();

            if (rebuildSailsFromVisual)
                SyncSailsFromVisualObjects();
            else
                SyncExistingSailsFromVisualObjects();
        }

        private void SyncMastsFromVisualObjects()
        {
            var synced = new List<MastDefinition>(3);
            var seen = new HashSet<Transform>();

            for (int i = 0; i < VisualMastNameHints.Length; i++)
            {
                Transform mastTransform = FindVisualChildByName(VisualMastNameHints[i]);
                if (mastTransform == null || seen.Contains(mastTransform))
                    continue;
                if (mastTransform.GetComponentInChildren<MeshFilter>(true) == null &&
                    mastTransform.GetComponentInChildren<MeshRenderer>(true) == null)
                    continue;

                bool hasBounds = TryGetInteractiveBounds(mastTransform, "Interactive_MastCollider", out Bounds bounds);
                if (!hasBounds)
                    hasBounds = TryGetWorldBounds(mastTransform, out bounds);
                if (!hasBounds)
                    continue;

                seen.Add(mastTransform);
                Vector3 mastWorldPos = mastTransform.position;
                var mast = new MastDefinition
                {
                    name = mastTransform.name,
                    // Keep mast axis centered on the actual mast transform (beam center),
                    // while using bounds only for vertical span.
                    baseLocal = transform.InverseTransformPoint(new Vector3(mastWorldPos.x, bounds.min.y, mastWorldPos.z)),
                    topLocal = transform.InverseTransformPoint(new Vector3(mastWorldPos.x, bounds.max.y, mastWorldPos.z)),
                    radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.45f, 0.12f, 0.55f),
                    yieldStrength = 40_000_000f,
                    elasticModulus = 12_000_000_000f,
                };
                synced.Add(mast);
            }

            if (synced.Count > 0)
            {
                synced.Sort((a, b) => b.baseLocal.z.CompareTo(a.baseLocal.z));
                masts = synced.ToArray();
            }
        }

        private void SyncSailsFromVisualObjects()
        {
            var candidates = new List<Transform>();
            var seen = new HashSet<Transform>();

            for (int i = 0; i < VisualSailNameHints.Length; i++)
            {
                Transform t = FindVisualChildByName(VisualSailNameHints[i]);
                if (t == null || seen.Contains(t))
                    continue;
                if (!t.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null)
                    continue;

                seen.Add(t);
                candidates.Add(t);
            }

            if (candidates.Count == 0)
            {
                Transform[] all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null || seen.Contains(t))
                        continue;
                    if (!IsVisualSailCandidate(t.name))
                        continue;
                    if (!t.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null)
                        continue;

                    seen.Add(t);
                    candidates.Add(t);
                }
            }

            if (candidates.Count == 0)
                return;

            // Keep order stable and intuitive: fore-to-aft, then lower-to-upper.
            candidates.Sort((a, b) =>
            {
                Vector3 la = transform.InverseTransformPoint(a.position);
                Vector3 lb = transform.InverseTransformPoint(b.position);
                int zCmp = lb.z.CompareTo(la.z);
                return zCmp != 0 ? zCmp : la.y.CompareTo(lb.y);
            });

            var syncedPanels = new List<SailPanel>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                Transform visual = candidates[i];
                SailPanel panel = CreatePanelTemplateFromExisting(visual.name);
                panel.name = visual.name;
                panel.visualSailName = visual.name;
                panel.pinBoundaryToRig = true;
                panel.hoist01 = 1f;
                panel.extension01 = 1f;

                if (!TryConfigureSailFromVisualMesh(panel, visual))
                    continue;

                AssignMastForSail(panel);
                syncedPanels.Add(panel);
            }

            if (syncedPanels.Count > 0)
                sails = syncedPanels.ToArray();
        }

        private void SyncExistingSailsFromVisualObjects()
        {
            if (sails == null)
                return;

            for (int i = 0; i < sails.Length; i++)
            {
                SailPanel sail = sails[i];
                if (sail == null)
                    continue;

                string visualName = string.IsNullOrWhiteSpace(sail.visualSailName) ? sail.name : sail.visualSailName;
                Transform visual = FindVisualChildByName(visualName);
                if (visual == null)
                    continue;
                if (!TryConfigureSailFromVisualMesh(sail, visual))
                    continue;

                sail.hoist01 = 1f;
                sail.extension01 = 1f;
                AssignMastForSail(sail);
            }
        }

        private SailPanel CreatePanelTemplateFromExisting(string visualName)
        {
            if (sails != null)
            {
                for (int i = 0; i < sails.Length; i++)
                {
                    SailPanel src = sails[i];
                    if (src == null)
                        continue;

                    bool sameVisual = !string.IsNullOrWhiteSpace(src.visualSailName) &&
                                      string.Equals(src.visualSailName, visualName, StringComparison.OrdinalIgnoreCase);
                    bool sameName = string.Equals(src.name, visualName, StringComparison.OrdinalIgnoreCase);
                    if (!sameVisual && !sameName)
                        continue;

                    return new SailPanel
                    {
                        name = src.name,
                        enabled = src.enabled,
                        visualSailName = src.visualSailName,
                        pinBoundaryToRig = src.pinBoundaryToRig,
                        triangularHint = src.triangularHint,
                        mastIndex = src.mastIndex,
                        mastAttachHeight = src.mastAttachHeight,
                        baseArea = src.baseArea,
                        hoist01 = src.hoist01,
                        extension01 = src.extension01,
                        sheetAngleDeg = src.sheetAngleDeg,
                        damage01 = src.damage01,
                        particleMass = src.particleMass,
                        structuralStiffness = src.structuralStiffness,
                        shearStiffness = src.shearStiffness,
                        bendStiffness = src.bendStiffness,
                        damping = src.damping,
                        gravityScale = src.gravityScale,
                        windDragCoeff = src.windDragCoeff,
                        windLiftCoeff = src.windLiftCoeff,
                        windFlutterStrength = src.windFlutterStrength,
                        windFlutterFrequency = src.windFlutterFrequency,
                        solverIterations = src.solverIterations,
                        clothColor = src.clothColor,
                    };
                }
            }

            return new SailPanel
            {
                name = visualName,
                enabled = true,
                visualSailName = visualName,
                pinBoundaryToRig = true,
                hoist01 = 1f,
                extension01 = 1f,
                sheetAngleDeg = 0f,
                damage01 = 0f,
                solverIterations = 6,
            };
        }

        private bool TryConfigureSailFromVisualMesh(SailPanel sail, Transform visual)
        {
            if (visual == null)
                return false;

            if (!visual.TryGetComponent(out MeshFilter meshFilter) || meshFilter.sharedMesh == null)
                return TryConfigureSailFromPrimitiveCollider(sail, visual);

            Mesh mesh = meshFilter.sharedMesh;

            Vector3[] vertices = mesh.vertices;
            if (vertices == null || vertices.Length < 3)
                return TryConfigureSailFromPrimitiveCollider(sail, visual);

            Vector3 meshSize = mesh.bounds.size;
            int thinAxis = GetSmallestAxis(meshSize);
            int axisA = (thinAxis + 1) % 3;
            int axisB = (thinAxis + 2) % 3;

            Vector3 upInVisual = visual.InverseTransformDirection(transform.up).normalized;
            Vector3 rightInVisual = visual.InverseTransformDirection(transform.right).normalized;

            int verticalAxis = Mathf.Abs(GetAxisComponent(upInVisual, axisA)) >= Mathf.Abs(GetAxisComponent(upInVisual, axisB)) ? axisA : axisB;
            int horizontalAxis = verticalAxis == axisA ? axisB : axisA;

            float verticalSign = Mathf.Sign(GetAxisComponent(upInVisual, verticalAxis));
            if (Mathf.Abs(verticalSign) < 0.5f) verticalSign = 1f;
            float horizontalSign = Mathf.Sign(GetAxisComponent(rightInVisual, horizontalAxis));
            if (Mathf.Abs(horizontalSign) < 0.5f) horizontalSign = 1f;

            float minU = GetAxisComponent(vertices[0], horizontalAxis) * horizontalSign;
            float maxU = minU;
            float minV = GetAxisComponent(vertices[0], verticalAxis) * verticalSign;
            float maxV = minV;

            for (int i = 1; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float u = GetAxisComponent(v, horizontalAxis) * horizontalSign;
                float vv = GetAxisComponent(v, verticalAxis) * verticalSign;
                minU = Mathf.Min(minU, u);
                maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, vv);
                maxV = Mathf.Max(maxV, vv);
            }

            float height = Mathf.Max(0.001f, maxV - minV);
            float topBand = maxV - height * 0.15f;
            float bottomBand = minV + height * 0.15f;

            float topMinU = float.MaxValue;
            float topMaxU = float.MinValue;
            float bottomMinU = float.MaxValue;
            float bottomMaxU = float.MinValue;
            bool hasTop = false;
            bool hasBottom = false;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float u = GetAxisComponent(v, horizontalAxis) * horizontalSign;
                float vv = GetAxisComponent(v, verticalAxis) * verticalSign;

                if (vv >= topBand)
                {
                    hasTop = true;
                    topMinU = Mathf.Min(topMinU, u);
                    topMaxU = Mathf.Max(topMaxU, u);
                }

                if (vv <= bottomBand)
                {
                    hasBottom = true;
                    bottomMinU = Mathf.Min(bottomMinU, u);
                    bottomMaxU = Mathf.Max(bottomMaxU, u);
                }
            }

            if (!hasTop)
            {
                topMinU = minU;
                topMaxU = maxU;
            }

            if (!hasBottom)
            {
                bottomMinU = minU;
                bottomMaxU = maxU;
            }

            float topWidth = Mathf.Max(0.001f, topMaxU - topMinU);
            float bottomWidth = Mathf.Max(0.001f, bottomMaxU - bottomMinU);
            bool triangular = sail.triangularHint || topWidth <= bottomWidth * 0.55f;
            sail.triangularHint = triangular;

            Vector3 topLeftMesh;
            Vector3 topRightMesh;
            Vector3 bottomLeftMesh;
            Vector3 bottomRightMesh;

            if (triangular)
            {
                Vector3 apex = vertices[0];
                float apexV = GetAxisComponent(apex, verticalAxis) * verticalSign;
                for (int i = 1; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    float vv = GetAxisComponent(v, verticalAxis) * verticalSign;
                    if (vv > apexV)
                    {
                        apex = v;
                        apexV = vv;
                    }
                }

                bottomLeftMesh = vertices[0];
                bottomRightMesh = vertices[0];
                float leftU = GetAxisComponent(bottomLeftMesh, horizontalAxis) * horizontalSign;
                float rightU = leftU;
                bool foundBottom = false;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    float vv = GetAxisComponent(v, verticalAxis) * verticalSign;
                    if (vv > bottomBand + height * 0.05f)
                        continue;

                    float u = GetAxisComponent(v, horizontalAxis) * horizontalSign;
                    if (!foundBottom || u < leftU)
                    {
                        bottomLeftMesh = v;
                        leftU = u;
                    }
                    if (!foundBottom || u > rightU)
                    {
                        bottomRightMesh = v;
                        rightU = u;
                    }
                    foundBottom = true;
                }

                if (!foundBottom)
                {
                    bottomLeftMesh = FindNearestByUV(vertices, minU, minV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                    bottomRightMesh = FindNearestByUV(vertices, maxU, minV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                }

                float apexHalfWidth = Mathf.Max(bottomWidth * 0.03f, 0.02f);
                Vector3 horizontalDir = GetAxisVector(horizontalAxis) * horizontalSign;
                topLeftMesh = apex - horizontalDir * apexHalfWidth;
                topRightMesh = apex + horizontalDir * apexHalfWidth;
            }
            else
            {
                topLeftMesh = FindNearestByUV(vertices, minU, maxV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                topRightMesh = FindNearestByUV(vertices, maxU, maxV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                bottomLeftMesh = FindNearestByUV(vertices, minU, minV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                bottomRightMesh = FindNearestByUV(vertices, maxU, minV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
            }

            sail.topLeftLocal = transform.InverseTransformPoint(visual.TransformPoint(topLeftMesh));
            sail.topRightLocal = transform.InverseTransformPoint(visual.TransformPoint(topRightMesh));
            sail.bottomLeftLocal = transform.InverseTransformPoint(visual.TransformPoint(bottomLeftMesh));
            sail.bottomRightLocal = transform.InverseTransformPoint(visual.TransformPoint(bottomRightMesh));

            FinalizeSailGeometryFromAnchorPoints(sail, triangular);
            return true;
        }

        private bool TryConfigureSailFromPrimitiveCollider(SailPanel sail, Transform visual)
        {
            Collider collider = FindPreferredPrimitiveCollider(visual);
            if (collider == null)
                return false;

            Vector3 center;
            Vector3 size;
            if (collider is BoxCollider box)
            {
                center = box.center;
                size = box.size;
            }
            else if (collider is SphereCollider sphere)
            {
                center = sphere.center;
                float d = sphere.radius * 2f;
                size = new Vector3(d, d, d);
            }
            else if (collider is CapsuleCollider capsule)
            {
                center = capsule.center;
                float d = capsule.radius * 2f;
                if (capsule.direction == 0)
                    size = new Vector3(capsule.height, d, d);
                else if (capsule.direction == 1)
                    size = new Vector3(d, capsule.height, d);
                else
                    size = new Vector3(d, d, capsule.height);
            }
            else
            {
                return false;
            }

            float halfX = Mathf.Max(0.02f, size.x * 0.5f);
            float halfY = Mathf.Max(0.02f, size.y * 0.5f);
            bool triangular = sail.triangularHint ||
                              visual.name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              visual.name.IndexOf("Tri", StringComparison.OrdinalIgnoreCase) >= 0;

            if (triangular)
            {
                float apexHalf = Mathf.Max(0.02f, halfX * 0.08f);
                sail.topLeftLocal = transform.InverseTransformPoint(collider.transform.TransformPoint(center + new Vector3(-apexHalf, halfY, 0f)));
                sail.topRightLocal = transform.InverseTransformPoint(collider.transform.TransformPoint(center + new Vector3(apexHalf, halfY, 0f)));
            }
            else
            {
                sail.topLeftLocal = transform.InverseTransformPoint(collider.transform.TransformPoint(center + new Vector3(-halfX, halfY, 0f)));
                sail.topRightLocal = transform.InverseTransformPoint(collider.transform.TransformPoint(center + new Vector3(halfX, halfY, 0f)));
            }

            sail.bottomLeftLocal = transform.InverseTransformPoint(collider.transform.TransformPoint(center + new Vector3(-halfX, -halfY, 0f)));
            sail.bottomRightLocal = transform.InverseTransformPoint(collider.transform.TransformPoint(center + new Vector3(halfX, -halfY, 0f)));

            FinalizeSailGeometryFromAnchorPoints(sail, triangular);
            return true;
        }

        private void FinalizeSailGeometryFromAnchorPoints(SailPanel sail, bool triangular)
        {
            sail.triangularHint = triangular;
            sail.baseArea = Mathf.Max(0.5f, ComputePanelArea(
                sail.topLeftLocal,
                sail.topRightLocal,
                sail.bottomLeftLocal,
                sail.bottomRightLocal));

            float topEdge = Vector3.Distance(sail.topLeftLocal, sail.topRightLocal);
            float bottomEdge = Vector3.Distance(sail.bottomLeftLocal, sail.bottomRightLocal);
            float leftEdge = Vector3.Distance(sail.topLeftLocal, sail.bottomLeftLocal);
            float rightEdge = Vector3.Distance(sail.topRightLocal, sail.bottomRightLocal);
            float width = Mathf.Max(topEdge, bottomEdge);
            float avgHeight = 0.5f * (leftEdge + rightEdge);

            sail.gridWidth = Mathf.Clamp(Mathf.RoundToInt(width / 0.35f) + 1, triangular ? 7 : 6, 16);
            sail.gridHeight = Mathf.Clamp(Mathf.RoundToInt(avgHeight / 0.35f) + 1, triangular ? 8 : 5, 16);

            RetuneSailPhysicsFromArea(sail, triangular);

            sail.pinBoundaryToRig = true;

            sail.initialized = false;
            sail.positions = null;
            sail.prevPositions = null;
            sail.restPositions = null;
            sail.pinned = null;
        }

        private static void RetuneSailPhysicsFromArea(SailPanel sail, bool triangular)
        {
            if (sail == null)
                return;

            float areaFactor = Mathf.Clamp01(sail.baseArea / 12f);
            sail.particleMass = Mathf.Lerp(0.14f, 0.32f, areaFactor);
            sail.structuralStiffness = Mathf.Lerp(260f, 520f, areaFactor);
            sail.shearStiffness = sail.structuralStiffness * 0.42f;
            sail.bendStiffness = sail.structuralStiffness * 0.08f;
            sail.damping = Mathf.Lerp(3.4f, 6.4f, areaFactor);
            sail.windDragCoeff = triangular ? 1.1f : 1.28f;
            sail.windLiftCoeff = triangular ? 0.34f : 0.42f;
            sail.windFlutterStrength = triangular ? 0.34f : 0.45f;
            sail.solverIterations = 4;
        }

        private void AssignMastForSail(SailPanel sail)
        {
            if (masts == null || masts.Length == 0 || sail == null)
                return;

            Vector3 center = (sail.topLeftLocal + sail.topRightLocal + sail.bottomLeftLocal + sail.bottomRightLocal) * 0.25f;

            int aftOverride = -1;
            if (IsAftSailName(sail.name) || IsAftSailName(sail.visualSailName))
                aftOverride = FindAftMostMastIndex();
            if (aftOverride >= 0)
            {
                sail.mastIndex = aftOverride;

                MastDefinition mast = masts[aftOverride];
                Vector3 axis = mast.topLocal - mast.baseLocal;
                float axisLenSqr = axis.sqrMagnitude;
                if (axisLenSqr > 0.0001f)
                    sail.mastAttachHeight = Mathf.Clamp01(Vector3.Dot(center - mast.baseLocal, axis) / axisLenSqr);
                return;
            }

            int bestIndex = -1;
            float bestSqrDist = float.MaxValue;
            float bestHeightT = 0.5f;

            for (int i = 0; i < masts.Length; i++)
            {
                MastDefinition mast = masts[i];
                if (mast == null)
                    continue;

                Vector3 axis = mast.topLocal - mast.baseLocal;
                float axisLenSqr = axis.sqrMagnitude;
                if (axisLenSqr < 0.0001f)
                    continue;

                float t = Mathf.Clamp01(Vector3.Dot(center - mast.baseLocal, axis) / axisLenSqr);
                Vector3 closest = mast.baseLocal + axis * t;
                float sqrDist = (center - closest).sqrMagnitude;

                if (sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    bestIndex = i;
                    bestHeightT = t;
                }
            }

            if (bestIndex >= 0)
            {
                sail.mastIndex = bestIndex;
                sail.mastAttachHeight = bestHeightT;
            }
        }

        private int FindAftMostMastIndex()
        {
            if (masts == null || masts.Length == 0)
                return -1;

            int bestIndex = -1;
            float minZ = float.MaxValue;
            for (int i = 0; i < masts.Length; i++)
            {
                MastDefinition mast = masts[i];
                if (mast == null)
                    continue;

                float z = mast.baseLocal.z;
                if (z < minZ)
                {
                    minZ = z;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool IsAftSailName(string sailName)
        {
            if (string.IsNullOrWhiteSpace(sailName))
                return false;

            return sailName.IndexOf("aft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sailName.IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sailName.IndexOf("spanker", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetWorldBounds(Transform target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
                return false;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                bool hasBounds = false;
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r == null)
                        continue;

                    if (!hasBounds)
                    {
                        bounds = r.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }

                if (hasBounds)
                    return true;
            }

            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            if (colliders != null && colliders.Length > 0)
            {
                bool hasBounds = false;
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider c = colliders[i];
                    if (c == null)
                        continue;

                    if (!hasBounds)
                    {
                        bounds = c.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(c.bounds);
                    }
                }

                return hasBounds;
            }

            return false;
        }

        private Transform FindVisualChildByName(string nameOrHint)
        {
            if (string.IsNullOrWhiteSpace(nameOrHint))
                return null;

            Transform[] all = GetComponentsInChildren<Transform>(true);

            // Pass 1: exact active match on real visual objects.
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == transform)
                    continue;
                if (!t.gameObject.activeInHierarchy)
                    continue;
                if (t.name.StartsWith("Sail_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsGameplayHelperNodeName(t.name))
                    continue;

                if (string.Equals(t.name, nameOrHint, StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            // Pass 2: active loose contains fallback.
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == transform)
                    continue;
                if (!t.gameObject.activeInHierarchy)
                    continue;
                if (t.name.StartsWith("Sail_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsGameplayHelperNodeName(t.name))
                    continue;
                if (t.name.IndexOf(nameOrHint, StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            }

            // Pass 3: exact inactive fallback for legacy prefabs.
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == transform)
                    continue;
                if (t.name.StartsWith("Sail_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsGameplayHelperNodeName(t.name))
                    continue;
                if (string.Equals(t.name, nameOrHint, StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            // Pass 4: loose inactive fallback.
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == transform)
                    continue;
                if (t.name.StartsWith("Sail_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsGameplayHelperNodeName(t.name))
                    continue;
                if (t.name.IndexOf(nameOrHint, StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            }

            return null;
        }

        private static bool IsVisualSailCandidate(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return false;
            if (objectName.StartsWith("Sail_", StringComparison.OrdinalIgnoreCase))
                return false;

            if (objectName.IndexOf("sail", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            for (int i = 0; i < VisualSailNameHints.Length; i++)
            {
                if (string.Equals(objectName, VisualSailNameHints[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static Vector3 FindNearestByXY(Vector3[] vertices, float targetX, float targetY)
        {
            Vector3 best = vertices[0];
            float bestSqr = float.MaxValue;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float dx = v.x - targetX;
                float dy = v.y - targetY;
                float sqr = dx * dx + dy * dy;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = v;
                }
            }

            return best;
        }

        private static Vector3 FindNearestByUV(
            Vector3[] vertices,
            float targetU,
            float targetV,
            int horizontalAxis,
            float horizontalSign,
            int verticalAxis,
            float verticalSign)
        {
            Vector3 best = vertices[0];
            float bestSqr = float.MaxValue;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float u = GetAxisComponent(v, horizontalAxis) * horizontalSign;
                float vv = GetAxisComponent(v, verticalAxis) * verticalSign;

                float du = u - targetU;
                float dv = vv - targetV;
                float sqr = du * du + dv * dv;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = v;
                }
            }

            return best;
        }

        private static int GetSmallestAxis(Vector3 v)
        {
            if (v.x <= v.y && v.x <= v.z) return 0;
            if (v.y <= v.x && v.y <= v.z) return 1;
            return 2;
        }

        private static float GetAxisComponent(Vector3 v, int axis)
        {
            if (axis == 0) return v.x;
            if (axis == 1) return v.y;
            return v.z;
        }

        private static Vector3 GetAxisVector(int axis)
        {
            if (axis == 0) return Vector3.right;
            if (axis == 1) return Vector3.up;
            return Vector3.forward;
        }

        private static Collider FindPreferredPrimitiveCollider(Transform visual)
        {
            if (visual == null)
                return null;

            Transform helper = visual.Find("Interactive_SailCollider");
            if (helper != null && helper.TryGetComponent(out Collider helperCol) && helperCol != null && !(helperCol is MeshCollider))
                return helperCol;

            Collider[] own = visual.GetComponents<Collider>();
            for (int i = 0; i < own.Length; i++)
            {
                Collider c = own[i];
                if (c == null || c is MeshCollider)
                    continue;
                return c;
            }

            return null;
        }

        private static bool TryGetInteractiveBounds(Transform target, string helperName, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
                return false;

            Transform helper = target.Find(helperName);
            if (helper == null)
                return false;
            if (!helper.TryGetComponent(out Collider collider) || collider == null)
                return false;

            bounds = collider.bounds;
            return true;
        }

        private static bool IsGameplayHelperNodeName(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName))
                return false;

            return nodeName.StartsWith("Gameplay", StringComparison.OrdinalIgnoreCase) ||
                   nodeName.StartsWith("Walkable_", StringComparison.OrdinalIgnoreCase) ||
                   nodeName.StartsWith("Blocked_", StringComparison.OrdinalIgnoreCase) ||
                   nodeName.EndsWith("_Climb", StringComparison.OrdinalIgnoreCase) ||
                   nodeName.EndsWith("_Zone", StringComparison.OrdinalIgnoreCase) ||
                   nodeName.EndsWith("_CrowNest", StringComparison.OrdinalIgnoreCase) ||
                   nodeName.EndsWith("_Interact", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldPinPerimeterPoint(int x, int y, int width, int height, int requestedAnchors)
        {
            if (width < 2 || height < 2)
                return false;

            bool isBoundary = x == 0 || y == 0 || x == width - 1 || y == height - 1;
            if (!isBoundary)
                return false;

            bool isCorner = (x == 0 || x == width - 1) && (y == 0 || y == height - 1);
            if (isCorner)
                return true;

            int perimeterCount = (width * 2) + (height * 2) - 4;
            int anchorCount = Mathf.Clamp(requestedAnchors, 4, perimeterCount);
            if (anchorCount >= perimeterCount)
                return true;

            int index = GetPerimeterIndex(x, y, width, height);
            if (index < 0)
                return false;

            float step = (perimeterCount - 1f) / Mathf.Max(1f, anchorCount - 1f);
            for (int i = 0; i < anchorCount; i++)
            {
                int targetIndex = Mathf.RoundToInt(i * step);
                if (targetIndex == index)
                    return true;
            }

            return false;
        }

        private static int GetPerimeterIndex(int x, int y, int width, int height)
        {
            if (y == 0)
                return x;
            if (x == width - 1)
                return (width - 1) + y;
            if (y == height - 1)
                return (width - 1) + (height - 1) + ((width - 1) - x);
            if (x == 0)
                return (width - 1) + (height - 1) + (width - 1) + ((height - 1) - y);

            return -1;
        }

        private static float ComputePanelArea(Vector3 topLeft, Vector3 topRight, Vector3 bottomLeft, Vector3 bottomRight)
        {
            float a0 = Vector3.Cross(topRight - topLeft, bottomLeft - topLeft).magnitude * 0.5f;
            float a1 = Vector3.Cross(bottomRight - topRight, bottomLeft - topRight).magnitude * 0.5f;
            return Mathf.Max(0.01f, a0 + a1);
        }

        #endregion

        #region Default Rig (synced with visual model masts/yards)

        /// <summary>
        /// Build default rig synchronized with ProceduralBrigShipMeshBuilder mast/yard positions.
        /// Mast positions:
        ///   ForeMast: Z=2.0, base Y≈2.85, top Y≈11.2
        ///   MainMast: Z=-3.9, base Y≈2.85, top Y≈12.1
        /// Yard positions (center, half-widths):
        ///   ForeCourseYard:     Y=5.1,  Z=2.0,  half-w=2.5
        ///   ForeTopsailYard:    Y=7.1,  Z=2.0,  half-w=2.15
        ///   ForeTopgallantYard: Y=9.35, Z=2.0,  half-w=1.55
        ///   MainCourseYard:     Y=5.45, Z=-3.9, half-w=2.8
        ///   MainTopsailYard:    Y=7.85, Z=-3.9, half-w=2.35
        ///   MainTopgallantYard: Y=10.35,Z=-3.9, half-w=1.7
        ///   MainGaff:           Y=6.25, Z=-4.2  (spanker)
        ///   MainBoom:           Y=3.95, Z=-4.1  (spanker bottom)
        /// </summary>
        private void BuildDefaultRig()
        {
            // Define masts matching the visual model
            masts = new[]
            {
                new MastDefinition
                {
                    name = "ForeMast",
                    baseLocal = new Vector3(0f, 2.85f, 2.0f),
                    topLocal = new Vector3(0f, 11.2f, 2.0f),
                    radius = 0.22f,
                    yieldStrength = 40_000_000f,
                    elasticModulus = 12_000_000_000f,
                },
                new MastDefinition
                {
                    name = "MainMast",
                    baseLocal = new Vector3(0f, 2.85f, -3.9f),
                    topLocal = new Vector3(0f, 12.1f, -3.9f),
                    radius = 0.24f,
                    yieldStrength = 40_000_000f,
                    elasticModulus = 12_000_000_000f,
                },
                new MastDefinition
                {
                    name = "BackMast",
                    baseLocal = new Vector3(1.05f, 2.9f, -14.95f),
                    topLocal = new Vector3(1.05f, 13.5f, -14.95f),
                    radius = 0.20f,
                    yieldStrength = 40_000_000f,
                    elasticModulus = 12_000_000_000f,
                },
            };

            // Sails attached to yards, positions synced with ProceduralBrigShipMeshBuilder.BuildSails()
            sails = new[]
            {
                // Fore Course: hangs below ForeCourseYard (Y=5.1), spans 4.2m wide, 2.2m tall
                new SailPanel
                {
                    name = "ForeCourse",
                    visualSailName = "StylShip_SailFront",
                    mastIndex = 0,
                    mastAttachHeight = 0.27f, // (5.1 - 2.85) / (11.2 - 2.85) ≈ 0.27
                    baseArea = 9.2f,          // 4.2 * 2.2
                    gridWidth = 8,
                    gridHeight = 6,
                    topLeftLocal = new Vector3(-2.1f, 5.1f, 2.0f),
                    topRightLocal = new Vector3(2.1f, 5.1f, 2.0f),
                    bottomLeftLocal = new Vector3(-2.0f, 2.9f, 2.0f),
                    bottomRightLocal = new Vector3(2.0f, 2.9f, 2.0f),
                    sheetAngleDeg = 0f,
                    particleMass = 0.24f,
                    structuralStiffness = 420f,
                    shearStiffness = 180f,
                    bendStiffness = 30f,
                    damping = 4.6f,
                    windDragCoeff = 1.3f,
                    solverIterations = 4,
                },
                // Fore Topsail: hangs below ForeTopsailYard (Y=7.1), 3.6m wide, 2.0m tall
                new SailPanel
                {
                    name = "ForeTopsail",
                    visualSailName = "StylShip_SailFront",
                    mastIndex = 0,
                    mastAttachHeight = 0.51f, // (7.1 - 2.85) / (11.2 - 2.85) ≈ 0.51
                    baseArea = 7.2f,          // 3.6 * 2.0
                    gridWidth = 7,
                    gridHeight = 5,
                    topLeftLocal = new Vector3(-1.8f, 7.1f, 2.0f),
                    topRightLocal = new Vector3(1.8f, 7.1f, 2.0f),
                    bottomLeftLocal = new Vector3(-1.7f, 5.1f, 2.0f),
                    bottomRightLocal = new Vector3(1.7f, 5.1f, 2.0f),
                    sheetAngleDeg = 0f,
                    particleMass = 0.2f,
                    structuralStiffness = 360f,
                    shearStiffness = 150f,
                    bendStiffness = 24f,
                    damping = 4.3f,
                    windDragCoeff = 1.2f,
                    solverIterations = 4,
                },
                // Fore Topgallant: below ForeTopgallantYard (Y=9.35), 2.6m wide, 1.4m tall
                new SailPanel
                {
                    name = "ForeTopgallant",
                    visualSailName = "StylShip_SailFront",
                    mastIndex = 0,
                    mastAttachHeight = 0.78f, // (9.35 - 2.85) / (11.2 - 2.85) ≈ 0.78
                    baseArea = 3.6f,          // 2.6 * 1.4
                    gridWidth = 6,
                    gridHeight = 4,
                    topLeftLocal = new Vector3(-1.3f, 9.35f, 2.0f),
                    topRightLocal = new Vector3(1.3f, 9.35f, 2.0f),
                    bottomLeftLocal = new Vector3(-1.2f, 7.95f, 2.0f),
                    bottomRightLocal = new Vector3(1.2f, 7.95f, 2.0f),
                    sheetAngleDeg = 0f,
                    particleMass = 0.16f,
                    structuralStiffness = 320f,
                    shearStiffness = 135f,
                    bendStiffness = 20f,
                    damping = 3.9f,
                    windDragCoeff = 1.1f,
                    solverIterations = 4,
                },
                // Main Course: below MainCourseYard (Y=5.45), 4.5m wide, 2.5m tall
                new SailPanel
                {
                    name = "MainCourse",
                    visualSailName = "StylShip_SailMid1",
                    mastIndex = 1,
                    mastAttachHeight = 0.28f, // (5.45 - 2.85) / (12.1 - 2.85) ≈ 0.28
                    baseArea = 11.25f,        // 4.5 * 2.5
                    gridWidth = 9,
                    gridHeight = 7,
                    topLeftLocal = new Vector3(-2.25f, 5.45f, -3.9f),
                    topRightLocal = new Vector3(2.25f, 5.45f, -3.9f),
                    bottomLeftLocal = new Vector3(-2.1f, 2.95f, -3.9f),
                    bottomRightLocal = new Vector3(2.1f, 2.95f, -3.9f),
                    sheetAngleDeg = 0f,
                    particleMass = 0.26f,
                    structuralStiffness = 440f,
                    shearStiffness = 190f,
                    bendStiffness = 32f,
                    damping = 4.8f,
                    windDragCoeff = 1.35f,
                    solverIterations = 4,
                },
                // Main Topsail: below MainTopsailYard (Y=7.85), 3.8m wide, 2.1m tall
                new SailPanel
                {
                    name = "MainTopsail",
                    visualSailName = "StylShip_SailMid2",
                    mastIndex = 1,
                    mastAttachHeight = 0.54f, // (7.85 - 2.85) / (12.1 - 2.85) ≈ 0.54
                    baseArea = 7.98f,         // 3.8 * 2.1
                    gridWidth = 8,
                    gridHeight = 6,
                    topLeftLocal = new Vector3(-1.9f, 7.85f, -3.9f),
                    topRightLocal = new Vector3(1.9f, 7.85f, -3.9f),
                    bottomLeftLocal = new Vector3(-1.8f, 5.75f, -3.9f),
                    bottomRightLocal = new Vector3(1.8f, 5.75f, -3.9f),
                    sheetAngleDeg = 0f,
                    particleMass = 0.22f,
                    structuralStiffness = 380f,
                    shearStiffness = 160f,
                    bendStiffness = 26f,
                    damping = 4.4f,
                    windDragCoeff = 1.25f,
                    solverIterations = 4,
                },
                // Main Topgallant: below MainTopgallantYard (Y=10.35), 2.8m wide, 1.5m tall
                new SailPanel
                {
                    name = "MainTopgallant",
                    visualSailName = "StylShip_SailMid2",
                    mastIndex = 1,
                    mastAttachHeight = 0.81f, // (10.35 - 2.85) / (12.1 - 2.85) ≈ 0.81
                    baseArea = 4.2f,          // 2.8 * 1.5
                    gridWidth = 6,
                    gridHeight = 4,
                    topLeftLocal = new Vector3(-1.4f, 10.35f, -3.9f),
                    topRightLocal = new Vector3(1.4f, 10.35f, -3.9f),
                    bottomLeftLocal = new Vector3(-1.3f, 8.85f, -3.9f),
                    bottomRightLocal = new Vector3(1.3f, 8.85f, -3.9f),
                    sheetAngleDeg = 0f,
                    particleMass = 0.18f,
                    structuralStiffness = 330f,
                    shearStiffness = 142f,
                    bendStiffness = 22f,
                    damping = 4f,
                    windDragCoeff = 1.15f,
                    solverIterations = 4,
                },
                // Aft triangular sail around StylShip_SailBack (editable point anchors in Scene view)
                new SailPanel
                {
                    name = "AftTri",
                    visualSailName = "StylShip_SailBack",
                    triangularHint = true,
                    mastIndex = 2,
                    mastAttachHeight = 0.62f,
                    baseArea = 7.8f,
                    gridWidth = 8,
                    gridHeight = 8,
                    topLeftLocal = new Vector3(1.18f, 13.45f, -10.10f),
                    topRightLocal = new Vector3(1.40f, 13.45f, -10.10f),
                    bottomLeftLocal = new Vector3(-0.35f, 7.45f, -14.75f),
                    bottomRightLocal = new Vector3(2.95f, 7.45f, -14.75f),
                    sheetAngleDeg = -6f,
                    particleMass = 0.22f,
                    structuralStiffness = 350f,
                    shearStiffness = 150f,
                    bendStiffness = 24f,
                    damping = 4.2f,
                    windDragCoeff = 1.1f,
                    windLiftCoeff = 0.34f,
                    windFlutterStrength = 0.48f,
                    solverIterations = 4,
                },
            };
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

        private bool ShouldApplyForcesForThisPeer()
        {
#if FUSION2
            var networkShip = GetComponent<NetworkShip>();
            if (networkShip != null && !networkShip.HasStateAuthority)
                return false;
#endif
            return true;
        }

        #endregion

        #region Debug

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (sails == null) return;

            if (editorDrawAttachmentPointsGizmo)
                DrawAnchorPointGizmos();

            if (editorDrawMeshPreviewGizmo)
                DrawPreviewMeshGizmos();

            if (debugDrawForces)
            {
                for (int i = 0; i < sails.Length; i++)
                {
                    SailPanel sail = sails[i];
                    if (sail == null || sail.lastForce.sqrMagnitude < 0.001f) continue;
                    Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.95f);
                    Gizmos.DrawLine(sail.lastForcePoint, sail.lastForcePoint + sail.lastForce * debugForceScale);
                }
            }

            if (debugDrawMastStress && masts != null)
            {
                for (int i = 0; i < masts.Length; i++)
                {
                    if (masts[i] == null) continue;
                    var mast = masts[i];
                    Vector3 baseW = transform.TransformPoint(mast.baseLocal);
                    Vector3 topW = transform.TransformPoint(mast.topLocal);

                    // Color based on stress: green->yellow->red
                    float stress = Mathf.Clamp01(mast.stressRatio);
                    Color stressColor = Color.Lerp(Color.green, Color.red, stress);
                    Gizmos.color = stressColor;
                    Gizmos.DrawLine(baseW, topW);
                    Gizmos.DrawWireSphere(topW, mast.radius * 2f);

                    // Draw force vector on mast
                    if (mast.totalForceOnMast.sqrMagnitude > 0.01f)
                    {
                        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
                        Vector3 forcePoint = Vector3.Lerp(baseW, topW, 0.6f);
                        Gizmos.DrawLine(forcePoint, forcePoint + mast.totalForceOnMast * debugForceScale);
                    }
                }
            }

            if (debugDrawCloth)
            {
                Gizmos.color = new Color(0.9f, 0.9f, 0.7f, 0.5f);
                for (int i = 0; i < sails.Length; i++)
                {
                    SailPanel sail = sails[i];
                    if (sail == null || sail.positions == null) continue;

                    int w = sail.gridWidth;
                    int h = sail.gridHeight;
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int idx = sail.GetIndex(x, y);
                            if (x < w - 1)
                                Gizmos.DrawLine(sail.positions[idx], sail.positions[sail.GetIndex(x + 1, y)]);
                            if (y < h - 1)
                                Gizmos.DrawLine(sail.positions[idx], sail.positions[sail.GetIndex(x, y + 1)]);
                        }
                    }
                }
            }
        }

        private void DrawAnchorPointGizmos()
        {
            for (int i = 0; i < sails.Length; i++)
            {
                SailPanel sail = sails[i];
                if (sail == null)
                    continue;

                Vector3 tl = transform.TransformPoint(sail.topLeftLocal);
                Vector3 tr = transform.TransformPoint(sail.topRightLocal);
                Vector3 bl = transform.TransformPoint(sail.bottomLeftLocal);
                Vector3 br = transform.TransformPoint(sail.bottomRightLocal);

                float size = Mathf.Max(0.03f, editorPointHandleSize);
                Gizmos.color = new Color(0.25f, 0.95f, 1f, 0.95f);
                Gizmos.DrawSphere(tl, size);
                Gizmos.DrawSphere(tr, size);
                Gizmos.DrawSphere(bl, size);
                Gizmos.DrawSphere(br, size);

                Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
                Gizmos.DrawLine(tl, tr);
                Gizmos.DrawLine(tr, br);
                Gizmos.DrawLine(br, bl);
                Gizmos.DrawLine(bl, tl);
            }
        }

        private void DrawPreviewMeshGizmos()
        {
            for (int i = 0; i < sails.Length; i++)
            {
                SailPanel sail = sails[i];
                if (sail == null)
                    continue;

                int w = Mathf.Max(2, sail.gridWidth);
                int h = Mathf.Max(2, sail.gridHeight);
                float deployment = sail.hoist01 * Mathf.Lerp(0.15f, 1f, sail.extension01);

                Gizmos.color = new Color(0.9f, 0.95f, 0.35f, 0.55f);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Vector3 p = ComputeAnchorPosition(sail, x, y, deployment);
                        if (x < w - 1)
                        {
                            Vector3 right = ComputeAnchorPosition(sail, x + 1, y, deployment);
                            Gizmos.DrawLine(p, right);
                        }

                        if (y < h - 1)
                        {
                            Vector3 down = ComputeAnchorPosition(sail, x, y + 1, deployment);
                            Gizmos.DrawLine(p, down);
                        }
                    }
                }
            }
        }
#endif

        #endregion
    }
}

