using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// Pure item / carrying rules (no Unity objects): one place for the numbers the host simulation,
    /// the HUD and the self-test agree on. Inputs come from attributes (Mass, CarryCapacity,
    /// ThrowImpulse, ImpactDamage*).
    /// </summary>
    public static class ItemRules
    {
        public const float MaxThrowSpeed = 12f;
        public const float MinThrowSpeed = 1f;
        public const float JumpLoadLimit = 0.7f; // can't jump carrying more than 70% of capacity

        /// <summary>
        /// Impact speed of a hit: the velocity change of the contact step (gravity removed), capped by the
        /// approach speed going into that step — so depenetration pushes (no approach) don't count as hits.
        /// </summary>
        public static float ImpactSpeed(float velocityChange, float approachSpeed) =>
            Mathf.Min(Mathf.Abs(velocityChange), Mathf.Max(0f, approachSpeed));

        /// <summary>Integrity lost by one hit: (impact speed − threshold) × scale, never negative.</summary>
        public static float ImpactDamage(float impactSpeed, float threshold, float scale) =>
            Mathf.Max(0f, Mathf.Abs(impactSpeed) - threshold) * Mathf.Max(0f, scale);

        public static bool CanLift(float itemMass, float capacity) => itemMass <= capacity + 1e-3f;

        /// <summary>Item mass relative to what the crew member can lift (0..1 for liftable items).</summary>
        public static float CarryLoad(float itemMass, float capacity) =>
            capacity <= 0f ? 1f : Mathf.Clamp01(itemMass / capacity);

        /// <summary>Walk / swim speed multiplier: 1 empty-handed, 0.5 at full capacity.</summary>
        public static float CarrySpeedFactor(float load) => Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(load));

        public static bool CanJumpWithLoad(float load) => load <= JumpLoadLimit;

        /// <summary>Throw speed (m/s): impulse / mass, a light bottle flies, a barrel barely leaves the hands.</summary>
        public static float ThrowSpeed(float impulse, float itemMass) =>
            Mathf.Clamp(impulse / Mathf.Max(0.1f, itemMass), MinThrowSpeed, MaxThrowSpeed);

        /// <summary>
        /// Upward buoyant acceleration factor for a body whose collider spans <paramref name="bottom"/>..+height
        /// against the water surface: submerged fraction 0..1.
        /// </summary>
        public static float SubmergedFraction(float surfaceY, float bottomY, float height) =>
            Mathf.Clamp01((surfaceY - bottomY) / Mathf.Max(0.05f, height));
    }
}
