#if FUSION2
using System.Collections.Generic;
using Fusion;
using RumOverboard.Core;
using RumOverboard.Core.Character;
using RumOverboard.Core.Character.States;
using RumOverboard.Gameplay;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Gameplay.Ocean.Features.Rigging;
using RumOverboard.StateMachine;
using RumOverboard.StateMachine.Serialization;
using UnityEngine;

namespace RumOverboard.Networking
{
    /// <summary>
    /// All of the crew member's replicated state in one struct, so NetworkPlayer's networked
    /// surface stays tiny. (The payload bytes are a separate NetworkArray because arrays can't
    /// live inside a plain INetworkStruct.)
    /// </summary>
    public struct PlayerNetState : INetworkStruct
    {
        public float Drunkenness;    // 0..1
        public float RagdollControl; // 0..1 — how much authority the ragdoll has
        public float PlanarSpeed;    // for animation on all peers
        public float TuckAmount;     // 0..1 jump/land leg-tuck — drives the capsule + anim on all peers
        public float LookYaw;        // world yaw, for cosmetics (head look)
        public float LookPitch;      // pitch, for cosmetics
        public uint StateMask;       // active PlayerState bit set
        public byte PayloadVersion;
        public byte PayloadLength;

        /// <summary>Ship (NetworkObject) the crew member is aboard — the camera's yaw space.</summary>
        public NetworkId Platform;

        /// <summary>Interactable we're attached to (rail / helm / rope station), for hints + IK.</summary>
        public InteractableRef Attached;

        public PlayerState States
        {
            get => (PlayerState)StateMask;
            set => StateMask = (uint)value;
        }
    }

    /// <summary>
    /// The networked crew member: driver for its mask-based state machine.
    ///
    /// AUTHORITY MODEL — pure host authority, no client prediction:
    ///   • Clients only send input (<see cref="NetworkInputData"/>): move axes, ship-relative look,
    ///     buttons and the interactable their hint shows.
    ///   • The host runs everything in <see cref="FixedUpdateNetwork"/> (sensors → state machine →
    ///     Rigidbody), validates the target (range + availability) and publishes the result.
    ///   • Every peer renders every body — ship AND players — from NetworkRigidbody3D snapshot
    ///     interpolation on the same timeline, so a crew member standing on the deck is always
    ///     consistent with the deck under them (no predicted-player-on-interpolated-ship jitter).
    ///   • Mouse look is applied locally and immediately by <see cref="PlayerCameraRig"/>, so the
    ///     view never feels lagged even though movement is host-confirmed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkPlayer : NetworkBehaviour
    {
        private const int PayloadCapacity = 64;

        [SerializeField] private GameConfig config;
        [SerializeField] private RagdollController ragdoll; // local cosmetic
        [SerializeField] private Animator animator;

        [Header("Procedural animation")]
        [Tooltip("Drives the capsule so it follows the tucked legs during a jump. Auto-found on this object.")]
        [SerializeField] private JumpColliderDriver jumpCollider;

        [Tooltip("Eye/head anchor the first-person camera snaps to for the local player. " +
                 "Falls back to this transform if unset.")]
        [SerializeField] private Transform cameraAnchor;

        [Header("First-person view")]
        [Tooltip("Eye point relative to the head bone, in the view's yaw frame (x right, y up, z forward).")]
        [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 0.09f, 0.11f);
        [Tooltip("How much of the head's vertical bob the camera follows (0 = steady anchor height, 1 = full bob).")]
        [Range(0f, 1f)] [SerializeField] private float headBobFollow = 0.45f;

        [Header("Sensors")]
        [SerializeField] private float groundProbe = 0.25f;
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("Kept for the procedural climb IK probe.")]
        [SerializeField] private LayerMask climbMask = ~0;

        [Tooltip("Optional water layers for trigger-based swimming detection (ocean height is used too).")]
        [SerializeField] private LayerMask waterMask;

        [Tooltip("Layers the view ray can hit (interactables + anything that should block them).")]
        [SerializeField] private LayerMask interactionMask = ~0;
        [Tooltip("Max view-ray length (each interactable also has its own range).")]
        [SerializeField] private float interactionReach = 3.2f;
        [SerializeField] private float interactionProbeRadius = 0.06f;
        [Tooltip("Extra range the host tolerates on top of an interactable's range (latency slack).")]
        [SerializeField] private float hostRangeSlack = 1.0f;

