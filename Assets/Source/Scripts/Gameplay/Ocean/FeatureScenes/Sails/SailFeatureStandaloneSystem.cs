using System;
using System.Collections.Generic;
using RumOverboard.Gameplay.Ocean.Simulation;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes.Sails
{
    /// <summary>
    /// Standalone static-mast sail simulator for feature-scene validation.
    /// Masts do not bend or move; wind/load/stress are shown via gizmos and debug UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SailFeatureStandaloneSystem : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanWindSystem windSystem;

        [Header("Aerodynamics")]
        [SerializeField] private float airDensity = 1.225f;
        [SerializeField] private float sailForceScale = 1f;
        [SerializeField] private float maxForcePerSail = 6000f;
        [SerializeField] private float liftFactor = 1.1f;
        [SerializeField] private float dragFactor = 1f;
        [SerializeField] private float sideForceFactor = 0.6f;
        [SerializeField] private float reverseDriveFactor = 0.12f;
        [SerializeField] private float verticalForceFactor = 0.08f;
        [SerializeField] private bool blockReverseDriveFromHeadwind = true;

        [Header("Mast setup")]
        [SerializeField] private List<SailMastFeatureEntry> mastEntries = new List<SailMastFeatureEntry>();

        [Header("Debug")]
        [SerializeField] private bool drawDebug = true;
        [SerializeField] private float debugVectorScale = 0.04f;

        private readonly List<MastRuntime> _mastRuntimes = new List<MastRuntime>();
        private Transform _rigRoot;

        public float AirDensity { get => airDensity; set => airDensity = Mathf.Max(0f, value); }
        public float SailForceScale { get => sailForceScale; set => sailForceScale = Mathf.Max(0f, value); }
        public float MaxForcePerSail { get => maxForcePerSail; set => maxForcePerSail = Mathf.Max(0f, value); }
        public float LiftFactor { get => liftFactor; set => liftFactor = Mathf.Clamp(value, 0f, 2f); }
        public float DragFactor { get => dragFactor; set => dragFactor = Mathf.Clamp(value, 0f, 2f); }
        public float SideForceFactor { get => sideForceFactor; set => sideForceFactor = Mathf.Clamp01(value); }
        public float ReverseDriveFactor { get => reverseDriveFactor; set => reverseDriveFactor = Mathf.Clamp01(value); }
        public float VerticalForceFactor { get => verticalForceFactor; set => verticalForceFactor = Mathf.Clamp01(value); }
        public bool BlockReverseDriveFromHeadwind { get => blockReverseDriveFromHeadwind; set => blockReverseDriveFromHeadwind = value; }

        public Vector3 ResultantForce { get; private set; }
        public Vector3 ResultantTorque { get; private set; }
        public Vector3 LastWind { get; private set; }
        public int MastCount => mastEntries != null ? mastEntries.Count : 0;

        public void Configure(OceanWaveField waves, OceanWindSystem wind)
        {
            waveField = waves;
            windSystem = wind;
        }

        public List<SailMastFeatureEntry> ExportMastEntries()
        {
            var copy = new List<SailMastFeatureEntry>(mastEntries.Count);
            for (int i = 0; i < mastEntries.Count; i++)
                copy.Add(CloneEntry(mastEntries[i]));
            return copy;
        }

        public void ImportMastEntries(List<SailMastFeatureEntry> entries)
        {
            mastEntries.Clear();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                    mastEntries.Add(CloneEntry(entries[i]));
            }

            EnsureDefaultMasts();
            RebuildRig();
        }

        public SailMastFeatureEntry GetMastEntry(int index)
        {
            if (index < 0 || index >= mastEntries.Count)
                return null;
            return mastEntries[index];
        }

        public void AddMast(SailMastRigType type)
        {
            var entry = new SailMastFeatureEntry
            {
                mastName = "Mast " + (mastEntries.Count + 1),
                rigType = type,
                localPosition = new Vector3(0f, 0f, -mastEntries.Count * 2.8f),
                mastHeight = 8f,
                mastRadius = 0.2f,
                areaScale = 1f,
            };
            mastEntries.Add(entry);
            RebuildRig();
        }

        public void RemoveLastMast()
        {
            if (mastEntries.Count <= 1)
                return;
            mastEntries.RemoveAt(mastEntries.Count - 1);
            RebuildRig();
        }

        public void CycleMastType(int index)
        {
            if (index < 0 || index >= mastEntries.Count)
                return;
            mastEntries[index].rigType = (SailMastRigType)(((int)mastEntries[index].rigType + 1) % 3);
            RebuildRig();
        }

        public void RequestRigRebuild()
        {
            RebuildRig();
        }

        private void Awake()
        {
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            if (windSystem == null)
                windSystem = FindAnyObjectByType<OceanWindSystem>();

            EnsureDefaultMasts();
            RebuildRig();
        }

        private void OnValidate()
        {
            EnsureDefaultMasts();
            if (Application.isPlaying)
                RebuildRig();
        }

        private void FixedUpdate()
        {
            float simTime = waveField != null ? waveField.OceanTimeNow : Time.time;
            LastWind = windSystem != null ? windSystem.EvaluateWind(transform.position, simTime) : Vector3.zero;
            ResultantForce = Vector3.zero;
            ResultantTorque = Vector3.zero;

            for (int i = 0; i < _mastRuntimes.Count; i++)
            {
                MastRuntime mast = _mastRuntimes[i];
                mast.totalForce = Vector3.zero;
                mast.currentMoment = 0f;

                for (int p = 0; p < mast.panels.Count; p++)
                {
                    SailPanelRuntime panel = mast.panels[p];
                    Vector3 panelPos = panel.visual.position;
                    Vector3 waterCurrent = waveField != null
                        ? waveField.Sample(panelPos, simTime).currentVelocity
                        : Vector3.zero;
                    Vector3 apparentWind = LastWind + waterCurrent;

                    SailAerodynamicsResult aero = SailAerodynamicsSolver.Solve(new SailAerodynamicsInput(
                        apparentWind,
                        panel.visual.right,
                        transform.forward,
                        panel.area,
                        airDensity,
                        sailForceScale,
                        maxForcePerSail,
                        liftFactor,
                        dragFactor,
                        sideForceFactor,
                        reverseDriveFactor,
                        verticalForceFactor,
                        blockReverseDriveFromHeadwind));

                    panel.lastForce = aero.DriveForce;
                    panel.lastRigForce = aero.RigForce;
                    mast.totalForce += aero.RigForce;
                    mast.currentMoment += MastStressSolver.ComputeMomentContribution(
                        mast.baseWorld,
                        mast.topWorld,
                        panel.attachHeight01,
                        aero.RigForce);

                    ResultantForce += aero.DriveForce;
                    ResultantTorque += Vector3.Cross(panelPos - transform.position, aero.DriveForce);
                }

                mast.stressRatio = MastStressSolver.ComputeStressRatio(mast.currentMoment, mast.maxMoment);
            }
        }

        private void RebuildRig()
        {
            EnsureRigRoot();
            ClearRigChildren();
            _mastRuntimes.Clear();

            for (int i = 0; i < mastEntries.Count; i++)
            {
                SailMastFeatureEntry entry = mastEntries[i];
                Transform mastRoot = new GameObject(string.IsNullOrWhiteSpace(entry.mastName) ? "Mast" : entry.mastName).transform;
                mastRoot.SetParent(_rigRoot, false);
                mastRoot.localPosition = entry.localPosition;
                mastRoot.localRotation = Quaternion.Euler(0f, entry.yawDegrees, 0f);

                var mastVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mastVisual.name = "MastVisual";
                mastVisual.transform.SetParent(mastRoot, false);
                mastVisual.transform.localScale = new Vector3(entry.mastRadius, entry.mastHeight * 0.5f, entry.mastRadius);
                mastVisual.transform.localPosition = new Vector3(0f, entry.mastHeight * 0.5f, 0f);

                var runtime = new MastRuntime
                {
                    entry = entry,
                    root = mastRoot,
                    baseWorld = mastRoot.position,
                    topWorld = mastRoot.position + mastRoot.up * entry.mastHeight,
                    maxMoment = MastStressSolver.ComputeMaxBendingMoment(entry.mastRadius, entry.yieldStrength),
                };

                BuildSailsForMast(runtime);
                _mastRuntimes.Add(runtime);
            }
        }

        private void BuildSailsForMast(MastRuntime mast)
        {
            switch (mast.entry.rigType)
            {
                case SailMastRigType.SingleSail:
                    CreatePanel(mast, "SingleSail", 0.55f, new Vector2(2.2f, 2.7f), false);
                    break;
                case SailMastRigType.DoubleSail:
                    CreatePanel(mast, "LowerSail", 0.42f, new Vector2(2.8f, 2.4f), false);
                    CreatePanel(mast, "UpperSail", 0.72f, new Vector2(2.3f, 1.9f), false);
                    break;
                case SailMastRigType.Triangular:
                    CreatePanel(mast, "TriSail", 0.58f, new Vector2(3.0f, 2.6f), true);
                    break;
            }
        }

        private void CreatePanel(MastRuntime mast, string name, float height01, Vector2 size, bool triangular)
        {
            var sail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sail.name = name;
            sail.transform.SetParent(mast.root, false);
            sail.transform.localPosition = new Vector3(0f, mast.entry.mastHeight * height01, 0f);
            sail.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            float width = size.x * mast.entry.areaScale;
            float height = size.y * mast.entry.areaScale;
            sail.transform.localScale = new Vector3(0.06f, height, width);

            if (triangular)
            {
                // Triangular rig is visualized by tapering one side through scale + offset.
                sail.transform.localScale = new Vector3(0.06f, height, width * 0.72f);
                sail.transform.localPosition += new Vector3(width * 0.12f, -height * 0.12f, 0f);
            }

            var panel = new SailPanelRuntime
            {
                visual = sail.transform,
                area = width * height * (triangular ? 0.6f : 1f),
                attachHeight01 = Mathf.Clamp01(height01),
            };

            mast.panels.Add(panel);
        }

        private void EnsureRigRoot()
        {
            if (_rigRoot != null)
                return;

            Transform existing = transform.Find("SailFeatureRig");
            if (existing != null)
            {
                _rigRoot = existing;
                return;
            }

            _rigRoot = new GameObject("SailFeatureRig").transform;
            _rigRoot.SetParent(transform, false);
        }

        private void ClearRigChildren()
        {
            if (_rigRoot == null)
                return;

            for (int i = _rigRoot.childCount - 1; i >= 0; i--)
            {
                Transform c = _rigRoot.GetChild(i);
                if (Application.isPlaying)
                    Destroy(c.gameObject);
                else
                    DestroyImmediate(c.gameObject);
            }
        }

        private void EnsureDefaultMasts()
        {
            if (mastEntries != null && mastEntries.Count > 0)
                return;

            mastEntries = new List<SailMastFeatureEntry>
            {
                new SailMastFeatureEntry { mastName = "Fore", rigType = SailMastRigType.DoubleSail, localPosition = new Vector3(0f, 0f, 3.6f), mastHeight = 9.5f },
                new SailMastFeatureEntry { mastName = "Main", rigType = SailMastRigType.DoubleSail, localPosition = new Vector3(0f, 0f, -0.6f), mastHeight = 10.8f },
                new SailMastFeatureEntry { mastName = "Mizzen", rigType = SailMastRigType.Triangular, localPosition = new Vector3(0f, 0f, -4.2f), mastHeight = 8.2f },
            };
        }

        private static SailMastFeatureEntry CloneEntry(SailMastFeatureEntry src)
        {
            return new SailMastFeatureEntry
            {
                mastName = src.mastName,
                rigType = src.rigType,
                localPosition = src.localPosition,
                yawDegrees = src.yawDegrees,
                mastHeight = src.mastHeight,
                mastRadius = src.mastRadius,
                areaScale = src.areaScale,
                yieldStrength = src.yieldStrength,
            };
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawDebug)
                return;

            float scale = Mathf.Max(0.001f, debugVectorScale);

            for (int i = 0; i < _mastRuntimes.Count; i++)
            {
                MastRuntime mast = _mastRuntimes[i];
                Color mastColor = Color.Lerp(new Color(0.2f, 1f, 0.35f, 0.95f), new Color(1f, 0.25f, 0.2f, 0.95f), Mathf.Clamp01(mast.stressRatio));
                Gizmos.color = mastColor;
                Gizmos.DrawLine(mast.baseWorld, mast.topWorld);
                Gizmos.DrawWireSphere(mast.topWorld, Mathf.Max(0.05f, mast.entry.mastRadius * 1.2f));

                Vector3 mastCenter = Vector3.Lerp(mast.baseWorld, mast.topWorld, 0.6f);
                Gizmos.color = new Color(1f, 0.72f, 0.22f, 0.9f);
                Gizmos.DrawLine(mastCenter, mastCenter + mast.totalForce * scale);

                for (int p = 0; p < mast.panels.Count; p++)
                {
                    SailPanelRuntime panel = mast.panels[p];
                    Vector3 pos = panel.visual.position;

                    Gizmos.color = new Color(0.3f, 1f, 0.55f, 0.95f);
                    Gizmos.DrawLine(pos, pos + panel.lastForce * scale);

                    Gizmos.color = new Color(1f, 0.95f, 0.3f, 0.85f);
                    Gizmos.DrawLine(pos, pos + LastWind * (scale * 0.2f));
                }
            }

            Gizmos.color = new Color(1f, 0.2f, 0.9f, 0.95f);
            Gizmos.DrawLine(transform.position, transform.position + ResultantForce * scale);

            Gizmos.color = new Color(0.25f, 0.8f, 1f, 0.95f);
            Gizmos.DrawLine(transform.position, transform.position + ResultantTorque * (scale * 0.07f));
        }
#endif

        private sealed class MastRuntime
        {
            public SailMastFeatureEntry entry;
            public Transform root;
            public Vector3 baseWorld;
            public Vector3 topWorld;
            public float maxMoment;
            public float currentMoment;
            public float stressRatio;
            public Vector3 totalForce;
            public readonly List<SailPanelRuntime> panels = new List<SailPanelRuntime>();
        }

        private sealed class SailPanelRuntime
        {
            public Transform visual;
            public float area;
            public float attachHeight01;
            public Vector3 lastForce;
            public Vector3 lastRigForce;
        }
    }
}

