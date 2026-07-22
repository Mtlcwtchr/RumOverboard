using UnityEngine;

namespace RumOverboard.Gameplay
{
    /// <summary>
    /// Makes the character's capsule follow the legs during a jump. As the crew member
    /// tucks their knees up (TuckAmount → 1), the capsule shrinks from the FEET while its
    /// head end stays put — so the collider "pulls up" with the legs. In that shortened
    /// pose the body drops to the deck, and on landing (TuckAmount → 0) the capsule snaps
    /// back to full height, which reads as a quick stand-up.
    ///
    /// Driven by <c>NetworkPlayer.SetTuck</c> from the replicated <c>TuckAmount</c>, so the
    /// simulated capsule (host + predicting client) and the visual match everywhere.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class JumpColliderDriver : MonoBehaviour
    {
        [SerializeField] private CapsuleCollider capsule;

        [Tooltip("Fraction of the capsule height removed at a full tuck (0 = none, 0.9 = almost gone).")]
        [Range(0f, 0.9f)] [SerializeField] private float tuckShrink = 0.45f;

        private float _baseHeight;
        private Vector3 _baseCenter;
        private bool _cached;

        private void Awake() => Cache();

        private void Cache()
        {
            if (_cached) return;
            if (capsule == null) capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                _baseHeight = capsule.height;
                _baseCenter = capsule.center;
            }
            _cached = true;
        }

        /// <summary>Idempotent; safe to call every tick and every frame.</summary>
        public void SetTuck(float amount)
        {
            Cache();
            if (capsule == null) return;

            amount = Mathf.Clamp01(amount);
            float height = _baseHeight * (1f - amount * tuckShrink);

            // Keep the head end fixed and raise the bottom by half the removed height:
            // the feet come up while the shoulders stay, i.e. knees-to-chest.
            capsule.height = height;
            capsule.center = _baseCenter + Vector3.up * ((_baseHeight - height) * 0.5f);
        }

        public void ResetCollider() => SetTuck(0f);
    }
}
