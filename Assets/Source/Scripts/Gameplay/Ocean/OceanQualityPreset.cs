using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    [CreateAssetMenu(fileName = "OceanQualityPreset", menuName = "RumOverboard/Ocean/Quality Preset")]
    public class OceanQualityPreset : ScriptableObject
    {
        [Header("Sampling")]
        [Range(1, 8)] public int horizontalDisplacementIterations = 3;
        [Range(1, 16)] public int maxCpuLargeWaves = 6;
        [Range(1, 16)] public int maxCpuMediumWaves = 6;

        [Header("Surface LOD")]
        [Range(8, 256)] public int nearResolution = 96;
        [Range(8, 256)] public int midResolution = 64;
        [Range(8, 256)] public int farResolution = 48;
        [Min(16f)] public float nearSize = 80f;
        [Min(32f)] public float midSize = 220f;
        [Min(64f)] public float farSize = 520f;

        [Header("Runtime toggles")]
        public bool enableDistantFoam = true;
        public bool enableRemoteSprayFx = true;

        public static OceanQualityPreset CreateRuntime(string name, int near, int mid, int far)
        {
            var preset = CreateInstance<OceanQualityPreset>();
            preset.name = name;
            preset.nearResolution = near;
            preset.midResolution = mid;
            preset.farResolution = far;
            return preset;
        }
    }
}

