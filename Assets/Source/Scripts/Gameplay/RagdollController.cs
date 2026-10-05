using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay
{
    /// <summary>
    /// LOCAL, cosmetic ragdoll blend. NOT networked: each peer runs its own copy from the
    /// replicated 0..1 amount (NetworkPlayer.RagdollControl).
    ///
    ///   • amount == 0 → fully animated.
    ///   • 0 &lt; amount &lt; knockout threshold → PARTIAL ragdoll: the Animator keeps driving the
    ///       body (you stay on your feet and in control) and a procedural sway is layered on top —
    ///       hips/spine/head loll and arms hang loose, more the higher the amount. Physics bones stay
    ///       kinematic, so a sip of rum can never collapse the skeleton.
    ///   • amount ≥ knockout threshold → full physics ragdoll (knockout): bones go dynamic with a
    ///       weak servo toward the rest pose. It only returns to animation once the amount drops
    ///       below the release threshold (hysteresis — no flicker at the boundary).
    ///
    /// Build the bone bodies/colliders/joints with RumOverboard ▸ Ragdoll ▸ Build On Selected.
    /// </summary>
    public class RagdollController : MonoBehaviour
    {
        [Tooltip("Animator that drives the animated (non-ragdoll) pose. Auto-found in children if empty.")]
        [SerializeField] private Animator animator;

        [Tooltip("The character's main/root Rigidbody (the networked one). Excluded from the ragdoll set.")]
        [SerializeField] private Rigidbody rootBody;

        [Tooltip("Servo strength pulling bones back to the rest pose during a knockout. 0 disables the servo.")]
        [SerializeField] private float restoreStrength = 25f;

        [Tooltip("Angular velocity cap for the servo, rad/s (keeps the blend stable).")]
        [SerializeField] private float maxAngularVelocity = 20f;

        [Header("Thresholds (overridden by GameConfig via Configure)")]
        [Range(0f, 1f)] [SerializeField] private float physicalThreshold = 0.85f;
        [Range(0f, 1f)] [SerializeField] private float releaseThreshold = 0.7f;

        [Header("Partial ragdoll sway")]
        [Tooltip("Peak sway (deg) at the knockout threshold.")]
        [SerializeField] private float swayDegrees = 16f;
        [Tooltip("Sway speed (Hz-ish).")]
        [SerializeField] private float swayFrequency = 0.55f;

        private Rigidbody[] _bones;
        private Collider[] _boneColliders;
        private Quaternion[] _restLocalRotations;
        private float _amount;
        private bool _physical;
        private bool _initialized;
        private float _seed;

        private Transform _hips, _spine, _chest, _neck, _head, _leftArm, _rightArm, _root;

        public float Amount => _amount;
        public bool IsPhysical => _physical;
        public float PhysicalThreshold => physicalThreshold;

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

            if (animator != null && animator.isHuman)
            {
                _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                _head = animator.GetBoneTransform(HumanBodyBones.Head);
                _leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                _rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }
            _root = rootBody != null ? rootBody.transform : (selfBody != null ? selfBody.transform : transform);
            _seed = (GetInstanceID() & 0xFFFF) * 0.013f;
            _initialized = true;

            ApplyPhysical(false); // start animated
        }

        /// <summary>Thresholds + sway tuning (from GameConfig). Cheap; safe to call every frame.</summary>
        public void Configure(float knockoutThreshold, float release, float swayDeg, float swayHz)
        {
            physicalThreshold = Mathf.Clamp(knockoutThreshold, 0.05f, 1f);
            releaseThreshold = Mathf.Clamp(release, 0f, physicalThreshold);
            swayDegrees = Mathf.Max(0f, swayDeg);
            swayFrequency = Mathf.Max(0.01f, swayHz);
        }

        /// <summary>Idempotent — safe to call every frame from NetworkPlayer.Render().</summary>
        public void SetAmount(float amount)
        {
            if (!_initialized) Cache();
            _amount = Mathf.Clamp01(amount);

            bool physical = _physical ? _amount > releaseThreshold : _amount >= physicalThreshold;
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

        // Partial ragdoll: additive sway on top of the animated pose. The Animator rewrites the bones
        // every frame, so this never accumulates. Runs after the Animator and IK rigs.
        private void LateUpdate()
        {
            if (_physical || _amount <= 0.001f || animator == null || !animator.enabled || _hips == null)
                return;

            float a = Mathf.Clamp01(_amount / Mathf.Max(0.05f, physicalThreshold));
            a = a * (2f - a); // ease-out: noticeable early, saturating near the threshold
            float deg = swayDegrees * a;
            float t = Time.time * swayFrequency;

            Vector3 fwd = _root.forward;
            Vector3 right = _root.right;

            float roll = Noise(t, 0f);           // whole-body side lean
            float pitch = Noise(t * 0.8f, 3.1f); // forward/back
            float headRoll = Noise(t * 1.3f, 7.7f);
            float headPitch = Noise(t * 1.1f, 11.3f);

            Tilt(_hips, fwd, right, roll * deg * 0.35f, pitch * deg * 0.15f);
            Tilt(_spine, fwd, right, roll * deg * 0.45f, pitch * deg * 0.3f);
            Tilt(_chest, fwd, right, roll * deg * 0.3f, pitch * deg * 0.2f);
            Tilt(_neck, fwd, right, headRoll * deg * 0.5f, headPitch * deg * 0.35f);
            Tilt(_head, fwd, right, headRoll * deg * 0.6f, (headPitch * 0.6f + 0.25f * a) * deg * 0.5f);

            // Arms hang looser and swing a little out of phase.
            float armSwing = deg * 0.9f;
            Tilt(_leftArm, fwd, right, (0.35f + 0.65f * Noise(t * 1.6f, 17f)) * armSwing, Noise(t * 1.2f, 21f) * armSwing * 0.6f);
            Tilt(_rightArm, fwd, right, -(0.35f + 0.65f * Noise(t * 1.6f, 29f)) * armSwing, Noise(t * 1.2f, 33f) * armSwing * 0.6f);
        }

        private float Noise(float t, float offset) => Mathf.PerlinNoise(t + _seed + offset, _seed * 0.7f + offset) * 2f - 1f;

        private static void Tilt(Transform bone, Vector3 fwd, Vector3 right, float rollDeg, float pitchDeg)
        {
            if (bone == null) return;
            bone.rotation = Quaternion.AngleAxis(rollDeg, fwd) * Quaternion.AngleAxis(pitchDeg, right) * bone.rotation;
        }

        // Knockout servo: drive each bone toward its rest local rotation with a strength that fades
        // to zero as amount → 1. Runs locally on every peer.
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

