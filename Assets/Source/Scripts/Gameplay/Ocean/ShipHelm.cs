#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Ocean.Features.Helm;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// The ship's wheel + rudder, host-authoritative. A crew member "takes the wheel" (NetworkPlayer
    /// occupies this helm) and steers with A/D. The wheel is a heavy body (see
    /// <see cref="HelmWheelPhysics"/>): A/D applies the helmsman's effort, the water on the rudder
    /// pushes back (harder the faster you go; current / waves / drift buffet it), so at speed the
    /// wheel is heavy, fights back to centre and can be torn from your hands; an UNMANNED wheel is
    /// spun by the sea. The rudder follows the wheel and produces a yaw force scaled by flow past it
    /// (you can only steer with way on, like a real ship).
    ///
    /// Networked: wheel angle + spin, rudder angle, load (for feedback), kick counter, occupant.
    /// The wheel MODEL is rotated on every peer from the replicated angle.
    /// </summary>
    public class ShipHelm : NetworkBehaviour
    {
        [Header("References")]
        [Tooltip("The wheel mesh that spins. Rotated on all peers from the networked wheel angle.")]
        [SerializeField] private Transform wheelModel;
        [Tooltip("Local axis the wheel spins around (the axle). Fore-aft on most ships.")]
        [SerializeField] private Vector3 wheelSpinAxis = Vector3.forward;
        [Tooltip("Where the helmsman stands. Falls back to this transform.")]
        [SerializeField] private Transform standAnchor;
        [Tooltip("If standAnchor is accidentally placed on the bow side of the wheel, mirror it to the aft side at runtime.")]
        [SerializeField] private bool autoFlipStandToAft = true;
        [Tooltip("Force the helmsman to face the ship's bow while steering.")]
        [SerializeField] private bool faceBow = true;
        [Tooltip("Rudder position in ship-local space (stern, underwater). Water flow is sampled here.")]
        [SerializeField] private Vector3 rudderLocal = new Vector3(0f, -0.8f, -4.6f);

        [Header("Wheel")]
        [Tooltip("Total wheel travel to each side (deg). 900 ≈ 2.5 turns lock-to-lock.")]
        [SerializeField] private float maxWheelDegrees = 900f;

        [Header("Rudder")]
        [SerializeField] private float maxRudderAngle = 35f;
        [Tooltip("How fast the rudder follows the wheel (fraction of full travel/sec).")]
        [SerializeField] private float rudderResponse = 2.5f;
        [Tooltip("Yaw force per unit (flow-speed × sin(rudder)). Tune to hull mass/inertia.")]
        [SerializeField] private float rudderYawCoefficient = 9000f;
        [Tooltip("Flip if A/D / wheel turn the ship the wrong way.")]
        [SerializeField] private bool invertSteering;

        [Header("Feel (wheel inertia, water on the rudder, helmsman strength)")]
        [SerializeField] private HelmFeelConfig feelConfig;

        [Networked] public float WheelAngle { get; set; }
        [Networked] public float WheelVelocity { get; set; }
        [Networked] public float RudderAngle { get; set; }
        [Networked] public PlayerRef Occupant { get; set; }
        /// <summary>Water's push on the wheel vs the helmsman's strength (signed; |x|≥1 = can't hold it).</summary>
        [Networked] public float Load { get; set; }
        /// <summary>Increments whenever the wheel kicks (hits a stop / torn from the hands) — feedback.</summary>
        [Networked] public byte KickCount { get; set; }

        private Rigidbody _rb;
        private OceanWaveField _waveField;
        private Quaternion _wheelBaseRotation;
        private float _pendingSteer; // host-only, set by the steering player each tick
        private float _renderAngle;
        private float _lateralMean; // host-only: slow average of side flow (steady drift)

        public bool IsOccupied => Occupant.IsRealPlayer;
        public float MaxWheelDegrees => maxWheelDegrees;
        public HelmFeelConfig Feel => feelConfig != null ? feelConfig : HelmFeelConfig.Active;

        /// <summary>Normalised wheel position, -1..1 (for HUD/debug).</summary>
        public float WheelNormalized => maxWheelDegrees > 0.01f ? Mathf.Clamp(WheelAngle / maxWheelDegrees, -1f, 1f) : 0f;

        public Vector3 StandPosition
        {
            get
            {
                Vector3 pos = standAnchor != null ? standAnchor.position : transform.position;
                if (!autoFlipStandToAft || wheelModel == null)
                    return pos;

                Vector3 wheelToStand = pos - wheelModel.position;
                wheelToStand.y = 0f;
                if (wheelToStand.sqrMagnitude < 0.0001f)
                    return pos;

                // Helmsman should stand on the aft side of the wheel (toward -ship forward).
                if (Vector3.Dot(wheelToStand, transform.forward) > 0f)
                    return wheelModel.position - wheelToStand;

                return pos;
            }
        }

        /// <summary>Yaw (deg) the helmsman should face — toward the wheel from the stand.</summary>
        public float FacingYaw
        {
            get
            {
                if (faceBow)
                    return transform.eulerAngles.y;

                if (wheelModel != null)
                {
                    Vector3 d = wheelModel.position - StandPosition;
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.0001f)
                        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                }
                return standAnchor != null ? standAnchor.eulerAngles.y : transform.eulerAngles.y;
            }
        }

        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();
            _waveField = FindAnyObjectByType<OceanWaveField>();
            if (wheelModel != null)
                _wheelBaseRotation = wheelModel.localRotation;
            if (feelConfig != null)
                HelmFeelConfig.Active = feelConfig;
            _renderAngle = WheelAngle;
        }

        // --- Host API (called by the occupying NetworkPlayer on the state authority) ---

        public bool TryOccupy(PlayerRef player)
        {
            if (!HasStateAuthority || !player.IsRealPlayer)
                return false;
            if (IsOccupied && Occupant != player)
                return false;
            Occupant = player;
            return true;
        }

        public void Release(PlayerRef player)
        {
            if (!HasStateAuthority)
                return;
            if (Occupant == player)
            {
                Occupant = PlayerRef.None;
                _pendingSteer = 0f;
            }
        }

        /// <summary>Steering signal from the helmsman this tick (A/D → -1..1).</summary>
        public void SubmitSteer(float steer)
        {
            if (HasStateAuthority)
                _pendingSteer = Mathf.Clamp(steer, -1f, 1f);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || _rb == null)
                return;

            float dt = Runner.DeltaTime;

            // Water flow past the rudder (ship's own motion + current + wave orbital velocity).
            Vector3 rudderPos = transform.TransformPoint(rudderLocal);
            Vector3 waterVel = _waveField != null
                ? _waveField.Sample(rudderPos, _waveField.OceanTimeNow).waterVelocity
                : Vector3.zero;
            Vector3 flow = waterVel - _rb.GetPointVelocity(rudderPos);
            float forwardFlow = Vector3.Dot(flow, -transform.forward); // >0 when making way ahead
            float lateralFlow = Vector3.Dot(flow, transform.right);     // sideways buffeting

            // Steady leeway / current just loads the rudder evenly — only the CHANGING part (waves,
            // gusts, yawing) buffets the wheel, so a drifting ship doesn't wind it onto its stop.
            _lateralMean = Mathf.Lerp(_lateralMean, lateralFlow, 1f - Mathf.Exp(-dt / 3f));
            lateralFlow -= _lateralMean;

            var state = new HelmWheelState { Wheel = WheelAngle, Velocity = WheelVelocity, Rudder = RudderAngle };
            var geometry = new HelmWheelGeometry(maxWheelDegrees, maxRudderAngle, rudderResponse, rudderYawCoefficient, invertSteering);
            HelmWheelResult step = HelmWheelPhysics.Step(ref state, _pendingSteer, IsOccupied, forwardFlow, lateralFlow,
                Runner.SimulationTime, dt, geometry, Feel);

            WheelAngle = state.Wheel;
            WheelVelocity = state.Velocity;
            RudderAngle = state.Rudder;
            Load = step.Load;
            if (step.Kick)
                unchecked { KickCount++; }
            _rb.AddTorque(Vector3.up * step.YawTorque, ForceMode.Force);

            _pendingSteer = 0f;
        }

        public override void Render()
        {
            if (wheelModel == null)
                return;
            // Networked angle steps at tick rate; extrapolate with the spin so a fast wheel turns smoothly.
            float target = WheelAngle;
            float predicted = _renderAngle + WheelVelocity * Time.deltaTime;
            _renderAngle = Mathf.Abs(predicted - target) < 25f
                ? Mathf.Lerp(predicted, target, 1f - Mathf.Exp(-20f * Time.deltaTime))
                : target;
            wheelModel.localRotation = _wheelBaseRotation * Quaternion.AngleAxis(_renderAngle, wheelSpinAxis);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Vector3 rudderPos = transform.TransformPoint(rudderLocal);
            Gizmos.DrawWireSphere(rudderPos, 0.4f);
            Gizmos.DrawLine(rudderPos, rudderPos + transform.forward * 1.5f);
            if (standAnchor != null)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
                Gizmos.DrawWireCube(standAnchor.position, Vector3.one * 0.4f);

                Gizmos.color = new Color(0.35f, 1f, 0.55f, 0.95f);
                Gizmos.DrawWireCube(StandPosition, Vector3.one * 0.3f);
                Gizmos.DrawLine(StandPosition, StandPosition + Quaternion.Euler(0f, FacingYaw, 0f) * Vector3.forward * 0.8f);
            }
        }
#endif
    }
}
#endif
