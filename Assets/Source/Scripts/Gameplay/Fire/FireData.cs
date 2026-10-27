using System;
using RumOverboard.Gameplay.Attributes;
using UnityEngine;

namespace RumOverboard.Gameplay.Fire
{
    /// <summary>Powder: once it burns, the fuse runs and it blows up (damage, fire and a shove around it).</summary>
    [Serializable]
    public sealed class ExplosiveData : IEntityData
    {
        [Tooltip("Seconds from catching fire to the explosion.")]
        public float FuseTime = 2.5f;

        [Tooltip("Blast radius (m): damage, push and knockdown fall off to zero at this distance.")]
        public float Radius = 4f;

        [Tooltip("Integrity damage at the centre (falls off linearly).")]
        public float Damage = 120f;

        [Tooltip("Flammable items closer than Radius × this catch fire.")]
        [Range(0f, 1f)] public float IgniteFraction = 0.6f;

        [Tooltip("Velocity change (m/s) given to items at the centre.")]
        public float ItemPush = 9f;

        [Tooltip("Crew closer than this are knocked out (full ragdoll); farther ones are only shoved.")]
        public float KnockoutRadius = 2.5f;

        [Tooltip("Velocity change (m/s) given to crew at the centre.")]
        public float CrewPush = 7f;
    }

    /// <summary>Lantern: breaking it spills burning oil — flammable items around catch fire at once.</summary>
    [Serializable]
    public sealed class LanternData : IEntityData
    {
        public float IgniteRadius = 1.8f;
    }

    /// <summary>Bucket: Q splashes its water in front (puts fire out, makes things wet).</summary>
    [Serializable]
    public sealed class BucketData : IEntityData
    {
        [Tooltip("Distance (m) in front of the hands where the water lands.")]
        public float SplashDistance = 1.6f;

        [Tooltip("Radius (m) of the splash.")]
        public float SplashRadius = 1.6f;
    }

    /// <summary>A water source on deck (water cask): E with a bucket fills it from the cask's Water.</summary>
    [Serializable]
    public sealed class WaterSourceData : IEntityData
    {
        [Tooltip("Never runs dry (sea pump…). Otherwise it spends its own Water.")]
        public bool Infinite;
    }
}
