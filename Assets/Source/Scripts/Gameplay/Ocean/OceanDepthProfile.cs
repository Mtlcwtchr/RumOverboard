using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    [CreateAssetMenu(fileName = "OceanDepthProfile", menuName = "RumOverboard/Ocean/Depth Profile")]
    public class OceanDepthProfile : ScriptableObject
    {
        [Header("Global")]
        [Min(1f)] public float fallbackDepth = 40f;
        [Min(0.1f)] public float shallowDepth = 4f;

        [Header("Texture-based depth (optional)")]
        public Texture2D depthMap;
        public Vector3 worldOrigin = Vector3.zero;
        public Vector2 worldSize = new Vector2(1024f, 1024f);
        [Min(1f)] public float maxDepthFromMap = 60f;

        [Header("Shallow modifier")]
        public AnimationCurve nearShoreWaveDamping = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1f));

        public float EvaluateDepth01(Vector3 worldPos)
        {
            if (depthMap == null || worldSize.x <= 0.01f || worldSize.y <= 0.01f)
                return 1f;

            float u = Mathf.InverseLerp(worldOrigin.x, worldOrigin.x + worldSize.x, worldPos.x);
            float v = Mathf.InverseLerp(worldOrigin.z, worldOrigin.z + worldSize.y, worldPos.z);
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return 1f;

            return depthMap.GetPixelBilinear(u, v).grayscale;
        }

        public float EvaluateDepthMeters(Vector3 worldPos)
        {
            if (depthMap == null)
                return fallbackDepth;
            return Mathf.Lerp(0f, maxDepthFromMap, EvaluateDepth01(worldPos));
        }
    }
}

