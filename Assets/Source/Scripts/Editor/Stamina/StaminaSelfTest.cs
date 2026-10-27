using System;
using System.Collections.Generic;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Crew;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RumOverboard.EditorTools.Stamina
{
    /// <summary>
    /// Checks of the crew stamina model (<see cref="CrewStamina"/>) with in-memory definitions that use
    /// the generated ids — no project assets needed.
    ///   Menu:  RumOverboard ▸ Stamina ▸ Run Self-Test
    ///   Batch: Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.Stamina.StaminaSelfTest.RunBatch
    /// </summary>
    public static class StaminaSelfTest
    {
        private const float Dt = 0.02f;
        private static int _failures;
        private static readonly List<Object> Temp = new();

        [MenuItem("RumOverboard/Stamina/Run Self-Test", priority = 20)]
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
            if (_failures == 0) Debug.Log("[StaminaTest] ALL PASSED");
            else Debug.LogError($"[StaminaTest] {_failures} FAILED");
            return _failures;
        }

        private static void Execute()
        {
            AttributeDefinition stamina = Def(Attr.Stamina, AttributeKind.Resource);
            var entries = new List<AttributeEntry>
            {
                new(stamina, 100f),
                new(Def(Attr.StaminaRegen), 12f),
                new(Def(Attr.StaminaRegenDelay), 1f),
                new(Def(Attr.StaminaRecoverThreshold), 30f),
                new(Def(Attr.StaminaSprintCost), 10f),
                new(Def(Attr.StaminaJumpCost), 8f),
                new(Def(Attr.StaminaClimbCost), 6f),
                new(Def(Attr.StaminaSwimCost), 5f),
                new(Def(Attr.StaminaHaulCost), 8f),
                new(Def(Attr.StaminaExhaustedSpeed, AttributeKind.Stat, 1f), 0.5f),
            };
            var exhaustedFlag = ScriptableObject.CreateInstance<FlagDefinition>();
            exhaustedFlag.EditorSetId(AttrFlag.Exhausted.Value);
            Keep(exhaustedFlag);

            // ── no stamina attribute → everything open, nothing spent ──
            var none = new AttributeSet(Profile(new List<AttributeEntry>()).Layout);
            StaminaGates open = CrewStamina.GatesFor(none);
            Check("no stamina: gates open", open.CanSprint && open.CanJump && Mathf.Approximately(open.SpeedFactor, 1f));
            new CrewStamina().Step(none, new StaminaActivity { Sprinting = true }, Dt); // must not throw

            var set = new AttributeSet(Profile(entries).Layout);
            var model = new CrewStamina();

            // ── sprinting drains 10/s, no regen while spending ──
            Run(model, set, new StaminaActivity { Sprinting = true }, 1f);
            Near("sprint 1 s → 90", set.GetCurrent(Attr.Stamina), 90f);

            // ── regen waits for the delay, then 12/s ──
            Run(model, set, default, 0.5f);
            Near("no regen before the 1 s delay", set.GetCurrent(Attr.Stamina), 90f);
            Run(model, set, default, 1f);
            Near("regen 12/s after the delay (0.5 s of it)", set.GetCurrent(Attr.Stamina), 96f, 0.6f);
            Run(model, set, default, 5f);
            Near("regen caps at max", set.GetCurrent(Attr.Stamina), 100f);

            // ── jump: one-off cost, gated by the amount ──
            model.Step(set, new StaminaActivity { Jumped = true }, Dt);
            Near("jump costs 8", set.GetCurrent(Attr.Stamina), 92f);
            set.SetCurrent(Attr.Stamina, 7f);
            Check("can't jump below the jump cost", !CrewStamina.GatesFor(set).CanJump);
            Check("can still sprint with a little left", CrewStamina.GatesFor(set).CanSprint);

            // ── per-activity drains ──
            Near("climb 6/s", CrewStamina.DrainPerSecond(set, new StaminaActivity { Climbing = true }), 6f);
            Near("sprint-climb ×2", CrewStamina.DrainPerSecond(set, new StaminaActivity { Climbing = true, ClimbSprint = true }), 12f);
            Near("treading water 30%", CrewStamina.DrainPerSecond(set, new StaminaActivity { Swimming = true, SwimEffort = 0f }), 1.5f);
            Near("swimming full effort", CrewStamina.DrainPerSecond(set, new StaminaActivity { Swimming = true, SwimEffort = 1f }), 5f);
            Near("haul × load", CrewStamina.DrainPerSecond(set, new StaminaActivity { Hauling = true, LineLoad = 1.5f }), 12f);
            Near("haul load clamped to 2", CrewStamina.DrainPerSecond(set, new StaminaActivity { Hauling = true, LineLoad = 9f }), 16f);
            Near("haul light line min 0.25", CrewStamina.DrainPerSecond(set, new StaminaActivity { Hauling = true, LineLoad = 0f }), 2f);

            // ── exhaustion at zero, with hysteresis ──
            Run(model, set, new StaminaActivity { Sprinting = true }, 1f);
            Near("drained to zero", set.GetCurrent(Attr.Stamina), 0f);
            Check("exhausted at zero", set.HasFlag(AttrFlag.Exhausted));
            StaminaGates tired = CrewStamina.GatesFor(set);
            Check("exhausted: no sprint, no jump", !tired.CanSprint && !tired.CanJump);
            Near("exhausted: climb/swim speed ×0.5", tired.SpeedFactor, 0.5f);
            Run(model, set, default, 1f + 20f / 12f); // delay + regen to 20
            Check("still exhausted below the recover threshold (20 < 30)", set.HasFlag(AttrFlag.Exhausted));
            Run(model, set, default, 1f);
            Check("recovered at the threshold", !set.HasFlag(AttrFlag.Exhausted));
            Check("sprint allowed again", CrewStamina.GatesFor(set).CanSprint);

            // ── cold = regen modifier (an effect lowers a stat, the model just reads it) ──
            set.SetCurrent(Attr.Stamina, 50f);
            model.Reset();
            var cold = ModifierSource.New();
            set.AddModifier(Attr.StaminaRegen, -6f, cold);
            Run(model, set, default, 1f);
            Near("cold halves regen (6/s)", set.GetCurrent(Attr.Stamina), 56f, 0.2f);
            set.RemoveSource(cold);

            // ── zero allocations per tick ──
            var act = new StaminaActivity { Sprinting = true, Swimming = true, SwimEffort = 0.5f };
            for (int i = 0; i < 100; i++) { model.Step(set, act, Dt); CrewStamina.GatesFor(set); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) { model.Step(set, act, Dt); CrewStamina.GatesFor(set); set.SetCurrent(Attr.Stamina, 80f); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check($"step allocates 0 bytes (got {allocated})", allocated == 0);
        }

        // ───────────────────────── helpers ─────────────────────────

        private static void Run(CrewStamina model, AttributeSet set, in StaminaActivity activity, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++) model.Step(set, activity, Dt);
        }

        private static AttributeDefinition Def(AttributeId id, AttributeKind kind = AttributeKind.Stat, float max = 1000f)
        {
            var d = ScriptableObject.CreateInstance<AttributeDefinition>();
            d.EditorSetId(id.Value);
            d.Kind = kind;
            d.Min = 0f;
            d.Max = max;
            return Keep(d);
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

        private static void Check(string what, bool ok)
        {
            if (ok) return;
            _failures++;
            Debug.LogError($"[StaminaTest] FAIL: {what}");
        }

        private static void Near(string what, float actual, float expected, float tolerance = 0.05f)
        {
            if (Mathf.Abs(actual - expected) <= tolerance) return;
            _failures++;
            Debug.LogError($"[StaminaTest] FAIL: {what} — expected {expected}, got {actual}");
        }
    }
}
