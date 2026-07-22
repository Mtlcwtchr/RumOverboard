using System;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public enum OceanWaveBand
    {
        Large = 0,
        Medium = 1,
        Ripple = 2,
    }

    [Serializable]
    public struct OceanWaveDefinition
    {
        [Tooltip("Direction in XZ plane.")]
        public Vector2 direction;
        [Min(0f)] public float amplitude;
        [Min(0.01f)] public float wavelength;
        [Min(0f)] public float speed;
        [Min(0f)] public float frequency;
        [Range(0f, 1f)] public float steepness;
        public float phase;
        [Range(0f, 1f)] public float physicsWeight;
        [Range(0f, 1f)] public float visualWeight;
        public OceanWaveBand band;

        public Vector2 DirectionNormalized
        {
            get
            {
                if (direction.sqrMagnitude < 0.0001f)
                    return Vector2.right;
                return direction.normalized;
            }
        }

        public bool IsValid => amplitude > 0f && wavelength > 0.01f;
    }
}

