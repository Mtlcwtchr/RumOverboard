using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes
{
    /// <summary>
    /// Standalone wheel/rudder sandbox with optional ocean coupling.
    /// Scene hierarchy is pre-built via the editor menu (RumOverboard → Feature Scenes → Setup WheelScene).
    /// At runtime the controller only validates references and binds the aggregator.
    /// </summary>
    [DisallowMultipleComponent]
    public class WheelFeatureSceneController : MonoBehaviour
    {
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanWindSystem windSystem;
        [SerializeField] private OceanSurfaceRenderer surfaceRenderer;
        [SerializeField] private ShipFeatureAggregator shipAggregator;
        [SerializeField] private WheelFeatureConfig config;

        public OceanWaveField WaveField => waveField;
        public OceanWindSystem WindSystem => windSystem;
        public OceanSurfaceRenderer SurfaceRenderer => surfaceRenderer;
        public WheelFeatureStandaloneSystem WheelSystem => shipAggregator != null ? shipAggregator.WheelSystem : null;

        /// <summary>
        /// Finds the existing controller in the scene.
        /// Returns null if the scene was not set up via the editor menu.
        /// </summary>
        public static WheelFeatureSceneController EnsureSceneReady()
        {
            var existing = FindAnyObjectByType<WheelFeatureSceneController>();
            if (existing != null)
            {
                existing.BindAndApplyConfig();
                return existing;
            }

            Debug.LogWarning("[WheelFeatureSceneController] No controller found in scene. " +
                             "Use RumOverboard → Feature Scenes → Setup WheelScene to configure the scene.");
            return null;
        }

        private void Awake()
        {
            BindAndApplyConfig();
        }

        private void BindAndApplyConfig()
        {
            ValidateReferences();

            if (shipAggregator != null && waveField != null)
                shipAggregator.Bind(waveField, windSystem);

            if (config == null)
                config = FeatureSceneConfigPersistence.LoadOrCreateWheelConfig();
            if (config != null)
                config.ApplyTo(this);

            // Bind debug window if present
            var debug = FindAnyObjectByType<WheelFeatureDebugWindow>();
            if (debug != null)
                debug.Bind(this);
        }

        public void ResetFromConfig()
        {
            if (config == null)
                config = FeatureSceneConfigPersistence.LoadOrCreateWheelConfig();

            if (config != null)
                config.ApplyTo(this);
        }

        public void SaveToConfig()
        {
            if (config == null)
                config = FeatureSceneConfigPersistence.LoadOrCreateWheelConfig();
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
            if (shipAggregator == null)
                shipAggregator = FindAnyObjectByType<ShipFeatureAggregator>();

            if (waveField == null)
                Debug.LogError("[WheelFeatureSceneController] OceanWaveField missing. Run Setup WheelScene.", this);
            if (shipAggregator == null)
                Debug.LogError("[WheelFeatureSceneController] ShipFeatureAggregator missing. Run Setup WheelScene.", this);
        }
    }
}
