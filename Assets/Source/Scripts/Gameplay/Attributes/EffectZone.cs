using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// Trigger volume that keeps an effect on every attributed entity inside (cold region, rain,
    /// spray, smoke…). Re-applies periodically so timed effects stay refreshed while inside; optionally
    /// removes the effect on exit (otherwise it simply runs out). Host/offline only — on clients the
    /// result arrives through NetworkAttributes.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class EffectZone : MonoBehaviour
    {
        [SerializeField] private EffectDefinition effect;
        [SerializeField] private bool removeOnExit = true;

        [Tooltip("Seconds between re-applications while inside.")]
        [SerializeField] private float refreshInterval = 0.5f;

        private readonly Dictionary<AttributesComponent, int> _inside = new(); // entity → overlapping colliders
        private float _timer;

        private void Reset() => GetComponent<Collider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            if (!Resolve(other, out AttributesComponent target)) return;
            _inside.TryGetValue(target, out int count);
            _inside[target] = count + 1;
            if (count == 0 && target.IsAuthority)
                target.ApplyEffect(effect);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!Resolve(other, out AttributesComponent target)) return;
            if (!_inside.TryGetValue(target, out int count)) return;
            if (count > 1)
            {
                _inside[target] = count - 1;
                return;
            }
            _inside.Remove(target);
            if (removeOnExit && target.IsAuthority)
                target.RemoveEffect(effect);
        }

        private void FixedUpdate()
        {
            _timer += Time.fixedDeltaTime;
            if (_timer < refreshInterval) return;
            _timer = 0f;

            foreach (KeyValuePair<AttributesComponent, int> pair in _inside)
                if (pair.Key != null && pair.Key.IsAuthority)
                    pair.Key.ApplyEffect(effect);
        }

        private static bool Resolve(Collider c, out AttributesComponent target)
        {
            if (AttributesComponent.TryGet(c, out target)) return true;
            target = c.GetComponentInParent<AttributesComponent>();
            return target != null;
        }

        private void OnDrawGizmos()
        {
            var col = GetComponent<BoxCollider>();
            if (col == null) return;
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(col.center, col.size);
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
            Gizmos.DrawWireCube(col.center, col.size);
        }
    }
}
