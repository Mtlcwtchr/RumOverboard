#if FUSION2
using System.Collections.Generic;
using Fusion;
using RumOverboard.Core.Sim;
using UnityEngine;

namespace RumOverboard.Gameplay.Interaction
{
    public enum InteractionKind : byte
    {
        Generic = 0,
        Helm = 1,
        Climb = 2,
        Rope = 3,
        BelayPin = 4,
        RopeEnd = 5,
    }

    /// <summary>Who is looking at / using an interactable (host sim or local HUD).</summary>
    public readonly struct InteractorInfo
    {
        public readonly PlayerRef Player;
        public readonly Vector3 Eye;
        public readonly Vector3 Feet;
        /// <summary>Line index this crew member is holding (on <see cref="HeldShip"/>), -1 = hands free.</summary>
        public readonly int HeldLine;
        public readonly RumOverboard.Networking.NetworkShip HeldShip;

        public bool HandsFree => HeldLine < 0;

        public InteractorInfo(PlayerRef player, Vector3 eye, Vector3 feet,
            int heldLine = -1, RumOverboard.Networking.NetworkShip heldShip = null)
        {
            Player = player;
            Eye = eye;
            Feet = feet;
            HeldLine = heldLine;
            HeldShip = heldShip;
        }
    }

    /// <summary>
    /// Network-stable handle to an interactable: owning NetworkObject + index among that object's
    /// interactables (hierarchy order — identical on every peer because it's the same prefab).
    /// Sent in input so the host acts on exactly the target the client saw in its hint.
    /// </summary>
    public struct InteractableRef : INetworkStruct
    {
        public NetworkId Object;
        public short IndexPlusOne; // 0 = none (so default(InteractableRef) is "nothing")

        public bool IsValid => IndexPlusOne > 0 && Object.IsValid;
        public static InteractableRef None => default;

        public InteractableRef(NetworkId obj, int index)
        {
            Object = obj;
            IndexPlusOne = (short)(index + 1);
        }

        public bool Equals(InteractableRef other) => Object == other.Object && IndexPlusOne == other.IndexPlusOne;
    }

    /// <summary>
    /// Data component for anything the crew can look at and press Interact on. Concrete
    /// interactables (helm station, climb surface, rope station...) derive from it and add their
    /// own authored data; the behaviour lives in the player states / systems that consume them.
    ///
    /// Registers in <see cref="SimWorld"/> and binds its colliders, so the view ray resolves a hit
    /// collider to an interactable with a dictionary lookup. Colliders that belong to a nested
    /// interactable are left to that one.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        [Header("Interaction")]
        [Tooltip("Hint text shown next to the key, e.g. \"Встать за штурвал\".")]
        [SerializeField] private string prompt = "Взаимодействовать";

        [Tooltip("Max distance from the eye to the hit point (m).")]
        [SerializeField] private float maxDistance = 2.4f;

        [Tooltip("Explicit colliders that resolve to this interactable. Empty = all child colliders " +
                 "not owned by a nested interactable.")]
        [SerializeField] private Collider[] colliders;

        private Collider[] _bound;
        private NetworkObject _owner;
        private int _index = -1;

        public abstract InteractionKind Kind { get; }
        public string Prompt { get => prompt; set => prompt = value; }
        public float MaxDistance { get => maxDistance; set => maxDistance = value; }

        /// <summary>Nearest NetworkObject above this interactable (the ship, usually).</summary>
        public NetworkObject Owner
        {
            get
            {
                if (_owner == null)
                    _owner = GetComponentInParent<NetworkObject>(true);
                return _owner;
            }
        }

        public InteractableRef Ref
        {
            get
            {
                NetworkObject owner = Owner;
                if (owner == null || !owner.IsValid)
                    return InteractableRef.None;
                if (_index < 0)
                    _index = InteractableIndex.IndexOf(owner, this);
                return _index < 0 ? InteractableRef.None : new InteractableRef(owner.Id, _index);
            }
        }

        /// <summary>Text for the hint. Override to make it state-dependent ("Штурвал занят").</summary>
        public virtual string GetPrompt(in InteractorInfo who) => prompt;

        /// <summary>Whether <paramref name="who"/> may start interacting right now.</summary>
        public virtual bool IsAvailable(in InteractorInfo who) => true;

        public void SetColliders(Collider[] explicitColliders) => colliders = explicitColliders;

