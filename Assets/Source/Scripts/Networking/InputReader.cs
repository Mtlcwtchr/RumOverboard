#if FUSION2
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Networking
{
    /// <summary>
    /// Reads the local device state (Input System) into the button/axis part of
    /// <see cref="NetworkInputData"/>. Look + target come from the camera rig.
    ///
    /// Binds: WASD / left stick move, Space / A jump, E / X interact, Shift / LB sprint,
    /// Q / Y drink, R / B ragdoll, LMB / RT haul, RMB / LT ease, G / D-pad down drop the line. Buttons are OR-accumulated between network ticks so a tap
    /// shorter than a tick is never lost.
    /// </summary>
    public sealed class InputReader
    {
        private bool _jump, _interact, _sprint, _ragdoll, _drink, _drop;

        /// <summary>Call every frame (Update) to latch short taps.</summary>
        public void Accumulate()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            _jump |= (kb != null && kb.spaceKey.isPressed) || (gp != null && gp.buttonSouth.isPressed);
            _interact |= (kb != null && kb.eKey.isPressed) || (gp != null && gp.buttonWest.isPressed);
            _sprint |= (kb != null && kb.leftShiftKey.isPressed) || (gp != null && gp.leftShoulder.isPressed);
            _ragdoll |= (kb != null && kb.rKey.isPressed) || (gp != null && gp.buttonEast.isPressed);
            _drink |= (kb != null && kb.qKey.isPressed) || (gp != null && gp.buttonNorth.isPressed);
            _drop |= (kb != null && kb.gKey.isPressed) || (gp != null && gp.dpad.down.isPressed);
        }

        public NetworkInputData Read()
        {
            Accumulate();

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

            data.Buttons.Set(NetworkInputData.ButtonJump, _jump);
            data.Buttons.Set(NetworkInputData.ButtonInteract, _interact);
            data.Buttons.Set(NetworkInputData.ButtonSprint, _sprint);
            data.Buttons.Set(NetworkInputData.ButtonRagdoll, _ragdoll);
            data.Buttons.Set(NetworkInputData.ButtonDrink, _drink);
            data.Buttons.Set(NetworkInputData.ButtonDrop, _drop);
            // Hauling is a hold — sampled live (with a locked cursor the mouse buttons are free).
            var mouse = Mouse.current;
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            data.Buttons.Set(NetworkInputData.ButtonHaul,
                (cursorLocked && mouse != null && mouse.leftButton.isPressed) || (gp != null && gp.rightTrigger.isPressed));
            data.Buttons.Set(NetworkInputData.ButtonEase,
                (cursorLocked && mouse != null && mouse.rightButton.isPressed) || (gp != null && gp.leftTrigger.isPressed));
            _jump = _interact = _sprint = _ragdoll = _drink = _drop = false;

            return data;
        }
    }
}
#endif
