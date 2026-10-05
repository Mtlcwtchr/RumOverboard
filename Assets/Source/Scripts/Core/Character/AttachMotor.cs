using UnityEngine;

namespace RumOverboard.Core.Character
{
    /// <summary>
    /// Keeps a DYNAMIC body glued to a moving anchor (a rung on the mast, the helm stand) purely
    /// through velocity: anchor point velocity (so it rides the ship's linear + angular motion)
    /// plus a critically-damped correction toward the anchor. The body stays a normal physics
    /// body — it still collides, NetworkRigidbody3D replicates it like any other — but it can't
    /// drift off, and there is no kinematic toggling (a common source of pops in netcode).
    /// </summary>
    public static class AttachMotor
    {
        /// <param name="anchorVelocity">Velocity of the anchor point (Rigidbody.GetPointVelocity).</param>
        /// <param name="catchUpTime">Seconds to close the positional error (~2-4 ticks feels glued).</param>
        /// <param name="maxCorrectionSpeed">Cap for the correction term (m/s) so a big error doesn't launch the body.</param>
        public static void Drive(Rigidbody body, Vector3 targetPosition, Vector3 anchorVelocity,
            Quaternion targetRotation, float dt, float catchUpTime = 0.06f, float maxCorrectionSpeed = 8f)
        {
            if (body == null || body.isKinematic)
                return;

            Vector3 error = targetPosition - body.position;
            Vector3 correction = error / Mathf.Max(dt, catchUpTime);
            correction = Vector3.ClampMagnitude(correction, Mathf.Max(0.1f, maxCorrectionSpeed));

            body.linearVelocity = anchorVelocity + correction;
            body.angularVelocity = Vector3.zero;
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, targetRotation, 720f * dt));
        }
    }
}
