using UnityEngine;

namespace RumOverboard.Core.Character
{
    public readonly struct ClimbProbeResult
    {
        public readonly bool NearClimb;
        public readonly int TargetId;
        public readonly Vector3 LocalPoint;

        public ClimbProbeResult(bool nearClimb, int targetId, Vector3 localPoint)
        {
            NearClimb = nearClimb;
            TargetId = targetId;
            LocalPoint = localPoint;
        }
    }

    /// <summary>
    /// Generic overlap-based climb probe that does not depend on networking types.
    /// </summary>
    public sealed class ClimbProbeService
    {
        private readonly Collider[] _hits;

        public ClimbProbeService(int capacity = 4)
        {
            _hits = new Collider[Mathf.Max(1, capacity)];
        }

        public ClimbProbeResult Probe(Transform actor, float reach, LayerMask climbMask)
        {
            int count = Physics.OverlapSphereNonAlloc(
                actor.position + Vector3.up,
                Mathf.Max(0.01f, reach),
                _hits,
                climbMask,
                QueryTriggerInteraction.Collide);

            if (count <= 0)
                return new ClimbProbeResult(false, 0, Vector3.zero);

            Transform climb = _hits[0] != null ? _hits[0].transform : null;
            if (climb == null)
                return new ClimbProbeResult(false, 0, Vector3.zero);

            return new ClimbProbeResult(
                true,
                climb.GetInstanceID(),
                climb.InverseTransformPoint(actor.position));
        }
    }
}

