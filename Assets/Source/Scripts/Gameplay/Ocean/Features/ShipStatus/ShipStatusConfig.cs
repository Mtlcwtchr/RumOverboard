using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Ocean.Features.ShipStatus
{
    /// <summary>
    /// Look of the ship status widget (wind vs bow, rudder, sails, lines). Authored asset:
    /// Assets/Source/Configs/FeatureScenes/ShipStatusConfig.asset — edited live from the Ship status
    /// debug window (F8) and saved back with "Save to config". Bound by <c>NetworkShip</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "ShipStatusConfig", menuName = "RumOverboard/Feature Configs/Ship Status")]
    public sealed class ShipStatusConfig : ScriptableObject
    {
        [Header("Visibility")]
        [Tooltip("Widget shown while aboard (the toggle key flips it at runtime).")]
        public bool Visible = true;
        [Tooltip("Key that shows / hides the widget.")]
        public Key ToggleKey = Key.Tab;

        [Header("Layout")]
        [Range(0.5f, 1.5f)] public float Scale = 1f;
        [Range(0.2f, 1f)] public float Opacity = 0.9f;
        [Tooltip("Offset of the panel's top-right corner from the screen's top-right (reference 1920×1080).")]
        public Vector2 Offset = new Vector2(-20f, -80f);

        [Header("Content")]
        [Tooltip("Compass dial is north-up and the hull icon turns with the ship's heading. " +
                 "Off: bow-up, the compass letters turn instead.")]
        public bool NorthUp = true;
        public bool ShowApparentWind = true;
        public bool ShowSails = true;
        public bool ShowLines = true;

        [Header("Readouts")]
        [Tooltip("Speed smoothing time (s).")]
        [Range(0.05f, 3f)] public float SpeedSmoothing = 0.6f;
        [Tooltip("Line tension below this reads «слабина».")]
        [Range(0f, 1f)] public float SlackBelow = 0.3f;

        private static ShipStatusConfig _fallback;
        private static ShipStatusConfig _active;

        /// <summary>The config used at runtime (assigned by NetworkShip; defaults otherwise).</summary>
        public static ShipStatusConfig Active
        {
            get
            {
                if (_active != null) return _active;
                if (_fallback == null) _fallback = CreateInstance<ShipStatusConfig>();
                return _fallback;
            }
            set => _active = value;
        }

        public void CopyFrom(ShipStatusConfig other)
        {
            if (other == null || other == this) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }
}
