using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    [CreateAssetMenu(fileName = "OceanSeaStateProfile", menuName = "RumOverboard/Ocean/Sea State Profile")]
    public class OceanSeaStateProfile : ScriptableObject
    {
        [Header("Waves")]
        public OceanWaveDefinition[] waves = new OceanWaveDefinition[0];

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
    }
}

