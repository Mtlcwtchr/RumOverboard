#if FUSION2
using System.Collections.Generic;
using Fusion;
using RumOverboard.Core;
using RumOverboard.Gameplay;
using RumOverboard.Gameplay.States;
using RumOverboard.StateMachine;
using RumOverboard.StateMachine.Serialization;
using UnityEngine;

namespace RumOverboard.Networking
{
    /// <summary>
    /// All of the crew member's replicated state in one struct, so NetworkPlayer's
    /// networked surface stays tiny. (The payload bytes are a separate NetworkArray
    /// because arrays can't live inside a plain INetworkStruct; PreviousButtons is
    /// kept separate too, as the proven weaver-friendly pattern.)
    /// </summary>
    public struct PlayerNetState : INetworkStruct
    {
        public float Drunkenness;    // 0..1
        public float RagdollControl; // 0..1 — how much authority the ragdoll has
        public float PlanarSpeed;    // for animation on all peers
        public float TuckAmount;     // 0..1 jump/land leg-tuck — drives the capsule + anim on all peers
        public uint StateMask;       // active PlayerState bit set
        public byte PayloadVersion;
        public byte PayloadLength;

        public PlayerState States
        {
            get => (PlayerState)StateMask;
            set => StateMask = (uint)value;
        }
    }

    /// <summary>
    /// The networked crew member and the driver for its mask-based state machine.
    /// Locomotion (Grounded/InAir/Climbing/Swimming) and the Action bit (DrinkingRum)
    /// share one replicated mask; ragdoll is a separate 0..1 value that intoxication
    /// feeds and knockouts spike. The host simulates (feeding sensors + each client's
    /// input into the machine, which drives the Rigidbody) and publishes; clients sync
    /// the mask, decode payloads, and run cosmetics + the local ragdoll blend.
    ///
    /// Movement uses only a plain Rigidbody, so swapping NetworkTransform for
    /// NetworkRigidbody3D (Physics addon) needs no code change.
    ///
    /// Client-side prediction: the simulation (FixedUpdateNetwork) runs for whoever has
    /// state authority AND — when the prefab carries the Fusion Physics addon's
    /// NetworkRigidbody3D — for the local input authority too. Fusion then forward-predicts
    /// the local player each tick and reconciles against the authoritative snapshot, so local
    /// input feels lag-free while the host stays authoritative. This is auto-detected: with
    /// NetworkRigidbody3D + RunnerSimulatePhysics3D the body is resimulated deterministically
    /// (prediction on); with a plain NetworkTransform it stays host-authoritative + interpolated
    /// (prediction off). See SETUP_MULTIPLAYER.md §2/§6.
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

        /// <summary>Where the local first-person camera sits (head height).</summary>
        public Transform CameraAnchor => cameraAnchor != null ? cameraAnchor : transform;

        /// <summary>True once the ragdoll has taken (nearly) full control — a knockout.</summary>
        public bool IsKnockedOut => State.RagdollControl >= 0.9f;

        [Header("Sensors")]
        [SerializeField] private float groundProbe = 0.15f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private LayerMask climbMask;
        [SerializeField] private float climbReach = 0.7f;

        [Tooltip("Optional water layers for trigger-based swimming detection. " +
                 "You can also mark trigger colliders with WaterTrigger component.")]
        [SerializeField] private LayerMask waterMask;

        // --- Replicated state (one struct + one payload buffer + input echo) ---
        [Networked] public PlayerNetState State { get; set; }
        [Networked, Capacity(PayloadCapacity)] public NetworkArray<byte> PayloadBytes { get; }
        [Networked] private NetworkButtons PreviousButtons { get; set; }

