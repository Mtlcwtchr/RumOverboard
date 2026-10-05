#if FUSION2
using System.Collections.Generic;
using Fusion;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Ocean.Features.Rigging;
using RumOverboard.Gameplay.Ocean;
using UnityEngine;

namespace RumOverboard.Networking
{
    /// <summary>
    /// Authoritative gameplay wrapper for the ship — the "world" its subsystems tick in.
    ///
    /// REPLICATION
    ///   • Pose + velocities: the Fusion Physics addon's <b>NetworkRigidbody3D</b> (host simulates the
    ///     one dynamic hull body; every peer renders it interpolated). Its interpolation target is
    ///     forced to NONE so the whole ship root — hull, masts, sails, colliders, stations — is
    ///     interpolated together, on the host as well as on clients.
    ///   • Derived values a proxy can't recompute: <see cref="LastImpactStrength"/>, <see cref="OceanTime"/>.
    ///   • Rig state: networked line arrays (value / holder / secured) — the storage behind every
    ///     <see cref="RigLine"/> (ECS-style: data components index into ship-owned arrays).
    ///
    /// HOST TICK ORDER (deterministic, all inside FixedUpdateNetwork):
    ///   ropes (line values) → apply lines to sails → sails + buoyancy (ShipRuntime).
    ///   The helm applies its rudder torque in its own FixedUpdateNetwork.
    ///
    /// SAFETY: a ship must be ONE dynamic Rigidbody. Nested dynamic bodies (e.g. a hull module that
    /// still carries its standalone Rigidbody/buoyancy) and the obsolete ShipSailSystem are stripped
    /// on spawn — they fight the hull body and are a major source of jitter.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkShip : NetworkBehaviour
    {
        public const int MaxLines = 16;

        [SerializeField] private ShipBuoyancyController buoyancy;
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private ShipSailsAggregator sailsAggregator;
        [SerializeField] private ShipFeatureAggregator featureAggregator;

        [Header("Crew spawns")]
        [Tooltip("Ship-local spawn points on deck. Auto-collected from children named 'CrewSpawn*' if empty.")]
        [SerializeField] private Transform[] crewSpawns;

        [Header("Ocean time correction (proxies)")]
        [SerializeField] private float oceanTimeCorrectionThreshold = 0.1f;
        [SerializeField] private float oceanTimeCorrectionGain = 0.1f;

        [Networked] public float LastImpactStrength { get; set; }
        [Networked] public float OceanTime { get; set; }

        // Running rigging: one slot per RigLine (data component) on this ship.
        [Networked, Capacity(MaxLines)] private NetworkArray<float> LineOut { get; }        // deck-side rope (m)
        [Networked, Capacity(MaxLines)] private NetworkArray<byte> LineMode { get; }        // RigLineMode
        [Networked, Capacity(MaxLines)] private NetworkArray<short> LinePin { get; }        // pin interactable index + 1
        [Networked, Capacity(MaxLines)] private NetworkArray<PlayerRef> LineHolders { get; }
        [Networked, Capacity(MaxLines)] private NetworkArray<Vector3> LineEnd { get; }      // ship-local free end

        private Rigidbody _rb;
        private ShipRuntime _shipRuntime;
        private readonly RigLine[] _lines = new RigLine[MaxLines];
        private readonly LineInput[] _input = new LineInput[MaxLines];      // host-only, per tick
        private readonly Vector3[] _endVelocity = new Vector3[MaxLines];    // host-only, loose-end sim (ship-local)

        [Header("Rigging")]
        [SerializeField] private RiggingConfig riggingConfig;

        public static readonly List<NetworkShip> All = new();

        public Rigidbody Body => _rb;
        public ShipSailsAggregator Sails => sailsAggregator;
        public float NetworkedImpactStrength => LastImpactStrength;

        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();
            StripConflictingSimulation();

            if (buoyancy == null)
                buoyancy = GetComponent<ShipBuoyancyController>();
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            if (sailsAggregator == null)
                sailsAggregator = GetComponent<ShipSailsAggregator>();
            if (sailsAggregator == null)
                sailsAggregator = gameObject.AddComponent<ShipSailsAggregator>();
            if (sailsAggregator.WindSystem == null)
                sailsAggregator.WindSystem = FindAnyObjectByType<OceanWindSystem>();

            if (featureAggregator == null)
                featureAggregator = GetComponent<ShipFeatureAggregator>();
            if (featureAggregator != null)
                featureAggregator.Bind(waveField, sailsAggregator.WindSystem);

            _shipRuntime = new ShipRuntime(buoyancy, sailsAggregator, waveField, _rb);
            _shipRuntime.Configure(HasStateAuthority);

            if (GetComponent<ShipWakeFoam>() == null)
                FitWakeFoam(gameObject.AddComponent<ShipWakeFoam>());

            if (TryGetComponent(out Fusion.Addons.Physics.NetworkRigidbody3D nrb))
            {
                // Interpolate the WHOLE ship root (see class doc) — never a visual sub-tree.
                nrb.InterpolationTarget = null;
                // Full-precision rotation: compressed quaternions quantise to ~0.0005 rad, which on a
                // 50 m hull is mm-to-cm of visible deck shimmer under the crew's feet.
                nrb.UsePreciseRotation = true;
            }
            else
            {
                Debug.LogError($"[NetworkShip] '{name}' has no NetworkRigidbody3D — the ship transform " +
                               "will NOT replicate. Run RumOverboard ▸ Ship Sandbox ▸ Prepare Ship + Player Prefabs.");
            }

            _rb.interpolation = RigidbodyInterpolation.None;
            if (!HasStateAuthority)
            {
                if (Object.IsInSimulation)
                    Runner.SetIsSimulated(Object, false);
                _rb.isKinematic = true; // proxies never simulate the hull
            }
            if (crewSpawns == null || crewSpawns.Length == 0)
                crewSpawns = CollectCrewSpawns();
            CollectLines();
            if (riggingConfig != null)
                RiggingConfig.Active = riggingConfig;

            if (HasStateAuthority)
            {
                if (waveField != null)
                    OceanTime = waveField.OceanTimeNow;
                ResetLines();
            }

            All.Add(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            RopeRenderSystem.Forget(this);
            InteractableIndex.Forget(Object);
        }

        // Bow/stern/beam of the actual hull from its solid colliders near the waterline.
        private void FitWakeFoam(ShipWakeFoam foam)
        {
            Bounds b = default;
            bool any = false;
            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                Bounds local = new Bounds(transform.InverseTransformPoint(c.bounds.center), Vector3.zero);
                local.Encapsulate(transform.InverseTransformPoint(c.bounds.min));
                local.Encapsulate(transform.InverseTransformPoint(c.bounds.max));
                if (!any) { b = local; any = true; } else b.Encapsulate(local);
            }
            if (!any || b.size.z < 12f)
                return; // small boat: script defaults fit
            float length = b.size.z;
            float scale = Mathf.Clamp(length / 9f, 1f, 4f) * 0.55f;
            foam.ConfigureHull(
                new Vector3(b.center.x, 0f, b.max.z - length * 0.16f),
                new Vector3(b.center.x, 0f, b.min.z + length * 0.08f),
                Mathf.Max(1.9f, b.extents.x * 0.75f), scale);
        }

