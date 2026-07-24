#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Ocean;
using UnityEngine;

namespace RumOverboard.Networking
{
    /// <summary>
    /// Authoritative gameplay wrapper for the ship. The host simulates the buoyant
    /// Rigidbody; everyone else sees an interpolated proxy.
    ///
    /// HYBRID REPLICATION — the transform is NOT hand-rolled here anymore:
    ///   • Pose + velocities (position, rotation, linear/angular velocity, kinematic,
    ///     constraints) are owned by the Fusion Physics addon's <b>NetworkRigidbody3D</b>.
    ///     It replicates them and interpolates proxies in its own Render(); proxies are
    ///     forced kinematic by the addon. Writing the pose ourselves would fight it.
    ///   • This component only replicates the two authority-only DERIVED values a proxy
    ///     cannot recompute from the synced transform:
    ///       - <see cref="LastImpactStrength"/> — a transient wave-impact event (buoyancy
    ///         runs on the authority only, so proxies would otherwise never see it);
    ///       - <see cref="OceanTime"/> — the authoritative ocean clock, so every peer
    ///         samples the same waves. Single source of truth for ocean time (do NOT also
    ///         put a NetworkOceanState in the scene, or the two will fight over the clock).
    ///
    /// Roll/pitch are intentionally not networked: they are pure functions of the replicated
    /// rotation, so ShipDeckMotionProvider reads them straight off the buoyancy component
    /// (which reads the synced transform) on every peer.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkShip : NetworkBehaviour
    {
        [SerializeField] private ShipBuoyancyController buoyancy;
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private ShipSailsAggregator sailsAggregator;
        [SerializeField] private ShipFeatureAggregator featureAggregator;

        [Header("Ocean time correction (proxies)")]
        [SerializeField] private float oceanTimeCorrectionThreshold = 0.1f;
        [SerializeField] private float oceanTimeCorrectionGain = 0.1f;

        // Authority-only derived state. Everything geometric lives on NetworkRigidbody3D.
        [Networked] public float LastImpactStrength { get; set; }
        [Networked] public float OceanTime { get; set; }

        private Rigidbody _rb;
        private bool _hasNetworkRigidbody;
        private ShipRuntime _shipRuntime;

        /// <summary>Latest wave-impact strength, valid on every peer (deck FX / camera shake).</summary>
        public float NetworkedImpactStrength => LastImpactStrength;

        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();
            if (buoyancy == null)
                buoyancy = GetComponent<ShipBuoyancyController>();
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            if (sailsAggregator == null)
                sailsAggregator = GetComponent<ShipSailsAggregator>();
            if (sailsAggregator == null)
                sailsAggregator = gameObject.AddComponent<ShipSailsAggregator>();

            if (sailsAggregator != null && sailsAggregator.WindSystem == null)
                sailsAggregator.WindSystem = FindAnyObjectByType<OceanWindSystem>();

            if (featureAggregator == null)
                featureAggregator = GetComponent<ShipFeatureAggregator>();
            if (featureAggregator != null)
                featureAggregator.Bind(waveField, sailsAggregator != null ? sailsAggregator.WindSystem : FindAnyObjectByType<OceanWindSystem>());

            _shipRuntime = new ShipRuntime(buoyancy, sailsAggregator, waveField, _rb);

            // Cosmetic bow/wake foam on every peer (derives speed from the synced transform).
            if (GetComponent<ShipWakeFoam>() == null)
                gameObject.AddComponent<ShipWakeFoam>();

            // The Physics addon must own the transform; without it the ship won't replicate.
            _hasNetworkRigidbody = TryGetComponent(out Fusion.Addons.Physics.NetworkRigidbody3D _);
            if (!_hasNetworkRigidbody)
            {
                Debug.LogError($"[NetworkShip] '{name}' has no NetworkRigidbody3D — the ship transform " +
                               "will NOT replicate. Add the Fusion Physics addon's NetworkRigidbody3D to the " +
                               "ship prefab and re-bake the NetworkObject (see SETUP_MULTIPLAYER.md).");
            }

            _shipRuntime?.Configure(HasStateAuthority);

            if (HasStateAuthority && waveField != null)
                OceanTime = waveField.OceanTimeNow;
        }

        public override void FixedUpdateNetwork()
        {
            if (HasStateAuthority)
            {
                if (waveField != null)
                    OceanTime = waveField.OceanTimeNow;

                // Step buoyancy inside the network tick so PhysX (stepped by the Fusion addon)
                // sees the forces on the same tick and resimulation stays deterministic.
                LastImpactStrength = (_shipRuntime != null && waveField != null)
                    ? _shipRuntime.StepAuthority(Runner.DeltaTime, waveField.OceanTimeNow, Runner.SimulationTime)
                    : 0f;

                return;
            }

            // Proxy: buoyancy stays off; nudge the local ocean clock toward the authority's.
            _shipRuntime?.EnsureProxyState();

            if (waveField != null)
            {
                float error = OceanTime - waveField.OceanTimeNow;
                if (Mathf.Abs(error) > Mathf.Max(0.01f, oceanTimeCorrectionThreshold))
                    waveField.ApplyTimeCorrection(error * Mathf.Clamp01(oceanTimeCorrectionGain));
            }
        }

    }
}
#endif