        private Rigidbody _rb;
        private PlayerStateMachine _machine;
        private StateContext _ctx;
        private readonly HashSet<Collider> _waterTriggers = new();
        private readonly byte[] _payloadScratch = new byte[PayloadCapacity];
        private readonly Collider[] _climbHits = new Collider[4];
        private float _smoothedAnimatorSpeed;
        private byte _lastPayloadVersion;
        private bool _networkRigidbody; // prefab uses the Physics addon (NetworkRigidbody3D) → predict
        private bool _warnedKinematic;  // one-shot guard for the "simulating but kinematic" diagnostic

        public float Drunkenness => State.Drunkenness;
        public float RagdollControl => State.RagdollControl;

        // --- Read-only surface for the procedural-animation layer (every peer) ---
        public float PlanarSpeed => State.PlanarSpeed;
        public float TuckAmount  => State.TuckAmount;
        public bool  IsClimbing  => (State.States & PlayerState.Climbing) == PlayerState.Climbing;
        public bool  IsInAir     => (State.States & PlayerState.InAir) == PlayerState.InAir;
        public bool  IsSwimming  => (State.States & PlayerState.Swimming) == PlayerState.Swimming;

        /// <summary>
        /// Who runs the simulation this tick: always the state authority (host); also the
        /// local input authority when the Physics addon is in use, which is what makes local
        /// movement lag-free (Fusion resimulates + reconciles the predicted body).
        /// </summary>
        private bool Simulating => HasStateAuthority || (_networkRigidbody && HasInputAuthority);

        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();
            if (ragdoll == null) ragdoll = GetComponentInChildren<RagdollController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Keep the root collider authoritative for floor contacts.
            _rb.detectCollisions = true;
            if (TryGetComponent(out CapsuleCollider capsule))
            {
                capsule.enabled = true;
                capsule.isTrigger = false;
            }

            // Jump-collider driver lives on the body (needs the CapsuleCollider). Use one that's
            // already on the prefab, otherwise add it so the feature works without manual wiring.
            if (jumpCollider == null) jumpCollider = GetComponent<JumpColliderDriver>();
            if (jumpCollider == null) jumpCollider = gameObject.AddComponent<JumpColliderDriver>();

            // Climb IK rig must sit on the Animator's GameObject (OnAnimatorIK only fires there).
            // Auto-added if missing; still needs "IK Pass" enabled on the Animator layer to run.
            if (animator != null)
            {
                var climbRig = animator.GetComponent<ProceduralClimbRig>();
                if (climbRig == null) climbRig = animator.gameObject.AddComponent<ProceduralClimbRig>();
                climbRig.Configure(this, climbMask);
            }

            // Prediction is available when the prefab uses the Physics addon's NetworkRigidbody3D:
            // it resimulates the body deterministically and OWNS its kinematic state, so we must
            // not touch isKinematic there. With a plain NetworkTransform we keep the original
            // host-authoritative setup (proxies kinematic + interpolated).
            _networkRigidbody = TryGetComponent(out Fusion.Addons.Physics.NetworkRigidbody3D _);

            _rb.interpolation = RigidbodyInterpolation.None; // Fusion drives interpolation, not Unity
            if (!_networkRigidbody)
            {
                _rb.isKinematic = !HasStateAuthority;
            }
            else if (HasInputAuthority && !HasStateAuthority)
            {
                // Predicting client: the local player must be in this client's simulation set,
                // otherwise NetworkRigidbody3D keeps the body KINEMATIC and every linearVelocity
                // write in the state machine is dropped ("Setting linear velocity of a kinematic
                // body is not supported") — i.e. you can't move. This is what puts it under
                // client-side prediction (paired with RunnerSimulatePhysics3D = SimulateForward).
                Runner.SetIsSimulated(Object, true);
            }

            _machine = PlayerStateMachineFactory.Build();
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
            _smoothedAnimatorSpeed = 0f;

            if (HasStateAuthority)
            {
                var s = State;
                s.States = _machine.Active;
                State = s;
            }

            // The local crew member drives the camera (first-person, PEAK-style).
            if (HasInputAuthority)
                PlayerCameraRig.Instance?.SetTarget(this);

