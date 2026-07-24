using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes
{
    /// <summary>
    /// Draws sampled current/wind/wave-normal vectors in feature scenes.
    /// </summary>
    public class OceanFieldDebugGizmos : MonoBehaviour
    {
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanWindSystem windSystem;
        [SerializeField] private bool drawGrid = true;
        [SerializeField] private int halfExtent = 2;
        [SerializeField] private float spacing = 6f;
        [SerializeField] private float vectorScale = 0.35f;

        public void Bind(OceanWaveField waves, OceanWindSystem wind)
        {
            waveField = waves;
            windSystem = wind;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawGrid || waveField == null)
                return;

            float time = waveField.OceanTimeNow;
            int extent = Mathf.Clamp(halfExtent, 1, 8);
            float step = Mathf.Max(1f, spacing);

            for (int z = -extent; z <= extent; z++)
            {
                for (int x = -extent; x <= extent; x++)
                {
                    Vector3 p = transform.position + new Vector3(x * step, 0f, z * step);
                    OceanSample sample = waveField.Sample(p, time);
                    Vector3 origin = new Vector3(p.x, sample.surfaceHeight, p.z);

                    Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.85f);
                    Gizmos.DrawLine(origin, origin + sample.currentVelocity * vectorScale);

                    Gizmos.color = new Color(0.35f, 1f, 0.55f, 0.85f);
                    Gizmos.DrawLine(origin, origin + sample.horizontalVelocity * vectorScale);

                    Gizmos.color = new Color(1f, 0.95f, 0.35f, 0.9f);
                    Gizmos.DrawLine(origin, origin + sample.surfaceNormal * 1.1f);

                    if (windSystem != null)
                    {
                        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
                        Vector3 wind = windSystem.EvaluateWind(origin, time);
                        Gizmos.DrawLine(origin, origin + wind * (vectorScale * 0.1f));
                    }
                }
            }
        }
#endif
    }
}

