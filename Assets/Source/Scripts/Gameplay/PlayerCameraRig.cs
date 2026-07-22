#if FUSION2
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay
{
    /// <summary>
    /// The local crew member's camera. Lives on the gameplay Main Camera and follows
    /// whichever NetworkPlayer has input authority (registered by NetworkPlayer.Spawned).
    ///
    ///   • Normal play  → first-person, PEAK-style: the camera sits at the head anchor
    ///     and mouse-look drives yaw/pitch. ConnectionManager.OnInput reads this camera's
    ///     yaw, so movement stays relative to where you look.
    ///   • Knocked out  → when the ragdoll takes over (RagdollControl ≈ 1) the view pulls
    ///     back into a third-person orbit around the limp body, so you can watch the flop.
    ///
    /// Yaw persists across the switch, so regaining control doesn't snap your heading.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        public static PlayerCameraRig Instance { get; private set; }

        [Header("Mouse look")]
        [SerializeField] private float sensitivity = 0.12f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;
        [SerializeField] private bool lockCursor = true;

        [Header("Third-person (knockout)")]
        [Tooltip("How far back the camera orbits when the crew member is knocked out.")]
        [SerializeField] private float knockoutDistance = 3.5f;
        [SerializeField] private float knockoutHeight = 1.2f;
        [Tooltip("Position smoothing for the third-person orbit (higher = snappier).")]
        [SerializeField] private float followSharpness = 8f;

        private NetworkPlayer _target;
        private float _yaw;
        private float _pitch;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetTarget(NetworkPlayer target)
        {
            _target = target;
            Transform t = target.transform;
            _yaw = t.eulerAngles.y;
            _pitch = 0f;
            ApplyCursorLock(true);
        }

        public void ClearTarget(NetworkPlayer target)
        {
            if (_target == target) _target = null;
            ApplyCursorLock(false);
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            AccumulateMouseLook();

            if (_target.IsKnockedOut)
                UpdateThirdPerson();
            else
                UpdateFirstPerson();
        }

        private void AccumulateMouseLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 delta = mouse.delta.ReadValue() * sensitivity;
            _yaw += delta.x;
            _pitch = Mathf.Clamp(_pitch - delta.y, minPitch, maxPitch);
        }

        private void UpdateFirstPerson()
        {
            Transform anchor = _target.CameraAnchor;
            transform.position = anchor.position;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void UpdateThirdPerson()
        {
            Quaternion look = Quaternion.Euler(Mathf.Max(_pitch, 5f), _yaw, 0f);
            Vector3 pivot = _target.transform.position + Vector3.up * knockoutHeight;
            Vector3 desired = pivot - look * Vector3.forward * knockoutDistance;

            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
        }

        private void ApplyCursorLock(bool active)
        {
            if (!lockCursor) return;
            Cursor.lockState = active ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !active;
        }
    }
}
#endif
