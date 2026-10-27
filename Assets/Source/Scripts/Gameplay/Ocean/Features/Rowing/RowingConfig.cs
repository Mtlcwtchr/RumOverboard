using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing
{
    /// <summary>
    /// Tuning for the sweeps (long oars at the bulwark). Authored asset:
    /// Assets/Source/Configs/FeatureScenes/RowingConfig.asset — edited live from the Rowing debug
    /// window (F9) and saved back with "Save to config". Bound to the ship by <c>NetworkShip</c>.
    ///
    /// Rhythm: one W press = one stroke (drive with the blade in the water, then recovery). Pressing
    /// again inside the window around the end of the stroke chains the next one in time and builds a
    /// streak (power grows); pressing too early fouls the stroke (power lost, streak reset). Rowers
    /// whose drives start together get a sync bonus.
    /// </summary>
    [CreateAssetMenu(fileName = "RowingConfig", menuName = "RumOverboard/Feature Configs/Rowing")]
    public sealed class RowingConfig : ScriptableObject
    {
        [Header("Stroke rhythm")]
        [Tooltip("Seconds for one full stroke (drive + recovery).")]
        [Range(0.6f, 4f)] public float StrokePeriod = 1.7f;
        [Tooltip("Share of the stroke with the blade in the water (thrust).")]
        [Range(0.2f, 0.9f)] public float DriveFraction = 0.5f;
        [Tooltip("The next press counts as \"in time\" from this many seconds before the stroke ends…")]
        [Range(0.05f, 1.5f)] public float EarlyWindow = 0.4f;
        [Tooltip("…until this many seconds after it ends.")]
        [Range(0.05f, 1.5f)] public float LateWindow = 0.45f;

        [Header("Stroke power")]
        [Tooltip("Power of a stroke started from rest / out of rhythm (0..1).")]
        [Range(0.1f, 1f)] public float StartPower = 0.5f;
        [Tooltip("Extra power per stroke in the streak (capped at 1).")]
        [Range(0f, 0.5f)] public float StreakStep = 0.17f;
        [Tooltip("A press too early fouls the running stroke: its power is multiplied by this.")]
        [Range(0f, 1f)] public float EarlyPenalty = 0.25f;

        [Header("Crew sync")]
        [Tooltip("Drives that start within this many seconds of each other (same direction) are in sync.")]
        [Range(0f, 1f)] public float SyncWindow = 0.3f;
        [Tooltip("Thrust bonus per other rower in sync.")]
        [Range(0f, 0.5f)] public float SyncBonusPerRower = 0.1f;
        [Tooltip("Cap on the total sync bonus.")]
        [Range(0f, 2f)] public float MaxSyncBonus = 0.5f;

        [Header("Thrust")]
        [Tooltip("Peak thrust of one sweep at full power (N), mid-drive.")]
        [Min(0f)] public float PeakForce = 3200f;
        [Tooltip("Backing water (S) is weaker than pulling.")]
        [Range(0f, 1.5f)] public float BackingFactor = 0.75f;
        [Tooltip("Ship speed (m/s) through the water at which the blades stop adding thrust.")]
        [Min(0.1f)] public float MaxRowSpeed = 3.5f;
        [Tooltip("Turning effect of a sweep (×physical lever): one side rowing alone turns the ship.")]
        [Min(0f)] public float YawLeverage = 14f;

        [Header("Visual")]
        [Tooltip("Fore-aft sweep of the blade during a stroke (deg each way).")]
        [Range(5f, 45f)] public float SweepAngle = 24f;
        [Tooltip("How high the blade is lifted on the recovery (deg).")]
        [Range(0f, 30f)] public float LiftAngle = 9f;

        private static RowingConfig _fallback;
        private static RowingConfig _active;

        /// <summary>The config used at runtime (assigned by NetworkShip; defaults otherwise).</summary>
        public static RowingConfig Active
        {
            get
            {
                if (_active != null) return _active;
                if (_fallback == null) _fallback = CreateInstance<RowingConfig>();
                return _fallback;
            }
            set => _active = value;
        }

        public void CopyFrom(RowingConfig other)
        {
            if (other == null || other == this) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }
}
