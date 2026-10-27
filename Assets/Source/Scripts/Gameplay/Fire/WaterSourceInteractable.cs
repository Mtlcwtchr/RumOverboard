#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Items;

namespace RumOverboard.Gameplay.Fire
{
    /// <summary>
    /// Water cask on deck: look at it with a bucket in hands + E → the bucket is filled from the cask's Water
    /// (<see cref="FireDirector.Fill"/>). Empty-handed it only shows how much water is left.
    /// </summary>
    public sealed class WaterSourceInteractable : Interactable
    {
        private WorldItem _item;

        public override InteractionKind Kind => InteractionKind.WaterSource;
        public WorldItem Item => _item != null ? _item : _item = GetComponentInParent<WorldItem>();

        private bool Infinite => Item != null && Item.Attributes.GetData<WaterSourceData>() is { Infinite: true };
        private float Left => Item != null ? Item.Attributes.GetCurrent(Attr.Water) : 0f;

        public override string GetPrompt(in InteractorInfo who)
        {
            WorldItem source = Item;
            if (source == null) return Prompt;
            string left = Infinite ? "" : $" ({Left:0})";
            WorldItem bucket = Bucket(who);
            if (bucket == null) return who.IsCarrying ? $"{source.DisplayName} — нужно ведро" : $"{source.DisplayName}{left}";
            if (!HasRoom(bucket)) return "Ведро полное";
            return !Infinite && Left < 0.01f ? $"{source.DisplayName}: пусто" : $"Набрать воды{left}";
        }

        public override bool IsAvailable(in InteractorInfo who)
        {
            WorldItem source = Item;
            if (source == null || source.Object == null || !source.Object.IsValid || source.Broken) return false;
            WorldItem bucket = Bucket(who);
            return bucket != null && HasRoom(bucket) && (Infinite || Left >= 0.01f);
        }

        private WorldItem Bucket(in InteractorInfo who)
        {
            if (!who.IsCarrying || Owner == null || Owner.Runner == null) return null;
            if (!Owner.Runner.TryFindObject(who.Carried, out NetworkObject obj) || obj == null) return null;
            return obj.TryGetComponent(out WorldItem item) && item.Attributes.GetData<BucketData>() != null ? item : null;
        }

        private static bool HasRoom(WorldItem bucket) =>
            bucket.Attributes.GetCurrent(Attr.Water) < bucket.Attributes.Get(Attr.Water) - 0.01f;
    }
}
#endif
