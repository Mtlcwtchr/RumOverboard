#if FUSION2
using RumOverboard.Core.Character;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing
{
    /// <summary>
    /// A sweep (long oar) through a port in the bulwark. E mans it (the Rowing state holds the
    /// crew member at <see cref="StandPosition"/>, facing aft); W/S presses are routed to
    /// <see cref="NetworkShip.PressOar"/>, which runs the rhythm (<see cref="RowingRhythm"/>) and
    /// <see cref="RowingSystem"/> turns the strokes into thrust.
    ///
    /// Pure view + routing — the oar's state lives on <see cref="NetworkShip"/> (host authority).
    /// Animates the oar on every peer from the replicated stroke (the trigger collider stays put:
    /// moving colliders on the hull body would disturb it).
    /// </summary>
    public sealed class OarStation : Interactable, IStationAnchor
    {
        [Header("Oar")]
        [SerializeField] private int oarIndex;
        [Tooltip("-1 = port, +1 = starboard.")]
        [SerializeField] private float side = 1f;
        [Tooltip("Pivot in the oar port (thole). Local +Z points outboard along the shaft.")]
        [SerializeField] private Transform pivot;
        [Tooltip("Where the rower stands (rotation = facing).")]
        [SerializeField] private Transform stand;
        [Tooltip("Shaft tilt (deg) that puts the blade in the water.")]
        [SerializeField] private float dipAngle = 25f;
        [SerializeField] private string busyPrompt = "Весло занято";

        private NetworkShip _ship;
        private Rigidbody _body;
        private Quaternion _pivotBase = Quaternion.identity;
        private bool _captured;

        public override InteractionKind Kind => InteractionKind.Oar;

        public int OarIndex => oarIndex;
        public float Side => side;
        public Transform Pivot => pivot;

        public NetworkShip Ship
        {
            get
            {
                if (_ship == null)
                    _ship = GetComponentInParent<NetworkShip>(true);
                return _ship;
            }
        }

        public Rigidbody Body => _body != null ? _body : (_body = GetComponentInParent<Rigidbody>());

        public Vector3 StandPosition => stand != null ? stand.position : transform.position;
        public Quaternion StandRotation => Quaternion.Euler(0f, (stand != null ? stand : transform).eulerAngles.y, 0f);

        public void Configure(int index, float portOrStarboard, Transform pivotTransform, Transform standAnchor, float dip)
        {
            oarIndex = index;
            side = portOrStarboard < 0f ? -1f : 1f;
            pivot = pivotTransform;
            stand = standAnchor;
            dipAngle = dip;
            _captured = false;
        }

        private bool Networked => Ship != null && Ship.Object != null && Ship.Object.IsValid && Ship.HasOar(oarIndex);

        // ---- Interactable ------------------------------------------------------------------------
        public override bool IsAvailable(in InteractorInfo who)
        {
            if (!who.HandsFree || !Networked)
                return false;
            Fusion.PlayerRef rower = Ship.GetOar(oarIndex).Rower;
            return !rower.IsRealPlayer || rower == who.Player;
        }

        public override string GetPrompt(in InteractorInfo who)
        {
            if (!who.HandsFree)
                return "Сначала закрепи или брось канат";
            if (!Networked)
                return Prompt;
            return IsAvailable(who) ? Prompt : busyPrompt;
        }

        private void Reset() => Prompt = "Взяться за весло";

        // ---- Cosmetics ---------------------------------------------------------------------------
        private void Awake() => Capture();

        private void Capture()
        {
            if (pivot != null)
                _pivotBase = pivot.localRotation;
            _captured = true;
        }

        private void LateUpdate()
        {
            if (pivot == null)
                return;
            if (!_captured)
                Capture();

            RowingConfig cfg = Ship != null ? Ship.RowingSettings : RowingConfig.Active;
            float fore = 0f, lift = 1f;
            bool manned = false;
            if (Networked && Ship.Runner != null)
            {
                OarState s = Ship.GetOar(oarIndex);
                manned = s.Rower.IsRealPlayer;
                var t = new RowTiming(cfg, Ship.Runner.DeltaTime);
                RowingRhythm.Pose(s, RenderTick(), t, out fore, out lift);
            }

            // Unmanned and resting: blade tossed up clear of the water.
            float pitch = dipAngle - cfg.LiftAngle * lift - (manned || lift < 0.999f ? 0f : cfg.LiftAngle);
            float yaw = cfg.SweepAngle * fore * (side > 0f ? -1f : 1f); // + = blade forward
            pivot.localRotation = _pivotBase * Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>Fractional simulation tick to animate at (host: local render time; clients: the host's).</summary>
        private float RenderTick()
        {
            Fusion.NetworkRunner runner = Ship.Runner;
            float dt = Mathf.Max(0.001f, runner.DeltaTime);
            float time = Ship.HasStateAuthority ? runner.LocalRenderTime : runner.RemoteRenderTime;
            return time / dt;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (stand == null) return;
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(stand.position + Vector3.up * 0.9f, new Vector3(0.4f, 1.8f, 0.4f));
            Gizmos.DrawLine(stand.position + Vector3.up * 1.2f, stand.position + Vector3.up * 1.2f + stand.forward * 0.6f);
        }
#endif
    }
}
#endif
