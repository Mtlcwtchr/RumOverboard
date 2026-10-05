using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay
{
    /// <summary>
    /// LOCAL, cosmetic active-ragdoll blend. NOT networked: each peer runs its own
    /// copy from the replicated 0..1 amount (NetworkPlayer.RagdollControl).
    ///
    ///   • amount == 0  → fully animated: bone bodies kinematic, Animator drives the pose.
    ///   • 0 < amount < 1 → "powered ragdoll": bones go dynamic; a per-bone servo pulls
    ///       them back toward the rest pose with strength scaled by (1 - amount), so the
    ///       drunker/more-hit you are, the floppier you get.
    ///   • amount == 1  → full flop (knockout): no servo, physics owns everything.
    ///
    /// Build the bone bodies/colliders/joints with RumOverboard ▸ Ragdoll ▸ Build On Selected.
    /// </summary>
    public class RagdollController : MonoBehaviour
    {
        [Tooltip("Animator that drives the animated (non-ragdoll) pose. Auto-found in children if empty.")]
        [SerializeField] private Animator animator;

        [Tooltip("The character's main/root Rigidbody (the networked one). Excluded from the ragdoll set.")]
        [SerializeField] private Rigidbody rootBody;

        [Tooltip("Servo strength pulling bones back to the rest pose at amount→0. 0 disables the servo.")]
        [SerializeField] private float restoreStrength = 25f;

        [Tooltip("Angular velocity cap for the servo, rad/s (keeps the blend stable).")]
        [SerializeField] private float maxAngularVelocity = 20f;

        private Rigidbody[] _bones;
        private Collider[] _boneColliders;
        private Quaternion[] _restLocalRotations;
        private float _amount;
        private bool _physical;
        private bool _initialized;

        public float Amount => _amount;

        /// <summary>How many ragdolls are physical right now (clients step cosmetic physics only then).</summary>
        public static int PhysicalCount { get; private set; }

        private void Awake() => Cache();

        private void OnDestroy()
        {
            if (_physical) PhysicalCount--;
            _physical = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PhysicalCount = 0;

        private void Cache()
        {
            if (_initialized) return;
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // The main/networked body must never be treated as a ragdoll bone (making it
            // kinematic would freeze movement). Exclude the explicitly-assigned rootBody AND,
            // defensively, whatever Rigidbody sits on this same GameObject — so a mis-assigned
            // rootBody can't silently capture the character's main body.
            Rigidbody selfBody = GetComponent<Rigidbody>();

            var bodies = new List<Rigidbody>();
            var colliders = new List<Collider>();
            var rests = new List<Quaternion>();
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == rootBody || rb == selfBody) continue; // never touch the networked root body
                bodies.Add(rb);
                rests.Add(rb.transform.localRotation);
                if (rb.TryGetComponent(out Collider col))
                    colliders.Add(col);
            }
            _bones = bodies.ToArray();
            _boneColliders = colliders.ToArray();
            _restLocalRotations = rests.ToArray();
            _initialized = true;

            ApplyPhysical(false); // start animated
        }

        /// <summary>Idempotent — safe to call every frame from NetworkPlayer.Render().</summary>
        public void SetAmount(float amount)
        {
            if (!_initialized) Cache();
            _amount = Mathf.Clamp01(amount);

            bool physical = _amount > 0.001f;
            if (physical != _physical)
            {
                _physical = physical;
                PhysicalCount += physical ? 1 : -1;
                ApplyPhysical(physical);
            }
        }

        private void ApplyPhysical(bool physical)
        {
            if (animator != null) animator.enabled = !physical;

            for (int i = 0; i < _bones.Length; i++)
            {
                var rb = _bones[i];
                if (rb == null) continue;
                rb.isKinematic = !physical;
                rb.detectCollisions = physical;
                if (physical)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            for (int i = 0; i < _boneColliders.Length; i++)
                if (_boneColliders[i] != null)
                    _boneColliders[i].enabled = physical;
        }

        // Powered-ragdoll servo: drive each bone toward its rest local rotation with a
        // strength that fades to zero as amount → 1. Runs locally on every peer.
        private void FixedUpdate()
        {
            if (!_physical) return;

            float strength = restoreStrength * (1f - _amount);
            if (strength <= 0f) return;

            for (int i = 0; i < _bones.Length; i++)
            {
                var rb = _bones[i];
                if (rb == null) continue;

                Transform parent = rb.transform.parent;
                Quaternion targetWorld = (parent != null ? parent.rotation : Quaternion.identity) * _restLocalRotations[i];
                Quaternion error = targetWorld * Quaternion.Inverse(rb.rotation);

                error.ToAngleAxis(out float angleDeg, out Vector3 axis);
                if (angleDeg > 180f) angleDeg -= 360f;
                if (Mathf.Abs(angleDeg) < 0.01f || float.IsInfinity(axis.x)) continue;

                Vector3 angVel = axis.normalized * (angleDeg * Mathf.Deg2Rad * strength);
                rb.angularVelocity = Vector3.ClampMagnitude(angVel, maxAngularVelocity);
            }
        }
    }
}
