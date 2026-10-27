using System.Collections.Generic;
using RumOverboard.EditorTools.Attributes;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.Items
{
    /// <summary>
    /// Physical items feature setup — part 1: attribute data (definitions, flags, profiles).
    /// Idempotent: creates what's missing, adds missing entries/flags/data to existing profiles,
    /// never overwrites values you tuned. Prefabs, catalog and the scene are built by
    /// <c>ItemsSceneSetup</c>. Menu: RumOverboard ▸ Items.
    /// </summary>
    public static partial class ItemsSetup
    {
        [MenuItem("RumOverboard/Items/Create Item Content (attributes + profiles)", priority = 10)]
        public static void CreateItemContentMenu()
        {
            CreateItemContent();
            AttributeRegistryBuilder.Rebuild(log: true);
        }

        public static void CreateItemContent()
        {
            AttributesSandboxSetup.CreateSampleContent(); // base defs (Mass, Integrity, flags, Player…)

            var mass = Def("Mass");
            var integrity = Def("Integrity");
            var floating = FlagAsset("Floating");
            var flammable = FlagAsset("Flammable");
            var critical = FlagAsset("CriticalItem");

            // ── item / crew definitions ──
            var carriable = AttributesSandboxSetup.Flag("Carriable", "A crew member can pick it up (one item in hands, GDD §26.3).");
            var impactThreshold = AttributesSandboxSetup.Tuning("ImpactDamageThreshold", 6f,
                "Impact speed (m/s, along the contact normal) above which a hit costs Integrity.");
            var impactScale = AttributesSandboxSetup.Tuning("ImpactDamageScale", 4f,
                "Integrity lost per m/s of impact speed above the threshold.");
            var slots = AttributesSandboxSetup.Attribute("StorageSlots", AttributeKind.Stat, 8f,
                "How many items a container holds (replicated: clients show 'full').");
            var sips = AttributesSandboxSetup.Attribute("Sips", AttributeKind.Resource, 5f,
                "Charges left in a bottle; each sip spends one.");
            var carryCapacity = AttributesSandboxSetup.Attribute("CarryCapacity", AttributeKind.Stat, 60f,
                "Heaviest item (kg) a crew member can lift. Load = mass / capacity slows walking and costs stamina.");
            var throwImpulse = AttributesSandboxSetup.Tuning("ThrowImpulse", 60f,
                "Throw impulse (N·s): throw speed = impulse / item mass, capped at 12 m/s.");
            var carryCost = AttributesSandboxSetup.Tuning("StaminaCarryCost", 6f,
                "Stamina per second while moving with an item, × load (mass / CarryCapacity).");

            // ── profiles: existing ones get the new entries / flags ──
            AttributeProfile player = Profile("Player");
            AttributesSandboxSetup.EnsureEntries(player, new() { new(carryCapacity, 60f), new(throwImpulse, 60f), new(carryCost, 6f) });

            AttributeProfile barrel = Profile("Barrel");
            AttributesSandboxSetup.EnsureEntries(barrel, new() { new(impactThreshold, 6f), new(impactScale, 4f) });
            EnsureFlags(barrel, carriable);

            AttributeProfile board = Profile("Board");
            AttributesSandboxSetup.EnsureEntries(board, new() { new(impactThreshold, 8f), new(impactScale, 2f) });
            EnsureFlags(board, carriable);
            EnsureData(board, new BreakableData());

            AttributeProfile crate = Profile("Crate");
            AttributesSandboxSetup.EnsureEntries(crate, new() { new(mass, 25f), new(impactThreshold, 7f), new(impactScale, 4f) });
            EnsureFlags(crate, carriable);

            // The attribute samples were named in English; the game speaks Russian (prompts, F8).
            RenameDisplay(barrel, "Barrel", "Бочка");
            RenameDisplay(Profile("PowderBarrel"), "Powder barrel", "Бочка с порохом");
            RenameDisplay(board, "Board", "Доска");
            RenameDisplay(crate, "Crate (static)", "Ящик");

            // ── new profiles ──
            AttributesSandboxSetup.Profile("GrogBottle", null,
                new() { new(mass, 1.5f), new(integrity, 4f), new(sips, 5f), new(impactThreshold, 5f), new(impactScale, 2f) },
                new() { carriable, floating },
                new() { new DisplayData { DisplayName = "Бутылка грога" }, new DrinkData(), new BreakableData() });

            AttributesSandboxSetup.Profile("Spyglass", null,
                new() { new(mass, 1f), new(integrity, 20f), new(impactThreshold, 10f), new(impactScale, 1f) },
                new() { carriable, floating, critical },
                new() { new DisplayData { DisplayName = "Подзорная труба" }, new SpyglassData() });

            AttributesSandboxSetup.Profile("Compass", null,
                new() { new(mass, 0.5f), new(integrity, 20f), new(impactThreshold, 10f), new(impactScale, 1f) },
                new() { carriable, floating, critical },
                new() { new DisplayData { DisplayName = "Компас" }, new CompassData() });

            AttributesSandboxSetup.Profile("Chest", null,
                new() { new(mass, 120f), new(integrity, 150f), new(slots, 8f), new(impactThreshold, 12f), new(impactScale, 3f) },
                new() { flammable },
                new() { new DisplayData { DisplayName = "Сундук" }, new StorageData(), new BreakableData() });

            AssetDatabase.SaveAssets();
        }

        // ───────────────────────── helpers ─────────────────────────

        internal static AttributeDefinition Def(string name) =>
            AttributesSandboxSetup.Load<AttributeDefinition>("Definitions", name);

        internal static FlagDefinition FlagAsset(string name) =>
            AttributesSandboxSetup.Load<FlagDefinition>("Flags", name);

        internal static AttributeProfile Profile(string name) =>
            AttributesSandboxSetup.Load<AttributeProfile>("Profiles", name);

        internal static void EnsureFlags(AttributeProfile profile, params FlagDefinition[] wanted)
        {
            var flags = new List<FlagDefinition>(profile.Flags);
            int added = 0;
            foreach (FlagDefinition f in wanted)
                if (f != null && !flags.Contains(f)) { flags.Add(f); added++; }
            if (added == 0) return;
            profile.EditorSetup(profile.Parent, new List<AttributeEntry>(profile.Attributes), flags, new List<IEntityData>(profile.Data));
            EditorUtility.SetDirty(profile);
        }

        /// <summary>Replaces the profile's own display name only while it still is <paramref name="from"/> (keeps your edits).</summary>
        private static void RenameDisplay(AttributeProfile profile, string from, string to)
        {
            if (profile == null) return;
            foreach (IEntityData d in profile.Data)
                if (d is DisplayData display && display.DisplayName == from)
                {
                    display.DisplayName = to;
                    EditorUtility.SetDirty(profile);
                }
        }

        internal static void EnsureData<T>(AttributeProfile profile, T block) where T : class, IEntityData
        {
            foreach (IEntityData d in profile.Data)
                if (d is T) return;
            var data = new List<IEntityData>(profile.Data) { block };
            profile.EditorSetup(profile.Parent, new List<AttributeEntry>(profile.Attributes), new List<FlagDefinition>(profile.Flags), data);
            EditorUtility.SetDirty(profile);
        }
    }
}
