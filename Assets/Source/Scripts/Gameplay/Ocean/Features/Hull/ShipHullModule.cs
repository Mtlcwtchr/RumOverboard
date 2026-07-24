using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Hull
{
    /// <summary>
    /// Reusable hull module prefab metadata (visual subtree + gameplay collider roots).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipHullModule : MonoBehaviour
    {
        [SerializeField] private string moduleId;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform solidCollidersRoot;
        [SerializeField] private Transform traversalTriggersRoot;
        [SerializeField] private Transform interactionZonesRoot;

        public string ModuleId => moduleId;
        public Transform VisualRoot => visualRoot;
        public Transform SolidCollidersRoot => solidCollidersRoot;
        public Transform TraversalTriggersRoot => traversalTriggersRoot;
        public Transform InteractionZonesRoot => interactionZonesRoot;

        public void Configure(
            string id,
            Transform visual,
            Transform solid,
            Transform traversal,
            Transform interaction)
        {
            moduleId = id;
            visualRoot = visual;
            solidCollidersRoot = solid;
            traversalTriggersRoot = traversal;
            interactionZonesRoot = interaction;
        }
    }
}