        [Header("Look follow")]
        [Tooltip("How far the head can yaw before the torso starts catching up (deg).")]
        [SerializeField] private float neckYawLimit = 60f;
        [Tooltip("Yaw offset left after torso catch-up starts (deg). Lower = body turns more aggressively.")]
        [SerializeField] private float keepHeadYawAfterBodyTurn = 30f;
        [SerializeField] private float lookBodyTurnSpeedDeg = 540f;

        // --- Replicated state ---
        [Networked] public PlayerNetState State { get; set; }
        [Networked, Capacity(PayloadCapacity)] public NetworkArray<byte> PayloadBytes { get; }
        [Networked] private NetworkButtons PreviousButtons { get; set; }

        private Rigidbody _rb;
        private CapsuleCollider _capsule;
        private PlayerStateMachine _machine;
        private StateContext _ctx;
        private readonly HashSet<Collider> _waterTriggers = new();
        private readonly byte[] _payloadScratch = new byte[PayloadCapacity];
        private InteractionSensor _sensor;
        private OceanWaveField _ocean;
        private float _smoothedAnimatorSpeed;
        private byte _lastPayloadVersion;

        // Host-side bookkeeping for attached interactables.
        private Interactable _target;   // validated look-at target this tick
        private Interactable _attached; // what we're currently using
        private LayerMask _defaultExclude;

        // Rendering: the transform NetworkRigidbody3D actually moves for display.
        private Transform _renderRoot;
        private Vector3 _eyeInRenderRoot;
        private Transform _headBone;
        private Vector3 _headBoneScale = Vector3.one;
        private bool _headHidden;

        private static PhysicsMaterial _frictionless;

        public static readonly List<NetworkPlayer> All = new();

        /// <summary>Where the first-person camera sits (head height), at the simulated pose.</summary>
        public Transform CameraAnchor => cameraAnchor != null ? cameraAnchor : transform;

        /// <summary>Head position at the RENDERED (interpolated) pose — what the camera should follow.</summary>
        public Vector3 RenderEyePosition =>
            _renderRoot != null ? _renderRoot.TransformPoint(_eyeInRenderRoot) : CameraAnchor.position;

        /// <summary>
        /// First-person camera point: in the eyes of the animated head (rendered pose), so the
        /// camera never sits behind the head when the run cycle leans the body forward.
        /// <paramref name="viewYaw"/> orients <see cref="eyeOffset"/>.
        /// </summary>
        public Vector3 GetFirstPersonEye(float viewYaw)
        {
            Vector3 steady = RenderEyePosition;
            if (_headBone == null)
                return steady;
            Vector3 eye = _headBone.position + Quaternion.Euler(0f, viewYaw, 0f) * eyeOffset;
            eye.y = Mathf.Lerp(steady.y, eye.y, headBobFollow);
            return eye;
        }

        /// <summary>Local first-person: collapse the head so the camera never sees its own skull.</summary>
        public void SetHeadHidden(bool hidden)
        {
            if (_headBone == null || hidden == _headHidden)
                return;
            _headHidden = hidden;
            _headBone.localScale = hidden ? _headBoneScale * 0.001f : _headBoneScale;
        }

        /// <summary>Body position at the rendered pose (third-person pivot).</summary>
        public Vector3 RenderPosition => _renderRoot != null ? _renderRoot.position : transform.position;

        /// <summary>True once the ragdoll has taken (nearly) full control — a knockout.</summary>
        public bool IsKnockedOut => State.RagdollControl >= 0.9f;

        public float Drunkenness => State.Drunkenness;
        public float RagdollControl => State.RagdollControl;
        public float ViewYaw => State.LookYaw;
        public float ViewPitch => State.LookPitch;
        public Vector3 ViewDirection => Quaternion.Euler(State.LookPitch, State.LookYaw, 0f) * Vector3.forward;

