using RumOverboard.Gameplay.Attributes;
using UnityEngine;

namespace RumOverboard.Gameplay.Crew
{
    /// <summary>
    /// Crew intoxication on top of the attribute system (moved out of <c>PlayerNetState.Drunkenness</c>).
    ///   • <c>Drunkenness</c> — Resource: current = intoxication 0..1, value = its cap (1). Replicated, so
    ///     camera sway and the HUD meter read it on every peer.
    ///   • <c>DrunkSoberRate</c> — host-only stat: intoxication lost per second while not drinking
    ///     (0 = never sobers up — the behaviour before the move).
    /// Raised by sips of rum (DrinkingRumState via StateContext.Drunkenness, GameConfig.RumSipIntoxication);
    /// read by the walk wobble, the ragdoll baseline and presentation. The feel curves stay in GameConfig.
    /// Without a Drunkenness attribute the crew member is always sober and sips do nothing.
    /// </summary>
    public static class CrewIntoxication
    {
        public static bool Has(AttributeSet set) => set != null && set.Has(Attr.Drunkenness);

        /// <summary>Intoxication 0..1 (0 without the attribute).</summary>
        public static float Get(AttributeSet set) =>
            Has(set) ? Mathf.Clamp01(set.GetCurrent(Attr.Drunkenness)) : 0f;

        /// <summary>Host: overwrite intoxication (clamped to 0..cap).</summary>
        public static void Set(AttributeSet set, float value)
        {
            if (Has(set)) set.SetCurrent(Attr.Drunkenness, Mathf.Clamp01(value));
        }

        /// <summary>Host tick: sober up at <c>DrunkSoberRate</c>/s unless a sip is in progress.</summary>
        public static void Step(AttributeSet set, bool drinking, float dt)
        {
            if (!Has(set) || drinking || dt <= 0f) return;
            float rate = set.Get(Attr.DrunkSoberRate);
            if (rate > 0f) set.AddCurrent(Attr.Drunkenness, -rate * dt);
        }
    }
}
