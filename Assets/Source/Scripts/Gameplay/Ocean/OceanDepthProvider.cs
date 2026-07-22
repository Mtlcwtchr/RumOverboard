using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public class OceanDepthProvider : MonoBehaviour
    {
        [SerializeField] private OceanSimulationConfig config;
        [SerializeField] private Terrain terrain;

        public float EvaluateDepthMeters(Vector3 worldPos, float surfaceHeight)
        {
            if (terrain != null)
            {
                float floor = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
                return Mathf.Max(0f, surfaceHeight - floor);
            }

            if (config != null && config.depthProfile != null)
                return config.depthProfile.EvaluateDepthMeters(worldPos);

            return 40f;
        }

        public float EvaluateShallowFactor(float depthMeters)
        {
            float shallowDepth = config != null && config.depthProfile != null
                ? config.depthProfile.shallowDepth
                : 4f;

            if (shallowDepth <= 0.001f)
                return 0f;

            return Mathf.Clamp01(1f - depthMeters / shallowDepth);
        }

        public float EvaluateWaveDepthDamping(float shallowFactor)
        {
            if (config == null || config.depthProfile == null || config.depthProfile.nearShoreWaveDamping == null)
                return 1f;

            return Mathf.Clamp(config.depthProfile.nearShoreWaveDamping.Evaluate(1f - shallowFactor), 0f, 1f);
        }
    }
}

