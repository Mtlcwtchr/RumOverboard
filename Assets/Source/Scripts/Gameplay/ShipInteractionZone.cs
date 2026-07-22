using UnityEngine;

namespace Source.Scripts.Gameplay
{
    /// <summary>
    /// Lightweight marker for ship interaction trigger volumes (wheel, anchor, doors, etc.).
    /// Gameplay code can later route Interact input by zone kind.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ShipInteractionZone : MonoBehaviour
    {
        public enum ZoneKind
        {
            Generic,
            Helm,
            Anchor,
            Door,
            Rope,
            Ladder,
            CrowNest,
            OarPort,
        }

        [SerializeField] private ZoneKind zone = ZoneKind.Generic;
        [SerializeField] private string prompt = "Interact";

        public ZoneKind Zone => zone;
        public string Prompt => prompt;

        public void Configure(ZoneKind kind, string promptText)
        {
            zone = kind;
            prompt = string.IsNullOrWhiteSpace(promptText) ? "Interact" : promptText;
        }

        private void Reset()
        {
            if (TryGetComponent(out Collider col))
                col.isTrigger = true;
        }
    }
}

