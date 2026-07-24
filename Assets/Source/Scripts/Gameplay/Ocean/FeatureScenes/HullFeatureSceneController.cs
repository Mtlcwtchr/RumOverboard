using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes
{
    /// <summary>
    /// Hull-only simulation sandbox for wave/buoyancy tuning.
    /// Scene hierarchy is pre-built via the editor menu (RumOverboard → Feature Scenes → Setup HullScene).
    /// At runtime the controller only validates references and binds the aggregator.
    /// </summary>
    [DisallowMultipleComponent]
    public class HullFeatureSceneController : MonoBehaviour
    {
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanWindSystem windSystem;
        [SerializeField] private OceanSurfaceRenderer surfaceRenderer;
        [SerializeField] private ShipFeatureAggregator shipAggregator;
        [SerializeField] private HullFeatureConfig config;

        public OceanWaveField WaveField => waveField;
        public OceanWindSystem WindSystem => windSystem;
        public OceanSurfaceRenderer SurfaceRenderer => surfaceRenderer;
        public ShipBuoyancyController HullBuoyancy => shipAggregator != null ? shipAggregator.Buoyancy : null;

        /// <summary>
        /// Finds the existing controller in the scene.
        /// Returns null if the scene was not set up via the editor menu.
        /// </summary>
        public static HullFeatureSceneController EnsureSceneReady()
        {
            var existing = FindAnyObjectByType<HullFeatureSceneController>();
            if (existing != null)
            {
                existing.BindAndApplyConfig();
                return existing;
            }

            Debug.LogWarning("[HullFeatureSceneController] No controller found in scene. " +
                             "Use RumOverboard → Feature Scenes → Setup HullScene to configure the scene.");
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
            {
                shipAggregator.Bind(waveField, windSystem);

                ShipBuoyancyController buoyancy = shipAggregator.Buoyancy;
                if (buoyancy != null)
                {
                    buoyancy.DebugDrawAlways = true;
                    buoyancy.DebugDrawForces = true;
                    buoyancy.DebugDrawResultants = true;
                    buoyancy.DebugDrawNormals = true;
                }
            }

            if (config == null)
                config = FeatureSceneConfigPersistence.LoadOrCreateHullConfig();
            if (config != null)
                config.ApplyTo(this);

            // Bind debug window if present
            var debug = FindAnyObjectByType<HullFeatureDebugWindow>();
            if (debug != null)
                debug.Bind(this);
        }

        public void ResetFromConfig()
        {
            if (config == null)
                config = FeatureSceneConfigPersistence.LoadOrCreateHullConfig();

            if (config != null)
                config.ApplyTo(this);
        }

        public void SaveToConfig()
        {
            if (config == null)
                config = FeatureSceneConfigPersistence.LoadOrCreateHullConfig();
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
                Debug.LogError("[HullFeatureSceneController] OceanWaveField missing. Run Setup HullScene.", this);
            if (shipAggregator == null)
                Debug.LogError("[HullFeatureSceneController] ShipFeatureAggregator missing. Run Setup HullScene.", this);
        }
    }
}
