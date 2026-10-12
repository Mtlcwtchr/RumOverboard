#if FUSION2
using System.Text;
using RumOverboard.Gameplay.Ocean.Features.ShipStatus;
using RumOverboard.Gameplay.World.Generation;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RumOverboard.Gameplay.World.Map
{
    /// <summary>
    /// Local close-up of the voyage chart: opens with E while looking at the <see cref="ChartTable"/>,
    /// closes with E / Esc, when walking away or getting knocked out. While open the player's input
    /// is neutral (<see cref="UiInputCapture"/>) and the cursor is free: wheel zooms, LMB drags.
    /// The ship is drawn on the chart (icon turned to its heading, dotted course to the next island)
    /// with a log panel: course, speed, wind, how well the sails are trimmed, rudder, next island.
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
        private GUIStyle _label, _title, _hint, _info;

        private NetworkShip _ship;
        private readonly ShipStatusSnapshot _snapshot = new();
        private readonly StringBuilder _sb = new();
        private Vector3 _lastPos, _velocity;
        private bool _hasPos;

        public bool IsOpen => _open;
        /// <summary>The ship whose chart table is open (null when closed).</summary>
        public NetworkShip Ship => _open ? _ship : null;
        /// <summary>Last ship read-out shown in the log panel.</summary>
        public ShipStatusSnapshot Snapshot => _snapshot;
        /// <summary>Screen position (GUI space) and rotation (deg) of the ship icon in the last OnGUI.</summary>
        public Vector2 IconScreenPosition { get; private set; }
        public float IconRotation { get; private set; }
        /// <summary>The log panel's text as last drawn (rich text).</summary>
        public string InfoText { get; private set; } = string.Empty;

        public static string CheckpointName(int index, int count) =>
            index == 0 ? "Гавань отплытия" : index == count - 1 ? "Последний остров" : $"Остров {index}";

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
            CaptureShip();
        }

        private void CaptureShip()
        {
            if (_ship == null || _ship.Object == null || !_ship.Object.IsValid)
                return;
            // Proxies' hulls are kinematic: difference the rendered pose, like the status widget.
            Vector3 pos = _ship.transform.position;
            float dt = Time.deltaTime;
            if (_hasPos && dt > 1e-4f)
            {
                Vector3 raw = (pos - _lastPos) / dt;
                if (raw.sqrMagnitude < 60f * 60f)
                    _velocity = Vector3.Lerp(_velocity, raw, 1f - Mathf.Exp(-dt / 0.35f));
            }
            _lastPos = pos;
            _hasPos = true;
            _snapshot.Capture(_ship, _velocity, _ship.Runner != null ? _ship.Runner.SimulationTime : Time.time);
        }

        public void Open(ChartTable table)
        {
            _open = true;
            _table = table;
            _openedFrame = Time.frameCount;
            _zoom = 1f;
            _pan = Vector2.zero;
            _ship = table != null ? table.GetComponentInParent<NetworkShip>() : null;
            _hasPos = false;
            _velocity = _ship != null && _ship.Body != null && !_ship.Body.isKinematic ? _ship.Body.linearVelocity : Vector3.zero;
            CaptureShip();
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
            _ship = null;
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
                int count = layout.Checkpoints.Count;
                int reached = Mathf.Clamp(World.Reached, 0, count - 1);
                int visible = World.RevealAllLocal ? count - 1 : Mathf.Min(reached + 1, count - 1);
                for (int i = 0; i <= visible; i++)
                {
                    WorldCheckpoint cp = layout.Checkpoints[i];
                    Vector2 p = ToScreen(map, rect, cp.IslandCenter);
                    string name = CheckpointName(i, count);
                    if (i <= reached) name += " ✓";
                    else if (i == reached + 1) name += " — курс";
                    var r = new Rect(p.x - 90f, p.y - 34f * Mathf.Sqrt(_zoom) - 10f, 180f, 22f);
                    GUI.Label(r, name, _label);
                }
            }

            DrawShip(map, layout, rect);

            int total = layout != null ? layout.Checkpoints.Count - 1 : 0;
            string status = World.VoyageComplete
                ? "Плавание завершено — карта открыта полностью"
                : $"Открыто чекпоинтов: {Mathf.Clamp(World.Reached, 0, total)} из {total}";
            GUI.Label(new Rect(0f, 10f, Screen.width, 30f), $"Карта плавания — {status}", _title);
            GUI.Label(new Rect(0f, Screen.height - 34f, Screen.width, 24f),
                "E / Esc — убрать карту   ·   колесо — масштаб   ·   ЛКМ — двигать", _hint);
        }

        private static Vector2 ToScreen(WorldMapPainter map, Rect rect, Vector2 world)
        {
            Vector2 uv = map.WorldToUv(world);
            return new Vector2(rect.x + uv.x * rect.width, rect.y + (1f - uv.y) * rect.height);
        }

        private void DrawShip(WorldMapPainter map, WorldLayout layout, Rect rect)
        {
            if (_ship == null)
            {
                InfoText = string.Empty;
                return;
            }
            Vector3 pos = _ship.transform.position;
            var shipXZ = new Vector2(pos.x, pos.z);
            Vector2 p = ToScreen(map, rect, shipXZ);
            p.x = Mathf.Clamp(p.x, rect.xMin, rect.xMax);
            p.y = Mathf.Clamp(p.y, rect.yMin, rect.yMax);

            // Dotted course to the next island.
            int next = -1;
            if (layout != null && !World.VoyageComplete)
            {
                int n = Mathf.Clamp(World.Reached, 0, layout.Checkpoints.Count - 1) + 1;
                if (n < layout.Checkpoints.Count)
                    next = n;
            }
            float nextDist = 0f, nextBearing = 0f;
            if (next >= 0)
            {
                Vector2 target = layout.Checkpoints[next].IslandCenter;
                Vector2 d = target - shipXZ;
                nextDist = d.magnitude;
                nextBearing = Mathf.Repeat(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 360f);
                Vector2 q = ToScreen(map, rect, target);
                float len = Vector2.Distance(p, q);
                GUI.color = new Color(0.55f, 0.1f, 0.08f, 0.85f);
                for (float s = 18f; s < len - 14f; s += 12f)
                {
                    Vector2 dot = Vector2.Lerp(p, q, s / len);
                    GUI.DrawTexture(new Rect(dot.x - 2f, dot.y - 2f, 4f, 4f), Texture2D.whiteTexture);
                }
                GUI.color = Color.white;
            }

            // Ship icon, bow up at heading 0, turned clockwise by the heading.
            float heading = _snapshot.HeadingDeg;
            IconScreenPosition = p;
            IconRotation = heading;
            float size = 34f * Mathf.Clamp(Mathf.Sqrt(_zoom), 1f, 1.8f);
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(heading, p);
            GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), ShipIcon.Texture);
            GUI.matrix = m;

            InfoText = BuildInfo(layout, next, nextDist, nextBearing);
            float panelW = 330f;
            float panelH = _info.CalcHeight(new GUIContent(InfoText), panelW - 24f) + 20f;
            var panel = new Rect(16f, 52f, panelW, panelH);
            GUI.color = new Color(0.08f, 0.06f, 0.04f, 0.82f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.7f, 0.4f, 0.9f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 12f, panel.y + 10f, panelW - 24f, panelH - 20f), InfoText, _info);
        }

        private string BuildInfo(WorldLayout layout, int next, float nextDist, float nextBearing)
        {
            ShipStatusSnapshot s = _snapshot;
            _sb.Clear();
            _sb.Append("<b><size=17>Судовой журнал</size></b>\n");
            _sb.Append($"Курс: <b>{s.HeadingDeg:000}°</b> ({ShipStatusWidget.CompassName(s.HeadingDeg)})\n");
            _sb.Append($"Скорость: <b>{s.SpeedKnots:0.0} уз</b>\n");
            if (s.TrueWindSpeed > 0.2f)
            {
                _sb.Append($"Ветер: <b>{s.TrueWindSpeed:0.0} м/с</b> с {ShipStatusWidget.CompassName(s.HeadingDeg + s.TrueWindFrom)}\n");
                _sb.Append($"<color=#FFD152>{s.PointOfSail}</color>, {s.Tack}\n");
            }
            else
            {
                _sb.Append("Ветер: <b>штиль</b>\n");
            }

            if (s.SailEfficiency >= 0f)
            {
                _sb.Append($"Паруса ловят ветер: <color={ShipStatusWidget.TrimColor(s.SailEfficiency)}><b>{Mathf.RoundToInt(s.SailEfficiency * 100f)}%</b></color>");
                _sb.Append($"  <size=12>({s.SailsSet} из {s.Sails.Count} поставлены)</size>\n");
                foreach (var sail in s.Sails)
                {
                    if (sail.Area <= 0.5f || sail.Trim < 0f)
                        continue;
                    _sb.Append($"<size=13>   {sail.Name}: <color={ShipStatusWidget.TrimColor(sail.Trim)}>{Mathf.RoundToInt(sail.Trim * 100f)}%</color>, рей {sail.YardAngle:+0;-0;0}°</size>\n");
                }
            }
            else
            {
                _sb.Append("Паруса: <b>убраны</b>\n");
            }

            if (s.HasHelm)
            {
                string dir = s.TurnDirection > 0f ? "вправо" : s.TurnDirection < 0f ? "влево" : "прямо";
                _sb.Append($"Руль: <b>{Mathf.Abs(s.Rudder):0}°</b> {dir}{(s.HelmManned ? string.Empty : " <size=12>(штурвал брошен)</size>")}\n");
            }

            if (next >= 0)
            {
                string dist = nextDist >= 1000f ? $"{nextDist / 1000f:0.0} км" : $"{nextDist:0} м";
                _sb.Append($"До «{CheckpointName(next, layout.Checkpoints.Count)}»: <b>{dist}</b>, пеленг {nextBearing:000}° ({ShipStatusWidget.CompassName(nextBearing)})");
                float off = Mathf.DeltaAngle(s.HeadingDeg, nextBearing);
                if (Mathf.Abs(off) > 5f)
                    _sb.Append($"\n<size=13>   отвернуть {Mathf.Abs(off):0}° {(off > 0f ? "вправо" : "влево")}</size>");
                else
                    _sb.Append("\n<size=13>   идём точно на остров</size>");
            }
            else if (World.VoyageComplete)
            {
                _sb.Append("<color=#7EE07E>Плавание завершено</color>");
            }
            return _sb.ToString().TrimEnd('\n');
        }

        /// <summary>Procedural top-down hull sprite (bow up) used on the chart close-up.</summary>
        public static class ShipIcon
        {
            private static Texture2D _texture;

            // Convex hull outline in 0..1 sprite space, clockwise, bow at the top.
            private static readonly Vector2[] Outline =
            {
                new(0.5f, 0.96f), new(0.70f, 0.66f), new(0.71f, 0.30f), new(0.62f, 0.06f),
                new(0.38f, 0.06f), new(0.29f, 0.30f), new(0.30f, 0.66f),
            };

            public static Texture2D Texture
            {
                get
                {
                    if (_texture == null)
                        _texture = Build(64);
                    return _texture;
                }
            }

            private static Texture2D Build(int size)
            {
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "ChartShipIcon", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                var fill = new Color(0.55f, 0.12f, 0.08f);
                var rim = new Color(0.16f, 0.06f, 0.03f);
                var deck = new Color(0.85f, 0.7f, 0.45f);
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                        float d = InsideDistance(uv) * size; // px inside the outline (< 0 = outside)
                        Color c;
                        if (d < -1f) c = Color.clear;
                        else if (d < 2.5f) c = rim;
                        else c = fill;
                        // Keel stripe so the bow reads clearly.
                        if (d >= 2.5f && Mathf.Abs(uv.x - 0.5f) < 0.035f && uv.y > 0.2f && uv.y < 0.8f) c = deck;
                        c.a = Mathf.Clamp01(d + 1f) * (d < -1f ? 0f : 1f);
                        px[y * size + x] = c;
                    }
                tex.SetPixels(px);
                tex.Apply(false, false);
                return tex;
            }

            private static float InsideDistance(Vector2 p)
            {
                float min = float.MaxValue;
                for (int i = 0; i < Outline.Length; i++)
                {
                    Vector2 a = Outline[i], b = Outline[(i + 1) % Outline.Length];
                    Vector2 e = b - a;
                    var inward = new Vector2(e.y, -e.x).normalized; // clockwise → right-hand normal points in
                    min = Mathf.Min(min, Vector2.Dot(p - a, inward));
                }
                return min;
            }
        }

        private void EnsureStyles()
        {
            if (_label != null)
                return;
            _info = new GUIStyle(GUI.skin.label)
            {
                richText = true, wordWrap = true, fontSize = 15, alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.95f, 0.9f, 0.78f) },
            };
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
