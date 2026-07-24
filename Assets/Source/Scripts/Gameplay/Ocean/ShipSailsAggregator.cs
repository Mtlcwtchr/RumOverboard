using System.Collections.Generic;
using RumOverboard.Gameplay.Ocean.Features.Masts;
using UnityEngine;
#if FUSION2
using RumOverboard.Networking;
#endif

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Ship-level sails aggregator. Discovers all <see cref="ShipMast"/> children,
    /// holds shared aerodynamic settings, drives simulation for all masts,
    /// and exposes unified API for debug/editor/network layers.
    /// Replaces the monolithic ShipSailSystem.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ShipSailsAggregator : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private Rigidbody shipBody;
        [SerializeField] private OceanWindSystem windSystem;

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
        [SerializeField] private bool simulateVisualsOnProxies = true;

        [Header("Debug")]
        [SerializeField] private bool debugDrawForces;
        [SerializeField] private bool debugDrawMastStress;
        [SerializeField] private bool debugDrawCloth;
        [SerializeField] private float debugForceScale = 0.001f;

        #endregion

        #region Private State

        private readonly List<ShipMast> _masts = new List<ShipMast>();
        private bool _externallyDriven;

        #endregion

        #region Public API

        public OceanWindSystem WindSystem
        {
            get => windSystem;
            set => windSystem = value;
        }

        public IReadOnlyList<ShipMast> Masts => _masts;
        public int MastCount => _masts.Count;

        /// <summary>Total number of sails across all masts.</summary>
        public int SailCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _masts.Count; i++)
                    total += _masts[i].SailCount;
                return total;
            }
        }

        public float AirDensity { get => airDensity; set => airDensity = Mathf.Max(0f, value); }
        public float SailForceScale { get => sailForceScale; set => sailForceScale = Mathf.Max(0f, value); }
        public float MaxForcePerSail { get => maxForcePerSail; set => maxForcePerSail = Mathf.Max(0f, value); }
        public float LiftFactor { get => liftFactor; set => liftFactor = Mathf.Clamp(value, 0f, 2f); }
        public float DragFactor { get => dragFactor; set => dragFactor = Mathf.Clamp(value, 0f, 2f); }
        public float SideForceFactor { get => sideForceFactor; set => sideForceFactor = Mathf.Clamp01(value); }
        public float ReverseDriveFactor { get => reverseDriveFactor; set => reverseDriveFactor = Mathf.Clamp01(value); }
        public float VerticalForceFactor { get => verticalForceFactor; set => verticalForceFactor = Mathf.Clamp01(value); }

        public bool DebugDrawForces { get => debugDrawForces; set => debugDrawForces = value; }
        public bool DebugDrawRopes { get => debugDrawCloth; set => debugDrawCloth = value; }

        /// <summary>Net wind thrust across all masts (N).</summary>
        public Vector3 TotalThrust
        {
            get
            {
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < _masts.Count; i++)
                    sum += _masts[i].TotalDriveForce;
                return sum;
            }
        }

        /// <summary>Get mast by index.</summary>
        public ShipMast GetMast(int index)
        {
            if (index < 0 || index >= _masts.Count) return null;
            return _masts[index];
        }

        /// <summary>
        /// Get stress ratio for a mast. 0 = no stress, 1 = breaking point.
        /// </summary>
        public float GetMastStressRatio(int mastIndex)
        {
            if (mastIndex < 0 || mastIndex >= _masts.Count) return 0f;
            return _masts[mastIndex].StressRatio;
        }

        public float GetMastBendingMoment(int mastIndex)
        {
            if (mastIndex < 0 || mastIndex >= _masts.Count) return 0f;
            return _masts[mastIndex].CurrentBendingMoment;
        }

        public float GetMastMaxBendingMoment(int mastIndex)
        {
            if (mastIndex < 0 || mastIndex >= _masts.Count) return 0f;
            return _masts[mastIndex].MaxBendingMoment;
        }

        public Vector3 GetMastForce(int mastIndex)
        {
            if (mastIndex < 0 || mastIndex >= _masts.Count) return Vector3.zero;
            return _masts[mastIndex].TotalForceOnMast;
        }

        /// <summary>
        /// Flat-index sail API: resolve (mastIndex, sailIndex) from a global index
        /// across all masts, for backwards-compatible per-sail controls.
        /// </summary>
        public bool TryGetFlatSail(int flatIndex, out ShipMast outMast, out int outSailIndex)
        {
            outMast = null;
            outSailIndex = 0;
            int offset = 0;
            for (int m = 0; m < _masts.Count; m++)
            {
                int count = _masts[m].SailCount;
                if (flatIndex < offset + count)
                {
                    outMast = _masts[m];
                    outSailIndex = flatIndex - offset;
                    return true;
                }
                offset += count;
            }
            return false;
        }

        // Flat-index convenience wrappers
        public string GetSailName(int flat) => TryGetFlatSail(flat, out var m, out int s) ? m.GetSailName(s) : $"Sail {flat}";
        public bool GetSailEnabled(int flat) => TryGetFlatSail(flat, out var m, out int s) && m.GetSailEnabled(s);
        public void SetSailEnabled(int flat, bool v) { if (TryGetFlatSail(flat, out var m, out int s)) m.SetSailEnabled(s, v); }
        public float GetHoist(int flat) => TryGetFlatSail(flat, out var m, out int s) ? m.GetHoist(s) : 0f;
        public float GetExtension(int flat) => TryGetFlatSail(flat, out var m, out int s) ? m.GetExtension(s) : 0f;
        public float GetSheetAngle(int flat) => TryGetFlatSail(flat, out var m, out int s) ? m.GetSheetAngle(s) : 0f;
        public float GetDamage(int flat) => TryGetFlatSail(flat, out var m, out int s) ? m.GetDamage(s) : 0f;
        public float GetEffectiveArea(int flat) => TryGetFlatSail(flat, out var m, out int s) ? m.GetEffectiveArea(s) : 0f;
        public Vector3 GetSailForce(int flat) => TryGetFlatSail(flat, out var m, out int s) ? m.GetSailForce(s) : Vector3.zero;
        public Vector3 GetSailForcePoint(int flat) => TryGetFlatSail(flat, out var m, out int s) ? m.GetSailForcePoint(s) : transform.position;

        public void SetHoist(int flat, float v) { if (TryGetFlatSail(flat, out var m, out int s)) m.SetHoist(s, v); }
        public void SetExtension(int flat, float v) { if (TryGetFlatSail(flat, out var m, out int s)) m.SetExtension(s, v); }
        public void SetSheetAngle(int flat, float v) { if (TryGetFlatSail(flat, out var m, out int s)) m.SetSheetAngle(s, v); }
        public void SetDamage(int flat, float v) { if (TryGetFlatSail(flat, out var m, out int s)) m.SetDamage(s, v); }

        public void SetExternallyDriven(bool externallyDriven)
        {
            _externallyDriven = externallyDriven;
        }

        /// <summary>
        /// Force re-discovery of ShipMast children.
        /// </summary>
        public void RefreshMasts()
        {
            GatherMasts();
        }

        #endregion

        #region Lifecycle

        private void Awake()
        {
            if (shipBody == null) shipBody = GetComponent<Rigidbody>();
            if (windSystem == null) windSystem = FindAnyObjectByType<OceanWindSystem>();
            GatherMasts();
        }

        private void OnValidate()
        {
            airDensity = Mathf.Max(0f, airDensity);
            sailForceScale = Mathf.Max(0f, sailForceScale);
            maxForcePerSail = Mathf.Max(0f, maxForcePerSail);
            debugForceScale = Mathf.Max(0f, debugForceScale);
            timeStep = Mathf.Max(0.001f, timeStep);
        }

        private void FixedUpdate()
        {
            if (_externallyDriven)
                return;

            Step(Time.fixedDeltaTime, Time.time);
        }

        /// <summary>
        /// External simulation entry point used by network authority.
        /// </summary>
        public void Step(float deltaTime, float simulationTime)
        {
            if (shipBody == null || _masts.Count == 0)
                return;

            bool applyForces = ShouldApplyForcesForThisPeer();
            if (!applyForces && !simulateVisualsOnProxies)
                return;

            if (windSystem == null)
                windSystem = FindAnyObjectByType<OceanWindSystem>();

            Vector3 shipWorldPos = shipBody.worldCenterOfMass;
            Vector3 trueWind = windSystem != null ? windSystem.EvaluateWind(shipWorldPos, simulationTime) : Vector3.zero;

            for (int i = 0; i < _masts.Count; i++)
            {
                _masts[i].Step(
                    deltaTime, simulationTime, trueWind, applyForces,
                    airDensity, sailForceScale, maxForcePerSail,
                    liftFactor, dragFactor, sideForceFactor,
                    reverseDriveFactor, verticalForceFactor,
                    blockReverseDriveFromHeadwind,
                    substeps, timeStep);
            }
        }

        #endregion

        #region Internal

        private void GatherMasts()
        {
            _masts.Clear();
            GetComponentsInChildren(true, _masts);

            // Bind ship references to each mast
            for (int i = 0; i < _masts.Count; i++)
                _masts[i].Bind(shipBody, transform);
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
            if (_masts == null || _masts.Count == 0) return;

            if (debugDrawForces)
            {
                for (int m = 0; m < _masts.Count; m++)
                {
                    var mast = _masts[m];
                    for (int s = 0; s < mast.SailCount; s++)
                    {
                        Vector3 f = mast.GetSailForce(s);
                        if (f.sqrMagnitude < 0.001f) continue;
                        Vector3 p = mast.GetSailForcePoint(s);
                        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.95f);
                        Gizmos.DrawLine(p, p + f * debugForceScale);
                    }
                }
            }

            if (debugDrawMastStress)
            {
                for (int i = 0; i < _masts.Count; i++)
                {
                    var mast = _masts[i];
                    var def = mast.Mast;
                    Vector3 baseW = transform.TransformPoint(def.baseLocal);
                    Vector3 topW = transform.TransformPoint(def.topLocal);

                    float stress = Mathf.Clamp01(def.stressRatio);
                    Gizmos.color = Color.Lerp(Color.green, Color.red, stress);
                    Gizmos.DrawLine(baseW, topW);
                    Gizmos.DrawWireSphere(topW, def.radius * 2f);

                    if (def.totalForceOnMast.sqrMagnitude > 0.01f)
                    {
                        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
                        Vector3 fp = Vector3.Lerp(baseW, topW, 0.6f);
                        Gizmos.DrawLine(fp, fp + def.totalForceOnMast * debugForceScale);
                    }
                }
            }
        }
#endif

        #endregion
    }
}

