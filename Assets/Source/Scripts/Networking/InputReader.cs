#if FUSION2
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Networking
{
    /// <summary>
    /// Reads the local device state (Input System) and builds a NetworkInputData.
    /// Kept as a plain helper (not a runner callback) so ConnectionManager stays
    /// the single INetworkRunnerCallbacks implementer. Reads devices directly to
    /// avoid coupling to a generated input-actions class.
    ///
    /// Default binds: WASD / left-stick move, Space / A jump, LeftShift / LB climb,
    /// E / X interact, R / B toggle ragdoll.
    /// </summary>
    public sealed class InputReader
    {
        public float CameraYaw;

        public NetworkInputData Read()
        {
            var data = new NetworkInputData();
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed) move.x -= 1f;
            }
            if (gp != null) move += gp.leftStick.ReadValue();

            data.Move = Vector2.ClampMagnitude(move, 1f);
            data.LookYaw = CameraYaw;

            bool jump = (kb != null && kb.spaceKey.isPressed) || (gp != null && gp.buttonSouth.isPressed);
            bool climb = (kb != null && kb.leftShiftKey.isPressed) || (gp != null && gp.leftShoulder.isPressed);
            bool interact = (kb != null && kb.eKey.isPressed) || (gp != null && gp.buttonWest.isPressed);
            bool ragdoll = (kb != null && kb.rKey.isPressed) || (gp != null && gp.buttonEast.isPressed);

            data.Buttons.Set(NetworkInputData.ButtonJump, jump);
            data.Buttons.Set(NetworkInputData.ButtonClimb, climb);
            data.Buttons.Set(NetworkInputData.ButtonInteract, interact);
            data.Buttons.Set(NetworkInputData.ButtonRagdoll, ragdoll);

            return data;
        }
    }
}
#endif
