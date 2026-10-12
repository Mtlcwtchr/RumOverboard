#if FUSION2
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing
{
    /// <summary>Local, replicated rhythm cue. The marker reaches the green zone at the next catch.</summary>
    public sealed class RowingHud : MonoBehaviour
    {
        private static RowingHud _instance;

        public static void Ensure()
        {
            if (_instance == null)
                _instance = new GameObject("RowingHud").AddComponent<RowingHud>();
            if (FindAnyObjectByType<RowingDebugWindow>() == null)
                _instance.gameObject.AddComponent<RowingDebugWindow>();
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void OnGUI()
        {
            NetworkPlayer player = null;
            foreach (var p in NetworkPlayer.All)
                if (p != null && p.Object != null && p.Object.IsValid && p.HasInputAuthority && p.IsRowing)
                { player = p; break; }
            if (player == null || !InteractableIndex.TryResolve(player.Runner, player.AttachedRef, out var target) ||
                !(target is OarStation oar) || oar.Ship == null || !oar.Ship.HasOar(oar.OarIndex)) return;

            var ship = oar.Ship;
            var cfg = ship.RowingSettings;
            var timing = new RowTiming(cfg, ship.Runner.DeltaTime);
            var state = ship.GetOar(oar.OarIndex);
            // Judge against the tick that the next local input will target, including on clients.
            int now = player.Runner.Tick;
            int elapsed = RowingRhythm.Elapsed(state, now);
            bool queued = state.StrokeTick > now;
            float cycle = elapsed == int.MaxValue ? 0f : queued ? elapsed + timing.Period : elapsed;
            float span = timing.Period + timing.Late;
            float x = (Screen.width - 390f) * 0.5f;
            float y = Screen.height - 175f;
            GUI.Box(new Rect(x, y, 390f, 118f), "Гребля — W: вперёд · S: назад · E: отпустить");
            var bar = new Rect(x + 20f, y + 40f, 350f, 18f);
            Paint(bar, new Color(0.12f, 0.15f, 0.18f));
            Paint(new Rect(bar.x + bar.width * (timing.Period - timing.Early) / span, bar.y,
                bar.width * (timing.Early + timing.Late) / span, bar.height), new Color(0.18f, 0.65f, 0.3f));
            Paint(new Rect(bar.x + Mathf.Clamp01(cycle / span) * (bar.width - 4f), bar.y - 3f, 4f, 24f), Color.white);
            string cue = state.Sign == 0 ? "Нажми W или S, чтобы начать" : queued ? "Следующий гребок готов" :
                RowingRhythm.InWindow(state, now, timing) ? "Сейчас! Нажми и отпусти клавишу" :
                elapsed > timing.Period + timing.Late ? "Ритм потерян — начни снова" : "Дождись зелёной зоны";
            GUI.Label(new Rect(x + 20f, y + 64f, 350f, 22f), cue);
            string feedback = (RowFeedback)state.Feedback switch
            {
                RowFeedback.InTime => "В такт!",
                RowFeedback.Early => "Слишком рано",
                RowFeedback.Late => "Поздно",
                RowFeedback.Backing => "Табаним",
                _ => "",
            };
            GUI.Label(new Rect(x + 20f, y + 88f, 350f, 22f), $"{feedback}   Серия: {state.Streak}   Сила: {state.Power}%");
        }

        private static void Paint(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
#endif
