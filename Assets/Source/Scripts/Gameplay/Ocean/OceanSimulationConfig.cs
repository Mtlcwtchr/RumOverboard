using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    [CreateAssetMenu(fileName = "OceanSimulationConfig", menuName = "RumOverboard/Ocean/Simulation Config")]
    public class OceanSimulationConfig : ScriptableObject
    {
        [Header("Deterministic base")]
        public uint sharedSeed = 1337;
        [Min(0f)] public float seaLevel = 0f;
        [Min(0.01f)] public float transitionSeconds = 8f;

        [Header("Profiles")]
        public OceanSeaStateProfile defaultProfile;
        public OceanSeaStateProfile[] availableProfiles = new OceanSeaStateProfile[0];

        [Header("Quality")]
        public OceanQualityPreset low;
        public OceanQualityPreset medium;
        public OceanQualityPreset high;

        [Header("Depth")]
        public OceanDepthProfile depthProfile;

        public OceanQualityPreset ResolveQuality(OceanQualityLevel level)
        {
            switch (level)
            {
                case OceanQualityLevel.Low:
                    return low != null ? low : medium;
                case OceanQualityLevel.High:
                    return high != null ? high : medium;
                default:
                    return medium != null ? medium : low;
            }
        }
    }

    public enum OceanQualityLevel
    {
        Low,
        Medium,
        High,
    }
}

