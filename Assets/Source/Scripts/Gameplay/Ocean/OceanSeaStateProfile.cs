using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    [CreateAssetMenu(fileName = "OceanSeaStateProfile", menuName = "RumOverboard/Ocean/Sea State Profile")]
    public class OceanSeaStateProfile : ScriptableObject
    {
        [Header("Waves")]
        public OceanWaveDefinition[] waves = new OceanWaveDefinition[0];

        [Header("Crest shape")]
        [Tooltip("Global multiplier on every wave's steepness — pushes crests from round (0.5) to sharp/choppy (1.5+). " +
                 "Kept below the point where Gerstner crests self-intersect by the per-wave clamp.")]
        [Range(0f, 2f)] public float choppiness = 1f;

        [Header("Dominant swell (big directional rollers)")]
        [Tooltip("Compass heading the primary swell travels TOWARD (0 = +Z, 90 = +X). Steer this so the tall " +
                 "rollers march across the play area — a ship sailing into it takes the big waves on the bow.")]
        [Range(0f, 360f)] public float swellDirectionDegrees = 0f;
        [Tooltip("Amplitude of the long-period swell added on top of the wave stack. 0 disables the swell.")]
        [Min(0f)] public float swellAmplitude = 0f;
        [Min(1f)] public float swellWavelength = 70f;
        [Min(0f)] public float swellSpeed = 7f;
        [Range(0f, 1f)] public float swellSteepness = 0.55f;

        [Header("Directional instability")]
        [Range(0f, 45f)] public float directionJitterDegrees = 3f;

        [Header("Currents")]
        public Vector2 globalCurrentDirection = new Vector2(1f, 0f);
        [Min(0f)] public float globalCurrentSpeed = 0.25f;

        [Header("Visual tuning")]
        [Range(0f, 1f)] public float foamIntensity = 0.35f;
        [Range(0f, 1f)] public float crestFoamThreshold = 0.65f;
        [Range(0f, 2f)] public float shallowColorBoost = 0.5f;

        [Header("Physics tuning")]
        [Range(0f, 3f)] public float buoyancyMultiplier = 1f;
        [Range(0f, 3f)] public float dragMultiplier = 1f;
        [Range(0f, 2f)] public float waveImpactMultiplier = 1f;

        [Header("FX")]
        [Min(0f)] public float splashFrequencyScale = 1f;

        public Vector3 GlobalCurrent => new Vector3(globalCurrentDirection.normalized.x, 0f, globalCurrentDirection.normalized.y) * globalCurrentSpeed;

        /// <summary>Swell heading as an XZ unit vector (matches the wind/degrees convention: 0 = +Z, 90 = +X).</summary>
        public Vector2 SwellDirection2D
        {
            get
            {
                float rad = swellDirectionDegrees * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                return d.sqrMagnitude < 0.0001f ? Vector2.up : d.normalized;
            }
        }

        public bool HasSwell => swellAmplitude > 0.0001f && swellWavelength > 0.01f;

        /// <summary>The dominant swell as a synthesized Large-band wave, so physics + visuals + shader share it.</summary>
        public OceanWaveDefinition BuildSwellWave()
        {
            return new OceanWaveDefinition
            {
                direction = SwellDirection2D,
                amplitude = swellAmplitude,
                wavelength = swellWavelength,
                speed = swellSpeed,
                frequency = 1f,
                steepness = swellSteepness,
                phase = 0f,
                physicsWeight = 1f,
                visualWeight = 1f,
                band = OceanWaveBand.Large,
            };
        }

        /// <summary>
        /// Heading of the strongest wave component (the swell if present, otherwise the
        /// highest-amplitude authored wave), as an XZ unit vector. Used for debug arrows.
        /// </summary>
        public Vector2 DominantWaveDirection2D
        {
            get
            {
                if (HasSwell)
                    return SwellDirection2D;

                float best = -1f;
                Vector2 dir = Vector2.up;
                if (waves != null)
                {
                    for (int i = 0; i < waves.Length; i++)
                    {
                        if (!waves[i].IsValid || waves[i].amplitude <= best)
                            continue;
                        best = waves[i].amplitude;
                        dir = waves[i].DirectionNormalized;
                    }
                }
                return dir;
            }
        }

        /// <summary>
        /// Rough significant-wave-height estimate (peak-to-trough-ish) for HUD/debug. Sums the
        /// large/medium band amplitudes plus the swell — not physically exact, just a readable gauge.
        /// </summary>
        public float EstimatedWaveHeight()
        {
            float h = HasSwell ? swellAmplitude : 0f;
            if (waves != null)
            {
                for (int i = 0; i < waves.Length; i++)
                {
                    if (!waves[i].IsValid || waves[i].band == OceanWaveBand.Ripple)
                        continue;
                    h += waves[i].amplitude * 0.6f;
                }
            }
            return h * 2f;
        }
    }
}
