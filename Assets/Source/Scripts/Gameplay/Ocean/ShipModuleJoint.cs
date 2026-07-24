using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Inter-module connector. Each ship module (hull, mast, wheel) registers itself here.
    /// Modules push forces/impulses/data through the joint; the joint routes them to the
    /// appropriate receivers (e.g. mast pushes thrust → joint applies to hull rigidbody).
    ///
    /// Lives on the ship root (or a dedicated "Joints" object). Modules find it via
    /// GetComponentInParent on Awake and register.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipModuleJoint : MonoBehaviour
    {
        [SerializeField] private Rigidbody shipBody;

        private readonly List<IForceProvider> _forceProviders = new();
        private readonly List<ITorqueProvider> _torqueProviders = new();

        public Rigidbody ShipBody => shipBody;

        private void Awake()
        {
            if (shipBody == null)
                shipBody = GetComponentInParent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (shipBody == null) return;

            for (int i = 0; i < _forceProviders.Count; i++)
            {
                var provider = _forceProviders[i];
                if (provider == null) continue;
                var (force, point) = provider.GetForce();
                if (force.sqrMagnitude > 0.001f)
                    shipBody.AddForceAtPosition(force, point, ForceMode.Force);
            }

            for (int i = 0; i < _torqueProviders.Count; i++)
            {
                var provider = _torqueProviders[i];
                if (provider == null) continue;
                Vector3 torque = provider.GetTorque();
                if (torque.sqrMagnitude > 0.001f)
                    shipBody.AddTorque(torque, ForceMode.Force);
            }
        }

        public void RegisterForceProvider(IForceProvider provider)
        {
            if (provider != null && !_forceProviders.Contains(provider))
                _forceProviders.Add(provider);
        }

        public void UnregisterForceProvider(IForceProvider provider)
        {
            _forceProviders.Remove(provider);
        }

        public void RegisterTorqueProvider(ITorqueProvider provider)
        {
            if (provider != null && !_torqueProviders.Contains(provider))
                _torqueProviders.Add(provider);
        }

        public void UnregisterTorqueProvider(ITorqueProvider provider)
        {
            _torqueProviders.Remove(provider);
        }

        /// <summary>Direct impulse application (e.g. collision, wave slam).</summary>
        public void ApplyImpulse(Vector3 impulse, Vector3 worldPoint)
        {
            if (shipBody != null)
                shipBody.AddForceAtPosition(impulse, worldPoint, ForceMode.Impulse);
        }

        /// <summary>Direct force for one frame (external effects).</summary>
        public void ApplyForce(Vector3 force, Vector3 worldPoint)
        {
            if (shipBody != null)
                shipBody.AddForceAtPosition(force, worldPoint, ForceMode.Force);
        }

        private void OnDestroy()
        {
            _forceProviders.Clear();
            _torqueProviders.Clear();
        }
    }

    /// <summary>Implemented by modules that generate continuous forces (sails, buoyancy, rudder).</summary>
    public interface IForceProvider
    {
        /// <returns>World-space force and application point.</returns>
        (Vector3 force, Vector3 worldPoint) GetForce();
    }

    /// <summary>Implemented by modules that generate torques (rudder yaw, stability).</summary>
    public interface ITorqueProvider
    {
        Vector3 GetTorque();
    }
}

