#if FUSION2
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing
{
    /// <summary>Calm-water rowing lab: furled sails, optional hull windage and water rendering.</summary>
    public sealed class RowingSceneController : MonoBehaviour
    {
        public bool WaterVisible = true;
        public bool WindEnabled;
        public bool WavesEnabled;
        private NetworkShip _ship;
        private Vector3 _position;
        private Quaternion _rotation;
        private ShipBuoyancyController _buoyancy;
        private float _windForce;
        private OceanSurfaceRenderer _water;
        private OceanWaveField _field;

        private void Update()
        {
            if (_water == null) _water = FindAnyObjectByType<OceanSurfaceRenderer>();
            if (_water != null) _water.enabled = WaterVisible;
            if (_field == null) _field = FindAnyObjectByType<OceanWaveField>();
            if (_field != null)
            {
                _field.RuntimeWaveAmplitudeMultiplier = WavesEnabled ? 1f : 0f;
                _field.RuntimeCurrentSpeedMultiplier = 0f;
                _field.WindToCurrentFactor = 0f;
            }
            if (_ship == null)
            {
                foreach (var ship in NetworkShip.All)
                    if (ship != null && ship.Object != null && ship.Object.IsValid && ship.HasStateAuthority)
                    { _ship = ship; break; }
                if (_ship == null) return;
                _position = _ship.transform.position;
                _rotation = _ship.transform.rotation;
                _buoyancy = _ship.GetComponent<ShipBuoyancyController>();
                if (_buoyancy != null) _windForce = _buoyancy.WindHullForceCoefficient;
                if (_ship.Sails != null)
                    for (int i = 0; i < _ship.Sails.SailCount; i++) _ship.SetSailFurl(i, 1f);
            }
            if (_buoyancy != null) _buoyancy.WindHullForceCoefficient = WindEnabled ? _windForce : 0f;
        }

        public void ResetShip()
        {
            if (_ship == null || !_ship.HasStateAuthority) return;
            _ship.ResetOars();
            var networkBody = _ship.GetComponent<Fusion.Addons.Physics.NetworkRigidbody3D>();
            if (networkBody != null) networkBody.Teleport(_position, _rotation);
            else
            {
                _ship.Body.position = _position;
                _ship.Body.rotation = _rotation;
            }
            _ship.Body.linearVelocity = Vector3.zero;
            _ship.Body.angularVelocity = Vector3.zero;
        }
    }
}
#endif
