using UnityEngine;

namespace RumOverboard.Core.Character
{
    /// <summary>
    /// Marker for water volumes used by NetworkPlayer trigger-based swimming detection.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WaterTrigger : MonoBehaviour
    {
        private void Reset()
        {
            EnsureTriggerCollider();
        }

        private void OnValidate()
        {
            EnsureTriggerCollider();
        }

        private void EnsureTriggerCollider()
        {
            if (TryGetComponent(out Collider col))
                col.isTrigger = true;
        }
    }
}