        public float PlanarSpeed => State.PlanarSpeed;
        public float TuckAmount  => State.TuckAmount;
        public PlayerState ActiveStates => State.States;
        public bool IsClimbing   => (State.States & PlayerState.Climbing) != 0;
        public bool IsInAir      => (State.States & PlayerState.InAir) != 0;
        public bool IsSwimming   => (State.States & PlayerState.Swimming) != 0;
        public bool IsSteering   => (State.States & PlayerState.Steering) != 0;
        public bool IsHoldingRope => (State.States & PlayerState.HoldingRope) != 0;
        public bool IsAttached   => (State.States & (PlayerState.Climbing | PlayerState.Steering)) != 0;
        public NetworkId Platform => State.Platform;
        public InteractableRef AttachedRef => State.Attached;
        public int InteractionMask => interactionMask;
        public float InteractionReach => interactionReach;
        public float InteractionProbeRadius => interactionProbeRadius;

        /// <summary>Test/automation hook: when set on the host, replaces this player's network input.</summary>
        public System.Func<NetworkInputData> InputOverride { get; set; }

        // =====================================================================================
        // Lifecycle
        // =====================================================================================
        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            if (ragdoll == null) ragdoll = GetComponentInChildren<RagdollController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (jumpCollider == null) jumpCollider = GetComponent<JumpColliderDriver>();
            if (jumpCollider == null && _capsule != null) jumpCollider = gameObject.AddComponent<JumpColliderDriver>();
            _ocean = FindAnyObjectByType<OceanWaveField>();

            ConfigurePhysicsLayers();
            ConfigureRenderRoot();
            ConfigureProceduralRigs();

            // Unity interpolation would fight NetworkRigidbody3D's own render interpolation.
            _rb.interpolation = RigidbodyInterpolation.None;
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            if (HasStateAuthority)
            {
                _rb.isKinematic = false;
                _rb.maxDepenetrationVelocity = 3f; // spawning/clipping into the deck never launches anyone
            }
            else
            {
                // Fusion puts the input-authority object into the CLIENT's simulation by default (for
                // prediction). We don't predict: take it out, so NetworkRigidbody3D keeps the body
                // kinematic and renders it in the same (remote) timeframe as the ship under it.
                if (Object.IsInSimulation)
                    Runner.SetIsSimulated(Object, false);
                _rb.isKinematic = true;
            }

            _machine = PlayerStateMachineFactory.Build();
            _sensor = new InteractionSensor();
            _ctx = new StateContext
            {
                Body = _rb,
                Transform = transform,
                Animator = animator,
                Config = config,
                Machine = _machine,
            };
            _machine.Start(_ctx);
            _lastPayloadVersion = State.PayloadVersion;

            if (HasStateAuthority)
            {
                var s = State;
                s.States = _machine.Active;
                s.LookYaw = transform.eulerAngles.y;
                State = s;
            }

            All.Add(this);

            if (HasInputAuthority)
                PlayerCameraRig.Instance?.SetTarget(this);

