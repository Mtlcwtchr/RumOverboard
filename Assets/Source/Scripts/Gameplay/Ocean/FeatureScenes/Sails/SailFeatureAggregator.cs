using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes.Sails
{
    /// <summary>
    /// Centralized dependency binder for the isolated sails feature scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SailFeatureAggregator : MonoBehaviour
    {
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanWindSystem windSystem;
        [SerializeField] private SailFeatureStandaloneSystem sailSystem;

        public SailFeatureStandaloneSystem SailSystem => sailSystem;

        private void Awake()
        {
            CacheRefs();
        }

        private void OnValidate()
        {
            CacheRefs();
        }

        public void Bind(OceanWaveField waves, OceanWindSystem wind)
        {
            waveField = waves;
            windSystem = wind;
            CacheRefs();

            if (sailSystem != null)
                sailSystem.Configure(waveField, windSystem);
        }

        private void CacheRefs()
        {
            if (sailSystem == null)
                sailSystem = GetComponent<SailFeatureStandaloneSystem>();
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            if (windSystem == null)
                windSystem = FindAnyObjectByType<OceanWindSystem>();
        }
    }
}

