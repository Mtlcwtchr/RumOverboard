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
