#if FUSION2
using System.Collections.Generic;
using Fusion;
using RumOverboard.Core.Character;
using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// Host-side "one item in hands" for a crew member (GDD §26.3). Plain C#, owned by NetworkPlayer.
    /// The item stays a dynamic body: it's driven to the hold point by velocity (<see cref="AttachMotor"/>),
    /// so it collides with the world (a long board catches on the rigging) and NetworkRigidbody3D replicates
    /// it like any other body. Collisions with the carrier's own body are ignored while held and come back
    /// shortly after release (no pop out of the chest).
    /// </summary>
    public sealed class CrewCarry
    {
        private const float SnagDistance = 1.5f;  // m from the hold point
        private const float SnagTime = 0.4f;      // s stuck that far → it slips out of the hands
        private const float RestoreDelay = 0.3f;  // s before the dropped item collides with us again

        private readonly Collider[] _own;
        private readonly List<(Collider[] item, float at)> _restore = new();
        private float _snagged;

        public WorldItem Item { get; private set; }
        public bool IsCarrying => Item != null;

        public CrewCarry(Collider[] ownColliders) => _own = ownColliders ?? new Collider[0];

        public void PickUp(WorldItem item, PlayerRef carrier, Vector3 holdPoint, bool snapToHands)
        {
            if (item == null) return;
            Item = item;
            item.Holder = carrier;
            item.Body.useGravity = false;
            SetIgnore(item.Colliders, true);
            if (snapToHands) // taken out of a container: appear in the hands, not inside the chest
            {
                item.Teleport(holdPoint);
                item.Body.linearVelocity = Vector3.zero;
            }
            _snagged = 0f;
        }

        /// <summary>Drive the item to the hold point. Returns false if it got snagged and slipped out.</summary>
        public bool Drive(Vector3 holdPoint, Quaternion holdRotation, Vector3 carrierVelocity, float dt)
        {
            if (Item == null) return false;
            Rigidbody body = Item.Body;
            AttachMotor.Drive(body, holdPoint, carrierVelocity, holdRotation, dt, catchUpTime: 0.08f, maxCorrectionSpeed: 10f);

            _snagged = (holdPoint - body.position).sqrMagnitude > SnagDistance * SnagDistance ? _snagged + dt : 0f;
            return _snagged < SnagTime;
        }

        /// <summary>Let go with <paramref name="velocity"/> (put down = carrier velocity, throw = + throw speed).</summary>
        public void Release(Vector3 velocity, float now)
        {
            WorldItem item = Item;
            Item = null;
            if (item == null || item.Object == null || !item.Object.IsValid) return;
            item.Holder = PlayerRef.None;
            item.Body.useGravity = true;
            item.Body.linearVelocity = velocity;
            _restore.Add((ToArray(item.Colliders), now + RestoreDelay));
        }

        /// <summary>The item went into a container / broke / vanished: forget it without touching its body.</summary>
        public void Forget()
        {
            WorldItem item = Item;
            Item = null;
            if (item == null) return;
            SetIgnore(item.Colliders, false);
            if (item.Object != null && item.Object.IsValid && !item.IsStored)
                item.Body.useGravity = true;
        }

        /// <summary>Restore collisions of recently dropped items.</summary>
        public void Tick(float now)
        {
            for (int i = _restore.Count - 1; i >= 0; i--)
            {
                if (now < _restore[i].at) continue;
                SetIgnore(_restore[i].item, false);
                _restore.RemoveAt(i);
            }
        }

        private void SetIgnore(IReadOnlyList<Collider> itemColliders, bool ignore)
        {
            if (itemColliders == null) return;
            for (int i = 0; i < itemColliders.Count; i++)
            {
                Collider a = itemColliders[i];
                if (a == null) continue;
                foreach (Collider b in _own)
                    if (b != null) Physics.IgnoreCollision(a, b, ignore);
            }
        }

        private static Collider[] ToArray(IReadOnlyList<Collider> list)
        {
            var arr = new Collider[list.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = list[i];
            return arr;
        }
    }
}
#endif
