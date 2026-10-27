using System;
using System.Collections.Generic;
using RumOverboard.Gameplay.Attributes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RumOverboard.EditorTools.Attributes
{
    /// <summary>
    /// Self-contained checks of the attribute core (in-memory definitions — no project assets needed).
    ///   Menu:  RumOverboard ▸ Attributes ▸ Run Self-Test
    ///   Batch: Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.Attributes.AttributesSelfTest.RunBatch
    /// Exit code 0 = all checks passed; report in the log ([AttributesTest]).
    /// </summary>
    public static class AttributesSelfTest
    {
        private static int _failures;
        private static readonly List<Object> Temp = new();

        [MenuItem("RumOverboard/Attributes/Run Self-Test", priority = 20)]
        public static void RunMenu() => Run();

        public static void RunBatch() => EditorApplication.Exit(Run() == 0 ? 0 : 1);

        public static int Run()
        {
            _failures = 0;
            try
            {
                Execute();
            }
            catch (Exception e)
            {
                _failures++;
                Debug.LogException(e);
            }
            finally
            {
                foreach (Object o in Temp) Object.DestroyImmediate(o);
                Temp.Clear();
            }

            if (_failures == 0) Debug.Log("[AttributesTest] ALL PASSED");
            else Debug.LogError($"[AttributesTest] {_failures} FAILED");
            return _failures;
        }

        private static void Execute()
        {
            const float rate = 50f;

            // ── definitions ──
            AttributeDefinition stamina = Attribute("Stamina", 1, AttributeKind.Resource, 0f, 1000f);
            AttributeDefinition mass = Attribute("Mass", 2, AttributeKind.Stat, 0f, 100f);
            AttributeDefinition absent = Attribute("Absent", 3, AttributeKind.Stat, 0f, 100f);
            FlagDefinition flammable = Flag("Flammable", 1);
            FlagDefinition wet = Flag("Wet", 2);

            EffectDefinition wetFx = Effect("WetFx", 1, 1f, EffectStacking.Refresh, 1);
            wetFx.Modifiers.Add(new EffectModifier { Attribute = mass, ValuePerStack = 2f });
            wetFx.GrantedFlags.Add(wet);

            EffectDefinition burnFx = Effect("BurnFx", 2, 10f, EffectStacking.Stack, 3);
            burnFx.Modifiers.Add(new EffectModifier { Attribute = mass, ValuePerStack = -1f });
            burnFx.Periodic.Add(new EffectPeriodic { Resource = stamina, PerSecondPerStack = -10f });

            EffectDefinition onceFx = Effect("OnceFx", 3, 0f, EffectStacking.Ignore, 1);

            AttributeProfile baseProfile = Profile("Barrel", null,
                new List<AttributeEntry> { new(stamina, 100f), new(mass, 10f) },
                new List<FlagDefinition> { flammable },
                new List<IEntityData> { new DisplayData { DisplayName = "Barrel" } });

            // ── 1. base values / layout ──
            var set = new AttributeSet(baseProfile.Layout);
            Check("stamina value", set.Get(stamina.Id), 100f);
            Check("stamina starts full", set.GetCurrent(stamina.Id), 100f);
            Check("mass base", set.Get(mass.Id), 10f);
            Check("absent → fallback", set.Get(absent.Id, -1f), -1f);
            Check("has flammable", set.HasFlag(flammable.Id));
            Check("no wet", !set.HasFlag(wet.Id));

            // ── 2. additive modifiers + clamp ──
            ModifierSource a = ModifierSource.New(), b = ModifierSource.New();
            set.AddModifier(mass.Id, 5f, a);
            Check("mod +5", set.Get(mass.Id), 15f);
            set.AddModifier(mass.Id, 200f, b);
            Check("clamped to max", set.Get(mass.Id), 100f);
            set.RemoveSource(b);
            Check("source b removed", set.Get(mass.Id), 15f);
            set.RemoveSource(a);
            Check("source a removed", set.Get(mass.Id), 10f);
            set.AddModifier(absent.Id, 5f, a); // must be ignored silently
            Check("modifier on absent ignored", set.Get(absent.Id, -1f), -1f);

            // ── 3. resources ──
            Check("AddCurrent applied", set.AddCurrent(stamina.Id, -30f), -30f);
            Check("current 70", set.GetCurrent(stamina.Id), 70f);
            set.SetBase(stamina.Id, 50f);
            Check("current clamped to new max", set.GetCurrent(stamina.Id), 50f);
            Check("TrySpend too much fails", !set.TrySpend(stamina.Id, 60f));
            Check("TrySpend ok", set.TrySpend(stamina.Id, 20f));
            Check("current 30", set.GetCurrent(stamina.Id), 30f);
            Check("normalized", set.GetNormalized(stamina.Id), 0.6f);

            // ── 4. timed effect: modifiers + flags + expiry ──
            set.Reset();
            Check("apply wet", set.ApplyEffect(wetFx, 0, rate));
            Check("wet mass +2", set.Get(mass.Id), 12f);
            Check("wet flag granted", set.HasFlag(wet.Id));
            for (int t = 1; t < 50; t++) set.Tick(t, 1f / rate);
            Check("wet still active at tick 49", set.HasEffect(wetFx));
            set.Tick(50, 1f / rate);
            Check("wet expired at tick 50", !set.HasEffect(wetFx));
            Check("wet mass restored", set.Get(mass.Id), 10f);
            Check("wet flag removed", !set.HasFlag(wet.Id));
            Check("base flag kept", set.HasFlag(flammable.Id));

            // ── 5. stacking + periodic ──
            set.ApplyEffect(burnFx, 0, rate);
            set.ApplyEffect(burnFx, 0, rate);
            Check("burn stacks 2", set.GetStacks(burnFx), 2f);
            Check("burn mass -2", set.Get(mass.Id), 8f);
            set.ApplyEffect(burnFx, 0, rate, 5);
            Check("burn stacks capped 3", set.GetStacks(burnFx), 3f);
            set.Tick(1, 1f);
            Check("burn drains 30/s", set.GetCurrent(stamina.Id), 70f);
            set.RemoveEffect(burnFx);
            Check("burn removed → mass back", set.Get(mass.Id), 10f);
            Check("ignore: first apply", set.ApplyEffect(onceFx, 0, rate));
            Check("ignore: second apply rejected", !set.ApplyEffect(onceFx, 0, rate));
            set.Tick(100000, 0f);
            Check("infinite effect survives", set.HasEffect(onceFx));

            // ── 6. change tracking ──
            set.ConsumeChanges(out _, out _, out _);
            set.AddCurrent(stamina.Id, -1f);
            set.ConsumeChanges(out ulong slots, out bool flagsChanged, out _);
            Check("stamina slot marked", (slots & (1UL << set.SlotOf(stamina.Id))) != 0);
            Check("flags not marked", !flagsChanged);
            set.ConsumeChanges(out slots, out _, out _);
            Check("consumed", slots == 0);

            // ── 7. profile inheritance + data ──
            AttributeProfile child = Profile("PowderBarrel", baseProfile,
                new List<AttributeEntry> { new(mass, 20f) },
                new List<FlagDefinition> { wet },
                null);
            var childSet = new AttributeSet(child.Layout);
            Check("child overrides mass", childSet.Get(mass.Id), 20f);
            Check("child inherits stamina", childSet.Get(stamina.Id), 100f);
            Check("child flags union", childSet.HasFlag(flammable.Id) && childSet.HasFlag(wet.Id));
            Check("child inherits data", child.GetData<DisplayData>()?.DisplayName == "Barrel");
            Check("missing data → null", child.GetData<BreakableData>() == null);
            Check("same ids/kinds → same layout hash", child.Layout.Hash == baseProfile.Layout.Hash);

            // ── 8. zero allocations on the hot path ──
            set.Reset();
            float sink = 0f;
            for (int i = 0; i < 100; i++) sink += Hot(set, stamina, mass, flammable);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) sink += Hot(set, stamina, mass, flammable);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check($"hot path allocates 0 bytes (got {allocated})", allocated == 0);
            if (float.IsNaN(sink)) Debug.Log(sink);
        }

        private static float Hot(AttributeSet set, AttributeDefinition stamina, AttributeDefinition mass, FlagDefinition flag)
        {
            float v = set.Get(mass.Id) + set.GetCurrent(stamina.Id);
            set.AddCurrent(stamina.Id, -0.001f);
            set.Tick(1, 0.02f);
            return set.HasFlag(flag.Id) ? v : -v;
        }

        // ───────────────────────── helpers ─────────────────────────

        private static void Check(string what, bool ok)
        {
            if (ok) return;
            _failures++;
            Debug.LogError($"[AttributesTest] FAIL: {what}");
        }

        private static void Check(string what, float actual, float expected)
        {
            if (Mathf.Abs(actual - expected) <= 1e-4f) return;
            _failures++;
            Debug.LogError($"[AttributesTest] FAIL: {what} — expected {expected}, got {actual}");
        }

        private static T Make<T>(string name) where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            o.name = name;
            o.hideFlags = HideFlags.HideAndDontSave;
            Temp.Add(o);
            return o;
        }

        private static AttributeDefinition Attribute(string name, ushort id, AttributeKind kind, float min, float max)
        {
            var d = Make<AttributeDefinition>(name);
            d.EditorSetId(id);
            d.Kind = kind;
            d.Min = min;
            d.Max = max;
            return d;
        }

        private static FlagDefinition Flag(string name, byte id)
        {
            var d = Make<FlagDefinition>(name);
            d.EditorSetId(id);
            return d;
        }

        private static EffectDefinition Effect(string name, ushort id, float duration, EffectStacking stacking, int maxStacks)
        {
            var d = Make<EffectDefinition>(name);
            d.EditorSetId(id);
            d.Duration = duration;
            d.Stacking = stacking;
            d.MaxStacks = maxStacks;
            return d;
        }

        private static AttributeProfile Profile(string name, AttributeProfile parent, List<AttributeEntry> entries,
                                                List<FlagDefinition> flags, List<IEntityData> data)
        {
            var p = Make<AttributeProfile>(name);
            p.EditorSetup(parent, entries, flags, data);
            return p;
        }
    }
}
