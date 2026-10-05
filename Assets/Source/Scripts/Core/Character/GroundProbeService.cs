using UnityEngine;

namespace RumOverboard.Core.Character
{
    public readonly struct GroundProbeResult
    {
        public readonly bool IsGrounded;
        public readonly Vector3 GroundNormal;
        public readonly Vector3 GroundVelocity;
        public readonly Rigidbody Body;

        public GroundProbeResult(bool isGrounded, Vector3 groundNormal, Vector3 groundVelocity, Rigidbody body = null)
        {
            Body = body;
            IsGrounded = isGrounded;
            GroundNormal = groundNormal;
            GroundVelocity = groundVelocity;
        }
    }

    /// <summary>
    /// Reusable ground probe logic for locomotion systems that need to ride moving platforms.
    /// </summary>
    public static class GroundProbeService
    {
        public static GroundProbeResult Probe(Transform actor, Rigidbody actorBody, float probeDistance, LayerMask groundMask)
        {
            // Sphere cast from inside the capsule's bottom so edges/steps/rope coils still count.
            const float radius = 0.2f;
            const float lift = 0.45f;
            Vector3 origin = actor.position + Vector3.up * lift;
            float castDistance = Mathf.Max(0.01f, probeDistance) + lift - radius;

            if (!Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, castDistance, groundMask, QueryTriggerInteraction.Ignore))
                return new GroundProbeResult(false, Vector3.up, Vector3.zero);
            if (hit.normal.y < 0.35f) // a wall brushing the sphere is not ground
                return new GroundProbeResult(false, Vector3.up, Vector3.zero);

            Rigidbody groundBody = hit.collider != null ? hit.collider.attachedRigidbody : null;
            Vector3 groundVelocity = (groundBody != null && groundBody != actorBody)
                ? groundBody.GetPointVelocity(hit.point)
                : Vector3.zero;

            return new GroundProbeResult(true, hit.normal, groundVelocity, groundBody);
        }
    }
}

