using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Provides ship motion signals for character balance/ragdoll gameplay.
    /// </summary>
    public class ShipDeckMotionProvider : MonoBehaviour
    {
        [SerializeField] private ShipBuoyancyController buoyancy;
        [SerializeField] private Rigidbody shipBody;

        public Vector3 LinearAcceleration => buoyancy != null ? buoyancy.LinearAcceleration : Vector3.zero;
        public Vector3 AngularAcceleration => buoyancy != null ? buoyancy.AngularAcceleration : Vector3.zero;
        public float RollDegrees => buoyancy != null ? buoyancy.RollDegrees : 0f;
        public float PitchDegrees => buoyancy != null ? buoyancy.PitchDegrees : 0f;
        public float LastWaveImpact => buoyancy != null ? buoyancy.LastImpactStrength : 0f;

        public void ConfigureReferences(ShipBuoyancyController buoyancyController, Rigidbody body)
        {
            buoyancy = buoyancyController;
            shipBody = body;
        }

        private void Awake()
        {
            if (buoyancy == null)
                buoyancy = GetComponentInParent<ShipBuoyancyController>();
            if (shipBody == null)
                shipBody = GetComponentInParent<Rigidbody>();
        }

        public Vector3 GetApparentForceAtPoint(Vector3 worldPoint)
        {
            if (buoyancy != null)
                return buoyancy.GetApparentForceAtDeckPoint(worldPoint);

            if (shipBody == null)
                return Physics.gravity;

            return Physics.gravity - shipBody.linearVelocity;
        }
    }
}

