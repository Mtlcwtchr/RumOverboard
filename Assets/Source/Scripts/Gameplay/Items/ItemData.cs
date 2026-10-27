using System;
using RumOverboard.Gameplay.Attributes;
using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// A bottle (grog / rum): Q while holding it takes a sip — one <c>Sips</c> charge of the bottle,
    /// intoxication for the drinker and grog (sanity reserve, GDD §15) back. Empty bottle = just an object.
    /// </summary>
    [Serializable]
    public sealed class DrinkData : IEntityData
    {
        [Tooltip("Intoxication (0..1) added over one sip. 0 = GameConfig.RumSipIntoxication.")]
        public float IntoxicationPerSip = 0.15f;

        [Tooltip("Grog (sanity reserve) restored per sip.")]
        public float GrogPerSip = 15f;
    }

    /// <summary>Spyglass: hold Q while holding it to look through (local camera zoom).</summary>
    [Serializable]
    public sealed class SpyglassData : IEntityData
    {
        [Tooltip("Camera field of view while looking through it (deg).")]
        public float ZoomFov = 18f;

        [Tooltip("How fast the view zooms in / out (1/s).")]
        public float ZoomSpeed = 8f;
    }

    /// <summary>Compass: while held, the HUD shows the heading (local presentation).</summary>
    [Serializable]
    public sealed class CompassData : IEntityData
    {
        [Tooltip("Degrees of random needle wobble (cheap compass / storm).")]
        public float NeedleWobble = 2f;
    }

    /// <summary>A container that stores carried items (chest, barrel rack…). Capacity is the <c>StorageSlots</c> stat.</summary>
    [Serializable]
    public sealed class StorageData : IEntityData
    {
        [Tooltip("Where stored items are parked, in the container's local space (hidden while inside).")]
        public Vector3 StowOffset = new Vector3(0f, 0.2f, 0f);
    }
}
