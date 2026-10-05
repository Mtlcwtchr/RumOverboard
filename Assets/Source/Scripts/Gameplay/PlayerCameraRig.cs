#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.UI;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay
{
    /// <summary>
    /// The local crew member's camera + view-ray focus.
    ///
    ///   • Position: the head of the RENDERED (interpolated) body, so the view sits on exactly the
    ///     same timeline as the deck it stands on — no extra smoothing lag, no shimmer.
    ///   • Rotation: mouse look applied immediately (never waits for the host). Yaw is kept
    ///     RELATIVE to the ship the player is aboard (<see cref="NetworkPlayer.Platform"/>), so
    ///     when the ship turns you turn with it; pitch is horizon-relative.
    ///   • Focus: after the camera is placed, a view ray picks the interactable under the crosshair;
    ///     the hint overlay shows it and <see cref="ConnectionManager"/> sends it to the host.
    ///   • Knocked out → third-person orbit around the limp body.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        public static PlayerCameraRig Instance { get; private set; }

        [Header("Mouse look")]
        [SerializeField] private float sensitivity = 0.12f;
        [SerializeField] private float gamepadSensitivity = 160f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;
        [SerializeField] private bool lockCursor = true;

        [Header("Third-person (knockout)")]
        [SerializeField] private float knockoutDistance = 3.5f;
        [SerializeField] private float knockoutHeight = 1.2f;
        [SerializeField] private float followSharpness = 8f;

        [Header("Hints")]
        [SerializeField] private bool showInteractionHints = true;

        private NetworkPlayer _target;
        private float _relYaw;      // yaw relative to _space (or world yaw when _space is none)
        private float _pitch;
        private NetworkId _space;   // NetworkObject our yaw is relative to
        private Interactable _focus;
        private InteractableRef _focusRef;

        /// <summary>Yaw as sent to the host (relative to <see cref="LookSpace"/>).</summary>
        public float Yaw => _relYaw;
        public float Pitch => _pitch;
        public NetworkId LookSpace => _space;
        public InteractableRef FocusRef => _focusRef;
        public Interactable Focus => _focus;
        public NetworkPlayer Target => _target;

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
            _space = default;
            _relYaw = target.transform.eulerAngles.y;
            _pitch = 0f;
            transform.position = target.RenderEyePosition;
            ApplyCursorLock(true);
            HintOverlay.Ensure().SetVisible(true);
        }

        public void ClearTarget(NetworkPlayer target)
        {
            if (_target != target) return;
            _target.SetHeadHidden(false);
            _target = null;
            _focus = null;
            _focusRef = default;
            ApplyCursorLock(false);
            if (HintOverlay.Instance != null)
                HintOverlay.Instance.SetVisible(false);
        }

        private void Update()
        {
            // Escape frees the cursor (menus / alt-tab); clicking back into the game re-locks it.
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                ApplyCursorLock(false);
            var mouse = Mouse.current;
            if (_target != null && mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                ApplyCursorLock(true);
        }

        private void LateUpdate()
        {
            if (_target == null || _target.Object == null || !_target.Object.IsValid)
                return;

            SyncLookSpace();
            AccumulateLook();

            float worldYaw = _relYaw + SpaceYaw(_space);
            bool thirdPerson = _target.IsKnockedOut;
            _target.SetHeadHidden(!thirdPerson);
            float strain = LocalStrain(out string strainLabel);
            if (thirdPerson)
                UpdateThirdPerson(worldYaw);
            else
            {
                transform.position = _target.GetFirstPersonEye(worldYaw);
                Vector3 feel = FeelOffsets(strain);
                transform.rotation = Quaternion.Euler(_pitch + feel.x, worldYaw + feel.y, feel.z);
            }

            HintOverlay.Ensure().SetStrain(thirdPerson ? 0f : strain, strainLabel);
            UpdateFocus();
        }

        // ---- Feel: drunk sway + effort shake (camera only, never the aim we send) ------------------
        [Header("Feel")]
        [Tooltip("Camera shake (deg) at full strain on a rope / the wheel.")]
        [SerializeField] private float strainShake = 0.45f;
        [Tooltip("Jolt (deg) when the wheel kicks in your hands / hits its stop.")]
        [SerializeField] private float kickJolt = 2.2f;

        private byte _lastKick;
        private float _kick;
        private float _kickSign = 1f;

        private Vector3 FeelOffsets(float strain)
        {
            float t = Time.time;
            float drunk = _target.Drunkenness;
            float sway = (_target.Config != null ? _target.Config.DrunkCameraSway : 3.5f) * drunk;
            float pitch = (Mathf.PerlinNoise(t * 0.31f, 2.7f) * 2f - 1f) * sway * 0.4f;
            float yaw = (Mathf.PerlinNoise(t * 0.23f, 5.3f) * 2f - 1f) * sway * 0.6f;
            float roll = (Mathf.PerlinNoise(t * 0.37f, 9.1f) * 2f - 1f) * sway;

            float shake = Mathf.Clamp01((strain - 0.25f) / 0.75f) * strainShake;
            pitch += (Mathf.PerlinNoise(t * 17f, 1.1f) * 2f - 1f) * shake;
            roll += (Mathf.PerlinNoise(t * 15f, 4.4f) * 2f - 1f) * shake;

            _kick = Mathf.MoveTowards(_kick, 0f, Time.deltaTime * 3.5f);
            float jolt = _kick * _kick * kickJolt;
            yaw += jolt * _kickSign * Mathf.Sin(t * 40f);
            roll += jolt * _kickSign;
            return new Vector3(pitch, yaw, roll);
        }

        /// <summary>
        /// How hard what we're holding pulls back (0..1+): the wheel's water load at the helm, the
        /// line's load on a rope. Also detects wheel kicks for a camera jolt.
        /// </summary>
        private float LocalStrain(out string label)
        {
            label = null;
            if (_target.IsSteering && _target.Runner != null &&
                InteractableIndex.TryResolve(_target.Runner, _target.AttachedRef, out Interactable attached) &&
                attached is HelmStation station && station.Helm != null)
            {
                Ocean.ShipHelm helm = station.Helm;
                if (helm.KickCount != _lastKick)
                {
                    _lastKick = helm.KickCount;
                    _kick = 1f;
                    _kickSign = helm.WheelVelocity >= 0f ? 1f : -1f;
                }
                label = "ШТУРВАЛ";
                return Mathf.Abs(helm.Load);
            }

            if (_target.IsHoldingRope && _target.Object != null)
            {
                PlayerRef me = _target.Object.InputAuthority;
                foreach (NetworkShip ship in NetworkShip.All)
                {
                    if (ship == null || ship.Object == null || !ship.Object.IsValid) continue;
                    int line = ship.LineHeldBy(me);
                    if (line < 0) continue;
                    label = "КАНАТ";
                    return ship.GetLineLoad(line);
                }
            }
            return 0f;
        }

        // The host tells us which ship we're aboard; when it changes, re-express our yaw in the
        // new space so the view doesn't jump.
        private void SyncLookSpace()
        {
            NetworkId platform = _target.Platform;
            if (platform == _space)
                return;
            float world = _relYaw + SpaceYaw(_space);
            _space = platform;
            _relYaw = Mathf.DeltaAngle(0f, world - SpaceYaw(_space));
        }

        private float SpaceYaw(NetworkId space)
        {
            if (!space.IsValid || _target.Runner == null)
                return 0f;
            return _target.Runner.TryFindObject(space, out NetworkObject obj) && obj != null
                ? obj.transform.eulerAngles.y
                : 0f;
        }

        private void AccumulateLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked && lockCursor)
                return;

            Vector2 delta = Vector2.zero;
            if (Mouse.current != null)
                delta += Mouse.current.delta.ReadValue() * sensitivity;
            if (Gamepad.current != null)
                delta += Gamepad.current.rightStick.ReadValue() * (gamepadSensitivity * Time.deltaTime);

            _relYaw = Mathf.Repeat(_relYaw + delta.x + 180f, 360f) - 180f;
            _pitch = Mathf.Clamp(_pitch - delta.y, minPitch, maxPitch);
        }

        private void UpdateThirdPerson(float worldYaw)
        {
            Quaternion look = Quaternion.Euler(Mathf.Max(_pitch, 5f), worldYaw, 0f);
            Vector3 pivot = _target.RenderPosition + Vector3.up * knockoutHeight;
            Vector3 desired = pivot - look * Vector3.forward * knockoutDistance;

            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
        }

        private void UpdateFocus()
        {
            HintOverlay hints = HintOverlay.Ensure();
            hints.SetState(_target.ActiveStates);
            hints.SetDrunkenness(_target.Drunkenness);

            _focus = null;
            _focusRef = default;

            // Attached (climbing / at the helm / on a rope): Interact means "let go", the controls
            // panel already says so; don't show a competing look-at prompt.
            if (_target.IsAttached || _target.IsKnockedOut)
            {
                hints.SetPrompt(null, false);
                return;
            }

            // Make sure queries see the colliders where they're RENDERED this frame.
            Physics.SyncTransforms();

            if (_target.ProbeLocalTarget(transform.position, transform.forward, out Interactable focus, out _))
            {
                InteractorInfo me = _target.LocalInteractor;
                bool available = focus.IsAvailable(me);
                _focus = focus;
                _focusRef = available ? focus.Ref : default;
                hints.SetPrompt(showInteractionHints ? focus.GetPrompt(me) : null, available);
            }
            else
            {
                hints.SetPrompt(null, false);
            }
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
