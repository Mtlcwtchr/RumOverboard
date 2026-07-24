using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Wheel
{
    /// <summary>
    /// Reusable wheel/helm module prefab metadata (wheel visual, rudder, stand anchor, colliders).
    /// Extracted from NetworkShip_Reference alongside hull and mast modules.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipWheelModule : MonoBehaviour
    {
        [SerializeField] private string moduleId;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform wheelModel;
        [SerializeField] private Transform rudderBlade;
        [SerializeField] private Transform standAnchor;
        [SerializeField] private Transform solidCollidersRoot;
        [SerializeField] private Transform interactionZonesRoot;

        public string ModuleId => moduleId;
        public Transform VisualRoot => visualRoot;
        public Transform WheelModel => wheelModel;
        public Transform RudderBlade => rudderBlade;
        public Transform StandAnchor => standAnchor;
        public Transform SolidCollidersRoot => solidCollidersRoot;
        public Transform InteractionZonesRoot => interactionZonesRoot;

        public void Configure(
            string id,
            Transform visual,
            Transform wheel,
            Transform rudder,
            Transform stand,
            Transform solidColliders,
            Transform interactionZones)
        {
            moduleId = id;
            visualRoot = visual;
            wheelModel = wheel;
            rudderBlade = rudder;
            standAnchor = stand;
            solidCollidersRoot = solidColliders;
            interactionZonesRoot = interactionZones;
        }
    }
}

