using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes.Sails
{
    /// <summary>
    /// Sail/mast feature sandbox for wind-load and stress visualization.
    /// Scene hierarchy is pre-built via the editor menu (RumOverboard → Feature Scenes → Setup SailScene).
    /// At runtime the controller only validates references and binds the aggregator.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SailFeatureSceneController : MonoBehaviour
    {
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanWindSystem windSystem;
        [SerializeField] private OceanSurfaceRenderer surfaceRenderer;
        [SerializeField] private SailFeatureAggregator featureAggregator;
        [SerializeField] private SailFeatureConfig config;

        public OceanWaveField WaveField => waveField;
        public OceanWindSystem WindSystem => windSystem;
        public OceanSurfaceRenderer SurfaceRenderer => surfaceRenderer;
        public SailFeatureStandaloneSystem SailSystem => featureAggregator != null ? featureAggregator.SailSystem : null;

        /// <summary>
        /// Finds the existing controller in the scene.
        /// Returns null if the scene was not set up via the editor menu.
        /// </summary>
        public static SailFeatureSceneController EnsureSceneReady()
        {
            var existing = FindAnyObjectByType<SailFeatureSceneController>();
            if (existing != null)
            {
                existing.BindAndApplyConfig();
                return existing;
            }

            Debug.LogWarning("[SailFeatureSceneController] No controller found in scene. " +
                             "Use RumOverboard → Feature Scenes → Setup SailScene to configure the scene.");
            return null;
        }

        private void Awake()
        {
            BindAndApplyConfig();
        }

        private void BindAndApplyConfig()
        {
            ValidateReferences();

            if (featureAggregator != null && waveField != null)
                featureAggregator.Bind(waveField, windSystem);

            if (config == null)
                config = FeatureSceneConfigPersistence.LoadOrCreateSailConfig();
            if (config != null)
                config.ApplyTo(this);

            // Bind debug window if present
            var debug = FindAnyObjectByType<SailFeatureDebugWindow>();
            if (debug != null)
                debug.Bind(this);
        }

        public void ResetFromConfig()
        {
            if (config == null)
                config = FeatureSceneConfigPersistence.LoadOrCreateSailConfig();
            config?.ApplyTo(this);
        }

        public void SaveToConfig()
        {
            if (config == null)
                config = FeatureSceneConfigPersistence.LoadOrCreateSailConfig();
            if (config == null)
                return;

            config.CaptureFrom(this);
            FeatureSceneConfigPersistence.SaveAsset(config);
        }

        private void ValidateReferences()
        {
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            if (windSystem == null)
                windSystem = FindAnyObjectByType<OceanWindSystem>();
            if (surfaceRenderer == null)
                surfaceRenderer = FindAnyObjectByType<OceanSurfaceRenderer>();
            if (featureAggregator == null)
                featureAggregator = FindAnyObjectByType<SailFeatureAggregator>();

            if (waveField == null)
                Debug.LogError("[SailFeatureSceneController] OceanWaveField missing. Run Setup SailScene.", this);
            if (featureAggregator == null)
                Debug.LogError("[SailFeatureSceneController] SailFeatureAggregator missing. Run Setup SailScene.", this);
        }
    }
}
