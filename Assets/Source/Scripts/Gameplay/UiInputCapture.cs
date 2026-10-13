using UnityEngine;

namespace RumOverboard.Gameplay
{
    /// <summary>
    /// Local, non-networked flag: a full-screen UI (e.g. the chart close-up) owns mouse and
    /// keyboard. While active the camera stops mouse-look and cursor re-locking, and gameplay
    /// input (move, jump, interact, …) is sent as neutral.
    /// </summary>
    public static class UiInputCapture
    {
        private static bool _active;

        public static bool Active
        {
            get => _active;
            set
            {
                if (_active && !value)
                    ReleasedFrame = Time.frameCount;
                _active = value;
            }
        }

        /// <summary>Frame the capture was last released (so the same Esc press doesn't open another UI).</summary>
        public static int ReleasedFrame { get; private set; } = -1;

        /// <summary>True while captured or on the frame the capture was released.</summary>
        public static bool BusyThisFrame => _active || ReleasedFrame == Time.frameCount;

        private static readonly System.Collections.Generic.List<Object> DebugOwners = new();

        /// <summary>
        /// A debug window (F2…F8) is open: the cursor stays free and visible (clicks don't re-lock it,
        /// the mouse doesn't turn the camera), but the keyboard still drives the player.
        /// </summary>
        public static bool DebugWindowOpen
        {
            get
            {
                DebugOwners.RemoveAll(o => o == null);
                return DebugOwners.Count > 0;
            }
        }

        /// <summary>Called by debug windows every frame with their visibility (cheap when unchanged).</summary>
        public static void SetDebugWindow(Object owner, bool open)
        {
            if (owner == null || DebugOwners.Contains(owner) == open)
                return;
            bool was = DebugWindowOpen;
            if (open)
            {
                if (!DebugOwners.Contains(owner))
                    DebugOwners.Add(owner);
            }
            else
            {
                DebugOwners.Remove(owner);
            }
            bool now = DebugWindowOpen;
            if (now)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (was && !_active)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active = false;
            ReleasedFrame = -1;
            DebugOwners.Clear();
        }
    }
}
