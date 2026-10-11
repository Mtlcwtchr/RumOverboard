#if FUSION2
using RumOverboard.Gameplay.Interaction;
using UnityEngine;

namespace RumOverboard.Gameplay.World.Mooring
{
    /// <summary>
    /// Pier bollard: E casts off / makes fast the ship's mooring lines at this harbor.
    /// Pure view + routing — all mooring state lives in <see cref="NetworkWorld"/> (host authority).
    /// </summary>
    public sealed class MooringBollard : Interactable, IHostUsable
    {
        public NetworkWorld World;
        public int Checkpoint;
        public bool Stern;

        public override InteractionKind Kind => InteractionKind.Generic;

        public override string GetPrompt(in InteractorInfo who) =>
            World != null ? World.MooringPrompt(Checkpoint, out _) : Prompt;

        public override bool IsAvailable(in InteractorInfo who)
        {
            if (World == null || !who.HandsFree)
                return false;
            World.MooringPrompt(Checkpoint, out bool available);
            return available;
        }

        public void HostUse(in InteractorInfo who)
        {
            if (World != null && IsAvailable(who))
                World.HostUseMooring(Checkpoint);
        }
    }
}
#endif
