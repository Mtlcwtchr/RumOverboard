using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public struct OceanSample
    {
        public float surfaceHeight;
        public Vector3 surfaceNormal;
        public Vector3 displacement;
        public float verticalVelocity;
        public Vector3 horizontalVelocity;
        public Vector3 currentVelocity;
        public Vector3 waterVelocity;
        public float depthMeters;
        public float shallowFactor;

        public bool IsUnderwater(Vector3 worldPos) => worldPos.y < surfaceHeight;
    }

    public interface IOceanSampler
    {
        OceanSample Sample(Vector3 worldPos, float simulationTime);
    }
}

