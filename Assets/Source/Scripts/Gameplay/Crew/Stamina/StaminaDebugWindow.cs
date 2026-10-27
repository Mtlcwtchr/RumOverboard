#if FUSION2
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Crew
{
    /// <summary>
    /// In-scene debug window for crew stamina (F7 toggles; frees the cursor while open).
    /// Per crew member: stamina bar, exhausted flag, what is being charged right now (host),
    /// spend timer and effective regen (modifiers included, e.g. Cold). On the host: live tuning of
    /// every stamina stat for the whole crew, Fill / Empty, Reset to profile, Save to profile
    /// (the tuning stats are attributes of the Player profile — the same values F6 shows).
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class StaminaDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 470f, 560f);

        private const int WindowId = 0x057A3141;
        private Vector2 _scroll;

        private static readonly (string label, AttributeId id, float min, float max)[] Tuning =
        {
            ("Regen /s", Attr.StaminaRegen, 0f, 60f),
            ("Regen delay s", Attr.StaminaRegenDelay, 0f, 5f),
            ("Recover at", Attr.StaminaRecoverThreshold, 0f, 100f),
            ("Sprint /s", Attr.StaminaSprintCost, 0f, 60f),
            ("Jump", Attr.StaminaJumpCost, 0f, 50f),
            ("Climb /s", Attr.StaminaClimbCost, 0f, 60f),
            ("Swim /s", Attr.StaminaSwimCost, 0f, 60f),
            ("Haul /s × load", Attr.StaminaHaulCost, 0f, 60f),
            ("Exhausted speed ×", Attr.StaminaExhaustedSpeed, 0.05f, 1f),
            ("Max stamina", Attr.Stamina, 10f, 300f),
        };

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame)
            {
                visible = !visible;
                Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = visible;
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;
            if (!visible)
            {
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 126f, 180f, 22f), "F7 — Stamina debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Crew stamina — F7");
        }

        private void Draw(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            NetworkPlayer host = null;

            foreach (NetworkPlayer p in NetworkPlayer.All)
            {
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                AttributesComponent a = p.Attributes;
                if (a == null || !a.Has(Attr.Stamina))
                {
                    GUILayout.Label($"<b>{p.Object.InputAuthority}</b>  — no stamina attribute on the prefab");
                    continue;
                }

                AttributeSet set = a.Set;
                float cur = set.GetCurrent(Attr.Stamina), max = set.Get(Attr.Stamina);
                GUILayout.Label($"<b>{p.Object.InputAuthority}</b>{(p.HasInputAuthority ? " (you)" : "")}  " +
                                $"{cur:0.0}/{max:0}  {(p.IsExhausted ? "<color=#ff6655>EXHAUSTED</color>" : "")}");
                Bar(set.GetNormalized(Attr.Stamina), p.IsExhausted);

                if (!p.HasStateAuthority) continue; // clients only see the replicated amount
                host ??= p;

                StaminaActivity act = p.LastStaminaActivity;
                float drain = CrewStamina.DrainPerSecond(set, act);
                GUILayout.Label($"spending: {Describe(act)}  ({drain:0.0}/s{(act.Jumped ? " + jump" : "")})");
                float regen = set.Get(Attr.StaminaRegen), regenBase = set.GetBase(Attr.StaminaRegen);
                float since = p.Stamina.SinceSpend;
                GUILayout.Label($"regen {regen:0.0}/s{(Mathf.Approximately(regen, regenBase) ? "" : $" (base {regenBase:0.0})")}  " +
                                $"since spend {(since > 999f ? "—" : $"{since:0.0}s")}  " +
                                $"effects: {EffectsText(set)}");

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Fill")) set.SetCurrent(Attr.Stamina, max);
                if (GUILayout.Button("Empty")) set.SetCurrent(Attr.Stamina, 0f);
                if (GUILayout.Button("Half")) set.SetCurrent(Attr.Stamina, max * 0.5f);
                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
            }

            if (host != null)
                DrawTuning(host);
            else
                GUILayout.Label("(tuning is host-only; no host-owned crew member with stamina yet)");

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private static void DrawTuning(NetworkPlayer reference)
        {
            AttributeSet refSet = reference.Attributes.Set;
            GUILayout.Space(6f);
            GUILayout.Label("<b>Tuning (Player profile attributes — whole crew)</b>");

            foreach (var (label, attr, min, max) in Tuning)
            {
                if (!refSet.Has(attr)) continue;
                float before = refSet.GetBase(attr);
                float after = Slider(label, before, min, max);
                if (!Mathf.Approximately(before, after))
                    ForEachHostCrew(set => set.SetBase(attr, after));
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to profile"))
                ForEachHostCrew(set =>
                {
                    foreach (var (_, attr, _, _) in Tuning)
                    {
                        int slot = set.SlotOf(attr);
                        if (slot >= 0) set.SetSlotBase(slot, set.Layout.BaseValues[slot]);
                    }
                });
#if UNITY_EDITOR
            if (GUILayout.Button("Save to profile"))
                SaveToProfile(reference.Attributes);
#endif
            GUILayout.EndHorizontal();
        }

        private static void ForEachHostCrew(System.Action<AttributeSet> action)
        {
            foreach (NetworkPlayer p in NetworkPlayer.All)
                if (p != null && p.HasStateAuthority && p.Attributes != null && p.Attributes.Set != null)
                    action(p.Attributes.Set);
        }

        private static string Describe(in StaminaActivity a)
        {
            if (!a.Any) return "nothing";
            var parts = new System.Collections.Generic.List<string>(4);
            if (a.Sprinting) parts.Add("sprint");
            if (a.Climbing) parts.Add(a.ClimbSprint ? "climb (fast)" : "climb");
            if (a.Swimming) parts.Add($"swim {a.SwimEffort:0.0}");
            if (a.Hauling) parts.Add($"haul ×{a.LineLoad:0.00}");
            if (a.Jumped) parts.Add("jump");
            return string.Join(", ", parts);
        }

        private static string EffectsText(AttributeSet set)
        {
            if (set.Effects.Count == 0) return "—";
            var names = new string[set.Effects.Count];
            for (int i = 0; i < names.Length; i++) names[i] = set.Effects[i].Definition.name;
            return string.Join(", ", names);
        }

        private static void Bar(float value, bool exhausted)
        {
            Rect r = GUILayoutUtility.GetRect(200f, 10f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, new Color(0f, 0f, 0f, 0.5f), 0f, 0f);
            r.width *= Mathf.Clamp01(value);
            Color c = exhausted ? new Color(0.9f, 0.25f, 0.2f) : Color.Lerp(new Color(0.95f, 0.75f, 0.2f), new Color(0.35f, 0.8f, 0.95f), value);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, c, 0f, 0f);
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:F2}", GUILayout.Width(200f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }

#if UNITY_EDITOR
        private static void SaveToProfile(AttributesComponent attributes)
        {
            AttributeProfile profile = attributes.Profile;
            if (profile == null) return;
            AttributeSet set = attributes.Set;
            int saved = 0;
            foreach (var (_, attr, _, _) in Tuning)
            {
                int slot = set.SlotOf(attr);
                if (slot < 0 || Mathf.Approximately(set.GetSlotBase(slot), set.Layout.BaseValues[slot])) continue;
                profile.EditorSetBaseValue(set.Layout.Definitions[slot], set.GetSlotBase(slot));
                saved++;
            }
            UnityEditor.EditorUtility.SetDirty(profile);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(profile);
            Debug.Log($"[Stamina] Saved {saved} tuning value(s) to profile '{profile.name}'.", profile);
        }
#endif
    }
}
#endif
