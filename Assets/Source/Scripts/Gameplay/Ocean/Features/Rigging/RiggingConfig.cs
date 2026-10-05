using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rigging
{
    /// <summary>
    /// Tuning for running rigging (halyards / sheets). Authored asset:
    /// Assets/Source/Configs/FeatureScenes/RiggingFeatureConfig.asset — edited live from the
    /// Rigging debug window (F2) and saved back with "Save to config".
    /// </summary>
    [CreateAssetMenu(fileName = "RiggingFeatureConfig", menuName = "RumOverboard/Feature Configs/Rigging")]
    public sealed class RiggingConfig : ScriptableObject
    {
        [Header("Hauling (rope in hand)")]
        [Tooltip("Hand-over-hand haul speed with LMB held (m of rope per second).")]
        public float HaulRate = 1.4f;
        [Tooltip("Paying out with RMB held (m/s) — the load takes the rope back up.")]
        public float EaseRate = 1.8f;
        [Tooltip("Max rope gained per second by walking away from the block with the rope taut.")]
        public float WalkHaulRate = 1.6f;
        [Tooltip("Pull (N) on the holder toward the block at a fully loaded line.")]
        public float LoadPull = 260f;
        [Tooltip("How hard a taut rope holds the crew member back (1/s correction).")]
        public float LeashStiffness = 12f;

        [Header("Loose line")]
        [Tooltip("Rate an unheld, untied line runs out under load (m/s) at full hoist; scaled down when light.")]
        public float RunRate = 3.5f;
        [Range(0f, 1f)] public float RunRateLight = 0.25f;
        public float EndGravity = 9.81f;
        [Range(0f, 5f)] public float EndAirDamping = 0.6f;
        [Range(0f, 1f)] public float EndBounce = 0.15f;
        [Range(0f, 1f)] public float EndFriction = 0.6f;
        public float EndRadius = 0.08f;

        [Header("Tying")]
        [Tooltip("Extra reach (m) when making a line fast on a pin a bit farther than the rope allows.")]
        public float TieSlack = 0.6f;

        [Header("Load (how heavy a line feels)")]
        [Tooltip("Sail wind force (N) that counts as a fully loaded line (load = 1).")]
        public float FullLoadForce = 3500f;
        [Tooltip("Halyard load from the yard + canvas weight alone at full hoist (0..1).")]
        [Range(0f, 1f)] public float HalyardWeight = 0.3f;
        [Tooltip("Share of the sail's wind force a halyard carries.")]
        [Range(0f, 1f)] public float HalyardWindShare = 0.5f;
        [Tooltip("Haul speed multiplier at full load (hand over hand gets slow when the sail pulls).")]
        [Range(0.02f, 1f)] public float HeavyHaulFactor = 0.22f;
        [Tooltip("Extra easing speed per unit of load (the load takes the rope).")]
        public float EaseLoadBoost = 1.2f;
        [Tooltip("Load a crew member can hold without the rope slipping through the hands.")]
        public float HoldGrip = 0.8f;
        [Tooltip("Slip speed (m/s) per unit of load above the grip.")]
        public float SlipRate = 2.2f;

        [Header("Braces / yard")]
        [Tooltip("Yard swing speed (deg/s) with no wind.")]
        public float YardSwingRate = 12f;
        [Tooltip("Extra yard swing speed (deg/s) per unit of sail load.")]
        public float YardSwingWindRate = 55f;
        [Tooltip("Angle mismatch (deg) between where the wind wants the yard and where the brace holds it at which the brace is fully loaded.")]
        public float BraceLoadAngle = 25f;
        [Tooltip("Apparent wind (m/s) below which the yard just stays where it is.")]
        public float YardMinWind = 0.8f;

        private static RiggingConfig _fallback;

        /// <summary>The config used at runtime (assigned by NetworkShip; defaults otherwise).</summary>
        public static RiggingConfig Active
        {
            get
            {
                if (_active != null) return _active;
                if (_fallback == null) _fallback = CreateInstance<RiggingConfig>();
                return _fallback;
            }
            set => _active = value;
        }

        private static RiggingConfig _active;

        public void CopyFrom(RiggingConfig other)
        {
            if (other == null || other == this) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }
}