            Debug.Log($"[NetworkPlayer] Spawned id={Object.Id} inputAuth={Object.InputAuthority} " +
                      $"local={Runner.LocalPlayer} StateAuth={HasStateAuthority} InputAuth={HasInputAuthority} " +
                      $"Proxy={Object.IsProxy} InSim={Object.IsInSimulation} NRB={_networkRigidbody} " +
                      $"kinematic={_rb.isKinematic} mode={Runner.GameMode}");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            _waterTriggers.Clear();
            if (HasInputAuthority && PlayerCameraRig.Instance != null)
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

        public override void FixedUpdateNetwork()
        {
            // Runs on the host (authoritative) and, when prediction is on, on the local
            // input authority (predicted + reconciled by Fusion). Pure proxies skip it and
            // ride the interpolated snapshot instead.
            if (!Simulating)
                return;

            // A kinematic body ignores velocity — writing it just spams the console and can't
            // move the crew. If we're meant to simulate but the body is still kinematic, that's
            // a setup problem (report once) — skip the tick rather than fight it.
            if (_rb.isKinematic)
            {
                if (!_warnedKinematic)
                {
                    _warnedKinematic = true;
                    var sim = FindFirstObjectByType<Fusion.Addons.Physics.RunnerSimulatePhysics3D>();
                    bool simOnRunner = Runner != null &&
                                       Runner.GetComponent<Fusion.Addons.Physics.RunnerSimulatePhysics3D>() != null;
                    Debug.LogWarning($"[NetworkPlayer] {name} id={Object.Id} SIMULATING but KINEMATIC. " +
                                     $"StateAuth={HasStateAuthority} InputAuth={HasInputAuthority} Proxy={Object.IsProxy} " +
                                     $"InSim={Object.IsInSimulation} | simMode={UnityEngine.Physics.simulationMode} " +
                                     $"simExists={sim != null} simOnRunner={simOnRunner} " +
                                     $"auth={(sim != null ? sim.PhysicsAuthority.ToString() : "?")} " +
                                     $"timing={(sim != null ? sim.PhysicsTiming.ToString() : "?")} " +
                                     $"clientSim={(sim != null ? sim.ClientPhysicsSimulation.ToString() : "?")}");
                }
                return;
            }

            var s = State;

            GetInput(out NetworkInputData input);
            FillContextForSimulation(input, s);

            _machine.FixedTick(_ctx);

            s.Drunkenness = _ctx.Drunkenness;
            s.PlanarSpeed = _ctx.PlanarSpeed;
            s.States = _machine.Active;
            UpdateRagdollControl(ref s);
            UpdateTuck(ref s);
            PublishPayload(ref s);

            State = s;

            // Update the capsule before the next physics step so ground/climb probes and
            // collisions match the tucked pose (host + predicting client).
            if (jumpCollider != null)
                jumpCollider.SetTuck(s.TuckAmount);
        }

        // Legs tuck up while airborne (capsule shrinks from the feet), then snap back on
        // landing. Networked via TuckAmount so proxies + physics agree.
        private void UpdateTuck(ref PlayerNetState s)
        {
            float target = _machine.IsActive(PlayerState.InAir) ? 1f : 0f;
            float rate = target > s.TuckAmount
                ? (config != null ? config.JumpTuckRate : 12f)
                : (config != null ? config.JumpStandRate : 9f);
            s.TuckAmount = Mathf.MoveTowards(s.TuckAmount, target, Mathf.Max(0.01f, rate) * Runner.DeltaTime);
        }

