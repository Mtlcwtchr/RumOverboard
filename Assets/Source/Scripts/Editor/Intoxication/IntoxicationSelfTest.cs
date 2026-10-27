using System;
using System.Collections.Generic;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Crew;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RumOverboard.EditorTools.Intoxication
{
    /// <summary>
    /// Checks of <see cref="CrewIntoxication"/> with in-memory definitions using the generated ids.
    ///   Menu:  RumOverboard ▸ Intoxication ▸ Run Self-Test
    ///   Batch: Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.Intoxication.IntoxicationSelfTest.RunBatch
    /// </summary>
    public static class IntoxicationSelfTest
    {
        private static int _failures;
        private static readonly List<Object> Temp = new();

        [MenuItem("RumOverboard/Intoxication/Run Self-Test", priority = 20)]
        public static void RunMenu() => Run();

        public static void RunBatch() => EditorApplication.Exit(Run() == 0 ? 0 : 1);

        public static int Run()
        {
            _failures = 0;
            try { Execute(); }
            catch (Exception e) { _failures++; Debug.LogException(e); }
            finally
            {
                foreach (Object o in Temp) Object.DestroyImmediate(o);
                Temp.Clear();
            }
            if (_failures == 0) Debug.Log("[IntoxicationTest] ALL PASSED");
            else Debug.LogError($"[IntoxicationTest] {_failures} FAILED");
            return _failures;
        }

        private static void Execute()
        {
            // No attribute → always sober, writes are ignored.
            var none = new AttributeSet(Profile(new List<AttributeEntry>()).Layout);
            CrewIntoxication.Set(none, 0.7f);
            Near("no attribute → 0", CrewIntoxication.Get(none), 0f);
            CrewIntoxication.Step(none, false, 1f); // must not throw

            var drunk = Def(Attr.Drunkenness, AttributeKind.Resource, max: 1f, startFull: false);
            var sober = Def(Attr.DrunkSoberRate, AttributeKind.Stat, max: 1f);
            var set = new AttributeSet(Profile(new List<AttributeEntry> { new(drunk, 1f), new(sober, 0f) }).Layout);

            Near("sober at spawn", CrewIntoxication.Get(set), 0f);
            CrewIntoxication.Set(set, 0.45f);
            Near("set", CrewIntoxication.Get(set), 0.45f);
            CrewIntoxication.Set(set, 3f);
            Near("clamped to 1", CrewIntoxication.Get(set), 1f);
            CrewIntoxication.Set(set, -1f);
            Near("clamped to 0", CrewIntoxication.Get(set), 0f);

            CrewIntoxication.Set(set, 0.5f);
            for (int i = 0; i < 50; i++) CrewIntoxication.Step(set, false, 0.02f);
            Near("sober rate 0 → stays drunk (old behaviour)", CrewIntoxication.Get(set), 0.5f);

            set.SetBase(Attr.DrunkSoberRate, 0.1f);
            for (int i = 0; i < 50; i++) CrewIntoxication.Step(set, true, 0.02f);
            Near("no sobering during a sip", CrewIntoxication.Get(set), 0.5f);
            for (int i = 0; i < 50; i++) CrewIntoxication.Step(set, false, 0.02f);
            Near("sobers 0.1/s", CrewIntoxication.Get(set), 0.4f, 0.005f);
            for (int i = 0; i < 500; i++) CrewIntoxication.Step(set, false, 0.02f);
            Near("sobers down to 0, not below", CrewIntoxication.Get(set), 0f);

            // An effect can change the cap or the rate like any stat (e.g. a "hangover" modifier).
            var src = ModifierSource.New();
            set.AddModifier(Attr.DrunkSoberRate, 0.2f, src);
            Near("sober rate modifiable by effects", set.Get(Attr.DrunkSoberRate), 0.3f);
            set.RemoveSource(src);
        }

        private static AttributeDefinition Def(AttributeId id, AttributeKind kind, float max, bool startFull = true)
        {
            var d = Keep(ScriptableObject.CreateInstance<AttributeDefinition>());
            d.EditorSetId(id.Value);
            d.Kind = kind;
            d.Min = 0f;
            d.Max = max;
            d.StartFull = startFull;
            return d;
        }

        private static AttributeProfile Profile(List<AttributeEntry> entries)
        {
            var p = Keep(ScriptableObject.CreateInstance<AttributeProfile>());
            p.EditorSetup(null, entries, new List<FlagDefinition>());
            return p;
        }

        private static T Keep<T>(T o) where T : Object
        {
            o.hideFlags = HideFlags.HideAndDontSave;
            Temp.Add(o);
            return o;
        }

        private static void Near(string what, float actual, float expected, float tolerance = 0.001f)
        {
            if (Mathf.Abs(actual - expected) <= tolerance) return;
            _failures++;
            Debug.LogError($"[IntoxicationTest] FAIL: {what} — expected {expected}, got {actual}");
        }
    }
}
