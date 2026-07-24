using UnityEngine;

namespace RumOverboard.Core.Character
{
    public readonly struct GroundProbeResult
    {
        public readonly bool IsGrounded;
        public readonly Vector3 GroundNormal;
        public readonly Vector3 GroundVelocity;

        public GroundProbeResult(bool isGrounded, Vector3 groundNormal, Vector3 groundVelocity)
        {
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
            Vector3 origin = actor.position + Vector3.up * 0.1f;
            float castDistance = Mathf.Max(0.01f, probeDistance) + 0.1f;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, castDistance, groundMask, QueryTriggerInteraction.Ignore))
                return new GroundProbeResult(false, Vector3.up, Vector3.zero);

            Rigidbody groundBody = hit.collider != null ? hit.collider.attachedRigidbody : null;
            Vector3 groundVelocity = (groundBody != null && groundBody != actorBody)
                ? groundBody.GetPointVelocity(hit.point)
                : Vector3.zero;

            return new GroundProbeResult(true, hit.normal, groundVelocity);
        }
    }
}

