using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Runtime aggregator for ship simulation modules. Keeps NetworkShip focused on replication only.
    /// </summary>
    public sealed class ShipRuntime
    {
        private readonly ShipBuoyancyController _buoyancy;
        private readonly ShipSailsAggregator _sails;
        private readonly OceanWaveField _waveField;
        private readonly Rigidbody _body;

        public ShipRuntime(
            ShipBuoyancyController buoyancy,
            ShipSailsAggregator sails,
            OceanWaveField waveField,
            Rigidbody body)
        {
            _buoyancy = buoyancy;
            _sails = sails;
            _waveField = waveField;
            _body = body;
        }

        public void Configure(bool stateAuthority)
        {
            if (_buoyancy != null)
            {
                _buoyancy.ConfigureReferences(_waveField, _body);
                _buoyancy.SetExternallyDriven(stateAuthority);
                _buoyancy.enabled = stateAuthority;
            }

            if (_sails != null && _sails.WindSystem == null)
                _sails.WindSystem = Object.FindAnyObjectByType<OceanWindSystem>();

            if (_sails != null)
                _sails.SetExternallyDriven(stateAuthority);
        }

        public float StepAuthority(float deltaTime, float oceanTime, float simulationTime)
        {
            if (_sails != null)
                _sails.Step(deltaTime, simulationTime);

            if (_buoyancy == null || _waveField == null)
                return 0f;

            _buoyancy.Step(deltaTime, oceanTime);
            return _buoyancy.LastImpactStrength;
        }

        public void EnsureProxyState()
        {
            if (_buoyancy != null && _buoyancy.enabled)
                _buoyancy.enabled = false;
        }
    }
}
