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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active = false;
            ReleasedFrame = -1;
        }
    }
}
