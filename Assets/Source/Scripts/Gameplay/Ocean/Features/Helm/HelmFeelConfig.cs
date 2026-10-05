using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Helm
{
    /// <summary>
    /// How the ship's wheel FEELS: it's a heavy wheel with inertia, connected to a rudder that the
    /// water pushes on. All values are wheel accelerations (deg/s²) so they're easy to reason about.
    /// Authored asset: Assets/Source/Configs/FeatureScenes/HelmFeelConfig.asset — edited live from
    /// the Helm debug window (F4) and saved back with "Save to config".
    /// </summary>
    [CreateAssetMenu(fileName = "HelmFeelConfig", menuName = "RumOverboard/Feature Configs/Helm Feel")]
    public sealed class HelmFeelConfig : ScriptableObject
    {
        [Header("Helmsman")]
        [Tooltip("Wheel acceleration the helmsman produces at full A/D (deg/s²).")]
        public float SteerAccel = 1500f;
        [Tooltip("Max acceleration a helmsman can resist when just holding the wheel (deg/s²). " +
                 "A sea that pushes harder than this kicks the wheel out of the hands.")]
        public float HoldStrength = 1100f;
        [Tooltip("How firmly a passive helmsman damps the wheel's spin (1/s).")]
        public float HoldDamping = 14f;

        [Header("Wheel")]
        [Tooltip("Bearing + linkage friction while manned (1/s). Terminal spin ≈ SteerAccel / this.")]
        public float WheelFriction = 4.5f;
        [Tooltip("Friction with nobody on the wheel (1/s) — low: the sea spins it freely.")]
        public float UnmannedFriction = 2f;
        [Tooltip("Dry friction of the rudder stock / tiller ropes (deg/s²): pushes weaker than this " +
                 "don't move a resting wheel, so it doesn't wander at anchor.")]
        public float StaticFriction = 160f;
        [Tooltip("Velocity kept (reversed) when the wheel hits its stop.")]
        [Range(0f, 1f)] public float StopBounce = 0.25f;

        [Header("Water on the rudder")]
        [Tooltip("Self-centring: wheel accel per (forward flow m/s)² × sin(rudder). Going astern it flips: " +
                 "the rudder slams over.")]
        public float RudderCentering = 28f;
        [Tooltip("Sideways flow past the rudder (current, waves, drift) per (m/s)².")]
        public float FlowBuffet = 55f;
        [Tooltip("Turbulent shudder (deg/s²) at speed — the wheel is never perfectly still.")]
        public float Turbulence = 140f;
        [Tooltip("Forward flow (m/s) at which turbulence is at full strength.")]
        public float TurbulenceFullFlow = 6f;

        private static HelmFeelConfig _fallback;
        private static HelmFeelConfig _active;

        /// <summary>The config used at runtime (assigned by ShipHelm; defaults otherwise).</summary>
        public static HelmFeelConfig Active
        {
            get
            {
                if (_active != null) return _active;
                if (_fallback == null) _fallback = CreateInstance<HelmFeelConfig>();
                return _fallback;
            }
            set => _active = value;
        }

        public void CopyFrom(HelmFeelConfig other)
        {
            if (other == null || other == this) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }
}

