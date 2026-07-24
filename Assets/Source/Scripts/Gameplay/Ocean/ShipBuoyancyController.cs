using System;
using RumOverboard.Gameplay.Ocean.Simulation;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public enum ShipWaterContactType
    {
        Enter,
        Exit,
        Impact,
    }

    public struct ShipWaterContactEvent
    {
        public ShipWaterContactType contactType;
        public int pointIndex;
        public Vector3 worldPoint;
        public Vector3 surfaceNormal;
        public Vector3 relativeVelocity;
        public float impactStrength;
    }

    [Serializable]
    public struct ShipBuoyancyPoint
    {
        public string name;
        public Vector3 localPosition;
        [Min(0.02f)] public float radius;
        [Min(0.05f)] public float maxSubmersionDepth;
        [Min(0f)] public float buoyancy;
        [Range(0f, 5f)] public float damping;
        public bool emitContacts;
    }

    [RequireComponent(typeof(Rigidbody))]
    public class ShipBuoyancyController : MonoBehaviour
    {
        [SerializeField] private OceanWaveField oceanField;
        [SerializeField] private OceanWindSystem windSystem;
        [SerializeField] private Rigidbody rb;

        [Header("Buoyancy points")]
        [SerializeField] private ShipBuoyancyPoint[] points =
        {
            new ShipBuoyancyPoint { name = "BowPort",   localPosition = new Vector3(-1.8f, 0f, 4.2f), radius = 0.35f, maxSubmersionDepth = 1.1f, buoyancy = 1f, damping = 1f, emitContacts = true },
            new ShipBuoyancyPoint { name = "BowStar",   localPosition = new Vector3( 1.8f, 0f, 4.2f), radius = 0.35f, maxSubmersionDepth = 1.1f, buoyancy = 1f, damping = 1f, emitContacts = true },
            new ShipBuoyancyPoint { name = "MidPort",   localPosition = new Vector3(-2.0f, 0f, 0f),  radius = 0.35f, maxSubmersionDepth = 1.2f, buoyancy = 1f, damping = 1f, emitContacts = false },
            new ShipBuoyancyPoint { name = "MidStar",   localPosition = new Vector3( 2.0f, 0f, 0f),  radius = 0.35f, maxSubmersionDepth = 1.2f, buoyancy = 1f, damping = 1f, emitContacts = false },
            new ShipBuoyancyPoint { name = "SternPort", localPosition = new Vector3(-1.7f, 0f, -4.3f), radius = 0.35f, maxSubmersionDepth = 1.1f, buoyancy = 1f, damping = 1f, emitContacts = true },
            new ShipBuoyancyPoint { name = "SternStar", localPosition = new Vector3( 1.7f, 0f, -4.3f), radius = 0.35f, maxSubmersionDepth = 1.1f, buoyancy = 1f, damping = 1f, emitContacts = true },
        };

        [Header("Force coefficients")]
        [SerializeField] private float buoyancyForce = 30f;
        [SerializeField] private float verticalDamping = 10f;
        [SerializeField] private float longitudinalDrag = 2f;
        [SerializeField] private float lateralDrag = 12f;
        [SerializeField] private float angularDrag = 2.2f;
        [SerializeField] private float currentRelativeDrag = 2.5f;

        [Header("Auto buoyancy from mass (realistic float)")]
        [Tooltip("Scale buoyancy (and the linear damping + per-point force cap) to the Rigidbody mass, " +
                 "so the hull floats at any mass instead of needing hand-tuned buoyancyForce (Archimedes).")]
        [SerializeField] private bool autoBuoyancyFromMass = true;
        [Tooltip("Fraction of a point's max submersion depth the hull settles to at rest.")]
        [Range(0.1f, 0.9f)] [SerializeField] private float equilibriumSubmersion = 0.45f;
        [Tooltip("Restoring headroom above just-floating. 1 = neutral, >1 bobs back up faster.")]
        [Min(1f)] [SerializeField] private float buoyancyHeadroom = 1.5f;
        [Tooltip("Mass the hand-tuned damping coefficients were authored for; damping scales up from this as mass grows.")]
        [Min(1f)] [SerializeField] private float referenceMass = 1800f;

        [Header("Stability assist")]
        [SerializeField] private float rollStability = 28f;
        [SerializeField] private float pitchStability = 22f;
        [SerializeField] private float rollDamping = 10f;
        [SerializeField] private float pitchDamping = 8f;
        [Range(0f, 1f)] [SerializeField] private float surfaceNormalInfluence = 0.35f;
        [SerializeField] private float maxStabilityTorque = 60f;
        [SerializeField] private float inversionRecoveryTorque = 45f;

        [Header("Wind coupling")]
        [SerializeField] private float windHullForceCoefficient = 120f;
        [SerializeField] private float windLongitudinalFactor = 0.35f;
        [SerializeField] private float windLateralFactor = 1f;
        [SerializeField] private float maxWindForce = 4500f;
        [SerializeField] private Vector3 windApplicationOffset = new Vector3(0f, 3f, 0f);

        [Header("Safety")]
        [SerializeField] private float maxForcePerPoint = 9000f;
        [SerializeField] private float maxShipSpeed = 22f;
        [SerializeField] private float maxAngularSpeed = 2.6f;
        [SerializeField] private int minRecommendedPointCount = 4;

        [Header("Contact events")]
        [SerializeField] private float impactSpeedThreshold = 2.8f;
        [SerializeField] private float contactCooldown = 0.12f;

        [Header("Debug")]
        [SerializeField] private bool debugDrawForces;
        [SerializeField] private bool debugDrawResultants = true;
        [SerializeField] private bool debugDrawNormals = true;
        [SerializeField] private bool debugDrawAlways;
        [SerializeField] private bool debugLabels;
        [SerializeField] private float debugForceScale = 0.001f;
        [SerializeField] private float debugResultantScale = 0.0008f;
        [SerializeField] private float debugTorqueScale = 0.08f;
        [SerializeField] private float debugNormalScale = 2f;

        private bool[] _wasSubmerged;
        private float[] _cooldowns;
        private Vector3[] _lastForces;
        private Vector3[] _lastWorldPoints;
        private Vector3[] _lastSurfaceNormals;
        private Vector3[] _lastBuoyancyForces;
        private Vector3[] _lastDampingForces;
        private float[] _lastSubmergence;
        private bool _warnedPointCount;
        private bool _externallyDriven; // when true, an authority (NetworkShip) calls Step() from FixedUpdateNetwork

        private Vector3 _previousVelocity;
        private Vector3 _previousAngularVelocity;
        private Vector3 _linearAcceleration;
        private Vector3 _angularAcceleration;
        private float _lastImpactStrength;
        private Vector3 _lastResultantForce;
        private Vector3 _lastResultantTorque;
        private Vector3 _lastAverageSurfaceNormal = Vector3.up;
        private Vector3 _lastCenterOfMass;
        private int _lastSubmergedPoints;
        private Vector3 _lastWindForce;
        private Vector3 _lastWindApplicationPoint;
        private Vector3 _lastWindVelocity;
        private float _nextWindLookupTime;

        public event Action<ShipWaterContactEvent> WaterContact;

        public Vector3 LinearAcceleration => _linearAcceleration;
        public Vector3 AngularAcceleration => _angularAcceleration;
        public float LastImpactStrength => _lastImpactStrength;

        public OceanWindSystem WindSystem
        {
            get => windSystem;
            set => windSystem = value;
        }

        public float WindHullForceCoefficient
        {
            get => windHullForceCoefficient;
            set => windHullForceCoefficient = Mathf.Max(0f, value);
        }

        public float WindLongitudinalFactor
        {
            get => windLongitudinalFactor;
            set => windLongitudinalFactor = Mathf.Max(0f, value);
        }

        public float WindLateralFactor
        {
            get => windLateralFactor;
            set => windLateralFactor = Mathf.Max(0f, value);
        }

        public float MaxWindForce
        {
            get => maxWindForce;
            set => maxWindForce = Mathf.Max(0f, value);
        }

        public float BuoyancyForce
        {
            get => buoyancyForce;
            set => buoyancyForce = Mathf.Max(0f, value);
        }

        public float VerticalDamping
        {
            get => verticalDamping;
            set => verticalDamping = Mathf.Max(0f, value);
        }

        public float LongitudinalDrag
        {
            get => longitudinalDrag;
            set => longitudinalDrag = Mathf.Max(0f, value);
        }

        public float LateralDrag
        {
            get => lateralDrag;
            set => lateralDrag = Mathf.Max(0f, value);
        }

        public float AngularDragCoefficient
        {
            get => angularDrag;
            set => angularDrag = Mathf.Max(0f, value);
        }

        public float CurrentRelativeDrag
        {
            get => currentRelativeDrag;
            set => currentRelativeDrag = Mathf.Max(0f, value);
        }

        public float RollStability
        {
            get => rollStability;
            set => rollStability = Mathf.Max(0f, value);
        }

        public float PitchStability
        {
            get => pitchStability;
            set => pitchStability = Mathf.Max(0f, value);
        }

        public bool DebugDrawAlways
        {
            get => debugDrawAlways;
            set => debugDrawAlways = value;
        }

        public bool DebugDrawForces
        {
            get => debugDrawForces;
            set => debugDrawForces = value;
        }

        public bool DebugDrawResultants
        {
            get => debugDrawResultants;
            set => debugDrawResultants = value;
        }

        public bool DebugDrawNormals
        {
            get => debugDrawNormals;
            set => debugDrawNormals = value;
        }

        public int LastSubmergedPoints => _lastSubmergedPoints;
        public Vector3 LastResultantForce => _lastResultantForce;
        public Vector3 LastResultantTorque => _lastResultantTorque;
        public Vector3 LastAverageSurfaceNormal => _lastAverageSurfaceNormal;

        public int PointCount => points != null ? points.Length : 0;

        public Vector3 GetLastPointWorld(int pointIndex)
        {
            if (_lastWorldPoints == null || pointIndex < 0 || pointIndex >= _lastWorldPoints.Length)
                return transform.position;
            return _lastWorldPoints[pointIndex];
        }

        public Vector3 GetLastPointForce(int pointIndex)
        {
            if (_lastForces == null || pointIndex < 0 || pointIndex >= _lastForces.Length)
                return Vector3.zero;
            return _lastForces[pointIndex];
        }

        public Vector3 GetLastPointSurfaceNormal(int pointIndex)
        {
            if (_lastSurfaceNormals == null || pointIndex < 0 || pointIndex >= _lastSurfaceNormals.Length)
                return Vector3.up;
            return _lastSurfaceNormals[pointIndex];
        }

        public float GetLastPointSubmergence(int pointIndex)
        {
            if (_lastSubmergence == null || pointIndex < 0 || pointIndex >= _lastSubmergence.Length)
                return 0f;
            return _lastSubmergence[pointIndex];
        }

        public float RollDegrees => NormalizeSigned(transform.eulerAngles.z);
        public float PitchDegrees => NormalizeSigned(transform.eulerAngles.x);

        public void ConfigureReferences(OceanWaveField waveField, Rigidbody body)
        {
            oceanField = waveField;
            rb = body;
        }

        private void Awake()
        {
            if (rb == null)
                rb = GetComponent<Rigidbody>();
            if (oceanField == null)
                oceanField = FindAnyObjectByType<OceanWaveField>();
            if (windSystem == null)
                windSystem = FindAnyObjectByType<OceanWindSystem>();

            EnsureRuntimeBuffers();
        }

        private void OnValidate()
        {
            if (buoyancyForce < 0f) buoyancyForce = 0f;
            if (verticalDamping < 0f) verticalDamping = 0f;
            if (longitudinalDrag < 0f) longitudinalDrag = 0f;
            if (lateralDrag < 0f) lateralDrag = 0f;
            if (angularDrag < 0f) angularDrag = 0f;
            if (currentRelativeDrag < 0f) currentRelativeDrag = 0f;
            if (rollStability < 0f) rollStability = 0f;
            if (pitchStability < 0f) pitchStability = 0f;
            if (rollDamping < 0f) rollDamping = 0f;
            if (pitchDamping < 0f) pitchDamping = 0f;
            if (maxStabilityTorque < 0f) maxStabilityTorque = 0f;
            if (inversionRecoveryTorque < 0f) inversionRecoveryTorque = 0f;
            if (windHullForceCoefficient < 0f) windHullForceCoefficient = 0f;
            if (windLongitudinalFactor < 0f) windLongitudinalFactor = 0f;
            if (windLateralFactor < 0f) windLateralFactor = 0f;
            if (maxWindForce < 0f) maxWindForce = 0f;
            if (debugForceScale < 0f) debugForceScale = 0f;
            if (debugResultantScale < 0f) debugResultantScale = 0f;
            if (debugTorqueScale < 0f) debugTorqueScale = 0f;
            if (debugNormalScale < 0f) debugNormalScale = 0f;
            if (maxForcePerPoint < 10f) maxForcePerPoint = 10f;
        }

        /// <summary>When true, an authority (NetworkShip) steps us from FixedUpdateNetwork instead
        /// of Unity's FixedUpdate — required for deterministic timing with the Fusion Physics addon.</summary>
        public void SetExternallyDriven(bool value) => _externallyDriven = value;

        private void FixedUpdate()
        {
            // Skip Unity's own step when an authority drives us from the network tick (avoids
            // double force application and keeps buoyancy inside Fusion's physics simulation).
            if (_externallyDriven)
                return;
            if (oceanField == null)
                return;

            Step(Time.fixedDeltaTime, oceanField.OceanTimeNow);
        }

        /// <summary>
        /// Applies one buoyancy / damping / wind step. Unity's FixedUpdate calls this for standalone
        /// ships; NetworkShip calls it from FixedUpdateNetwork on the authority (Fusion addon timing).
        /// </summary>
        public void Step(float deltaTime, float simTime)
        {
            if (rb == null || points == null || points.Length == 0)
                return;

            EnsureRuntimeBuffers();

            if (!_warnedPointCount && points.Length < minRecommendedPointCount)
            {
                _warnedPointCount = true;
                Debug.LogWarning($"[ShipBuoyancyController] '{name}' has only {points.Length} buoyancy points. Recommended: {minRecommendedPointCount}+.");
            }

            float dt = Mathf.Max(0.0001f, deltaTime);

            // Mass-aware scaling: buoyancy grows with weight (so the hull floats), and the linear
            // damping + per-point cap scale with it so a heavy ship stays critically damped.
            float effBuoyancy = ResolveBuoyancyForce();
            float massScale = autoBuoyancyFromMass ? Mathf.Clamp(rb.mass / Mathf.Max(1f, referenceMass), 0.1f, 50f) : 1f;
            float effMaxForcePerPoint = autoBuoyancyFromMass ? Mathf.Max(maxForcePerPoint, effBuoyancy * 3f) : maxForcePerPoint;

            _lastImpactStrength = 0f;
            _lastResultantForce = Vector3.zero;
            _lastResultantTorque = Vector3.zero;
            _lastSubmergedPoints = 0;
            _lastCenterOfMass = rb.worldCenterOfMass;
            _lastWindForce = Vector3.zero;
            _lastWindVelocity = Vector3.zero;
            _lastWindApplicationPoint = _lastCenterOfMass;

            Vector3 averageNormalAccum = Vector3.zero;
            float averageNormalWeight = 0f;

            OceanSeaStateProfile sea = oceanField.ActiveProfile;
            float buoyancyMul = sea != null ? sea.buoyancyMultiplier : 1f;
            float dragMul = sea != null ? sea.dragMultiplier : 1f;
            float impactMul = sea != null ? sea.waveImpactMultiplier : 1f;

            Vector3 up = Vector3.up;
            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;

            for (int i = 0; i < points.Length; i++)
            {
                ShipBuoyancyPoint point = points[i];
                Vector3 worldPoint = transform.TransformPoint(point.localPosition);
                OceanSample sample = oceanField.Sample(worldPoint, simTime);

                _lastWorldPoints[i] = worldPoint;
                _lastSurfaceNormals[i] = sample.surfaceNormal;

                float depth = sample.surfaceHeight - worldPoint.y + Mathf.Max(0.01f, point.radius);
                bool submerged = depth > 0f;

                float cooldown = _cooldowns[i] - dt;
                _cooldowns[i] = cooldown > 0f ? cooldown : 0f;

                if (!submerged)
                {
                    if (_wasSubmerged[i])
                        EmitContact(ShipWaterContactType.Exit, i, worldPoint, sample.surfaceNormal, Vector3.zero, 0f);

                    _wasSubmerged[i] = false;
                    _lastForces[i] = Vector3.zero;
                    _lastBuoyancyForces[i] = Vector3.zero;
                    _lastDampingForces[i] = Vector3.zero;
                    _lastSubmergence[i] = 0f;
                    continue;
                }

                float submergence = Mathf.Clamp01(depth / Mathf.Max(0.05f, point.maxSubmersionDepth));
                Vector3 pointVelocity = rb.GetPointVelocity(worldPoint);
                Vector3 relativeVelocity = pointVelocity - sample.waterVelocity;
                _lastSubmergence[i] = submergence;

                var forceInput = new ShipBuoyancyPointForceInput(
                    up,
                    fwd,
                    right,
                    sample.surfaceNormal,
                    relativeVelocity,
                    submergence,
                    effBuoyancy,
                    point.buoyancy,
                    buoyancyMul,
                    Mathf.Max(0f, point.damping),
                    verticalDamping,
                    lateralDrag,
                    longitudinalDrag,
                    currentRelativeDrag,
                    dragMul,
                    massScale,
                    effMaxForcePerPoint);

                ShipBuoyancyPointForceResult forceResult = ShipBuoyancyPointForceSolver.Solve(forceInput);
                Vector3 force = forceResult.TotalForce;
                Vector3 buoyancyComponent = forceResult.BuoyancyForce;
                Vector3 dampingComponent = forceResult.DampingForce;

                rb.AddForceAtPosition(force, worldPoint, ForceMode.Force);
                _lastForces[i] = force;
                _lastBuoyancyForces[i] = buoyancyComponent;
                _lastDampingForces[i] = dampingComponent;
                _lastResultantForce += force;
                _lastResultantTorque += Vector3.Cross(worldPoint - _lastCenterOfMass, force);
                _lastSubmergedPoints++;

                averageNormalAccum += sample.surfaceNormal * submergence;
                averageNormalWeight += submergence;

                if (!_wasSubmerged[i])
                    EmitContact(ShipWaterContactType.Enter, i, worldPoint, sample.surfaceNormal, relativeVelocity, 0f);

                float impactSpeed = Mathf.Max(0f, Vector3.Dot(-sample.surfaceNormal, relativeVelocity));
                if (point.emitContacts && impactSpeed > impactSpeedThreshold && _cooldowns[i] <= 0f)
                {
                    float strength = impactSpeed * impactMul;
                    EmitContact(ShipWaterContactType.Impact, i, worldPoint, sample.surfaceNormal, relativeVelocity, strength);
                    _cooldowns[i] = contactCooldown;
                    _lastImpactStrength = Mathf.Max(_lastImpactStrength, strength);
                }

                _wasSubmerged[i] = true;
            }

            _lastAverageSurfaceNormal = averageNormalWeight > 0.0001f
                ? (averageNormalAccum / averageNormalWeight).normalized
                : Vector3.up;

            Vector3 stabilizationTorque = ComputeStabilityTorque(dragMul, _lastAverageSurfaceNormal);
            if (stabilizationTorque.sqrMagnitude > 0.000001f)
            {
                rb.AddTorque(stabilizationTorque, ForceMode.Acceleration);
                _lastResultantTorque += stabilizationTorque;
            }

            Vector3 angularDampingTorque = -rb.angularVelocity * (angularDrag * dragMul);
            rb.AddTorque(angularDampingTorque, ForceMode.Acceleration);
            _lastResultantTorque += angularDampingTorque;

            ApplyWindForce(simTime);

            rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, maxShipSpeed);
            rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, maxAngularSpeed);

            _linearAcceleration = (rb.linearVelocity - _previousVelocity) / dt;
            _angularAcceleration = (rb.angularVelocity - _previousAngularVelocity) / dt;
            _previousVelocity = rb.linearVelocity;
            _previousAngularVelocity = rb.angularVelocity;
        }

        private void ApplyWindForce(float simulationTime)
        {
            if (windSystem == null)
            {
                if (Time.time >= _nextWindLookupTime)
                {
                    _nextWindLookupTime = Time.time + 1f;
                    windSystem = FindAnyObjectByType<OceanWindSystem>();
                }
                return;
            }

            Vector3 windVelocity = windSystem.EvaluateWind(_lastCenterOfMass, simulationTime);
            _lastWindVelocity = windVelocity;
            if (windVelocity.sqrMagnitude < 0.0001f)
                return;

            Vector3 windForce = ShipHullWindForceSolver.Solve(new ShipHullWindForceInput(
                windVelocity,
                rb.linearVelocity,
                transform.forward,
                transform.right,
                windLongitudinalFactor,
                windLateralFactor,
                windHullForceCoefficient,
                maxWindForce));

            if (windForce.sqrMagnitude < 0.0001f)
                return;

            Vector3 applicationPoint = _lastCenterOfMass + transform.TransformVector(windApplicationOffset);
            rb.AddForceAtPosition(windForce, applicationPoint, ForceMode.Force);

            _lastWindForce = windForce;
            _lastWindApplicationPoint = applicationPoint;
            _lastResultantForce += windForce;
            _lastResultantTorque += Vector3.Cross(applicationPoint - _lastCenterOfMass, windForce);
        }

        // Per-unit-buoyancy coefficient that makes total buoyancy at the equilibrium submersion equal
        // the hull weight × headroom — so the ship floats at any mass without re-tuning buoyancyForce.
        private float ResolveBuoyancyForce()
        {
            if (!autoBuoyancyFromMass || rb == null || points == null || points.Length == 0)
                return buoyancyForce;

            float g = Mathf.Max(0.01f, Mathf.Abs(Physics.gravity.y));
            float sumBuoy = 0f;
            for (int i = 0; i < points.Length; i++)
                sumBuoy += Mathf.Max(0f, points[i].buoyancy);
            if (sumBuoy < 0.001f)
                return buoyancyForce;

            float s = Mathf.Clamp(equilibriumSubmersion, 0.1f, 0.9f);
            return (rb.mass * g * Mathf.Max(1f, buoyancyHeadroom)) / (sumBuoy * s);
        }

        private Vector3 ComputeStabilityTorque(float dragMul, Vector3 averageSurfaceNormal)
        {
            if (rb == null || _lastSubmergedPoints <= 0)
                return Vector3.zero;

            return ShipStabilityTorqueSolver.Solve(new ShipStabilityTorqueInput(
                transform.rotation,
                transform.up,
                transform.right,
                rb.angularVelocity,
                averageSurfaceNormal,
                surfaceNormalInfluence,
                pitchStability,
                rollStability,
                pitchDamping,
                rollDamping,
                inversionRecoveryTorque,
                maxStabilityTorque,
                dragMul));
        }

        public Vector3 GetApparentForceAtDeckPoint(Vector3 worldPoint)
        {
            Vector3 r = worldPoint - rb.worldCenterOfMass;
            Vector3 angular = Vector3.Cross(_angularAcceleration, r) + Vector3.Cross(rb.angularVelocity, Vector3.Cross(rb.angularVelocity, r));
            return Physics.gravity - _linearAcceleration - angular;
        }

        private void EmitContact(ShipWaterContactType type, int pointIndex, Vector3 worldPoint, Vector3 normal, Vector3 relativeVelocity, float impact)
        {
            var handler = WaterContact;
            if (handler == null)
                return;

            ShipWaterContactEvent ev;
            ev.contactType = type;
            ev.pointIndex = pointIndex;
            ev.worldPoint = worldPoint;
            ev.surfaceNormal = normal;
            ev.relativeVelocity = relativeVelocity;
            ev.impactStrength = impact;

            handler.Invoke(ev);
        }

        private void EnsureRuntimeBuffers()
        {
            int count = points != null ? points.Length : 0;
            if (count <= 0)
                return;

            if (_wasSubmerged == null || _wasSubmerged.Length != count)
            {
                _wasSubmerged = new bool[count];
                _cooldowns = new float[count];
                _lastForces = new Vector3[count];
                _lastWorldPoints = new Vector3[count];
                _lastSurfaceNormals = new Vector3[count];
                _lastBuoyancyForces = new Vector3[count];
                _lastDampingForces = new Vector3[count];
                _lastSubmergence = new float[count];
            }
        }

        private static float NormalizeSigned(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (debugDrawAlways)
                DrawDebugGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            DrawDebugGizmos();
        }

        private void DrawDebugGizmos()
        {
            if (points == null)
                return;

            for (int i = 0; i < points.Length; i++)
            {
                Vector3 world = transform.TransformPoint(points[i].localPosition);
                if (_lastWorldPoints != null && i < _lastWorldPoints.Length && _lastWorldPoints[i] != Vector3.zero)
                    world = _lastWorldPoints[i];

                bool submerged = _lastSubmergence != null && i < _lastSubmergence.Length && _lastSubmergence[i] > 0.001f;
                Gizmos.color = submerged
                    ? new Color(0.15f, 0.95f, 1f, 0.95f)
                    : new Color(0.45f, 0.45f, 0.45f, 0.9f);
                Gizmos.DrawWireSphere(world, Mathf.Max(0.03f, points[i].radius));

                if (debugDrawNormals && _lastSurfaceNormals != null && i < _lastSurfaceNormals.Length)
                {
                    Vector3 n = _lastSurfaceNormals[i];
                    if (n.sqrMagnitude > 0.0001f)
                        DrawArrow(world, n.normalized * debugNormalScale, new Color(0.35f, 0.75f, 1f, 0.9f));
                }

                if (!debugDrawForces || _lastForces == null || i >= _lastForces.Length)
                    continue;

                if (_lastBuoyancyForces != null && i < _lastBuoyancyForces.Length)
                    DrawArrow(world, _lastBuoyancyForces[i] * debugForceScale, new Color(0.25f, 1f, 0.25f, 0.95f));

                if (_lastDampingForces != null && i < _lastDampingForces.Length)
                    DrawArrow(world, _lastDampingForces[i] * debugForceScale, new Color(1f, 0.6f, 0.15f, 0.95f));

                DrawArrow(world, _lastForces[i] * debugForceScale, new Color(1f, 1f, 1f, 0.95f));

                if (debugLabels)
                {
                    string name = string.IsNullOrEmpty(points[i].name) ? $"P{i}" : points[i].name;
                    float forceMag = _lastForces[i].magnitude;
                    float sub = _lastSubmergence != null && i < _lastSubmergence.Length ? _lastSubmergence[i] : 0f;
                    UnityEditor.Handles.Label(world + Vector3.up * 0.25f, $"{name}\\nF:{forceMag:0} N\\nSub:{sub:0.00}");
                }
            }

            if (rb != null && debugDrawResultants)
            {
                Vector3 com = _lastCenterOfMass != Vector3.zero ? _lastCenterOfMass : rb.worldCenterOfMass;
                Gizmos.color = new Color(1f, 0.35f, 0.95f, 0.95f);
                Gizmos.DrawSphere(com, 0.12f);

                DrawArrow(com, _lastResultantForce * debugResultantScale, new Color(1f, 0.25f, 0.95f, 0.95f));
                DrawArrow(com, _lastResultantTorque * debugTorqueScale, new Color(1f, 0.85f, 0.2f, 0.95f));

                if (debugDrawNormals)
                    DrawArrow(com, _lastAverageSurfaceNormal * debugNormalScale * 1.2f, new Color(0.3f, 0.8f, 1f, 0.9f));

                if (_lastWindForce.sqrMagnitude > 0.0001f)
                {
                    Vector3 windOrigin = _lastWindApplicationPoint;
                    DrawArrow(windOrigin, _lastWindForce * debugForceScale, new Color(1f, 0.8f, 0.2f, 0.95f));
                    DrawArrow(com, _lastWindVelocity * 0.2f, new Color(1f, 0.95f, 0.35f, 0.9f));
                }

                if (debugLabels)
                {
                    UnityEditor.Handles.Label(
                        com + Vector3.up * 0.4f,
                        $"Resultant F: {_lastResultantForce.magnitude:0} N\\nResultant T: {_lastResultantTorque.magnitude:0.00}\\nSubmerged points: {_lastSubmergedPoints}");
                }
            }
        }

        private static void DrawArrow(Vector3 origin, Vector3 vector, Color color)
        {
            if (vector.sqrMagnitude < 0.000001f)
                return;

            Gizmos.color = color;
            Vector3 end = origin + vector;
            Gizmos.DrawLine(origin, end);

            Vector3 dir = vector.normalized;
            float headLength = Mathf.Min(vector.magnitude * 0.2f, 0.45f);
            if (headLength < 0.001f)
                return;

            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.0001f)
                side = Vector3.Cross(dir, Vector3.right);
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir).normalized;

            float wing = headLength * 0.4f;
            Gizmos.DrawLine(end, end - dir * headLength + side * wing);
            Gizmos.DrawLine(end, end - dir * headLength - side * wing);
            Gizmos.DrawLine(end, end - dir * headLength + up * wing);
            Gizmos.DrawLine(end, end - dir * headLength - up * wing);
        }
#endif
    }
}