        private void FillContextForSimulation(NetworkInputData input, PlayerNetState s)
        {
            NetworkButtons pressed = input.Buttons.GetPressed(PreviousButtons);
            PreviousButtons = input.Buttons;

            // The ragdoll steals control as its value rises: at 1 the crew member is limp.
            _ctx.ControlAuthority = 1f - s.RagdollControl;
            _ctx.Move = input.Move * _ctx.ControlAuthority;
            _ctx.LookYaw = input.LookYaw;
            _ctx.JumpPressed = pressed.IsSet(NetworkInputData.ButtonJump);
            _ctx.ClimbPressed = pressed.IsSet(NetworkInputData.ButtonClimb);
            _ctx.InteractPressed = pressed.IsSet(NetworkInputData.ButtonInteract);
            _ctx.RagdollPressed = pressed.IsSet(NetworkInputData.ButtonRagdoll);

            bool probedGrounded = ProbeGround();
            _ctx.IsGrounded = probedGrounded;
            _ctx.InWater = _waterTriggers.Count > 0;
            _ctx.NearClimb = ProbeClimb();

            _ctx.Drunkenness = s.Drunkenness;
            _ctx.DeltaTime = Runner.DeltaTime;
            _ctx.Time = Runner.SimulationTime;
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

        private bool ProbeGround() => Physics.Raycast(
            transform.position + Vector3.up * 0.1f,
            Vector3.down,
            groundProbe + 0.1f,
            groundMask,
            QueryTriggerInteraction.Ignore);

        private bool ProbeClimb()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position + Vector3.up,
                climbReach,
                _climbHits,
                climbMask,
                QueryTriggerInteraction.Collide);

            if (count == 0)
            {
                _ctx.ClimbTargetId = 0;
                return false;
            }

            Transform climb = _climbHits[0].transform;
            _ctx.ClimbTargetId = climb.GetInstanceID();
            _ctx.ClimbPoint = climb.InverseTransformPoint(transform.position);
            return true;
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

        // Render() runs every frame on every peer — cosmetics + client-side sync.
        public override void Render()
        {
            var s = State;
            _ctx.PlanarSpeed = s.PlanarSpeed;
            _ctx.Drunkenness = s.Drunkenness;
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

            // Keep the capsule matching the tuck on proxies too (cosmetic parity).
            if (jumpCollider != null)
                jumpCollider.SetTuck(s.TuckAmount);
        }

        private void ApplyAnimatorParamsFromState(PlayerNetState s)
        {
            if (animator == null)
                return;

            PlayerState active = s.States;
            bool grounded = (active & PlayerState.Grounded) == PlayerState.Grounded;
            bool climbing = (active & PlayerState.Climbing) == PlayerState.Climbing;
            bool swimming = (active & PlayerState.Swimming) == PlayerState.Swimming;
            bool drinking = (active & PlayerState.DrinkingRum) == PlayerState.DrinkingRum;

            float dt = Runner != null ? Runner.DeltaTime : Time.deltaTime;

            float riseRate = config != null ? config.AnimatorSpeedRiseRate : 55f;
            float fallRate = config != null ? config.AnimatorSpeedFallRate : 45f;
            float rate = s.PlanarSpeed >= _smoothedAnimatorSpeed ? riseRate : fallRate;
            _smoothedAnimatorSpeed = Mathf.MoveTowards(_smoothedAnimatorSpeed, s.PlanarSpeed, Mathf.Max(0.01f, rate) * Mathf.Max(0.0001f, dt));

            animator.SetFloat(AnimatorParams.Speed, _smoothedAnimatorSpeed);
            animator.SetBool(AnimatorParams.Grounded, grounded);
            animator.SetBool(AnimatorParams.Climbing, climbing);
            animator.SetBool(AnimatorParams.Swimming, swimming);
            animator.SetBool(AnimatorParams.Drinking, drinking);
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

        private void OnTriggerExit(Collider other)
        {
            _waterTriggers.Remove(other);
        }

        private void OnDisable()
        {
            _waterTriggers.Clear();
        }

        private bool IsWaterTrigger(Collider other)
        {
            if (other == null || !other.isTrigger)
                return false;

            if (other.CompareTag("Water"))
                return true;

            return (waterMask.value & (1 << other.gameObject.layer)) != 0;
        }
    }
}
#endif