        // ---------------------------------------------------------------------------------
        // Crew spawning
        // ---------------------------------------------------------------------------------

        /// <summary>World pose for crew spawn <paramref name="index"/> on this ship's deck.</summary>
        public bool TryGetCrewSpawn(int index, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (crewSpawns == null || crewSpawns.Length == 0)
                return false;
            Transform t = crewSpawns[Mathf.Abs(index) % crewSpawns.Length];
            if (t == null)
                return false;
            position = t.position;
            rotation = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
            return true;
        }

        private Transform[] CollectCrewSpawns()
        {
            var list = new List<Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("CrewSpawn"))
                    list.Add(t);
            return list.ToArray();
        }

        // ---------------------------------------------------------------------------------
        // Running rigging storage (RigLine data components index into these arrays)
        // ---------------------------------------------------------------------------------
        public float GetLineOut(int i) => Valid(i) ? LineOut.Get(i) : 0f;
        public RigLineMode GetLineMode(int i) => Valid(i) ? (RigLineMode)LineMode.Get(i) : RigLineMode.Tied;
        public PlayerRef GetLineHolder(int i) => Valid(i) ? LineHolders.Get(i) : PlayerRef.None;
        public int GetLinePinIndex(int i) => Valid(i) ? LinePin.Get(i) - 1 : -1;
        public Vector3 GetLineEndLocal(int i) => Valid(i) ? LineEnd.Get(i) : Vector3.zero;

        /// <summary>Line value 0..1 (sail setting) — derived from Out on every peer.</summary>
        public float GetLineValue(int i)
        {
            RigLine line = Line(i);
            return line != null ? line.ValueFromOut(GetLineOut(i)) : 0f;
        }

        public RigLine Line(int i) => Valid(i) ? _lines[i] : null;

        public BelayPin GetLinePin(int i)
        {
            int pin = GetLinePinIndex(i);
            if (pin < 0) return null;
            Interactable[] all = InteractableIndex.Get(Object);
            return pin < all.Length ? all[pin] as BelayPin : null;
        }

        /// <summary>Line currently made fast on <paramref name="pin"/>, or -1.</summary>
        public int LineTiedTo(BelayPin pin)
        {
            int pinIndex = InteractableIndex.IndexOf(Object, pin);
            for (int i = 0; i < MaxLines; i++)
                if (_lines[i] != null && GetLineMode(i) == RigLineMode.Tied && GetLinePinIndex(i) == pinIndex)
                    return i;
            return -1;
        }

        /// <summary>Line held by <paramref name="player"/> on this ship, or -1.</summary>
        public int LineHeldBy(PlayerRef player)
        {
            if (!player.IsRealPlayer) return -1;
            for (int i = 0; i < MaxLines; i++)
                if (_lines[i] != null && GetLineMode(i) == RigLineMode.Held && GetLineHolder(i) == player)
                    return i;
            return -1;
        }

        // --- Host operations (validated) ---------------------------------------------------
        /// <summary>Take a loose end, or cast a line off its pin, into <paramref name="player"/>'s hands.</summary>
        public bool TryTakeLine(int i, PlayerRef player)
        {
            if (!HasStateAuthority || Line(i) == null || !player.IsRealPlayer || LineHeldBy(player) >= 0)
                return false;
            if (GetLineMode(i) == RigLineMode.Held)
                return false;
            LineMode.Set(i, (byte)RigLineMode.Held);
            LineHolders.Set(i, player);
            LinePin.Set(i, 0);
            return true;
        }

        /// <summary>Make the held line fast on a pin (it must reach). Returns false if it can't.</summary>
        public bool TryTieLine(int i, PlayerRef player, BelayPin pin)
        {
            RigLine line = Line(i);
            if (!HasStateAuthority || line == null || pin == null || GetLineHolder(i) != player || LineTiedTo(pin) >= 0)
                return false;
            float reach = Vector3.Distance(line.Block.position, pin.TiePoint);
            float rope = GetLineOut(i);
            if (reach > rope + RiggingConfig.Active.TieSlack)
                return false;
            LineOut.Set(i, Mathf.Clamp(Mathf.Max(rope, reach), line.OutMin, line.OutMax));
            LineMode.Set(i, (byte)RigLineMode.Tied);
            LineHolders.Set(i, PlayerRef.None);
            LinePin.Set(i, (short)(InteractableIndex.IndexOf(Object, pin) + 1));
            LineEnd.Set(i, transform.InverseTransformPoint(pin.TiePoint));
            _input[i] = default;
            return true;
        }

        /// <summary>Let go of a held line: the end goes loose and the load starts running it out.</summary>
        public void DropLine(int i, PlayerRef player)
        {
            if (!HasStateAuthority || Line(i) == null || GetLineHolder(i) != player)
                return;
            LineMode.Set(i, (byte)RigLineMode.Loose);
            LineHolders.Set(i, PlayerRef.None);
            _endVelocity[i] = Vector3.zero;
            _input[i] = default;
        }

        public void SubmitLineInput(int i, PlayerRef player, bool haul, bool ease)
        {
            if (HasStateAuthority && Valid(i) && GetLineHolder(i) == player)
                _input[i] = new LineInput { Haul = haul, Ease = ease };
        }

        // --- Raw writes for RopeSystem / debug tools (host) ---------------------------------
        public void SetLineOut(int i, float outLength)
        {
            RigLine line = Line(i);
            if (HasStateAuthority && line != null)
                LineOut.Set(i, Mathf.Clamp(outLength, line.OutMin, line.OutMax));
        }

        public void SetLineEndLocal(int i, Vector3 local)
        {
            if (HasStateAuthority && Valid(i)) LineEnd.Set(i, local);
        }

        public LineInput ConsumeLineInput(int i)
        {
            if (!Valid(i)) return default;
            LineInput v = _input[i];
            _input[i] = default;
            return v;
        }

        public ref Vector3 EndVelocity(int i) => ref _endVelocity[i];

        /// <summary>Host: every line back on its home pin at its initial setting.</summary>
        public void ResetLines()
        {
            if (!HasStateAuthority) return;
            for (int i = 0; i < MaxLines; i++)
            {
                RigLine line = _lines[i];
                LineHolders.Set(i, PlayerRef.None);
                _endVelocity[i] = Vector3.zero;
                _input[i] = default;
                if (line == null)
                {
                    LineMode.Set(i, (byte)RigLineMode.Tied);
                    LinePin.Set(i, 0);
                    continue;
                }
                LineOut.Set(i, line.OutFromValue(line.InitialValue));
                if (line.HomePin != null)
                {
                    LineMode.Set(i, (byte)RigLineMode.Tied);
                    LinePin.Set(i, (short)(InteractableIndex.IndexOf(Object, line.HomePin) + 1));
                    LineEnd.Set(i, transform.InverseTransformPoint(line.HomePin.TiePoint));
                }
                else
                {
                    LineMode.Set(i, (byte)RigLineMode.Loose);
                    LinePin.Set(i, 0);
                    LineEnd.Set(i, transform.InverseTransformPoint(line.Block.position + Vector3.down * line.OutMin));
                }
            }
        }

        public struct LineInput
        {
            public bool Haul;
            public bool Ease;
        }

        private static bool Valid(int i) => i >= 0 && i < MaxLines;

        private void CollectLines()
        {
            System.Array.Clear(_lines, 0, _lines.Length);
            foreach (RigLine line in GetComponentsInChildren<RigLine>(true))
                if (Valid(line.LineIndex))
                    _lines[line.LineIndex] = line;
        }

        // ---------------------------------------------------------------------------------
        // Tick + render
        // ---------------------------------------------------------------------------------
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
            {
                _shipRuntime?.EnsureProxyState();
                if (waveField != null)
                {
                    float error = OceanTime - waveField.OceanTimeNow;
                    if (Mathf.Abs(error) > Mathf.Max(0.01f, oceanTimeCorrectionThreshold))
                        waveField.ApplyTimeCorrection(error * Mathf.Clamp01(oceanTimeCorrectionGain));
                }
                return;
            }

            float dt = Runner.DeltaTime;
            if (waveField != null)
                OceanTime = waveField.OceanTimeNow;

            RopeSystem.Tick(this, dt);
            RopeSystem.Apply(this, sailsAggregator);

            LastImpactStrength = (_shipRuntime != null && waveField != null)
                ? _shipRuntime.StepAuthority(dt, waveField.OceanTimeNow, Runner.SimulationTime)
                : 0f;
        }

        public override void Render()
        {
            if (!HasStateAuthority)
                RopeSystem.Apply(this, sailsAggregator); // cosmetic cloth on proxies follows the lines
        }

        private void LateUpdate()
        {
            // After every Render (NetworkRigidbody3D has placed the ship + players for this frame).
            if (Object != null && Object.IsValid)
                RopeRenderSystem.Render(this);
        }

        // ---------------------------------------------------------------------------------
        // Safety
        // ---------------------------------------------------------------------------------
        private void StripConflictingSimulation()
        {
            // Obsolete monolithic sail system: applies forces from FixedUpdate on every peer.
#pragma warning disable CS0618
            foreach (ShipSailSystem legacy in GetComponentsInChildren<ShipSailSystem>(true))
            {
                legacy.enabled = false;
                Destroy(legacy);
            }
#pragma warning restore CS0618

            // Nested dynamic bodies: the ship must be one rigid body. Their colliders then join the
            // root body's compound collider automatically.
            foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
            {
                if (body == _rb)
                    continue;
                foreach (ShipDeckMotionProvider d in body.GetComponents<ShipDeckMotionProvider>())
                    DestroyImmediate(d);
                // Anything that [RequireComponent(Rigidbody)] must go first or Unity refuses.
                foreach (MonoBehaviour mb in body.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    foreach (RequireComponent rc in mb.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                        if (rc.m_Type0 == typeof(Rigidbody) || rc.m_Type1 == typeof(Rigidbody) || rc.m_Type2 == typeof(Rigidbody))
                        {
                            DestroyImmediate(mb);
                            break;
                        }
                }
                Debug.LogWarning($"[NetworkShip] Removed nested Rigidbody on '{body.name}' (ship must be a single body). " +
                                 "Run RumOverboard ▸ Ship Sandbox ▸ Prepare Ship + Player Prefabs to fix the prefab.");
                DestroyImmediate(body);
            }
        }
    }
}
#endif
