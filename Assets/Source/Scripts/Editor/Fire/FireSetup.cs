using System.Collections.Generic;
using RumOverboard.EditorTools.Attributes;
using RumOverboard.EditorTools.Items;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Fire;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.Fire
{
    /// <summary>
    /// Fire feature setup — part 1: attribute data. Idempotent (creates what's missing, adds missing
    /// entries / data blocks to existing profiles, never overwrites tuned values).
    ///   Water (Resource) · Exploded flag · OnFireFx (burning until put out; grants Burning)
    ///   FlammableData on Board / Crate / Chest · ExplosiveData on PowderBarrel
    ///   profiles: Bucket, Lantern, WaterCask.
    /// Menu: RumOverboard ▸ Fire.
    /// </summary>
    public static partial class FireSetup
    {
        [MenuItem("RumOverboard/Fire/Create Fire Content (attributes + profiles)", priority = 10)]
        public static void CreateFireContentMenu()
        {
            CreateFireContent();
            AttributeRegistryBuilder.Rebuild(log: true);
        }

        public static void CreateFireContent()
        {
            ItemsSetup.CreateItemContent(); // items (and through it the base attribute content)

            var mass = ItemsSetup.Def("Mass");
            var integrity = ItemsSetup.Def("Integrity");
            var impactThreshold = ItemsSetup.Def("ImpactDamageThreshold");
            var impactScale = ItemsSetup.Def("ImpactDamageScale");
            var carriable = ItemsSetup.FlagAsset("Carriable");
            var floating = ItemsSetup.FlagAsset("Floating");
            var burning = ItemsSetup.FlagAsset("Burning");

            var water = AttributesSandboxSetup.Attribute("Water", AttributeKind.Resource, 3f,
                "Water carried (bucket splashes) or stored (water cask).");
            AttributesSandboxSetup.Flag("Exploded", "Blew up (powder): every peer shows the explosion instead of plain debris.");
            AttributesSandboxSetup.Effect("OnFireFx", 0f, EffectStacking.Ignore, 1, e =>
            {
                e.GrantedFlags.Add(burning);
                e.Description = "Burning until put out (bucket) or burnt down. Burn damage / spread: FireConfig + FlammableData.";
            });

            ItemsSetup.EnsureData(ItemsSetup.Profile("Board"), new FlammableData { IgnitionTime = 2f, SpreadRadius = 1.2f });
            ItemsSetup.EnsureData(ItemsSetup.Profile("Crate"), new FlammableData { IgnitionTime = 3f, SpreadRadius = 1.5f });
            ItemsSetup.EnsureData(ItemsSetup.Profile("Chest"), new FlammableData { IgnitionTime = 5f, SpreadRadius = 1.8f });
            ItemsSetup.EnsureData(ItemsSetup.Profile("PowderBarrel"), new ExplosiveData());

            AttributesSandboxSetup.Profile("Bucket", null,
                new() { new(mass, 4f), new(integrity, 30f), new(water, 3f), new(impactThreshold, 8f), new(impactScale, 2f) },
                new() { carriable, floating },
                new() { new DisplayData { DisplayName = "Ведро" }, new BucketData() });

            AttributesSandboxSetup.Profile("Lantern", null,
                new() { new(mass, 2f), new(integrity, 6f), new(impactThreshold, 4f), new(impactScale, 3f) },
                new() { carriable },
                new() { new DisplayData { DisplayName = "Фонарь" }, new LanternData(), new BreakableData() });

            AttributesSandboxSetup.Profile("WaterCask", null,
                new() { new(mass, 150f), new(integrity, 120f), new(water, 40f), new(impactThreshold, 12f), new(impactScale, 2f) },
                new(),
                new() { new DisplayData { DisplayName = "Бочка с водой" }, new WaterSourceData(), new BreakableData() });

            AssetDatabase.SaveAssets();
        }
    }
}
