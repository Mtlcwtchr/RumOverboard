#if FUSION2
using Fusion;
using UnityEngine;

namespace RumOverboard.Networking
{
    /// <summary>
    /// Per-tick input snapshot sent from each client to whoever simulates it
    /// (the host in Host mode, or the local client in Shared mode). Kept tiny —
    /// Fusion sends this every tick.
    /// </summary>
    public struct NetworkInputData : INetworkInput
    {
        // Button indices for NetworkButtons (NOT bit masks — NetworkButtons.Set takes an index).
        public const int ButtonJump = 0;
        public const int ButtonInteract = 1;
        public const int ButtonClimb = 2;
        public const int ButtonRagdoll = 3;

        /// <summary>Raw movement axis (x = strafe, y = forward), pre-orientation.</summary>
        public Vector2 Move;

        /// <summary>Camera yaw (deg) so movement is relative to where the player looks.</summary>
        public float LookYaw;

        public NetworkButtons Buttons;
    }
}
#endif
