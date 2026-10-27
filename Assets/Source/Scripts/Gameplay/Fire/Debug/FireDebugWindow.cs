#if FUSION2
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Fire
{
    /// <summary>
    /// In-scene fire debug window (F9 toggles; frees the cursor while open).
    /// Every peer: the items that burn / are wet / heat up, and what this peer has shown (flames, blasts, splashes).
    /// Host: per item Ignite / Put out / Explode / Break, Ignite all / Put out all, refill buckets and casks,
    /// live FireConfig tuning (runtime copy), Reset, Save to config (the FireConfig asset).
    /// Per-type values (ignition time, spread radius, blast…) are profile data blocks — edit them in the profile.
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class FireDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Rect windowRect = new Rect(16f, 300f, 520f, 560f);

        private const int WindowId = 0x0F12E009;
        private Vector2 _scroll;
        private bool _showAll;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame)
            {
                visible = !visible;
                Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = visible;
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (!visible)
            {
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 166f, 180f, 22f), "F9 — Fire debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Fire — F9");
        }

        private void Draw(int id)
        {
            FireDirector director = FireDirector.Instance;
            bool host = director != null && director.Runner != null && director.Runner.IsServer;
            FireVisuals fx = FireVisuals.Instance;

            if (host)
                GUILayout.Label($"host: ignitions {director.Ignitions} · put out {director.Extinguished} · " +
                                $"explosions {director.Explosions} · splashes {director.Splashes}");
            if (fx != null)
                GUILayout.Label($"this peer shows: flames {fx.FlamesShown} · blasts {fx.ExplosionsShown} · " +
                                $"splashes {fx.SplashesShown} · steam {fx.SteamShown}");

            if (host)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Ignite all")) foreach (WorldItem i in WorldItem.All) director.Ignite(i);
                if (GUILayout.Button("Put out all")) foreach (WorldItem i in WorldItem.All) if (Valid(i)) director.Extinguish(i, soak: false);
                if (GUILayout.Button("Refill water")) RefillWater();
                GUILayout.EndHorizontal();
            }
            _showAll = GUILayout.Toggle(_showAll, "show every item (not only flammable / fire items)");

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (WorldItem item in WorldItem.All)
            {
                if (!Valid(item) || item.Broken) continue;
                AttributesComponent a = item.Attributes;
                bool relevant = a.HasFlag(AttrFlag.Flammable) || a.HasFlag(AttrFlag.Burning) || a.HasFlag(AttrFlag.Wet) ||
                                a.GetData<BucketData>() != null || a.GetData<WaterSourceData>() != null ||
                                a.GetData<LanternData>() != null;
                if (!relevant && !_showAll) continue;
                DrawItem(item, director, host);
            }
            GUILayout.EndScrollView();

            if (host) DrawTuning(director);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private static void DrawItem(WorldItem item, FireDirector director, bool host)
        {
            AttributesComponent a = item.Attributes;
            string state = a.HasFlag(AttrFlag.Burning) ? "<color=#ff7733>BURNING</color>"
                         : a.HasFlag(AttrFlag.Wet) ? "<color=#66aaff>wet</color>"
                         : a.HasFlag(AttrFlag.Flammable) ? "dry" : "—";
            string integrity = a.Has(Attr.Integrity) ? $" hp {a.GetCurrent(Attr.Integrity):0.0}/{a.Get(Attr.Integrity):0}" : "";
            string water = a.Has(Attr.Water) ? $" water {a.GetCurrent(Attr.Water):0.#}/{a.Get(Attr.Water):0}" : "";
            string heat = host && director.HeatOf(item) > 0f ? $" heat {director.HeatOf(item):0.00}" : "";
            float fuse = host ? director.FuseLeft(item) : -1f;
            string fuseText = fuse >= 0f ? $" <color=#ff4444>fuse {fuse:0.0}s</color>" : "";

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{item.DisplayName} [{item.Object.Id.Raw}] {state}{integrity}{water}{heat}{fuseText}", GUILayout.Width(330f));
            if (host)
            {
                if (GUILayout.Button("Ignite", GUILayout.Width(52f))) director.Ignite(item);
                if (GUILayout.Button("Out", GUILayout.Width(36f))) director.Extinguish(item, soak: true);
                if (a.GetData<ExplosiveData>() != null && GUILayout.Button("Boom", GUILayout.Width(46f))) director.Explode(item);
                else if (GUILayout.Button("Break", GUILayout.Width(46f))) item.Break();
            }
            GUILayout.EndHorizontal();
        }

        private static void RefillWater()
        {
            foreach (WorldItem i in WorldItem.All)
                if (Valid(i) && i.HasStateAuthority && i.Attributes.Has(Attr.Water))
                    i.Attributes.Set.SetCurrent(Attr.Water, i.Attributes.Get(Attr.Water));
        }

        private static void DrawTuning(FireDirector director)
        {
            FireConfig c = director.Config;
            GUILayout.Space(4f);
            GUILayout.Label("<b>Tuning (FireConfig — runtime copy)</b>");
            c.BurnDamagePerSecond = Slider("Burn damage /s", c.BurnDamagePerSecond, 0f, 30f);
            c.SpreadMultiplier = Slider("Spread ×", c.SpreadMultiplier, 0f, 5f);
            c.HeatDecayPerSecond = Slider("Heat decay /s", c.HeatDecayPerSecond, 0f, 2f);
            c.DefaultSpreadRadius = Slider("Default spread m", c.DefaultSpreadRadius, 0f, 5f);
            c.DefaultIgnitionTime = Slider("Default ignition s", c.DefaultIgnitionTime, 0.1f, 15f);
            c.SubmergedToQuench = Slider("Submerged to quench", c.SubmergedToQuench, 0.05f, 1f);
            c.WaterPerSplash = Slider("Water per splash", c.WaterPerSplash, 0.1f, 5f);
            c.FlameSize = Slider("Flame size ×", c.FlameSize, 0.2f, 3f);
            c.FlameLightIntensity = Slider("Flame light", c.FlameLightIntensity, 0f, 10f);
            c.FlameLightRange = Slider("Flame light range", c.FlameLightRange, 1f, 15f);
            c.ExplosionLightIntensity = Slider("Blast light", c.ExplosionLightIntensity, 0f, 60f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to config")) director.ResetConfig();
#if UNITY_EDITOR
            if (director.Asset != null && GUILayout.Button("Save to config"))
            {
                director.Asset.CopyFrom(c);
                director.Asset.name = director.Asset.name.Replace(" (runtime)", "");
                UnityEditor.EditorUtility.SetDirty(director.Asset);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(director.Asset);
                Debug.Log($"[Fire] Saved tuning to '{UnityEditor.AssetDatabase.GetAssetPath(director.Asset)}'.", director.Asset);
            }
#endif
            GUILayout.EndHorizontal();
        }

        private static bool Valid(WorldItem i) => i != null && i.Object != null && i.Object.IsValid && i.Attributes.Set != null;

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:F2}", GUILayout.Width(200f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
#endif
