#if FUSION2
using RumOverboard.Gameplay.Interaction;
using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// Look at a carriable item + E → pick it up. Available with free hands (no line, no other item)
    /// and if the crew member can lift it (Mass ≤ CarryCapacity); the prompt says why not otherwise.
    /// </summary>
    public sealed class ItemInteractable : Interactable
    {
        private WorldItem _item;

        public override InteractionKind Kind => InteractionKind.Item;
        public WorldItem Item => _item != null ? _item : _item = GetComponentInParent<WorldItem>();

        public override string GetPrompt(in InteractorInfo who)
        {
            WorldItem item = Item;
            if (item == null) return Prompt;
            if (!who.HandsFree) return $"{item.DisplayName} — руки заняты";
            if (!ItemRules.CanLift(item.Mass, who.CarryCapacity)) return $"{item.DisplayName} — слишком тяжело ({item.Mass:0} кг)";
            return $"Взять: {item.DisplayName}";
        }

        public override bool IsAvailable(in InteractorInfo who)
        {
            WorldItem item = Item;
            return item != null && item.Object != null && item.Object.IsValid && item.IsCarriable &&
                   !item.IsHeld && !item.IsStored && who.HandsFree && ItemRules.CanLift(item.Mass, who.CarryCapacity);
        }
    }

}
#endif
