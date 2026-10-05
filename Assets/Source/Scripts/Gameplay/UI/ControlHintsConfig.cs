using System;
using RumOverboard.StateMachine;
using UnityEngine;

namespace RumOverboard.Gameplay.UI
{
    [Serializable]
    public struct ControlHint
    {
        public string Key;
        public string Text;

        public ControlHint(string key, string text)
        {
            Key = key;
            Text = text;
        }
    }

    [Serializable]
    public sealed class StateHintSet
    {
        [Tooltip("Shown while this locomotion state bit is active (first matching set wins).")]
        public PlayerState State;
        public ControlHint[] Hints = Array.Empty<ControlHint>();
    }

    /// <summary>
    /// Data for the contextual controls panel: which keys to show in which player state, plus
    /// the key label used for the interaction prompt. Authored as an asset
    /// (Assets ▸ Create ▸ RumOverboard ▸ Control Hints); <see cref="CreateDefault"/> is the
    /// fallback when none is assigned.
    /// </summary>
    [CreateAssetMenu(fileName = "ControlHints", menuName = "RumOverboard/Control Hints")]
    public sealed class ControlHintsConfig : ScriptableObject
    {
        [Tooltip("Key label for the look-at interaction prompt.")]
        public string InteractKey = "E";

        public StateHintSet[] ByState = Array.Empty<StateHintSet>();

        [Tooltip("Shown when no state set matches.")]
        public ControlHint[] Fallback = Array.Empty<ControlHint>();

        public ControlHint[] HintsFor(PlayerState active)
        {
            if (ByState != null)
                foreach (StateHintSet set in ByState)
                    if (set != null && set.State != PlayerState.None && (active & set.State) == set.State)
                        return set.Hints;
            return Fallback;
        }

        public static ControlHintsConfig CreateDefault()
        {
            var cfg = CreateInstance<ControlHintsConfig>();
            cfg.name = "ControlHints (default)";
            cfg.ApplyDefaults();
            return cfg;
        }

        public void ApplyDefaults()
        {
            InteractKey = "E";
            ByState = new[]
            {
                new StateHintSet
                {
                    State = PlayerState.HoldingRope,
                    Hints = new[]
                    {
                        new ControlHint("ЛКМ", "Выбирать канат (поднять / подтянуть)"),
                        new ControlHint("ПКМ", "Травить"),
                        new ControlHint("Отойти", "Натянуть — уходя от мачты, тоже выбираешь"),
                        new ControlHint("E на нагель", "Завязать"),
                        new ControlHint("G", "Бросить конец"),
                    },
                },
                new StateHintSet
                {
                    State = PlayerState.Climbing,
                    Hints = new[]
                    {
                        new ControlHint("W / S", "Вверх / вниз"),
                        new ControlHint("A / D", "Вбок"),
                        new ControlHint("Space", "Спрыгнуть"),
                        new ControlHint("E", "Отпустить"),
                    },
                },
                new StateHintSet
                {
                    State = PlayerState.Steering,
                    Hints = new[]
                    {
                        new ControlHint("A / D", "Крутить штурвал"),
                        new ControlHint("E", "Отойти от штурвала"),
                    },
                },
                new StateHintSet
                {
                    State = PlayerState.Swimming,
                    Hints = new[]
                    {
                        new ControlHint("WASD", "Плыть"),
                        new ControlHint("Space", "Всплыть"),
                    },
                },
            };
            Fallback = new[]
            {
                new ControlHint("WASD", "Идти"),
                new ControlHint("Space", "Прыжок"),
                new ControlHint("E", "Действие (по прицелу)"),
                new ControlHint("Q", "Глоток рома"),
                new ControlHint("R", "Регдолл"),
            };
        }
    }
}
