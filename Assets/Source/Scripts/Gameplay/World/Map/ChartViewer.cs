#if FUSION2
using RumOverboard.Gameplay.World.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.World.Map
{
    /// <summary>
    /// Local close-up of the voyage chart: opens with E while looking at the <see cref="ChartTable"/>,
    /// closes with E / Esc, when walking away or getting knocked out. While open the player's input
    /// is neutral (<see cref="UiInputCapture"/>) and the cursor is free: wheel zooms, LMB drags.
    /// </summary>
    [DefaultExecutionOrder(1250)]
    public sealed class ChartViewer : MonoBehaviour
    {
        private const float CloseDistance = 3.6f;

        public NetworkWorld World;

        private bool _open;
        private int _openedFrame;
        private ChartTable _table;
        private float _zoom = 1f;
        private Vector2 _pan;
        private bool _dragging;
        private GUIStyle _label, _title, _hint;

        public bool IsOpen => _open;

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            PlayerCameraRig rig = PlayerCameraRig.Instance;
            if (!_open)
            {
                if (kb != null && kb.eKey.wasPressedThisFrame && rig != null && rig.Target != null &&
                    !rig.Target.IsKnockedOut && rig.Focus is ChartTable table && table.IsAvailable(rig.Target.LocalInteractor))
                    Open(table);
                return;
            }

            bool close = World == null || World.Map == null || rig == null || rig.Target == null || rig.Target.IsKnockedOut ||
                         _table == null || Vector3.Distance(rig.Target.RenderPosition, _table.transform.position) > CloseDistance;
            if (kb != null && Time.frameCount != _openedFrame && (kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                close = true;
            if (close)
            {
                Close();
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f)
                    _zoom = Mathf.Clamp(_zoom * (wheel > 0f ? 1.15f : 1f / 1.15f), 0.6f, 6f);
                if (mouse.leftButton.wasPressedThisFrame) _dragging = true;
                if (!mouse.leftButton.isPressed) _dragging = false;
                if (_dragging)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    _pan += new Vector2(delta.x, -delta.y);
                }
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Open(ChartTable table)
        {
            _open = true;
            _table = table;
            _openedFrame = Time.frameCount;
            _zoom = 1f;
            _pan = Vector2.zero;
            UiInputCapture.Active = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            if (!_open)
                return;
            _open = false;
            _table = null;
            _dragging = false;
            UiInputCapture.Active = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            if (_open)
                Close();
        }

        private void OnGUI()
        {
            if (!_open || World == null || World.Map == null || World.Map.Texture == null)
                return;
            EnsureStyles();
            WorldMapPainter map = World.Map;
            WorldLayout layout = World.Layout;
            GUI.depth = -100;

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float aspect = map.Width / (float)map.Height;
            float maxW = Screen.width * 0.9f, maxH = (Screen.height - 90f) * 0.95f;
            float w = maxW, h = maxW / aspect;
            if (h > maxH)
            {
                h = maxH;
                w = h * aspect;
            }
            w *= _zoom;
            h *= _zoom;
            var rect = new Rect((Screen.width - w) * 0.5f + _pan.x, (Screen.height - h) * 0.5f + 20f - _pan.y, w, h);
            GUI.DrawTexture(rect, map.Texture, ScaleMode.StretchToFill, false);

            if (layout != null)
            {
                int reached = Mathf.Clamp(World.Reached, 0, layout.Checkpoints.Count - 1);
                int visible = World.RevealAllLocal ? layout.Checkpoints.Count - 1 : Mathf.Min(reached + 1, layout.Checkpoints.Count - 1);
                for (int i = 0; i <= visible; i++)
                {
                    WorldCheckpoint cp = layout.Checkpoints[i];
                    Vector2 uv = map.WorldToUv(cp.IslandCenter);
                    var p = new Vector2(rect.x + uv.x * rect.width, rect.y + (1f - uv.y) * rect.height);
                    string name = i == 0 ? "Гавань отплытия" : i == layout.Checkpoints.Count - 1 ? "Последний остров" : $"Остров {i}";
                    if (i <= reached) name += " ✓";
                    else if (i == reached + 1) name += " — курс";
                    var r = new Rect(p.x - 90f, p.y - 34f * Mathf.Sqrt(_zoom) - 10f, 180f, 22f);
                    GUI.Label(r, name, _label);
                }
            }

            int total = layout != null ? layout.Checkpoints.Count - 1 : 0;
            string status = World.VoyageComplete
                ? "Плавание завершено — карта открыта полностью"
                : $"Открыто чекпоинтов: {Mathf.Clamp(World.Reached, 0, total)} из {total}";
            GUI.Label(new Rect(0f, 10f, Screen.width, 30f), $"Карта плавания — {status}", _title);
            GUI.Label(new Rect(0f, Screen.height - 34f, Screen.width, 24f),
                "E / Esc — убрать карту   ·   колесо — масштаб   ·   ЛКМ — двигать", _hint);
        }

        private void EnsureStyles()
        {
            if (_label != null)
                return;
            _label = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 14,
                normal = { textColor = new Color(0.25f, 0.12f, 0.05f) },
            };
            _title = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 20,
                normal = { textColor = new Color(0.95f, 0.88f, 0.7f) },
            };
            _hint = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 14,
                normal = { textColor = new Color(0.85f, 0.8f, 0.7f) },
            };
        }
    }
}
#endif