        /// <summary>Distance from <paramref name="point"/> to the nearest bound collider (host range check).</summary>
        public float DistanceTo(Vector3 point, out Vector3 closest)
        {
            closest = transform.position;
            float best = float.MaxValue;
            if (_bound == null)
                return (point - closest).magnitude;

            for (int i = 0; i < _bound.Length; i++)
            {
                Collider c = _bound[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                    continue;
                bool exact = c is BoxCollider || c is SphereCollider || c is CapsuleCollider ||
                             (c is MeshCollider mc && mc.convex);
                Vector3 p = exact ? c.ClosestPoint(point) : c.bounds.ClosestPoint(point);
                float d = (p - point).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    closest = p;
                }
            }
            return best == float.MaxValue ? (point - closest).magnitude : Mathf.Sqrt(best);
        }

        protected virtual void OnEnable()
        {
            SimWorld.Add(this);
            _bound = ResolveColliders();
            SimWorld.BindColliders(this, _bound);
        }

        protected virtual void OnDisable()
        {
            SimWorld.Remove(this);
            SimWorld.UnbindColliders(this, _bound);
        }

        private Collider[] ResolveColliders()
        {
            if (colliders != null && colliders.Length > 0)
                return colliders;

            var result = new List<Collider>();
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
            {
                Interactable nearest = c.GetComponentInParent<Interactable>(true);
                if (nearest == this)
                    result.Add(c);
            }
            return result.ToArray();
        }
    }

    /// <summary>Per-NetworkObject ordered list of interactables, for <see cref="InteractableRef"/>.</summary>
    public static class InteractableIndex
    {
        private static readonly Dictionary<NetworkObject, Interactable[]> Cache = new();

        public static int IndexOf(NetworkObject owner, Interactable item)
        {
            Interactable[] list = Get(owner);
            return System.Array.IndexOf(list, item);
        }

        public static Interactable[] Get(NetworkObject owner)
        {
            if (owner == null)
                return System.Array.Empty<Interactable>();
            if (Cache.TryGetValue(owner, out Interactable[] list))
                return list;

            var found = new List<Interactable>();
            foreach (Interactable i in owner.GetComponentsInChildren<Interactable>(true))
                if (i.GetComponentInParent<NetworkObject>(true) == owner)
                    found.Add(i);
            list = found.ToArray();
            Cache[owner] = list;
            return list;
        }

        public static bool TryResolve(NetworkRunner runner, InteractableRef handle, out Interactable target)
        {
            target = null;
            if (runner == null || !handle.IsValid)
                return false;
            if (!runner.TryFindObject(handle.Object, out NetworkObject owner) || owner == null)
                return false;
            Interactable[] list = Get(owner);
            int index = handle.IndexPlusOne - 1;
            if (index < 0 || index >= list.Length)
                return false;
            target = list[index];
            return target != null && target.isActiveAndEnabled;
        }

        public static void Forget(NetworkObject owner)
        {
            if (owner != null)
                Cache.Remove(owner);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();
    }

    /// <summary>
    /// View-ray sensor shared by the host simulation and the local HUD. Walks sphere-cast hits
    /// nearest-first: a collider bound to an interactable wins (if within its range), triggers
    /// that aren't interactables are ignored (water volumes, zones, hint volumes never block),
    /// and the first solid non-interactable collider blocks the ray (no interacting through walls).
    /// </summary>
    public sealed class InteractionSensor
    {
        private readonly RaycastHit[] _hits;

        public InteractionSensor(int capacity = 16) => _hits = new RaycastHit[Mathf.Max(4, capacity)];

        public bool Probe(Vector3 origin, Vector3 direction, float reach, float radius, int mask,
            Transform ignoreRoot, out Interactable target, out RaycastHit targetHit)
        {
            target = null;
            targetHit = default;

            int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(0f, radius), direction.normalized, _hits,
                Mathf.Max(0.05f, reach), mask, QueryTriggerInteraction.Collide);
            if (count <= 0)
                return false;

            System.Array.Sort(_hits, 0, count, HitDistanceComparer.Instance);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                Collider col = hit.collider;
                if (col == null)
                    continue;
                if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot))
                    continue;

                if (SimWorld.TryGetByCollider(col, out Interactable candidate) && candidate.isActiveAndEnabled)
                {
                    if (hit.distance <= candidate.MaxDistance)
                    {
                        target = candidate;
                        targetHit = hit;
                        return true;
                    }
                    if (col.isTrigger)
                        continue;
                    return false; // solid but out of range: blocks whatever is behind it
                }

                if (col.isTrigger)
                    continue;

                return false; // solid, non-interactable geometry blocks the view ray
            }

            return false;
        }

        private sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
#endif
