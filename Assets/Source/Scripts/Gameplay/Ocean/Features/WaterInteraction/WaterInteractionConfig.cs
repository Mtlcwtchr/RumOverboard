using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.WaterInteraction
{
    /// <summary>
    /// Tuning for how the sea reacts to hulls: the GPU interaction map (foam + displacement waves),
    /// what each hull writes into it, and splash effects. Authored asset:
    /// Assets/Source/Configs/FeatureScenes/WaterInteractionConfig.asset — edited live from the
    /// Water debug window (F3) and saved back with "Save to config".
    /// </summary>
    [CreateAssetMenu(fileName = "WaterInteractionConfig", menuName = "RumOverboard/Feature Configs/Water Interaction")]
    public sealed class WaterInteractionConfig : ScriptableObject
    {
        [Header("Interaction map (follows the camera)")]
        [Tooltip("World size (m) of the square the map covers.")]
        public float MapSize = 192f;
        [Tooltip("Map resolution (texels per side).")]
        public int Resolution = 512;
        public Shader SimShader;

        [Header("Foam")]
        [Tooltip("Foam fade (1/s). 0.15 ≈ a wake that lingers ~15 s.")]
        public float FoamDecay = 0.16f;
        [Tooltip("How fast foam spreads out (blur weight per second).")]
        public float FoamSpread = 2.2f;

        [Header("Displacement waves (hull pushes the water)")]
        [Tooltip("Ripple speed (m/s) of the small waves the hull makes.")]
        public float WaveSpeed = 3.2f;
        [Tooltip("Ripple damping (1/s).")]
        public float WaveDamping = 0.9f;
        [Tooltip("Pull of the displaced surface back to rest (1/s).")]
        public float HeightRestore = 0.35f;
        [Tooltip("How strongly a stamp drags the surface toward its target height (0..1).")]
        [Range(0f, 1f)] public float StampPush = 0.35f;

        [Header("Hull")]
        [Tooltip("Waterline samples per side of the hull.")]
        public int SamplesPerSide = 11;
        [Tooltip("Speed through the water (m/s) below which the hull makes no way-foam.")]
        public float MinSpeed = 0.35f;
        [Tooltip("Speed (m/s) at which way-foam / bow wave are at full strength.")]
        public float FullSpeed = 7f;
        [Tooltip("Foam along the waterline at full speed.")]
        public float HullFoam = 0.85f;
        [Tooltip("Foam stamp radius (m) along the hull.")]
        public float HullFoamRadius = 0.9f;
        [Tooltip("Foam when the hull slams down / the sea rises up its side, per m/s of immersion speed.")]
        public float ImmersionFoam = 0.45f;
        [Tooltip("Bow wave height (m) at full speed.")]
        public float BowWave = 0.55f;
        [Tooltip("Trough (m) along the quarters / stern at full speed.")]
        public float SternTrough = 0.3f;
        [Tooltip("Turbulent wake foam behind the stern at full speed.")]
        public float WakeFoam = 0.95f;
        [Tooltip("Wake width as a fraction of the beam.")]
        public float WakeWidth = 0.8f;

        [Header("Splashes")]
        [Tooltip("Splash effect prefab (particles). Spawned where the hull slams into the sea.")]
        public GameObject SplashPrefab;
        [Tooltip("Immersion speed (m/s) at a bow/quarter point that throws a splash.")]
        public float SplashMinImmersion = 1.6f;
        [Tooltip("Seconds between splashes from the same hull.")]
        public float SplashCooldown = 0.35f;
        [Tooltip("Splash size at the threshold; grows with impact.")]
        public float SplashScale = 0.9f;
        [Tooltip("Speed (m/s) above which the bow throws spray continuously.")]
        public float BowSpraySpeed = 4.5f;

        [Header("Ocean surface (shader)")]
        [Tooltip("Vertical scale of the map's displacement on the rendered sea.")]
        public float DisplacementScale = 1f;
        [Tooltip("How much the displacement tilts the surface normals.")]
        public float NormalStrength = 1.4f;

        private static WaterInteractionConfig _fallback;
        private static WaterInteractionConfig _active;

        public static WaterInteractionConfig Active
        {
            get
            {
                if (_active != null) return _active;
                if (_fallback == null) _fallback = CreateInstance<WaterInteractionConfig>();
                return _fallback;
            }
            set => _active = value;
        }

        public void CopyFrom(WaterInteractionConfig other)
        {
            if (other == null || other == this) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }
}

