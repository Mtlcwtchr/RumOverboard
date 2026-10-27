using UnityEngine;

namespace RumOverboard.Gameplay.Fire
{
    /// <summary>
    /// Pure fire maths (no Unity objects), shared by the host director, the debug window and the self-test.
    /// </summary>
    public static class FireRules
    {
        /// <summary>
        /// Heat (0..1 of "enough to ignite") a fire adds per second to a neighbour at <paramref name="distance"/>:
        /// full rate (1 / ignitionTime of the target) at the source, linearly down to zero at <paramref name="spreadRadius"/>.
        /// </summary>
        public static float HeatRate(float distance, float spreadRadius, float ignitionTime, float spreadMultiplier = 1f)
        {
            if (spreadRadius <= 0f || distance >= spreadRadius) return 0f;
            float falloff = 1f - Mathf.Max(0f, distance) / spreadRadius;
            return falloff * Mathf.Max(0f, spreadMultiplier) / Mathf.Max(0.05f, ignitionTime);
        }

        /// <summary>Heat accumulated so far, after <paramref name="dt"/> s of <paramref name="rate"/> and cooling at <paramref name="decay"/> /s.</summary>
        public static float StepHeat(float heat, float rate, float decay, float dt)
        {
            heat += (rate > 0f ? rate : -Mathf.Max(0f, decay)) * dt;
            return Mathf.Clamp01(heat);
        }

        /// <summary>Ignites when the accumulated heat reaches 1.</summary>
        public static bool Ignites(float heat) => heat >= 1f - 1e-4f;

        /// <summary>Linear blast falloff: 1 at the centre, 0 at the radius and beyond.</summary>
        public static float BlastFalloff(float distance, float radius) =>
            radius <= 0f ? 0f : Mathf.Clamp01(1f - distance / radius);

        /// <summary>Points on a horizontal disc: the splash lands <paramref name="distance"/> m ahead of <paramref name="origin"/>.</summary>
        public static Vector3 SplashCentre(Vector3 origin, Vector3 forward, float distance)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            return origin + forward.normalized * distance;
        }

        /// <summary>Is <paramref name="point"/> within the splash (vertical cylinder, generous height).</summary>
        public static bool InSplash(Vector3 point, Vector3 centre, float radius, float height = 2.5f)
        {
            Vector3 d = point - centre;
            if (d.y > height || d.y < -height) return false;
            d.y = 0f;
            return d.sqrMagnitude <= radius * radius;
        }
    }
}
