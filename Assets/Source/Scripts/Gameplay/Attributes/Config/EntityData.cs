using System;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// Static, non-numeric part of an entity profile: references (prefabs, VFX, sounds), texts,
    /// tuning that never changes at runtime. Lives in <see cref="AttributeProfile"/> next to the
    /// numbers and flags, so everything about an entity type is in one asset — but it is never
    /// copied per instance and never replicated (every peer has the same asset).
    ///
    /// Add a new block = add a [Serializable] class implementing this interface; Alchemy shows it
    /// in the profile's "Data" dropdown automatically.
    /// </summary>
    public interface IEntityData
    {
    }

    /// <summary>Name/description for hints and debug UI.</summary>
    [Serializable]
    public sealed class DisplayData : IEntityData
    {
        public string DisplayName;
        [TextArea] public string Description;
    }

    /// <summary>Example: what a destructible object turns into when its Integrity reaches zero.</summary>
    [Serializable]
    public sealed class BreakableData : IEntityData
    {
        public GameObject DebrisPrefab;
        public AudioClip BreakSound;
    }

    /// <summary>Example: fire behaviour of a flammable object.</summary>
    [Serializable]
    public sealed class FlammableData : IEntityData
    {
        [Tooltip("Seconds a nearby fire needs to ignite this object.")]
        public float IgnitionTime = 3f;

        [Tooltip("Radius (m) in which this object, while burning, ignites others.")]
        public float SpreadRadius = 1.5f;

        public GameObject FireVfx;
    }
}