            Debug.Log($"[NetworkPlayer] Spawned id={Object.Id} inputAuth={Object.InputAuthority} " +
                      $"stateAuth={HasStateAuthority} local={HasInputAuthority} mode={Runner.GameMode}");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            _waterTriggers.Clear();
            if (HasStateAuthority && _attached != null)
                ReleaseStation(_attached);
            if (HasStateAuthority && _heldShip != null && _heldLine >= 0)
                _heldShip.DropLine(_heldLine, Object.InputAuthority);
            _attached = null;
            if (PlayerCameraRig.Instance != null)
                PlayerCameraRig.Instance.ClearTarget(this);
        }

        /// <summary>Server-side hazards (wave, flying fish, falling mast...) knock a crew member out.</summary>
        public void Knockout()
        {
            if (!HasStateAuthority) return;
            var s = State;
            s.RagdollControl = 1f;
            State = s;
        }

        private void ConfigurePhysicsLayers()
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            int ragdollLayer = LayerMask.NameToLayer("Ragdoll");
            if (playerLayer >= 0)
                gameObject.layer = playerLayer;
            if (ragdollLayer >= 0)
                foreach (Rigidbody bone in GetComponentsInChildren<Rigidbody>(true))
                    if (bone != _rb)
                        bone.gameObject.layer = ragdollLayer;

            // The interaction ray / ground probe must never see our own (or anyone's) body.
            int exclude = 0;
            if (playerLayer >= 0) exclude |= 1 << playerLayer;
            if (ragdollLayer >= 0) exclude |= 1 << ragdollLayer;
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) exclude |= 1 << ui;
            exclude |= 1 << 2; // Ignore Raycast
            interactionMask &= ~exclude;
            groundMask &= ~exclude;
            climbMask &= ~exclude;

            if (_capsule != null)
            {
                _capsule.enabled = true;
                _capsule.isTrigger = false;
                if (_frictionless == null)
                    _frictionless = new PhysicsMaterial("CrewFrictionless")
                    {
                        dynamicFriction = 0f,
                        staticFriction = 0f,
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                        bounciness = 0f,
                        bounceCombine = PhysicsMaterialCombine.Minimum,
                    };
                _capsule.sharedMaterial = _frictionless; // velocity is commanded; friction only snags on walls
                _defaultExclude = _capsule.excludeLayers;
            }
        }

        private void ConfigureRenderRoot()
        {
            // NetworkRigidbody3D moves its interpolation target (host) or the root (proxies) for
            // display. Follow whichever it uses so the camera sees the same interpolated pose as
            // everybody else (the ship included).
            _renderRoot = transform;
            if (TryGetComponent(out Fusion.Addons.Physics.NetworkRigidbody3D nrb))
            {
                nrb.UsePreciseRotation = true;
                if (nrb.InterpolationTarget != null)
                    _renderRoot = nrb.InterpolationTarget;
            }
            _eyeInRenderRoot = _renderRoot.InverseTransformPoint(CameraAnchor.position);

            if (animator != null && animator.isHuman)
                _headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (_headBone != null)
                _headBoneScale = _headBone.localScale;
        }

        private void ConfigureProceduralRigs()
        {
            if (animator == null)
                return;

            var climbRig = animator.GetComponent<ProceduralClimbRig>();
            if (climbRig == null) climbRig = animator.gameObject.AddComponent<ProceduralClimbRig>();
            climbRig.Configure(this, interactionMask); // rails live on regular layers now

            var lookRig = animator.GetComponent<ProceduralLookRig>();
            if (lookRig == null) lookRig = animator.gameObject.AddComponent<ProceduralLookRig>();
            lookRig.Configure(this);
        }

        // =====================================================================================
        // Simulation (host only)
        // =====================================================================================
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            // A tick whose input hasn't arrived (network jitter) repeats the last one. Falling back to
            // default would read a held button as released → pressed again: a double Interact
            // that grabs and instantly lets go.
            NetworkInputData input;
            if (InputOverride != null)
                input = InputOverride();
            else if (GetInput(out NetworkInputData received))
                input = _lastInput = received;
            else
                input = _lastInput;

            var s = State;
            PlayerState before = _machine.Active;

            FillContext(input, s);
            _machine.FixedTick(_ctx);
            PlayerState after = _machine.Active;

            HandleAttachmentChanges(before, after, input);
            DriveStations(input);
            HandleRigging(input);
            UpdateCollisionMode();
            UpdatePlatform(ref s);
            if ((_machine.Active & (PlayerState.Climbing | PlayerState.Steering)) == 0)
                ApplyLookDrivenBodyTurn();

            s.Drunkenness = _ctx.Drunkenness;
            s.PlanarSpeed = _ctx.PlanarSpeed;
            s.LookYaw = _ctx.LookYaw;
            s.LookPitch = _ctx.LookPitch;
            s.States = _machine.Active;
            s.Attached = _attached != null ? _attached.Ref : InteractableRef.None;
            if (_ctx.RagdollPressed)
                s.RagdollControl = 1f;
            UpdateRagdollControl(ref s);
            UpdateTuck(ref s);
            PublishPayload(ref s);
            State = s;

            if (jumpCollider != null)
                jumpCollider.SetTuck(s.TuckAmount);
        }

        private void FillContext(NetworkInputData input, PlayerNetState s)
        {
            NetworkButtons pressed = input.Buttons.GetPressed(PreviousButtons);
            PreviousButtons = input.Buttons;

            _ctx.ControlAuthority = 1f - s.RagdollControl;
            _ctx.Move = input.Move * _ctx.ControlAuthority;
            _ctx.LookYaw = ResolveWorldYaw(input);
            _ctx.LookPitch = Mathf.Clamp(input.LookPitch, -89f, 89f);
            _ctx.JumpPressed = pressed.IsSet(NetworkInputData.ButtonJump);
            _ctx.InteractPressed = pressed.IsSet(NetworkInputData.ButtonInteract);
            _ctx.DrinkPressed = pressed.IsSet(NetworkInputData.ButtonDrink);
            _ctx.RagdollPressed = pressed.IsSet(NetworkInputData.ButtonRagdoll);
            _ctx.SprintHeld = input.Buttons.IsSet(NetworkInputData.ButtonSprint);
            _dropPressed = pressed.IsSet(NetworkInputData.ButtonDrop);
            _ctx.ReleaseRequested = false;

            GroundProbeResult ground = GroundProbeService.Probe(transform, _rb, groundProbe, groundMask);
            _ctx.IsGrounded = ground.IsGrounded;
            _ctx.GroundVelocity = ground.GroundVelocity;
            _ctx.GroundNormal = ground.GroundNormal;
            _groundBody = ground.IsGrounded ? ground.Body : null;
            _ctx.GroundBody = _groundBody;

            _ctx.WaterSurfaceY = _ocean != null ? _ocean.SampleHeight(transform.position) : float.NegativeInfinity;
            bool belowSurface = transform.position.y + 0.9f < _ctx.WaterSurfaceY;
            _ctx.InWater = _waterTriggers.Count > 0 || (belowSurface && !_ctx.IsGrounded);

            _ctx.StepAhead = _ctx.IsGrounded && ProbeStep();
            ResolveTarget(input);

            _ctx.Drunkenness = s.Drunkenness;
            _ctx.DeltaTime = Runner.DeltaTime;
            _ctx.Time = Runner.SimulationTime;
        }

        private Rigidbody _groundBody;
        private NetworkInputData _lastInput;

        // Ankle-high ray blocked by something steep, knee-high ray free → a step we can walk up.
        private bool ProbeStep()
        {
            Vector3 wish = Quaternion.Euler(0f, _ctx.LookYaw, 0f) * new Vector3(_ctx.Move.x, 0f, _ctx.Move.y);
            if (wish.sqrMagnitude < 0.04f)
                return false;
            wish.Normalize();
            Vector3 feet = transform.position;
            float reach = (_capsule != null ? _capsule.radius : 0.28f) + 0.25f;
            if (!Physics.Raycast(feet + Vector3.up * 0.06f, wish, out RaycastHit low, reach, groundMask, QueryTriggerInteraction.Ignore))
                return false;
            if (low.normal.y > 0.6f)
                return false; // a ramp, the slope projection already handles it
            return !Physics.Raycast(feet + Vector3.up * 0.42f, wish, reach + 0.1f, groundMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Input yaw is relative to the ship the client thinks we're on (see camera rig).</summary>
        private float ResolveWorldYaw(NetworkInputData input)
        {
            if (input.LookSpace.IsValid && Runner.TryFindObject(input.LookSpace, out NetworkObject space) && space != null)
                return input.LookYaw + space.transform.eulerAngles.y;
            return input.LookYaw;
        }

        /// <summary>
        /// Host-side validation of the client's look-at target: it must exist, be within its range
        /// (+ latency slack) of our eye, and be available to us. Only then is it exposed to the
        /// state machine, typed by kind.
        /// </summary>
        private void ResolveTarget(NetworkInputData input)
        {
            _target = null;
            _ctx.ClimbTarget = null;
            _ctx.HelmTarget = null;
            _ctx.HasInteractionTarget = false;

            if (!InteractableIndex.TryResolve(Runner, input.Target, out Interactable candidate))
                return;

            Vector3 eye = CameraAnchor.position;
            float dist = candidate.DistanceTo(eye, out Vector3 closest);
            if (dist > candidate.MaxDistance + hostRangeSlack)
                return;

            var who = Interactor(eye, transform.position);
            if (!candidate.IsAvailable(who))
                return;

            _target = candidate;
            _ctx.HasInteractionTarget = true;
            _ctx.TargetPoint = closest;
            switch (candidate)
            {
                case ClimbSurface rail:
                    _ctx.ClimbTarget = rail;
                    _ctx.TargetPoint = transform.position + Vector3.up * 1.15f; // grab at our own grip height
                    break;
                case HelmStation helm:
                    _ctx.HelmTarget = helm;
                    break;
            }
        }

        // Occupancy bookkeeping for stations (helm / rope): claimed on enter, released on exit.
        private void HandleAttachmentChanges(PlayerState before, PlayerState after, NetworkInputData input)
        {
            const PlayerState attachedMask = PlayerState.Climbing | PlayerState.Steering;
            PlayerState entered = after & ~before & attachedMask;
            PlayerState exited = before & ~after & attachedMask;

            if (exited != 0 && _attached != null)
            {
                ReleaseStation(_attached);
                _attached = null;
            }

            if (entered == 0)
                return;

            _attached = _target;
            bool claimed = _attached switch
            {
                HelmStation helm => helm.Helm != null && helm.Helm.TryOccupy(Object.InputAuthority),
                ClimbSurface _ => true,
                _ => false,
            };

            if (!claimed)
            {
                _machine.Deactivate(entered, _ctx); // lost a same-tick race for the station
                _attached = null;
            }
        }

        private void ReleaseStation(Interactable station)
        {
            if (station is HelmStation helm && helm.Helm != null)
                helm.Helm.Release(Object.InputAuthority);
        }

        private void DriveStations(NetworkInputData input)
        {
            switch (_attached)
            {
                case HelmStation helm when _machine.IsActive(PlayerState.Steering) && helm.Helm != null:
                    helm.Helm.SubmitSteer(_ctx.Move.x);
                    break;
            }
        }

        // ---- Running rigging: take / haul / tie / drop a line end ---------------------------
        private NetworkShip _heldShip;
        private int _heldLine = -1;
        private bool _dropPressed;

        private void HandleRigging(NetworkInputData input)
        {
            PlayerRef me = Object.InputAuthority;

            // The ship is the source of truth: lost the line (e.g. reset) → stop holding.
            if (_heldShip != null && _heldShip.LineHeldBy(me) != _heldLine)
                StopHolding();

            bool holding = _heldShip != null;
            bool canAct = _ctx.ControlAuthority > 0.5f && (_machine.Active & (PlayerState.Climbing | PlayerState.Steering)) == 0;

            if (holding)
            {
                if (!canAct || _dropPressed)
                {
                    _heldShip.DropLine(_heldLine, me);
                    StopHolding();
                    return;
                }
                if (_ctx.InteractPressed && _target is BelayPin pin && pin.Ship == _heldShip &&
                    _heldShip.TryTieLine(_heldLine, me, pin))
                {
                    StopHolding();
                    return;
                }
                _heldShip.SubmitLineInput(_heldLine, me,
                    input.Buttons.IsSet(NetworkInputData.ButtonHaul),
                    input.Buttons.IsSet(NetworkInputData.ButtonEase));
                return;
            }

            if (!_ctx.InteractPressed || !canAct)
                return;

            switch (_target)
            {
                case RopeEnd end when end.Line != null && end.Line.Ship != null:
                    TryHold(end.Line.Ship, end.Line.LineIndex);
                    break;
                case BelayPin pin when pin.Ship != null && pin.TiedLine >= 0:
                    TryHold(pin.Ship, pin.TiedLine); // cast off the pin into our hands
                    break;
            }
        }

        private void TryHold(NetworkShip ship, int line)
        {
            if (!ship.TryTakeLine(line, Object.InputAuthority))
                return;
            _heldShip = ship;
            _heldLine = line;
            _machine.ForceActivate(PlayerState.HoldingRope, _ctx);
        }

        private void StopHolding()
        {
            _heldShip = null;
            _heldLine = -1;
            _machine.Deactivate(PlayerState.HoldingRope, _ctx);
        }

        /// <summary>Interactor description incl. the line in our hands (read from the ship on any peer).</summary>
        private InteractorInfo Interactor(Vector3 eye, Vector3 feet)
        {
            PlayerRef me = Object != null ? Object.InputAuthority : PlayerRef.None;
            foreach (NetworkShip ship in NetworkShip.All)
            {
                int line = ship != null && ship.Object != null && ship.Object.IsValid ? ship.LineHeldBy(me) : -1;
                if (line >= 0)
                    return new InteractorInfo(me, eye, feet, line, ship);
            }
            return new InteractorInfo(me, eye, feet);
        }

        // While glued to a rail the motor owns the body: drop collisions so yards, platforms and
        // the mast itself can't wedge the climber. Restored as soon as we let go.
        private void UpdateCollisionMode()
        {
            if (_capsule == null)
                return;
            bool climbing = _machine.IsActive(PlayerState.Climbing);
            LayerMask want = climbing ? (LayerMask)~0 : _defaultExclude;
            if (_capsule.excludeLayers != want)
                _capsule.excludeLayers = want;
        }

        // The ship we're aboard defines the camera's yaw space on the owning client.
        private void UpdatePlatform(ref PlayerNetState s)
        {
            if (_attached != null && _attached.Owner != null)
            {
                s.Platform = _attached.Owner.Id;
                return;
            }

            if (_machine.IsActive(PlayerState.Swimming))
            {
                s.Platform = default;
                return;
            }

            if (_ctx.IsGrounded)
            {
                NetworkShip ship = _groundBody != null ? _groundBody.GetComponentInParent<NetworkShip>() : null;
                s.Platform = ship != null ? ship.Object.Id : default;
            }
            // Airborne: keep whatever we had (jumping on deck mustn't swap the look space).
        }

        private void UpdateTuck(ref PlayerNetState s)
        {
            float target = _machine.IsActive(PlayerState.InAir) ? 1f : 0f;
            float rate = target > s.TuckAmount
                ? (config != null ? config.JumpTuckRate : 12f)
                : (config != null ? config.JumpStandRate : 9f);
            s.TuckAmount = Mathf.MoveTowards(s.TuckAmount, target, Mathf.Max(0.01f, rate) * Runner.DeltaTime);
        }

        private void UpdateRagdollControl(ref PlayerNetState s)
        {
            float influence = config != null ? config.DrunkRagdollInfluence : 0.5f;
            float recover = config != null ? config.RagdollRecoverRate : 0.4f;

            // Intoxication sets a floppy baseline; knockouts spike to 1 and recover down to it.
            float baseline = s.Drunkenness * influence;
            float decayed = s.RagdollControl - recover * Runner.DeltaTime;
            s.RagdollControl = Mathf.Clamp01(Mathf.Max(baseline, decayed));
        }

        // Head-only look until the neck limit, then the torso catches up.
        private void ApplyLookDrivenBodyTurn()
        {
            if (_rb == null || _ctx.ControlAuthority <= 0.001f || IsKnockedOut || _machine.IsActive(PlayerState.Swimming))
                return;

            float limit = Mathf.Max(5f, neckYawLimit);
            float keep = Mathf.Clamp(keepHeadYawAfterBodyTurn, 0f, limit - 1f);
            float bodyYaw = _rb.rotation.eulerAngles.y;
            float deltaYaw = Mathf.DeltaAngle(bodyYaw, _ctx.LookYaw);

            // Walking always turns the body to the view; standing still only past the neck limit.
            bool moving = _ctx.Move.sqrMagnitude > 0.01f;
            if (!moving && Mathf.Abs(deltaYaw) <= limit)
                return;

            float targetYaw = moving ? _ctx.LookYaw : _ctx.LookYaw - Mathf.Sign(deltaYaw) * keep;
            float maxStep = Mathf.Max(0.01f, lookBodyTurnSpeedDeg) * Runner.DeltaTime;
            float nextYaw = Mathf.MoveTowardsAngle(bodyYaw, targetYaw, maxStep);
            _rb.MoveRotation(Quaternion.Euler(0f, nextYaw, 0f));
        }

        private void PublishPayload(ref PlayerNetState s)
        {
            IStatePayload payload = _ctx.ConsumePayload();
            if (payload == null) return;

            int len = PayloadCodec.Encode(payload, _payloadScratch);
            for (int i = 0; i < len; i++)
                PayloadBytes.Set(i, _payloadScratch[i]);
            s.PayloadLength = (byte)len;
            unchecked { s.PayloadVersion++; }
        }

        // =====================================================================================
        // Local helpers (camera rig / HUD)
        // =====================================================================================

        /// <summary>
        /// Client-side view ray from the rendered camera, same rules as the host check. The result
        /// is sent to the host in input, so what the hint shows is exactly what Interact acts on.
        /// </summary>
        public bool ProbeLocalTarget(Vector3 origin, Vector3 direction, out Interactable target, out RaycastHit hit)
        {
            if (_sensor == null)
                _sensor = new InteractionSensor();
            return _sensor.Probe(origin, direction, interactionReach, interactionProbeRadius, interactionMask,
                transform, out target, out hit);
        }

        public InteractorInfo LocalInteractor => Interactor(RenderEyePosition, RenderPosition);

        // =====================================================================================
        // Render (every peer, every frame)
        // =====================================================================================
        public override void Render()
        {
            var s = State;
            _ctx.PlanarSpeed = s.PlanarSpeed;
            _ctx.Drunkenness = s.Drunkenness;
            _ctx.LookYaw = s.LookYaw;
            _ctx.LookPitch = s.LookPitch;
            _ctx.Animator = animator;

            if (!HasStateAuthority)
            {
                _machine.SetActiveFromNetwork(s.States, _ctx);
                DispatchPayloadIfChanged(s);
            }

            _machine.Render(_ctx);
            ApplyAnimatorParamsFromState(s);

            if (ragdoll != null)
                ragdoll.SetAmount(s.RagdollControl);

            if (jumpCollider != null)
                jumpCollider.SetTuck(s.TuckAmount);
        }

        private void ApplyAnimatorParamsFromState(PlayerNetState s)
        {
            if (animator == null)
                return;

            PlayerState active = s.States;
            float riseRate = config != null ? config.AnimatorSpeedRiseRate : 9f;
            float fallRate = config != null ? config.AnimatorSpeedFallRate : 7f;
            float rate = s.PlanarSpeed >= _smoothedAnimatorSpeed ? riseRate : fallRate;
            _smoothedAnimatorSpeed = Mathf.MoveTowards(_smoothedAnimatorSpeed, s.PlanarSpeed,
                Mathf.Max(0.01f, rate) * Time.deltaTime);

            animator.SetFloat(AnimatorParams.Speed, _smoothedAnimatorSpeed);
            animator.SetBool(AnimatorParams.Grounded, (active & (PlayerState.Grounded | PlayerState.Steering)) != 0);
            animator.SetBool(AnimatorParams.Climbing, (active & PlayerState.Climbing) != 0);
            animator.SetBool(AnimatorParams.Swimming, (active & PlayerState.Swimming) != 0);
            animator.SetBool(AnimatorParams.Drinking, (active & PlayerState.DrinkingRum) != 0);
            animator.SetBool(AnimatorParams.Steering, (active & PlayerState.Steering) != 0);
        }

        private void DispatchPayloadIfChanged(PlayerNetState s)
        {
            if (s.PayloadVersion == _lastPayloadVersion) return;
            _lastPayloadVersion = s.PayloadVersion;

            int len = s.PayloadLength;
            for (int i = 0; i < len; i++)
                _payloadScratch[i] = PayloadBytes.Get(i);

            IStatePayload payload = PayloadCodec.Decode(_payloadScratch, len);
            if (payload != null)
                _machine.DispatchPayload(_ctx, payload);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsWaterTrigger(other))
                _waterTriggers.Add(other);
        }

        private void OnTriggerExit(Collider other) => _waterTriggers.Remove(other);

        private void OnDisable() => _waterTriggers.Clear();

        private bool IsWaterTrigger(Collider other)
        {
            if (other == null || !other.isTrigger)
                return false;
            if (other.CompareTag("Water") || other.GetComponent<WaterTrigger>() != null)
                return true;
            return (waterMask.value & (1 << other.gameObject.layer)) != 0;
        }
    }
}
#endif
