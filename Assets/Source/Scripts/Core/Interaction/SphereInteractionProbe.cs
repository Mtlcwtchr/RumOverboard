using UnityEngine;

namespace RumOverboard.Core.Interaction
{
    /// <summary>
    /// Reusable view-based sphere cast helper that returns the nearest valid hit.
    /// </summary>
    public sealed class SphereInteractionProbe
    {
        private readonly RaycastHit[] _hits;

        public SphereInteractionProbe(int capacity = 8)
        {
            _hits = new RaycastHit[Mathf.Max(1, capacity)];
        }

        public bool TryProbe(
            Vector3 origin,
            Vector3 direction,
            float radius,
            float reach,
            LayerMask mask,
            Transform ignoreRoot,
            out RaycastHit bestHit)
        {
            int count = Physics.SphereCastNonAlloc(
                origin,
                Mathf.Max(0f, radius),
                direction,
                _hits,
                Mathf.Max(0.01f, reach),
                mask,
                QueryTriggerInteraction.Collide);

            float nearestDistance = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                Collider col = hit.collider;
                if (col == null)
                    continue;

                if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot))
                    continue;

                if (hit.distance >= nearestDistance)
                    continue;

                nearestDistance = hit.distance;
                bestIndex = i;
            }

            if (bestIndex < 0)
            {
                bestHit = default;
                return false;
            }

            bestHit = _hits[bestIndex];
            return true;
        }
    }
}

