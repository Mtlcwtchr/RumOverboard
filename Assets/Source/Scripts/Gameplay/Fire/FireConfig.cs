using UnityEngine;

namespace RumOverboard.Gameplay.Fire
{
    /// <summary>
    /// Global fire tuning (per-type values live in the profiles: FlammableData / ExplosiveData / LanternData /
    /// BucketData). Edited live in the F9 window (on a runtime copy), Reset / Save-to-config from there.
    /// </summary>
    [CreateAssetMenu(fileName = "FireConfig", menuName = "RumOverboard/Fire/Fire Config")]
    public sealed class FireConfig : ScriptableObject
    {
        [Header("Burning (host)")]
        [Tooltip("Integrity a burning item loses per second (it breaks at 0 — burnt down).")]
        public float BurnDamagePerSecond = 4f;

        [Tooltip("× every heat rate: global speed of fire spreading between items.")]
        public float SpreadMultiplier = 1f;

        [Tooltip("Heat (0..1) a heated, not burning item loses per second once nothing heats it.")]
        public float HeatDecayPerSecond = 0.25f;

        [Tooltip("Spread radius (m) of burning items without FlammableData.")]
        public float DefaultSpreadRadius = 1.5f;

        [Tooltip("Ignition time (s) of flammable items without FlammableData.")]
        public float DefaultIgnitionTime = 3f;

        [Tooltip("Submerged fraction above which the sea puts an item out (and fills a bucket).")]
        [Range(0.05f, 1f)] public float SubmergedToQuench = 0.3f;

        [Header("Bucket")]
        [Tooltip("Water spent per splash (bucket holds its Water attribute).")]
        public float WaterPerSplash = 1f;

        [Header("Visuals (every peer)")]
        public float FlameSize = 1f;
        public float FlameLightIntensity = 3f;
        public float FlameLightRange = 6f;
        public float ExplosionLightIntensity = 25f;

        public static FireConfig CreateDefault()
        {
            var c = CreateInstance<FireConfig>();
            c.name = "FireConfig (default)";
            return c;
        }

        public void CopyFrom(FireConfig other)
        {
            if (other == null || other == this) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }
}
