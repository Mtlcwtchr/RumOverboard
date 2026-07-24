using RumOverboard.Gameplay.Ocean.Features.Hull;
using RumOverboard.Gameplay.Ocean.Features.Masts;
using RumOverboard.Gameplay.Ocean.Features.Wheel;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Centralized binder for ship feature dependencies in standalone feature scenes.
    /// The gameplay ship may reuse the same pattern: all cross-component links pass through one place.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class ShipFeatureAggregator : MonoBehaviour
    {
        [SerializeField] private Rigidbody shipBody;
        [SerializeField] private ShipBuoyancyController buoyancy;
        [SerializeField] private ShipDeckMotionProvider deckMotion;
        [SerializeField] private ShipWaterFxController waterFx;
        [SerializeField] private ShipSailsAggregator sailsAggregator;
        [SerializeField] private WheelFeatureStandaloneSystem wheelSystem;
        [SerializeField] private ShipHullModule hullModule;
        [SerializeField] private ShipWheelModule wheelModule;
        [SerializeField] private ShipMastModule[] mastModules;

        public Rigidbody ShipBody => shipBody;
        public ShipBuoyancyController Buoyancy => buoyancy;
        public WheelFeatureStandaloneSystem WheelSystem => wheelSystem;
        public ShipHullModule HullModule => hullModule;
        public ShipWheelModule WheelModule => wheelModule;
        public ShipMastModule[] MastModules => mastModules;

        private void Awake()
        {
            CacheLocalReferences();
        }

        private void OnValidate()
        {
            CacheLocalReferences();
        }

        public void Bind(OceanWaveField waveField, OceanWindSystem windSystem)
        {
            CacheLocalReferences();

            if (buoyancy != null)
            {
                buoyancy.ConfigureReferences(waveField, shipBody);
                if (windSystem != null)
                    buoyancy.WindSystem = windSystem;
            }

            if (deckMotion != null && buoyancy != null)
                deckMotion.ConfigureReferences(buoyancy, shipBody);

            if (waterFx != null && buoyancy != null)
                waterFx.ConfigureReferences(buoyancy, null);

            if (sailsAggregator != null && windSystem != null)
                sailsAggregator.WindSystem = windSystem;

            if (wheelSystem != null)
                wheelSystem.Configure(shipBody, waveField, windSystem);
        }

        private void CacheLocalReferences()
        {
            if (shipBody == null)
                shipBody = GetComponent<Rigidbody>();
            if (buoyancy == null)
                buoyancy = GetComponent<ShipBuoyancyController>();
            if (deckMotion == null)
                deckMotion = GetComponent<ShipDeckMotionProvider>();
            if (waterFx == null)
                waterFx = GetComponent<ShipWaterFxController>();
            if (sailsAggregator == null)
                sailsAggregator = GetComponent<ShipSailsAggregator>();
            if (wheelSystem == null)
                wheelSystem = GetComponent<WheelFeatureStandaloneSystem>();

            if (hullModule == null)
                hullModule = GetComponentInChildren<ShipHullModule>(true);
            if (wheelModule == null)
                wheelModule = GetComponentInChildren<ShipWheelModule>(true);
            mastModules = GetComponentsInChildren<ShipMastModule>(true);
        }
    }
}

