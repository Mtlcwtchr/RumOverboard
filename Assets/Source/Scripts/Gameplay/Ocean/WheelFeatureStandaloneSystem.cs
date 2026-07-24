using RumOverboard.Gameplay.Ocean.Simulation;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Non-network wheel/rudder simulator used in WheelScene feature testing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class WheelFeatureStandaloneSystem : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody shipBody;
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanWindSystem windSystem;
        [SerializeField] private Transform wheelModel;
        [SerializeField] private Transform rudderBlade;
        [SerializeField] private Transform standAnchor;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Vector3 rudderLocal = new Vector3(0f, -0.8f, -4.6f);

        [Header("Interaction")]
        [SerializeField] private bool occupied;
        [SerializeField] private float occupyDistance = 2f;

        [Header("Wheel")]
        [SerializeField] private Vector3 wheelSpinAxis = Vector3.forward;
        [SerializeField] private float maxWheelDegrees = 900f;
        [SerializeField] private float wheelTurnRate = 320f;

        [Header("Rudder")]
        [SerializeField] private float maxRudderAngle = 35f;
        [SerializeField] private float rudderResponse = 2.5f;
        [SerializeField] private float rudderYawCoefficient = 9000f;
        [SerializeField] private bool invertSteering;

        [Header("Water disturbance")]
        [SerializeField] private bool simulateWater = true;
        [SerializeField] private float waveWheelDisturbance = 40f;
        [SerializeField] private float waveRudderBuffet = 1.5f;
        [SerializeField] private float unmannedCentering = 12f;

        [Header("Debug")]
        [SerializeField] private bool drawDebugGizmos = true;

        private Quaternion _wheelBaseRotation;
        private Quaternion _rudderBaseRotation;
        private float _wheelAngle;
        private float _rudderAngle;
        private float _pendingSteer;
        private Vector3 _lastFlow;
        private float _lastYawTorque;

        public bool Occupied
        {
            get => occupied;
            set => occupied = value;
        }

        public bool SimulateWater
        {
            get => simulateWater;
            set => simulateWater = value;
        }

        public float MaxWheelDegrees
        {
            get => maxWheelDegrees;
            set => maxWheelDegrees = Mathf.Max(1f, value);
        }

        public float WheelTurnRate
        {
            get => wheelTurnRate;
            set => wheelTurnRate = Mathf.Max(1f, value);
        }

        public float MaxRudderAngle
        {
            get => maxRudderAngle;
            set => maxRudderAngle = Mathf.Clamp(value, 1f, 85f);
        }

        public float RudderResponse
        {
            get => rudderResponse;
            set => rudderResponse = Mathf.Max(0.01f, value);
        }

        public float RudderYawCoefficient
        {
            get => rudderYawCoefficient;
            set => rudderYawCoefficient = Mathf.Max(0f, value);
        }

        public bool InvertSteering
        {
            get => invertSteering;
            set => invertSteering = value;
        }

        public float WaveWheelDisturbance
        {
            get => waveWheelDisturbance;
            set => waveWheelDisturbance = Mathf.Max(0f, value);
        }

        public float WaveRudderBuffet
        {
            get => waveRudderBuffet;
            set => waveRudderBuffet = Mathf.Max(0f, value);
        }

        public float UnmannedCentering
        {
            get => unmannedCentering;
            set => unmannedCentering = Mathf.Max(0f, value);
        }

        public float WheelAngle => _wheelAngle;
        public float RudderAngle => _rudderAngle;
        public float LastYawTorque => _lastYawTorque;
        public Vector3 LastFlow => _lastFlow;

        public void Configure(Rigidbody body, OceanWaveField waves, OceanWindSystem wind)
        {
            shipBody = body;
            waveField = waves;
            windSystem = wind;
        }

        public void SetRigReferences(Transform wheel, Transform rudder, Transform stand, Camera cam)
        {
            wheelModel = wheel;
            rudderBlade = rudder;
            standAnchor = stand;
            playerCamera = cam;

            if (wheelModel != null)
                _wheelBaseRotation = wheelModel.localRotation;
            if (rudderBlade != null)
                _rudderBaseRotation = rudderBlade.localRotation;
        }

        private void Awake()
        {
            if (shipBody == null)
                shipBody = GetComponent<Rigidbody>();
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            if (windSystem == null)
                windSystem = FindAnyObjectByType<OceanWindSystem>();
            if (playerCamera == null)
                playerCamera = Camera.main;
            if (standAnchor == null)
                standAnchor = transform;

            if (wheelModel != null)
                _wheelBaseRotation = wheelModel.localRotation;
            if (rudderBlade != null)
                _rudderBaseRotation = rudderBlade.localRotation;
        }

        private void Update()
        {
            if (playerCamera == null)
                playerCamera = Camera.main;

            if (TryReadInteractPressed() && CanToggleOccupancy())
                occupied = !occupied;

            _pendingSteer = occupied ? ReadSteerInput() : 0f;
            UpdateVisuals();
        }

        private void FixedUpdate()
        {
            if (shipBody == null)
                return;

            float dt = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            Vector3 flow = Vector3.zero;
            float forwardFlow = 0f;
            float lateralFlow = 0f;

            if (simulateWater)
            {
                Vector3 rudderPos = transform.TransformPoint(rudderLocal);
                Vector3 waterVel = waveField != null
                    ? waveField.Sample(rudderPos, waveField.OceanTimeNow).waterVelocity
                    : Vector3.zero;
                flow = waterVel - shipBody.GetPointVelocity(rudderPos);
                forwardFlow = Vector3.Dot(flow, -transform.forward);
                lateralFlow = Vector3.Dot(flow, transform.right);
            }

            var input = new ShipHelmDynamicsInput(
                dt,
                _wheelAngle,
                _rudderAngle,
                _pendingSteer,
                occupied,
                forwardFlow,
                lateralFlow);

            var settings = new ShipHelmDynamicsSettings(
                maxWheelDegrees,
                wheelTurnRate,
                maxRudderAngle,
                rudderResponse,
                rudderYawCoefficient,
                waveWheelDisturbance,
                waveRudderBuffet,
                unmannedCentering,
                invertSteering);

            ShipHelmDynamicsResult result = ShipHelmDynamicsSolver.Step(input, settings);
            _wheelAngle = result.WheelAngle;
            _rudderAngle = result.RudderAngle;
            _lastYawTorque = result.YawTorque;
            _lastFlow = flow;

            shipBody.AddTorque(Vector3.up * _lastYawTorque, ForceMode.Force);
            _pendingSteer = 0f;
        }

        private void UpdateVisuals()
        {
            if (wheelModel != null)
                wheelModel.localRotation = _wheelBaseRotation * Quaternion.AngleAxis(_wheelAngle, wheelSpinAxis);

            if (rudderBlade != null)
                rudderBlade.localRotation = _rudderBaseRotation * Quaternion.AngleAxis(_rudderAngle, Vector3.up);
        }

        private bool CanToggleOccupancy()
        {
            if (standAnchor == null || playerCamera == null)
                return true;

            float distance = Vector3.Distance(playerCamera.transform.position, standAnchor.position);
            return distance <= Mathf.Max(0.3f, occupyDistance);
        }

        private float ReadSteerInput()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return 0f;
            float steer = 0f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)
                steer -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
                steer += 1f;
            return Mathf.Clamp(steer, -1f, 1f);
#else
            return Mathf.Clamp(Input.GetAxisRaw("Horizontal"), -1f, 1f);
#endif
        }

        private static bool TryReadInteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos)
                return;

            Vector3 rudderPos = transform.TransformPoint(rudderLocal);
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.95f);
            Gizmos.DrawWireSphere(rudderPos, 0.3f);
            Gizmos.DrawLine(rudderPos, rudderPos + _lastFlow * 0.5f);

            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.95f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * Mathf.Clamp(_lastYawTorque * 0.0004f, -3f, 3f));

            if (standAnchor != null)
            {
                Gizmos.color = occupied ? new Color(0.3f, 1f, 0.45f, 0.95f) : new Color(1f, 0.45f, 0.35f, 0.95f);
                Gizmos.DrawWireCube(standAnchor.position, Vector3.one * 0.3f);
            }
        }
#endif
    }
}

