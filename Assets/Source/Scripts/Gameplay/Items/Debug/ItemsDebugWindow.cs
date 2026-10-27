#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// In-scene debug window for physical items (F8 toggles; frees the cursor while open).
    /// Every WorldItem: mass, integrity, holder / container / broken, sips; container fill.
    /// Host: spawn any catalog item in front of the local crew member, give an item into its hands, damage −25%, break, refill a
    /// bottle, despawn. Item tuning (mass, integrity, impact damage, slots, sips…) lives in the item
    /// profiles — edit / reset / save them in the F6 attributes window.
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public sealed class ItemsDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Rect windowRect = new Rect(16f, 16f, 560f, 600f);
        [SerializeField] private ItemCatalog catalog;

        private const int WindowId = 0x17E3D3B6;
        private Vector2 _scroll;

        public void SetCatalog(ItemCatalog value) => catalog = value;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame)
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
                GUI.Label(new Rect(Screen.width - 190f, Screen.height - 146f, 180f, 22f), "F8 — Items debug");
                return;
            }
            windowRect = GUI.Window(WindowId, windowRect, Draw, "Items — F8");
        }

        private void Draw(int id)
        {
            NetworkRunner runner = Runner();
            bool host = runner != null && runner.IsServer;
            NetworkPlayer local = LocalCrew();

            if (host && catalog != null && local != null)
            {
                GUILayout.Label("<b>Spawn in front of you</b>");
                GUILayout.BeginHorizontal();
                int col = 0;
                foreach (ItemCatalog.Entry e in catalog.Items)
                {
                    if (e.Prefab == null) continue;
                    if (GUILayout.Button(e.Id, GUILayout.Width(100f)))
                    {
                        Vector3 pos = local.transform.position + Vector3.up * 1.2f + local.transform.forward * 1.6f;
                        runner.Spawn(e.Prefab, pos, Quaternion.Euler(0f, local.ViewYaw, 0f));
                    }
                    if (++col % 5 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                }
                GUILayout.EndHorizontal();
            }

            if (local != null)
            {
                WorldItem held = local.CarriedItem;
                GUILayout.Label($"you: {(held != null ? $"carrying {held.DisplayName} ({held.Mass:0.#} kg, load {held.Mass / Mathf.Max(1f, local.CarryCapacity):P0})" : "hands free")}, capacity {local.CarryCapacity:0} kg");
            }

            GUILayout.Space(4f);
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = WorldItem.All.Count - 1; i >= 0; i--)
            {
                WorldItem item = WorldItem.All[i];
                if (item == null || item.Object == null || !item.Object.IsValid) continue;
                AttributesComponent a = item.Attributes;
                string integrity = a.Has(Attr.Integrity) ? $"{a.GetCurrent(Attr.Integrity):0}/{a.Get(Attr.Integrity):0}" : "—";
                string state = item.Broken ? "<color=#ff6655>BROKEN</color>" :
                               item.IsHeld ? $"held by {item.Holder}" :
                               item.IsStored ? "stored" : "free";
                string sips = a.Has(Attr.Sips) ? $"  sips {a.GetCurrent(Attr.Sips):0}" : "";
                string storage = item.TryGetComponent(out ItemStorage s) ? $"  [{s.Count}/{s.Capacity}]" : "";
                GUILayout.Label($"{item.DisplayName}  {item.Mass:0.#} kg  hp {integrity}  {state}{sips}{storage}");

                if (!host) continue;
                GUILayout.BeginHorizontal();
                if (a.Has(Attr.Integrity) && GUILayout.Button("−25%", GUILayout.Width(50f)))
                    a.Set.AddCurrent(Attr.Integrity, -a.Get(Attr.Integrity) * 0.25f);
                if (local != null && item.IsCarriable && !item.IsHeld && GUILayout.Button("Give", GUILayout.Width(50f)))
                    local.DebugGiveItem(item);
                if (GUILayout.Button("Break", GUILayout.Width(60f))) item.Break();
                if (a.Has(Attr.Sips) && GUILayout.Button("Refill", GUILayout.Width(60f)))
                    a.Set.SetCurrent(Attr.Sips, a.Get(Attr.Sips));
                if (GUILayout.Button("Despawn", GUILayout.Width(70f))) runner.Despawn(item.Object);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private static NetworkRunner Runner()
        {
            foreach (NetworkRunner r in NetworkRunner.Instances)
                if (r != null && r.IsRunning) return r;
            return null;
        }

        private static NetworkPlayer LocalCrew()
        {
            foreach (NetworkPlayer p in NetworkPlayer.All)
                if (p != null && p.Object != null && p.Object.IsValid && p.HasInputAuthority) return p;
            return null;
        }
    }
}
#endif
