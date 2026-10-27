#if FUSION2
using RumOverboard.Gameplay.Interaction;
using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// Look at a container + E: with an item in hands — put it in (if there's room); empty-handed —
    /// take the top item out into your hands.
    /// </summary>
    public sealed class StorageInteractable : Interactable
    {
        private ItemStorage _storage;

        public override InteractionKind Kind => InteractionKind.Storage;
        public ItemStorage Storage => _storage != null ? _storage : _storage = GetComponentInParent<ItemStorage>();

        public override string GetPrompt(in InteractorInfo who)
        {
            ItemStorage s = Storage;
            if (s == null || s.Object == null || !s.Object.IsValid) return Prompt;
            string name = s.DisplayName;
            if (who.IsCarrying)
                return s.HasSpace ? $"Положить в {name} ({s.Count}/{s.Capacity})" : $"{name}: полон ({s.Count}/{s.Capacity})";
            return s.Count > 0 ? $"Достать из {name} ({s.Count})" : $"{name}: пусто";
        }

        public override bool IsAvailable(in InteractorInfo who)
        {
            ItemStorage s = Storage;
            if (s == null || s.Object == null || !s.Object.IsValid || s.Self.Broken) return false;
            if (who.IsCarrying) return who.HeldLine < 0 && s.HasSpace && who.Carried != s.Object.Id;
            return who.HandsFree && s.Count > 0;
        }
    }
}
#endif
