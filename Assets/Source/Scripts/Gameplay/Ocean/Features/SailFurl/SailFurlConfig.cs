using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.SailFurl
{
    /// <summary>
    /// Tuning for furling / unfurling sails on the yards (gaskets). Authored asset:
    /// Assets/Source/Configs/FeatureScenes/SailFurlConfig.asset — edited live from the Sail furl debug
    /// window (F7) and saved back with "Save to config". Bound to the ship by <c>NetworkShip</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "SailFurlConfig", menuName = "RumOverboard/Feature Configs/Sail Furl")]
    public sealed class SailFurlConfig : ScriptableObject
    {
        [Header("Voyage start")]
        [Tooltip("Sails start gasketed to their yards (the crew must go aloft and cast them loose).")]
        public bool StartFurled = true;

        [Header("Work on the yard")]
        [Tooltip("Seconds for one crew member to furl a sail with no wind in it.")]
        [Min(0.1f)] public float FurlTime = 5f;
        [Tooltip("Seconds to cast the gaskets off (the canvas then drops as far as the halyard allows).")]
        [Min(0.1f)] public float UnfurlTime = 2f;
        [Tooltip("Furling slows by (1 + this × sail load): a canvas full of wind fights the crew.")]
        [Min(0f)] public float WindResistance = 2f;
        [Tooltip("Extra distance (m) beyond the zone's reach the worker may drift before the work pauses.")]
        [Min(0f)] public float WorkSlack = 1.2f;

        [Header("Interaction zone")]
        [Tooltip("How far from the eye the yard can be worked (m). The view ray reaches 3.2 m.")]
        [Range(1f, 3.2f)] public float ZoneReach = 3.1f;

        [Header("Visual")]
        [Tooltip("Diameter (m) of the furled canvas roll on the yard.")]
        [Range(0.05f, 0.6f)] public float RollDiameter = 0.26f;

        private static SailFurlConfig _fallback;
        private static SailFurlConfig _active;

        /// <summary>The config used at runtime (assigned by NetworkShip; defaults otherwise).</summary>
        public static SailFurlConfig Active
        {
            get
            {
                if (_active != null) return _active;
                if (_fallback == null) _fallback = CreateInstance<SailFurlConfig>();
                return _fallback;
            }
            set => _active = value;
        }

        public void CopyFrom(SailFurlConfig other)
        {
            if (other == null || other == this) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }
}
