using UnityEngine;

namespace RumOverboard.Core
{
    /// <summary>
    /// Central tuning + connection config. Create one via
    /// Assets ▸ Create ▸ RumOverboard ▸ Game Config and assign it on the
    /// ConnectionManager / NetworkPlayer. All fields have sane inline defaults,
    /// so the game also runs without an asset assigned.
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "RumOverboard/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        /// <summary>Fallback App Id used when no config asset is assigned.</summary>
        public const string DefaultAppId = "0d31993b-5624-4d9f-a496-73f5d0332a22";

        [Header("Photon Fusion")]
        [Tooltip("Fusion 2 App Id (dashboard.photonengine.com, product type: Fusion).")]
        public string PhotonAppId = DefaultAppId;

        [Tooltip("Force a Photon region, e.g. 'eu', 'us', 'asia', 'sa'. Empty = Best Region (ping-based).")]
        public string FixedRegion = "";

        [Header("Session")]
        [Range(2, 4)] public int MaxCrew = 4;
        public string DefaultSessionName = "rum-overboard";

        [Header("Character — locomotion")]
        public float MoveSpeed = 4.5f;

        [Tooltip("Units/sec^2 acceleration on ground. Around MoveSpeed gives ~1s to full run.")]
        public float GroundAcceleration = 40.0f;

        [Tooltip("Units/sec^2 deceleration on ground when stopping/changing direction.")]
        public float GroundDeceleration = 55.0f;

        [Tooltip("Speed multiplier while Sprint (Shift) is held on deck.")]
        public float SprintMultiplier = 1.6f;

        public float ClimbSpeed = 2.5f;
        public float TurnSpeedDeg = 720f;
        public float JumpImpulse = 6f;

        [Tooltip("Max ground slope (deg) the crew can walk up freely (stair ramps, hull sides). Steeper = treated as a wall.")]
        [Range(5f, 85f)]
        public float MaxWalkableAngle = 60f;

        [Tooltip("Seconds to keep a jump press buffered until a valid grounded/coyote moment.")]
        public float JumpBufferTime = 0.18f;

        [Tooltip("Seconds after leaving ground where jump is still allowed.")]
        public float CoyoteTime = 0.12f;


        [Tooltip("Additional downward acceleration while rising after jump.")]
        public float ExtraRiseGravity = 8f;

        [Tooltip("Additional downward acceleration while falling (snappier landing).")]
        public float ExtraFallGravity = 20f;

        [Header("Animation blending")]
        [Tooltip("How fast Animator 'Speed' rises toward target planar speed (units/sec).")]
        public float AnimatorSpeedRiseRate = 9f;

        [Tooltip("How fast Animator 'Speed' falls toward target planar speed (units/sec).")]
        public float AnimatorSpeedFallRate = 7f;

        [Header("Character — jump tuck (collider follows the legs)")]
        [Tooltip("How fast the body tucks (0→1/sec) once airborne — legs pull up, capsule shrinks from the feet.")]
        public float JumpTuckRate = 12f;

        [Tooltip("How fast the body un-tucks (stands up) after landing. Higher = snappier stand-up.")]
        public float JumpStandRate = 9f;

        [Header("Character — rum / intoxication")]
        [Tooltip("Maps intoxication (0..1) to how much control is lost (0 = none, 1 = full stagger).")]
        public AnimationCurve DrunkControlLoss = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Peak steering wobble in degrees at full intoxication.")]
        public float DrunkMaxWobbleDeg = 45f;

        [Tooltip("Seconds one sip of rum takes.")]
        public float RumSipDuration = 2f;

        [Tooltip("Intoxication (0..1) added per full sip.")]
        public float RumSipIntoxication = 0.15f;

        [Header("Ragdoll (0..1 blend)")]
        [Tooltip("How much full intoxication feeds the ragdoll baseline (0..1). Higher = wobblier when drunk.")]
        [Range(0f, 1f)] public float DrunkRagdollInfluence = 0.5f;

        [Tooltip("Per-second recovery from a knockout (ragdoll=1) back down to the drunkenness baseline.")]
        public float RagdollRecoverRate = 0.4f;
    }
}
