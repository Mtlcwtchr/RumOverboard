using UnityEngine;

namespace Source.Scripts.Gameplay
{
    /// <summary>
    /// Marker component for passability volumes on the ship.
    /// Walkable zones can be used by gameplay/debug tools, Blocked zones highlight hard blockers.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ShipTraversalZone : MonoBehaviour
    {
        public enum TraversalType
        {
            Walkable,
            Blocked,
        }

        [SerializeField] private TraversalType traversal = TraversalType.Walkable;

        public TraversalType Traversal => traversal;

        public void Configure(TraversalType traversalType)
        {
            traversal = traversalType;
        }

        private void Reset()
        {
            if (TryGetComponent(out Collider col))
                col.isTrigger = true;
        }
    }
}

