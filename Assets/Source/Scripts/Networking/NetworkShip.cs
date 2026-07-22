#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Ocean;
using UnityEngine;

namespace RumOverboard.Networking
{
    public struct ShipNetState : INetworkStruct
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
        public float RollDegrees;
        public float PitchDegrees;
        public float LastImpactStrength;
        public float OceanTime;
    }

    /// <summary>
    /// Authoritative network wrapper for the ship rigidbody + buoyancy stack.
    /// Host simulates the body and replicates compact motion/ocean state.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkShip : NetworkBehaviour
    {
        [SerializeField] private ShipBuoyancyController buoyancy;
        [SerializeField] private OceanWaveField waveField;

        [Header("Proxy behaviour")]
        [SerializeField] private bool disableBuoyancyOnProxies = true;
        [SerializeField] private float proxySmoothing = 12f;

        [Header("Collider safety")]
        [SerializeField] private bool disableNonConvexVisualMeshColliders = true;

        [Header("Ocean time correction")]
        [SerializeField] private float oceanTimeCorrectionThreshold = 0.1f;
        [SerializeField] private float oceanTimeCorrectionGain = 0.1f;

        [Networked] public ShipNetState State { get; set; }

        private Rigidbody _rb;
        private bool _hasBuiltInTransformSync;
        private ShipNetState _proxyState;
        private bool _proxyStateReady;

        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();
            if (buoyancy == null)
                buoyancy = GetComponent<ShipBuoyancyController>();
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();

            SanitizeVisualColliders();

            _hasBuiltInTransformSync =
                TryGetComponent(out Fusion.Addons.Physics.NetworkRigidbody3D _) ||
                TryGetComponent(out NetworkTransform _);

            ConfigureRigidbodyMode();

            if (buoyancy != null)
            {
                buoyancy.ConfigureReferences(waveField, _rb);
                if (disableBuoyancyOnProxies)
                    buoyancy.enabled = HasStateAuthority;
            }

            if (HasStateAuthority)
            {
                State = CaptureState();
            }
            else
            {
                _proxyState = State;
                _proxyStateReady = true;
                ApplyProxyState(_proxyState, 1f, true);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (_rb == null)
                return;

            if (HasStateAuthority)
            {
                if (disableBuoyancyOnProxies && buoyancy != null && !buoyancy.enabled)
                    buoyancy.enabled = true;

                State = CaptureState();
                return;
            }

            if (disableBuoyancyOnProxies && buoyancy != null && buoyancy.enabled)
                buoyancy.enabled = false;

            bool hadProxyState = _proxyStateReady;
            _proxyState = State;
            _proxyStateReady = true;

            if (waveField != null)
            {
                float error = _proxyState.OceanTime - waveField.OceanTimeNow;
                if (Mathf.Abs(error) > Mathf.Max(0.01f, oceanTimeCorrectionThreshold))
                    waveField.ApplyTimeCorrection(error * Mathf.Clamp01(oceanTimeCorrectionGain));
            }

            if (!_hasBuiltInTransformSync && !hadProxyState)
                ApplyProxyState(_proxyState, 1f, true);
        }

        public override void Render()
        {
            if (HasStateAuthority || !_proxyStateReady || _hasBuiltInTransformSync)
                return;

            float dt = Runner != null ? Runner.DeltaTime : Time.deltaTime;
            float alpha = Mathf.Clamp01(Mathf.Max(0f, proxySmoothing) * Mathf.Max(0.0001f, dt));
            ApplyProxyState(_proxyState, alpha, false);
        }

        private ShipNetState CaptureState()
        {
            ShipNetState s = State;
            s.Position = _rb.position;
            s.Rotation = _rb.rotation;
            s.LinearVelocity = _rb.linearVelocity;
            s.AngularVelocity = _rb.angularVelocity;
            s.RollDegrees = buoyancy != null ? buoyancy.RollDegrees : NormalizeSigned(transform.eulerAngles.z);
            s.PitchDegrees = buoyancy != null ? buoyancy.PitchDegrees : NormalizeSigned(transform.eulerAngles.x);
            s.LastImpactStrength = buoyancy != null ? buoyancy.LastImpactStrength : 0f;
            s.OceanTime = waveField != null ? waveField.OceanTimeNow : 0f;
            return s;
        }

        private void ConfigureRigidbodyMode()
        {
            if (_rb == null)
                return;

            if (HasStateAuthority)
            {
                _rb.isKinematic = false;
                return;
            }

            // Without built-in Fusion transform sync, proxies should be pose-driven only.
            if (!_hasBuiltInTransformSync)
                _rb.isKinematic = true;
        }

        private void SanitizeVisualColliders()
        {
            if (!disableNonConvexVisualMeshColliders || _rb == null)
                return;

            int disabled = 0;
            MeshCollider[] colliders = GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                MeshCollider meshCollider = colliders[i];
                if (meshCollider == null || meshCollider.convex || !meshCollider.enabled)
                    continue;

                // Dynamic rigidbodies are not compatible with non-convex mesh colliders.
                meshCollider.enabled = false;
                disabled++;
            }

            if (disabled > 0)
                Debug.LogWarning($"[NetworkShip] Disabled {disabled} non-convex MeshCollider components on '{name}' to keep dynamic rigidbody simulation valid.");
        }

        private void ApplyProxyState(ShipNetState s, float alpha, bool snap)
        {
            if (_rb == null)
                return;

            if (snap)
            {
                _rb.position = s.Position;
                _rb.rotation = s.Rotation;
            }
            else
            {
                _rb.position = Vector3.Lerp(_rb.position, s.Position, alpha);
                _rb.rotation = Quaternion.Slerp(_rb.rotation, s.Rotation, alpha);
            }

            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, s.LinearVelocity, alpha);
                _rb.angularVelocity = Vector3.Lerp(_rb.angularVelocity, s.AngularVelocity, alpha);
            }
        }

        private static float NormalizeSigned(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
#endif

