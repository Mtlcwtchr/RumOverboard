#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Interaction;
using UnityEngine;

namespace RumOverboard.Networking
{
    /// <summary>
    /// Per-tick input snapshot sent from each client to the host. Pure intent — the host owns all
    /// physics and validates everything (target range, availability) before acting on it.
    /// </summary>
    public struct NetworkInputData : INetworkInput
    {
        // Button indices for NetworkButtons (NOT bit masks — NetworkButtons.Set takes an index).
        public const int ButtonJump = 0;
        public const int ButtonInteract = 1;
        public const int ButtonSprint = 2;
        public const int ButtonRagdoll = 3;
        public const int ButtonDrink = 4;
        public const int ButtonHaul = 5;  // hold: haul the line in your hands (LMB)
        public const int ButtonEase = 6;  // hold: pay the line out (RMB)
        public const int ButtonDrop = 7;  // let go of the line (G)

        /// <summary>Raw movement axis (x = strafe, y = forward), pre-orientation.</summary>
        public Vector2 Move;

        /// <summary>
        /// Camera yaw (deg). Relative to <see cref="LookSpace"/>'s yaw when that is set (aboard a
        /// ship you turn with the deck), world yaw otherwise.
        /// </summary>
        public float LookYaw;

        /// <summary>Camera pitch (deg, signed, horizon-relative).</summary>
        public float LookPitch;

        /// <summary>NetworkObject whose yaw <see cref="LookYaw"/> is relative to (default = world).</summary>
        public NetworkId LookSpace;

        /// <summary>The interactable the client is looking at (what its hint shows).</summary>
        public InteractableRef Target;

        public NetworkButtons Buttons;
    }
}
#endif
