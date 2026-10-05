using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Core.Sim
{
    /// <summary>
    /// Lightweight ECS-style registry. Data components (MonoBehaviours / NetworkBehaviours that
    /// only hold authored data + replicated state) register themselves here on enable; systems
    /// iterate the typed lists instead of keeping ad-hoc references or calling FindObjectsOfType.
    ///
    ///   • <see cref="All{T}"/>            — every live component of type T (registration order).
    ///   • <see cref="TryGetByCollider{T}"/> — O(1) collider → component lookup, used by sensors
    ///     (interaction ray, climb probe) so a raycast hit resolves to data without GetComponent.
    ///
    /// Not networked: every peer builds the same registry from the same scene/prefabs.
    /// </summary>
    public static class SimWorld
    {
        private static class Store<T> where T : class
        {
            public static readonly List<T> Items = new();
        }

        private static readonly Dictionary<Collider, Component> ByCollider = new();

        public static void Add<T>(T component) where T : class
        {
            List<T> items = Store<T>.Items;
            if (!items.Contains(component))
                items.Add(component);
        }

        public static void Remove<T>(T component) where T : class
        {
            Store<T>.Items.Remove(component);
        }

        public static IReadOnlyList<T> All<T>() where T : class => Store<T>.Items;

        /// <summary>Maps every collider under <paramref name="owner"/> (or the given set) to it.</summary>
        public static void BindColliders(Component owner, IReadOnlyList<Collider> colliders)
        {
            if (owner == null || colliders == null)
                return;
            for (int i = 0; i < colliders.Count; i++)
                if (colliders[i] != null)
                    ByCollider[colliders[i]] = owner;
        }

        public static void UnbindColliders(Component owner, IReadOnlyList<Collider> colliders)
        {
            if (colliders == null)
                return;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider c = colliders[i];
                if (c != null && ByCollider.TryGetValue(c, out Component bound) && bound == owner)
                    ByCollider.Remove(c);
            }
        }

        public static bool TryGetByCollider<T>(Collider collider, out T component) where T : Component
        {
            component = null;
            if (collider == null || !ByCollider.TryGetValue(collider, out Component bound) || bound == null)
                return false;
            component = bound as T;
            return component != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain reload may be disabled: drop stale entries from the previous play session.
            ByCollider.Clear();
        }
    }

    /// <summary>
    /// Base for plain (non-networked) data components: registers into <see cref="SimWorld"/>
    /// under its concrete type and binds its colliders for sensor lookups.
    /// </summary>
    public abstract class SimComponent<TSelf> : MonoBehaviour where TSelf : SimComponent<TSelf>
    {
        private Collider[] _colliders;

        /// <summary>Colliders that resolve to this component (defaults to all children).</summary>
        protected virtual Collider[] CollectColliders() => GetComponentsInChildren<Collider>(true);

        protected virtual void OnEnable()
        {
            SimWorld.Add((TSelf)this);
            _colliders = CollectColliders();
            SimWorld.BindColliders(this, _colliders);
        }

        protected virtual void OnDisable()
        {
            SimWorld.Remove((TSelf)this);
            SimWorld.UnbindColliders(this, _colliders);
        }
    }
}
