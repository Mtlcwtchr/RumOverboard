#if FUSION2
using Fusion;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// The ship's wheel + rudder, host-authoritative. A crew member "takes the wheel" (NetworkPlayer
    /// occupies this helm) and steers with A/D; the host turns the wheel, the wheel sets the rudder,
    /// and the rudder — a hydrofoil at the stern — produces a yaw force scaled by how fast water
    /// flows past it (so you can only steer with way on, like a real ship). Waves and current push
    /// the rudder sideways: with a helmsman it just buffets the handling, but an UNMANNED wheel gets
    /// spun by the sea and slowly trails back to centre.
    ///
    /// Networked surface is tiny: wheel angle, rudder angle, and who's steering. The wheel MODEL is
    /// rotated on every peer from the replicated angle.
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
        [Tooltip("Rudder position in ship-local space (stern, underwater). Water flow is sampled here.")]
        [SerializeField] private Vector3 rudderLocal = new Vector3(0f, -0.8f, -4.6f);

        [Header("Wheel")]
        [Tooltip("Total wheel travel to each side (deg). 900 ≈ 2.5 turns lock-to-lock.")]
        [SerializeField] private float maxWheelDegrees = 900f;
        [Tooltip("Wheel spin rate at full A/D (deg/sec).")]
        [SerializeField] private float wheelTurnRate = 320f;

        [Header("Rudder")]
        [SerializeField] private float maxRudderAngle = 35f;
        [Tooltip("How fast the rudder follows the wheel (fraction of full travel/sec).")]
        [SerializeField] private float rudderResponse = 2.5f;
        [Tooltip("Yaw force per unit (flow-speed × sin(rudder)). Tune to hull mass/inertia.")]
        [SerializeField] private float rudderYawCoefficient = 9000f;
        [Tooltip("Flip if A/D / wheel turn the ship the wrong way.")]
        [SerializeField] private bool invertSteering;

        [Header("Sea disturbance")]
        [Tooltip("How hard sideways water flow spins an UNMANNED wheel.")]
        [SerializeField] private float waveWheelDisturbance = 40f;
        [Tooltip("How hard sideways flow buffets the rudder while a helmsman holds the wheel (deg per unit flow).")]
        [SerializeField] private float waveRudderBuffet = 1.5f;
        [Tooltip("Forward flow trails an unmanned wheel back toward centre (deg/sec per unit flow).")]
        [SerializeField] private float unmannedCentering = 12f;

        [Networked] public float WheelAngle { get; set; }
        [Networked] public float RudderAngle { get; set; }
        [Networked] public PlayerRef Occupant { get; set; }

        private Rigidbody _rb;
        private OceanWaveField _waveField;
        private Quaternion _wheelBaseRotation;
        private float _pendingSteer; // host-only, set by the steering player each tick

        public bool IsOccupied => Occupant.IsRealPlayer;
        public float MaxWheelDegrees => maxWheelDegrees;

        /// <summary>Normalised wheel position, -1..1 (for HUD/debug).</summary>
        public float WheelNormalized => maxWheelDegrees > 0.01f ? Mathf.Clamp(WheelAngle / maxWheelDegrees, -1f, 1f) : 0f;

        public Vector3 StandPosition => standAnchor != null ? standAnchor.position : transform.position;

        /// <summary>Yaw (deg) the helmsman should face — toward the wheel from the stand.</summary>
        public float FacingYaw
        {
            get
            {
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

            bool manned = IsOccupied;
            float steer = manned ? _pendingSteer : 0f;

            // Turn the wheel from the helmsman; an unmanned wheel is spun by the sea and trails to centre.
            float wheel = WheelAngle + steer * wheelTurnRate * dt;
            if (!manned)
            {
                wheel += lateralFlow * waveWheelDisturbance * dt;
                wheel -= Mathf.Sign(wheel) * Mathf.Abs(forwardFlow) * unmannedCentering * dt;
            }
            WheelAngle = Mathf.Clamp(wheel, -maxWheelDegrees, maxWheelDegrees);

            // Rudder follows the wheel; while manned, the sea only buffets it a little.
            float targetRudder = WheelNormalized * maxRudderAngle;
            if (manned)
                targetRudder += lateralFlow * waveRudderBuffet;
            targetRudder = Mathf.Clamp(targetRudder, -maxRudderAngle * 1.5f, maxRudderAngle * 1.5f);
            RudderAngle = Mathf.MoveTowards(RudderAngle, targetRudder, rudderResponse * maxRudderAngle * dt);

            // Hydrofoil yaw: side force ∝ flow speed × sin(deflection). No way on ⇒ no steering.
            float yaw = rudderYawCoefficient * forwardFlow * Mathf.Sin(RudderAngle * Mathf.Deg2Rad);
            if (invertSteering)
                yaw = -yaw;
            _rb.AddTorque(Vector3.up * yaw, ForceMode.Force);

            _pendingSteer = 0f;
        }

        public override void Render()
        {
            if (wheelModel == null)
                return;
            wheelModel.localRotation = _wheelBaseRotation * Quaternion.AngleAxis(WheelAngle, wheelSpinAxis);
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
            }
        }
#endif
    }
}
#endif
