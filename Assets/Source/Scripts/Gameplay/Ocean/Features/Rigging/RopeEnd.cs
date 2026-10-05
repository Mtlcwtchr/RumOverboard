#if FUSION2
using RumOverboard.Gameplay.Interaction;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rigging
{
    /// <summary>
    /// Grab handle on a line's free end. Its transform follows the replicated end position
    /// (RopeRenderSystem / RopeSystem) and its trigger is only live while the end is loose.
    /// </summary>
    public sealed class RopeEnd : Interactable
    {
        [SerializeField] private RigLine line;

        public override InteractionKind Kind => InteractionKind.RopeEnd;
        public RigLine Line => line != null ? line : (line = GetComponentInParent<RigLine>());

        public void Configure(RigLine owner) => line = owner;

        private bool Loose =>
            Line != null && Line.Ship != null && Line.Ship.Object != null && Line.Ship.Object.IsValid &&
            Line.Ship.GetLineMode(Line.LineIndex) == RigLineMode.Loose;

        public override bool IsAvailable(in InteractorInfo who) => who.HandsFree && Loose;

        public override string GetPrompt(in InteractorInfo who) =>
            !Loose ? null : who.HandsFree ? $"Поймать конец: {Line.DisplayName}" : "Руки заняты";
    }
}
#endif
