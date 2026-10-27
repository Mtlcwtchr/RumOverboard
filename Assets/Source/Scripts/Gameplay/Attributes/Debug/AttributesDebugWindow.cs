using System.Collections.Generic;
using RumOverboard.Core.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// In-scene debug window for attributes (F6 toggles; frees the cursor while open).
    /// Lists every <see cref="AttributesComponent"/> from SimWorld; for the selected one shows
    /// base / value / current, flags, active effects with remaining time. On the host/offline:
    /// edit base values and resources, toggle base flags, apply/remove effects, reset to profile,
    /// save base values to the profile asset (editor only). On clients: read-only view of replicated state.
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class AttributesDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 520f, 620f);

        private const int WindowId = 0x0A77B5E7;
        private Vector2 _listScroll;
        private Vector2 _scroll;
        private AttributesComponent _selected;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f6Key.wasPressedThisFrame)
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
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 106f, 180f, 22f), "F6 — Attributes debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Attributes — F6");
        }

        private void Draw(int id)
        {
            IReadOnlyList<AttributesComponent> all = SimWorld.All<AttributesComponent>();

            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(110f));
            for (int i = 0; i < all.Count; i++)
            {
                AttributesComponent a = all[i];
                if (a == null) continue;
                string label = $"{(a == _selected ? "▶ " : "")}{a.name}  [{(a.Profile != null ? a.Profile.name : "no profile")}]";
                if (GUILayout.Button(label, GUI.skin.label))
                    _selected = a;
            }
            GUILayout.EndScrollView();

            if (_selected == null || _selected.Set == null)
            {
                GUILayout.Label("Select an entity.");
                GUI.DragWindow();
                return;
            }

            AttributeSet set = _selected.Set;
            bool canEdit = _selected.IsAuthority;

            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label($"<b>{_selected.name}</b>  tick {_selected.Tick}  v{set.Version}  {(canEdit ? "authority" : "client (read-only)")}");

            DrawAttributes(set, canEdit);
            DrawFlags(set, canEdit);
            DrawEffects(set, canEdit);

            if (canEdit)
            {
                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset to profile")) _selected.ResetToProfile();
#if UNITY_EDITOR
                if (GUILayout.Button("Save base → profile")) SaveBaseToProfile(_selected);
#endif
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private static void DrawAttributes(AttributeSet set, bool canEdit)
        {
            GUILayout.Space(6f);
            GUILayout.Label("<b>Attributes</b>  (base → value | current)");
            AttributeLayout layout = set.Layout;
            for (int slot = 0; slot < layout.Count; slot++)
            {
                AttributeDefinition def = layout.Definitions[slot];
                bool resource = set.IsResource(slot);
                float baseValue = set.GetSlotBase(slot);
                float value = set.GetSlotValue(slot);

                GUILayout.BeginHorizontal();
                string text = resource
                    ? $"{def.name}: {baseValue:0.##} → {value:0.##} | {set.GetSlotCurrent(slot):0.##}"
                    : $"{def.name}: {baseValue:0.##} → {value:0.##}";
                GUILayout.Label(text, GUILayout.Width(280f));
                if (canEdit)
                {
                    float step = Mathf.Max(1f, Mathf.Abs(baseValue) * 0.1f);
                    if (GUILayout.Button("−", GUILayout.Width(24f))) set.SetSlotBase(slot, baseValue - step);
                    if (GUILayout.Button("+", GUILayout.Width(24f))) set.SetSlotBase(slot, baseValue + step);
                    if (resource)
                    {
                        if (GUILayout.Button("−10%", GUILayout.Width(44f))) set.SetSlotCurrent(slot, set.GetSlotCurrent(slot) - value * 0.1f);
                        if (GUILayout.Button("Fill", GUILayout.Width(36f))) set.SetSlotCurrent(slot, value);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private static void DrawFlags(AttributeSet set, bool canEdit)
        {
            AttributeRegistry registry = AttributeRegistry.Instance;
            if (registry == null) return;

            GUILayout.Space(6f);
            GUILayout.Label("<b>Flags</b>  (effective; toggling edits base flags)");
            GUILayout.BeginHorizontal();
            int col = 0;
            foreach (FlagDefinition flag in registry.AllFlags)
            {
                if (flag == null) continue;
                bool on = set.HasFlag(flag.Id);
                bool toggled = GUILayout.Toggle(on, flag.name, GUILayout.Width(120f));
                if (canEdit && toggled != on) set.SetFlag(flag.Id, toggled);
                if (++col % 4 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawEffects(AttributeSet set, bool canEdit)
        {
            GUILayout.Space(6f);
            GUILayout.Label("<b>Effects</b>");
            for (int i = 0; i < set.Effects.Count; i++)
            {
                AttributeSet.ActiveEffect e = set.Effects[i];
                float left = e.RemainingSeconds(_selected.Tick, _selected.TickRate);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{e.Definition.name} ×{e.Stacks}  {(e.IsInfinite ? "∞" : $"{left:0.0}s")}", GUILayout.Width(280f));
                if (canEdit && GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    _selected.RemoveEffect(e.Definition);
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }

            AttributeRegistry registry = AttributeRegistry.Instance;
            if (!canEdit || registry == null) return;

            GUILayout.Label("Apply:");
            GUILayout.BeginHorizontal();
            int col = 0;
            foreach (EffectDefinition effect in registry.AllEffects)
            {
                if (effect == null) continue;
                if (GUILayout.Button(effect.name, GUILayout.Width(120f)))
                    _selected.ApplyEffect(effect);
                if (++col % 4 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            GUILayout.EndHorizontal();
        }

#if UNITY_EDITOR
        private static void SaveBaseToProfile(AttributesComponent target)
        {
            AttributeProfile profile = target.Profile;
            AttributeSet set = target.Set;
            if (profile == null || set == null) return;

            AttributeLayout layout = set.Layout;
            int saved = 0;
            for (int slot = 0; slot < layout.Count; slot++)
            {
                float current = set.GetSlotBase(slot);
                if (Mathf.Approximately(current, layout.BaseValues[slot])) continue;
                profile.EditorSetBaseValue(layout.Definitions[slot], current);
                saved++;
            }

            UnityEditor.EditorUtility.SetDirty(profile);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(profile);
            Debug.Log($"[Attributes] Saved {saved} base value(s) to '{profile.name}'. " +
                      "Note: per-instance overrides on the component are applied on top.", profile);
        }
#endif
    }
}
