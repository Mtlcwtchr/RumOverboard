#if FUSION2
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay
{
    /// <summary>
    /// Procedural hand/foot placement for climbing — the mast, the rigging, a deck ladder,
    /// or a rope when hauling yourself back aboard after going overboard. Runs on every peer
    /// as pure cosmetics (reads the replicated <c>IsClimbing</c> flag off NetworkPlayer, does
    /// its own local surface probe), so it needs no extra networked data.
    ///
    /// Requires a Humanoid avatar and an Animator layer with **IK Pass** enabled (that's what
    /// makes Unity call <see cref="OnAnimatorIK"/>). Weights blend in/out so grabbing and
    /// letting go are smooth rather than popping.
    /// </summary>
    public sealed class ProceduralClimbRig : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private NetworkPlayer player;

        [Header("Surface probe")]
        [Tooltip("Layers that count as climbable (same mask you use on NetworkPlayer).")]
        [SerializeField] private LayerMask climbMask = ~0;
        [SerializeField] private float reach = 0.9f;
        [Tooltip("Horizontal gap between the two hands / feet on the surface.")]
        [SerializeField] private float limbSpread = 0.22f;
        [SerializeField] private float handHeight = 0.35f;
        [SerializeField] private float footDrop = 0.55f;

        [Header("Blend")]
        [Range(0f, 1f)] [SerializeField] private float maxWeight = 0.9f;
        [SerializeField] private float weightLerp = 10f;

        private float _weight;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (player == null) player = GetComponentInParent<NetworkPlayer>();
        }

        /// <summary>Runtime wiring when the rig is added by NetworkPlayer instead of the prefab.</summary>
        public void Configure(NetworkPlayer owner, LayerMask mask)
        {
            player = owner;
            climbMask = mask;
            if (animator == null) animator = GetComponent<Animator>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null) return;

            bool climbing = player != null && player.IsClimbing;
            _weight = Mathf.MoveTowards(_weight, climbing ? maxWeight : 0f, weightLerp * Time.deltaTime);

            if (_weight <= 0.001f)
            {
                ClearLimb(AvatarIKGoal.LeftHand);
                ClearLimb(AvatarIKGoal.RightHand);
                ClearLimb(AvatarIKGoal.LeftFoot);
                ClearLimb(AvatarIKGoal.RightFoot);
                return;
            }

            // Probe forward from the chest into the surface the crew member is hugging.
            Transform a = animator.transform;
            Vector3 origin = BonePosition(HumanBodyBones.Chest, a.position + Vector3.up * 1.2f);
            if (!Physics.Raycast(origin, a.forward, out RaycastHit hit, reach, climbMask, QueryTriggerInteraction.Collide))
            {
                // No surface right in front — fade the goals out but keep the weight we have.
                ClearLimb(AvatarIKGoal.LeftHand);
                ClearLimb(AvatarIKGoal.RightHand);
                ClearLimb(AvatarIKGoal.LeftFoot);
                ClearLimb(AvatarIKGoal.RightFoot);
                return;
            }

            Vector3 right = Vector3.Cross(Vector3.up, hit.normal);
            right = right.sqrMagnitude > 0.001f ? right.normalized : a.right;
            Quaternion grip = Quaternion.LookRotation(-hit.normal, Vector3.up);

            SetLimb(AvatarIKGoal.LeftHand,  hit.point - right * limbSpread + Vector3.up * handHeight, grip);
            SetLimb(AvatarIKGoal.RightHand, hit.point + right * limbSpread + Vector3.up * handHeight, grip);
            SetLimb(AvatarIKGoal.LeftFoot,  hit.point - right * limbSpread - Vector3.up * footDrop,  grip);
            SetLimb(AvatarIKGoal.RightFoot, hit.point + right * limbSpread - Vector3.up * footDrop,  grip);
        }

        private void SetLimb(AvatarIKGoal goal, Vector3 pos, Quaternion rot)
        {
            animator.SetIKPositionWeight(goal, _weight);
            animator.SetIKRotationWeight(goal, _weight);
            animator.SetIKPosition(goal, pos);
            animator.SetIKRotation(goal, rot);
        }

        private void ClearLimb(AvatarIKGoal goal)
        {
            animator.SetIKPositionWeight(goal, 0f);
            animator.SetIKRotationWeight(goal, 0f);
        }

        private Vector3 BonePosition(HumanBodyBones bone, Vector3 fallback)
        {
            Transform t = animator.isHuman ? animator.GetBoneTransform(bone) : null;
            return t != null ? t.position : fallback;
        }
    }

    /// <summary>
    /// Head-only look IK driven by replicated camera yaw/pitch.
    /// Uses Animator look-at so the neck stays within a natural range.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralLookRig : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private NetworkPlayer player;

        [Header("Look-at")]
        [SerializeField] private float targetDistance = 8f;
        [SerializeField] private float weightLerp = 10f;
        [Range(0f, 89f)] [SerializeField] private float maxPitchUp = 60f;
        [Range(0f, 89f)] [SerializeField] private float maxPitchDown = 55f;

        [Header("Weights")]
        [Range(0f, 1f)] [SerializeField] private float bodyWeight = 0f;
        [Range(0f, 1f)] [SerializeField] private float headWeight = 0.95f;
        [Range(0f, 1f)] [SerializeField] private float eyesWeight = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float clampWeight = 0.45f;

        private float _weight;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (player == null) player = GetComponentInParent<NetworkPlayer>();
        }

        /// <summary>Runtime wiring when added by NetworkPlayer.</summary>
        public void Configure(NetworkPlayer owner)
        {
            player = owner;
            if (animator == null) animator = GetComponent<Animator>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || !animator.enabled || !animator.isHuman)
                return;

            bool canLook = player != null && !player.IsKnockedOut;
            _weight = Mathf.MoveTowards(_weight, canLook ? 1f : 0f, weightLerp * Time.deltaTime);
            if (_weight <= 0.001f)
            {
                animator.SetLookAtWeight(0f);
                return;
            }

            float pitch = Mathf.Clamp(player.ViewPitch, -maxPitchDown, maxPitchUp);
            Vector3 origin = player.CameraAnchor.position;
            Vector3 dir = Quaternion.Euler(pitch, player.ViewYaw, 0f) * Vector3.forward;

            animator.SetLookAtWeight(_weight, bodyWeight, headWeight, eyesWeight, clampWeight);
            animator.SetLookAtPosition(origin + dir * Mathf.Max(1f, targetDistance));
        }
    }
}
#endif
