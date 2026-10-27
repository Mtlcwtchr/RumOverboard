using System;
using RumOverboard.EditorTools.Attributes;
using RumOverboard.EditorTools.Items;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Fire;
using RumOverboard.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.Fire
{
    /// <summary>
    /// Fire rules + data sanity (no play mode).
    ///   Menu:  RumOverboard ▸ Fire ▸ Run Self-Test
    ///   Batch: Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.Fire.FireSelfTest.RunBatch
    /// </summary>
    public static class FireSelfTest
    {
        private static int _failures;

        [MenuItem("RumOverboard/Fire/Run Self-Test", priority = 20)]
        public static void RunMenu() => Run();

        public static void RunBatch() => EditorApplication.Exit(Run() == 0 ? 0 : 1);

        public static int Run()
        {
            _failures = 0;
            try { Execute(); }
            catch (Exception e) { _failures++; Debug.LogException(e); }
            if (_failures == 0) Debug.Log("[FireTest] ALL PASSED");
            else Debug.LogError($"[FireTest] {_failures} FAILED");
            return _failures;
        }

        private static void Execute()
        {
            // ── rules ──
            Near("heat at the source = 1 / ignition time", FireRules.HeatRate(0f, 1.5f, 3f), 1f / 3f);
            Near("heat falls off linearly", FireRules.HeatRate(0.75f, 1.5f, 3f), 0.5f / 3f);
            Near("no heat at / past the radius", FireRules.HeatRate(1.5f, 1.5f, 3f), 0f);
            Near("spread multiplier", FireRules.HeatRate(0f, 1.5f, 3f, 2f), 2f / 3f);
            Near("heat accumulates", FireRules.StepHeat(0.2f, 0.5f, 0.25f, 1f), 0.7f);
            Near("heat decays without a source", FireRules.StepHeat(0.2f, 0f, 0.25f, 0.4f), 0.1f);
            Near("heat clamps at 1", FireRules.StepHeat(0.9f, 1f, 0.25f, 1f), 1f);
            Check("ignites at 1", FireRules.Ignites(1f) && !FireRules.Ignites(0.98f));
            Near("blast centre", FireRules.BlastFalloff(0f, 4f), 1f);
            Near("blast half way", FireRules.BlastFalloff(2f, 4f), 0.5f);
            Near("blast outside", FireRules.BlastFalloff(5f, 4f), 0f);
            Vector3 c = FireRules.SplashCentre(Vector3.zero, new Vector3(0f, -1f, 2f), 1.6f);
            Check("splash lands ahead, horizontally", Vector3.Distance(c, new Vector3(0f, 0f, 1.6f)) < 1e-3f);
            Check("in splash", FireRules.InSplash(new Vector3(0.5f, 0.5f, 1.6f), c, 1.6f));
            Check("not in splash (too far)", !FireRules.InSplash(new Vector3(2f, 0f, 1.6f), c, 1.6f));
            Check("not in splash (too high)", !FireRules.InSplash(new Vector3(0f, 4f, 1.6f), c, 1.6f));

            // ── data ──
            Check("Water attribute replicated (clients show splashes)", Def("Water") != null && Def("Water").Replicated);
            EffectDefinition onFire = AttributesSandboxSetup.Load<EffectDefinition>("Effects", "OnFireFx");
            if (Check("OnFireFx exists", onFire != null))
            {
                Check("OnFireFx lasts until put out", onFire.Duration <= 0f);
                Check("OnFireFx grants Burning", onFire.GrantedFlags.Exists(f => f != null && f.Id.Equals(AttrFlag.Burning)));
            }

            foreach (string name in new[] { "Barrel", "PowderBarrel", "Board", "Crate", "Chest" })
            {
                AttributeProfile p = Profile(name);
                if (!Check($"{name}: profile exists", p != null)) continue;
                var set = new AttributeSet(p.Layout);
                Check($"{name}: flammable with FlammableData", set.HasFlag(AttrFlag.Flammable) && p.GetData<FlammableData>() != null);
                Check($"{name}: has Integrity (burns down)", set.Has(Attr.Integrity));
            }
            Check("PowderBarrel: ExplosiveData", Profile("PowderBarrel")?.GetData<ExplosiveData>() != null);
            Check("Barrel: not explosive", Profile("Barrel")?.GetData<ExplosiveData>() == null);

            AttributeProfile bucket = Profile("Bucket");
            if (Check("Bucket: profile exists", bucket != null))
            {
                var set = new AttributeSet(bucket.Layout);
                Check("Bucket: carriable, BucketData", set.HasFlag(AttrFlag.Carriable) && bucket.GetData<BucketData>() != null);
                Check("Bucket: starts full of water", set.Has(Attr.Water) && set.GetCurrent(Attr.Water) >= 1f);
                Check("Bucket: not flammable", !set.HasFlag(AttrFlag.Flammable));
            }
            AttributeProfile lantern = Profile("Lantern");
            Check("Lantern: carriable, LanternData, breakable",
                  lantern != null && new AttributeSet(lantern.Layout).HasFlag(AttrFlag.Carriable) &&
                  lantern.GetData<LanternData>() != null && new AttributeSet(lantern.Layout).Has(Attr.Integrity));
            AttributeProfile cask = Profile("WaterCask");
            if (Check("WaterCask: profile exists", cask != null))
            {
                var set = new AttributeSet(cask.Layout);
                Check("WaterCask: not carriable, WaterSourceData, has water",
                      !set.HasFlag(AttrFlag.Carriable) && cask.GetData<WaterSourceData>() != null && set.GetCurrent(Attr.Water) > 3f);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemsSetup.CatalogPath);
            if (Check("catalog exists", catalog != null))
            {
                foreach (string id in new[] { "Bucket", "Lantern", "WaterCask" })
                {
                    GameObject prefab = catalog.Get(id);
                    Check($"catalog '{id}': prefab with WorldItem, no missing scripts",
                          prefab != null && prefab.GetComponent<WorldItem>() != null &&
                          GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) == 0);
                }
                GameObject caskPrefab = catalog.Get("WaterCask");
                Check("WaterCask prefab: WaterSourceInteractable, no ItemInteractable",
                      caskPrefab != null && caskPrefab.GetComponent<WaterSourceInteractable>() != null && caskPrefab.GetComponent<ItemInteractable>() == null);
                GameObject lanternPrefab = catalog.Get("Lantern");
                Check("Lantern prefab: has a light", lanternPrefab != null && lanternPrefab.GetComponentInChildren<Light>() != null);
            }
            Check("FireConfig asset", AssetDatabase.LoadAssetAtPath<FireConfig>(FireSetup.ConfigPath) != null);
        }

        private static AttributeProfile Profile(string name) => AttributesSandboxSetup.Load<AttributeProfile>("Profiles", name);
        private static AttributeDefinition Def(string name) => AttributesSandboxSetup.Load<AttributeDefinition>("Definitions", name);

        private static bool Check(string what, bool ok)
        {
            if (!ok)
            {
                _failures++;
                Debug.LogError($"[FireTest] FAIL: {what}");
            }
            return ok;
        }

        private static void Near(string what, float actual, float expected)
        {
            if (Mathf.Abs(actual - expected) <= 1e-3f) return;
            _failures++;
            Debug.LogError($"[FireTest] FAIL: {what} — expected {expected}, got {actual}");
        }
    }
}
