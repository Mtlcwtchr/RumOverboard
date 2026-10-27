using System;
using RumOverboard.EditorTools.Attributes;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.Items
{
    /// <summary>
    /// Item rules + data sanity (no play mode).
    ///   Menu:  RumOverboard ▸ Items ▸ Run Self-Test
    ///   Batch: Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.Items.ItemsSelfTest.RunBatch
    /// </summary>
    public static class ItemsSelfTest
    {
        private static int _failures;

        [MenuItem("RumOverboard/Items/Run Self-Test", priority = 20)]
        public static void RunMenu() => Run();

        public static void RunBatch() => EditorApplication.Exit(Run() == 0 ? 0 : 1);

        public static int Run()
        {
            _failures = 0;
            try { Execute(); }
            catch (Exception e) { _failures++; Debug.LogException(e); }
            if (_failures == 0) Debug.Log("[ItemsTest] ALL PASSED");
            else Debug.LogError($"[ItemsTest] {_failures} FAILED");
            return _failures;
        }

        private static void Execute()
        {
            // ── rules ──
            Near("no damage below threshold", ItemRules.ImpactDamage(5f, 6f, 4f), 0f);
            Near("damage above threshold", ItemRules.ImpactDamage(10f, 6f, 4f), 16f);
            Near("impact sign ignored", ItemRules.ImpactDamage(-10f, 6f, 4f), 16f);
            Near("impact speed = velocity change of the contact step", ItemRules.ImpactSpeed(16f, 20f), 16f);
            Near("depenetration push (no approach) is not a hit", ItemRules.ImpactSpeed(16f, 0.2f), 0.2f);
            Check("lift at capacity", ItemRules.CanLift(60f, 60f));
            Check("can't lift over capacity", !ItemRules.CanLift(61f, 60f));
            Near("load", ItemRules.CarryLoad(30f, 60f), 0.5f);
            Near("speed empty-handed", ItemRules.CarrySpeedFactor(0f), 1f);
            Near("speed at capacity", ItemRules.CarrySpeedFactor(1f), 0.5f);
            Check("jump with light load", ItemRules.CanJumpWithLoad(0.5f));
            Check("no jump with heavy load", !ItemRules.CanJumpWithLoad(0.8f));
            Near("throw a bottle: capped", ItemRules.ThrowSpeed(60f, 1.5f), ItemRules.MaxThrowSpeed);
            Near("throw a barrel: impulse / mass", ItemRules.ThrowSpeed(60f, 40f), 1.5f);
            Near("throw floor", ItemRules.ThrowSpeed(60f, 500f), ItemRules.MinThrowSpeed);
            Near("half submerged", ItemRules.SubmergedFraction(0f, -0.5f, 1f), 0.5f);
            Near("above water", ItemRules.SubmergedFraction(0f, 0.2f, 1f), 0f);

            // ── data: every item profile is wired the way the code expects ──
            foreach (string name in new[] { "Barrel", "PowderBarrel", "Board", "Crate", "GrogBottle", "Spyglass", "Compass" })
            {
                AttributeLayout layout = Profile(name)?.Layout;
                if (!Check($"{name}: profile exists", layout != null)) continue;
                var set = new AttributeSet(layout);
                Check($"{name}: carriable", set.HasFlag(AttrFlag.Carriable));
                Check($"{name}: has Mass + Integrity", set.Has(Attr.Mass) && set.Has(Attr.Integrity));
                Check($"{name}: impact damage tuned", set.Has(Attr.ImpactDamageThreshold) && set.Has(Attr.ImpactDamageScale));
            }

            AttributeProfile chest = Profile("Chest");
            if (Check("Chest: profile exists", chest != null))
            {
                var set = new AttributeSet(chest.Layout);
                Check("Chest: not carriable", !set.HasFlag(AttrFlag.Carriable));
                Check("Chest: has StorageSlots (replicated)", set.Has(Attr.StorageSlots) && chest.Layout.Definitions[set.SlotOf(Attr.StorageSlots)].Replicated);
                Check("Chest: StorageData", chest.GetData<StorageData>() != null);
            }

            AttributeProfile bottle = Profile("GrogBottle");
            if (bottle != null)
            {
                var set = new AttributeSet(bottle.Layout);
                Check("Bottle: starts full of sips", set.GetCurrent(Attr.Sips) >= 1f);
                Check("Bottle: DrinkData", bottle.GetData<DrinkData>() != null);
                Check("Bottle: floats", set.HasFlag(AttrFlag.Floating));
            }

            AttributeProfile player = Profile("Player");
            if (player != null)
            {
                var set = new AttributeSet(player.Layout);
                Check("Player: CarryCapacity, ThrowImpulse, StaminaCarryCost",
                      set.Has(Attr.CarryCapacity) && set.Has(Attr.ThrowImpulse) && set.Has(Attr.StaminaCarryCost));
                Check("Player can lift the powder barrel (60 kg)",
                      ItemRules.CanLift(new AttributeSet(Profile("PowderBarrel").Layout).Get(Attr.Mass), set.Get(Attr.CarryCapacity)));
            }

            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemsSetup.CatalogPath);
            if (Check("catalog exists", catalog != null))
                foreach (ItemCatalog.Entry e in catalog.Items)
                {
                    bool ok = e.Prefab != null && e.Prefab.GetComponent<WorldItem>() != null &&
                              GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(e.Prefab) == 0;
                    Check($"catalog '{e.Id}': prefab with WorldItem, no missing scripts", ok);
                }
        }

        private static AttributeProfile Profile(string name) => AttributesSandboxSetup.Load<AttributeProfile>("Profiles", name);

        private static bool Check(string what, bool ok)
        {
            if (!ok)
            {
                _failures++;
                Debug.LogError($"[ItemsTest] FAIL: {what}");
            }
            return ok;
        }

        private static void Near(string what, float actual, float expected)
        {
            if (Mathf.Abs(actual - expected) <= 1e-3f) return;
            _failures++;
            Debug.LogError($"[ItemsTest] FAIL: {what} — expected {expected}, got {actual}");
        }
    }
}
