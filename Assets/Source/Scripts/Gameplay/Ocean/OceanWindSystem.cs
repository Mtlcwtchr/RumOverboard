using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Lightweight runtime wind model for ship movement prototyping.
    /// </summary>
    public class OceanWindSystem : MonoBehaviour
    {
        [SerializeField] private bool windEnabled = true;
        [SerializeField] [Range(0f, 360f)] private float directionDegrees = 35f;
        [SerializeField] [Min(0f)] private float baseStrength = 12f;

        [Header("Gusts")]
        [SerializeField] [Min(0f)] private float gustStrength = 4f;
        [SerializeField] [Min(0.01f)] private float gustFrequency = 0.12f;

        [Header("Turbulence")]
        [SerializeField] [Min(0f)] private float turbulenceStrength = 1.5f;
        [SerializeField] [Min(0.001f)] private float turbulenceScale = 0.05f;

        private const float TwoPi = 6.28318530718f;

        public bool WindEnabled
        {
            get => windEnabled;
            set => windEnabled = value;
        }

        public float DirectionDegrees
        {
            get => directionDegrees;
            set
            {
                directionDegrees = value % 360f;
                if (directionDegrees < 0f)
                    directionDegrees += 360f;
            }
        }

        public float BaseStrength
        {
            get => baseStrength;
            set => baseStrength = Mathf.Max(0f, value);
        }

        public float GustStrength
        {
            get => gustStrength;
            set => gustStrength = Mathf.Max(0f, value);
        }

        public float GustFrequency
        {
            get => gustFrequency;
            set => gustFrequency = Mathf.Max(0.01f, value);
        }

        public float TurbulenceStrength
        {
            get => turbulenceStrength;
            set => turbulenceStrength = Mathf.Max(0f, value);
        }

        public float TurbulenceScale
        {
            get => turbulenceScale;
            set => turbulenceScale = Mathf.Max(0.001f, value);
        }

        public Vector3 DirectionVector => Quaternion.Euler(0f, directionDegrees, 0f) * Vector3.forward;

        public Vector3 EvaluateWind(Vector3 worldPos, float time)
        {
            if (!windEnabled || baseStrength <= 0f)
                return Vector3.zero;

            float gust = gustStrength * Mathf.Sin(time * gustFrequency * TwoPi);
            float turbulence = 0f;
            if (turbulenceStrength > 0.0001f)
            {
                float noise = Mathf.PerlinNoise(worldPos.x * turbulenceScale + 17.2f, worldPos.z * turbulenceScale + time * turbulenceScale + 3.1f);
                turbulence = (noise - 0.5f) * 2f * turbulenceStrength;
            }

            float strength = Mathf.Max(0f, baseStrength + gust + turbulence);
            return DirectionVector * strength;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position;
            Vector3 wind = DirectionVector * Mathf.Max(0.5f, baseStrength * 0.4f);
            Gizmos.color = new Color(1f, 0.95f, 0.3f, 0.9f);
            Gizmos.DrawLine(origin, origin + wind);
            Gizmos.DrawSphere(origin + wind, 0.15f);
        }
#endif
    }
}

