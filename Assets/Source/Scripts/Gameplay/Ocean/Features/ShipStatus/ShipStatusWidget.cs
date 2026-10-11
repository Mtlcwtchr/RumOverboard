#if FUSION2
using System.Collections.Generic;
using System.Text;
using Fusion;
using RumOverboard.Gameplay.Ocean.Features.Rigging;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RumOverboard.Gameplay.Ocean.Features.ShipStatus
{
    /// <summary>
    /// Compact HUD panel (top-right) about the ship the local player is aboard:
    ///   • dial — the hull seen from above (bow up) with its yards at their brace angles, the true
    ///     wind arrow (and the apparent one) coming in from where it blows relative to the bow, and
    ///     the rudder blade;
    ///   • speed / heading, wind speed + point of sail and tack, rudder angle with turn direction;
    ///   • every sail: furled / being worked / set and hoist, yard angle;
    ///   • its lines: halyard and braces — on the pin / in hands / let go, with a tension bar.
    /// Local only, reads replicated state (<see cref="ShipStatusSnapshot"/>). Never takes input or
    /// raycasts; the toggle key (Tab) shows / hides it.
    /// </summary>
    public sealed class ShipStatusWidget : MonoBehaviour
    {
        public static ShipStatusWidget Instance { get; private set; }

        [SerializeField] private int sortingOrder = 45;

        private readonly ShipStatusSnapshot _snapshot = new();
        private readonly StringBuilder _sb = new();
        private NetworkPlayer _target;
        private bool _userVisible = true;
        private bool _userVisibleInit;

        private NetworkShip _ship;
        private Vector3 _lastPos;
        private Vector3 _velocity;
        private bool _hasPos;

        // UI
        private CanvasGroup _group;
        private RectTransform _panel;
        private Text _header;
        private Text _windText;
        private Text _helmText;
        private RectTransform _rudderFill;
        private Image _rudderFillImage;
        private RectTransform _dial;
        private RectTransform _shipFrame;
        private readonly RectTransform[] _compass = new RectTransform[4];
        private RectTransform _trueArrow;
        private RectTransform _apparentArrow;
        private RectTransform _rudderBlade;
        private RectTransform _sailsRoot;
        private readonly List<RectTransform> _yards = new();
        private readonly List<Image> _yardImages = new();
        private readonly List<SailRow> _sailRows = new();
        private Font _font;

        private sealed class SailRow
        {
            public GameObject Root;
            public Text Title;
            public GameObject ChipsRoot;
            public readonly List<Chip> Chips = new();
        }

        private sealed class Chip
        {
            public GameObject Root;
            public Text Label;
            public RectTransform Fill;
            public Image FillImage;
        }

        public ShipStatusSnapshot Snapshot => _snapshot;
        public NetworkShip Ship => _ship;
        public bool IsShowing => _panel != null && _panel.gameObject.activeSelf;
        /// <summary>Bearing (deg, clockwise from dial-up) the hull icon's bow points at.</summary>
        public float HullIconBearing => _shipFrame != null ? Mathf.Repeat(-_shipFrame.localEulerAngles.z, 360f) : 0f;
        public bool UserVisible { get => _userVisible; set { _userVisible = value; _userVisibleInit = true; } }

        public static ShipStatusWidget Ensure()
        {
            if (Instance != null)
                return Instance;
            var existing = FindAnyObjectByType<ShipStatusWidget>();
            if (existing != null)
                return existing;
            return new GameObject("ShipStatusWidget").AddComponent<ShipStatusWidget>();
        }

        public void SetTarget(NetworkPlayer target)
        {
            _target = target;
            _ship = null;
            _hasPos = false;
        }

        public void ClearTarget(NetworkPlayer target)
        {
            if (_target == target)
                SetTarget(null);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Build();
            _panel.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ---- Update ----------------------------------------------------------------------------
        private void Update()
        {
            ShipStatusConfig cfg = ShipStatusConfig.Active;
            if (!_userVisibleInit)
            {
                _userVisible = cfg.Visible;
                _userVisibleInit = true;
            }

            var kb = Keyboard.current;
            if (kb != null && !UiInputCapture.BusyThisFrame && cfg.ToggleKey != Key.None &&
                kb[cfg.ToggleKey].wasPressedThisFrame)
                _userVisible = !_userVisible;

            NetworkShip ship = ResolveShip();
            if (ship != _ship)
            {
                _ship = ship;
                _hasPos = false;
                _velocity = Vector3.zero;
            }

            bool show = _userVisible && _ship != null && !UiInputCapture.Active;
            if (_panel.gameObject.activeSelf != show)
                _panel.gameObject.SetActive(show);
            if (_ship == null)
                return;

            TrackVelocity(cfg);
            if (!show)
                return;

            _group.alpha = cfg.Opacity;
            _panel.localScale = Vector3.one * cfg.Scale;
            _panel.anchoredPosition = cfg.Offset;

            float time = _ship.Runner != null ? _ship.Runner.SimulationTime : Time.time;
            _snapshot.Capture(_ship, _velocity, time);
            Refresh(cfg);
        }

        private NetworkShip ResolveShip()
        {
            if (_target == null || _target.Object == null || !_target.Object.IsValid || _target.Runner == null)
                return null;
            NetworkId platform = _target.Platform;
            if (!platform.IsValid)
                return null;
            if (_ship != null && _ship.Object != null && _ship.Object.IsValid && _ship.Object.Id == platform)
                return _ship;
            return _target.Runner.TryFindObject(platform, out NetworkObject obj) && obj != null
                ? obj.GetComponent<NetworkShip>()
                : null;
        }

        // Proxies' hulls are kinematic: difference the rendered pose (works the same everywhere).
        private void TrackVelocity(ShipStatusConfig cfg)
        {
            Vector3 pos = _ship.transform.position;
            float dt = Time.deltaTime;
            if (_hasPos && dt > 1e-4f)
            {
                Vector3 raw = (pos - _lastPos) / dt;
                if (raw.sqrMagnitude < 60f * 60f) // ignore teleports
                    _velocity = Vector3.Lerp(_velocity, raw, 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, cfg.SpeedSmoothing)));
            }
            _lastPos = pos;
            _hasPos = true;
        }

        // ---- Refresh ---------------------------------------------------------------------------
        private static readonly Color Gold = new(1f, 0.82f, 0.32f);
        private static readonly Color Sky = new(0.55f, 0.82f, 1f);
        private static readonly Color Slack = new(0.55f, 0.62f, 0.72f);
        private static readonly Color Taut = new(0.45f, 0.86f, 0.45f);
        private static readonly Color Strain = new(0.95f, 0.35f, 0.25f);

        private void Refresh(ShipStatusConfig cfg)
        {
            ShipStatusSnapshot s = _snapshot;

            _header.text = $"<b>КОРАБЛЬ</b>   {s.SpeedKnots:0.0} уз   курс {s.HeadingDeg:000}°";

            // Wind.
            _sb.Clear();
            if (s.TrueWindSpeed > 0.2f)
            {
                _sb.Append($"Ветер <b>{s.TrueWindSpeed:0.0} м/с</b> с {CompassName(s.HeadingDeg + s.TrueWindFrom)}\n");
                _sb.Append($"<color=#FFD152>{s.PointOfSail}</color>, {s.Tack}\n");
                _sb.Append($"дует {SideText(s.TrueWindFrom)}");
                if (cfg.ShowApparentWind)
                    _sb.Append($"\n<color=#8CD1FF>вымпел. {s.ApparentWindSpeed:0.0} м/с, {SideShort(s.ApparentWindFrom)}</color>");
            }
            else
            {
                _sb.Append("Ветер: <b>штиль</b>");
            }
            _windText.text = _sb.ToString();

            // Rudder.
            if (s.HasHelm)
            {
                string dir = s.TurnDirection > 0f ? "→ вправо" : s.TurnDirection < 0f ? "← влево" : "прямо";
                _helmText.text = $"Руль <b>{Mathf.Abs(s.Rudder):0}°</b> {dir}\n<size=12>штурвал {(s.HelmManned ? "в руках" : "брошен")}, {Mathf.RoundToInt(s.Wheel * 100f):+0;-0;0}%</size>";
                float k = Mathf.Clamp(s.Rudder / s.RudderMax, -1f, 1f) * (s.TurnDirection != 0f ? Mathf.Sign(s.TurnDirection) * Mathf.Sign(s.Rudder) : 1f);
                _rudderFill.anchorMin = new Vector2(Mathf.Min(0.5f, 0.5f + k * 0.5f), 0f);
                _rudderFill.anchorMax = new Vector2(Mathf.Max(0.5f, 0.5f + k * 0.5f), 1f);
                _rudderFillImage.color = Mathf.Abs(k) > 0.85f ? Strain : Gold;
                _rudderBlade.localEulerAngles = new Vector3(0f, 0f, s.TurnDirection * Mathf.Abs(s.Rudder));
            }
            else
            {
                _helmText.text = "Руль: —";
                _rudderFill.anchorMin = _rudderFill.anchorMax = new Vector2(0.5f, 0f);
            }

            // Dial: north-up compass, the hull turns with the heading (or bow-up with a turning compass).
            float dialHeading = cfg.NorthUp ? s.HeadingDeg : 0f;   // bearing that points up on the dial
            float compassTurn = cfg.NorthUp ? 0f : -s.HeadingDeg;
            _shipFrame.localEulerAngles = new Vector3(0f, 0f, -dialHeading); // uGUI +Z is counter-clockwise
            for (int i = 0; i < _compass.Length; i++)
            {
                float r = (i * 90f + compassTurn) * Mathf.Deg2Rad;
                _compass[i].anchoredPosition = new Vector2(Mathf.Sin(r), Mathf.Cos(r)) * 52f;
            }
            PlaceArrow(_trueArrow, s.TrueWindFrom + dialHeading, 54f, s.TrueWindSpeed > 0.2f);
            PlaceArrow(_apparentArrow, s.ApparentWindFrom + dialHeading, 44f, cfg.ShowApparentWind && s.ApparentWindSpeed > 0.2f);
            RefreshYards();

            // Sails + lines.
            _sailsRoot.gameObject.SetActive(cfg.ShowSails);
            if (cfg.ShowSails)
                RefreshSails(cfg);
        }

        private static readonly string[] CompassPoints = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };

        /// <summary>8-point compass name for a bearing (deg clockwise from north).</summary>
        public static string CompassName(float bearing) =>
            CompassPoints[Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8];

        private static string SideText(float from)
        {
            float a = Mathf.Abs(from);
            if (a < 5f) return "прямо в нос";
            if (a > 175f) return "прямо в корму";
            return $"{a:0}° {(from > 0f ? "справа" : "слева")} от носа";
        }

        private static string SideShort(float from)
        {
            float a = Mathf.Abs(from);
            if (a < 5f) return "в нос";
            if (a > 175f) return "в корму";
            return $"{a:0}° {(from > 0f ? "справа" : "слева")}";
        }

        private static void PlaceArrow(RectTransform arrow, float fromDeg, float radius, bool visible)
        {
            if (arrow.gameObject.activeSelf != visible)
                arrow.gameObject.SetActive(visible);
            if (!visible) return;
            float r = fromDeg * Mathf.Deg2Rad;
            arrow.anchoredPosition = new Vector2(Mathf.Sin(r), Mathf.Cos(r)) * radius;
            // uGUI +Z is counter-clockwise; the sprite points up → turn it to point at the centre.
            arrow.localEulerAngles = new Vector3(0f, 0f, -(fromDeg + 180f));
        }

        private void RefreshYards()
        {
            ShipSailsAggregator sails = _ship.Sails;
            int count = _snapshot.Sails.Count;
            while (_yards.Count < count)
            {
                Image img = NewGraphic<Image>($"Yard{_yards.Count}", _shipFrame);
                img.sprite = Sprites.Box;
                _yards.Add(img.rectTransform);
                _yardImages.Add(img);
            }
            for (int i = 0; i < _yards.Count; i++)
            {
                bool on = i < count && sails != null && sails.TryGetFlatSail(i, out _, out _);
                if (_yards[i].gameObject.activeSelf != on)
                    _yards[i].gameObject.SetActive(on);
                if (!on) continue;
                sails.TryGetFlatSail(i, out var mast, out int local);
                var panel = mast.Sails[local];
                Vector3 pivot = (panel.topLeftLocal + panel.topRightLocal) * 0.5f;
                float half = Mathf.Abs(panel.topRightLocal.x - panel.topLeftLocal.x) * 0.5f;
                const float pxPerM = 3.1f;
                _yards[i].anchoredPosition = new Vector2(pivot.x * pxPerM, pivot.z * pxPerM);
                _yards[i].sizeDelta = new Vector2(half * 2f * pxPerM + 6f, 3f);
                _yards[i].localEulerAngles = new Vector3(0f, 0f, -_snapshot.Sails[i].YardAngle);
                var st = _snapshot.Sails[i];
                _yardImages[i].color = st.Furl >= 0.5f
                    ? new Color(0.62f, 0.5f, 0.36f)
                    : Color.Lerp(new Color(0.85f, 0.8f, 0.7f), Color.white, st.Hoist);
            }
        }

        private void RefreshSails(ShipStatusConfig cfg)
        {
            int count = _snapshot.Sails.Count;
            while (_sailRows.Count < count)
                _sailRows.Add(NewSailRow(_sailRows.Count));
            for (int i = 0; i < _sailRows.Count; i++)
            {
                SailRow row = _sailRows[i];
                bool on = i < count;
                if (row.Root.activeSelf != on) row.Root.SetActive(on);
                if (!on) continue;

                var st = _snapshot.Sails[i];
                string stateColor = st.FurlInProgress ? "#FFD152" : st.Furl >= 0.5f ? "#B0A48F" : "#9BE39B";
                row.Title.text = $"<b>{Cap(st.Name)}</b>  <color={stateColor}>{st.State}</color>  <color=#9AA7B5>рей {st.YardAngle:+0;-0;0}°</color>";

                bool showLines = cfg.ShowLines;
                if (row.ChipsRoot.activeSelf != showLines) row.ChipsRoot.SetActive(showLines);
                if (!showLines) continue;

                int c = 0;
                foreach (RigLineKind kind in KindOrder)
                {
                    foreach (var line in _snapshot.Lines)
                    {
                        if (line.Sail != i || line.Kind != kind) continue;
                        if (c >= row.Chips.Count) row.Chips.Add(NewChip(row.ChipsRoot.transform, c));
                        Chip chip = row.Chips[c++];
                        if (!chip.Root.activeSelf) chip.Root.SetActive(true);
                        string modeColor = line.Mode == RigLineMode.Tied ? "#C9D3DD" : line.Mode == RigLineMode.Held ? "#FFD152" : "#FF8A70";
                        bool strained = line.TensionText == "перегруз";
                        string modeShort = line.Mode == RigLineMode.Tied ? "закр." : line.ModeText;
                        chip.Label.text = $"<b>{line.Short}</b> <color={modeColor}>{modeShort}</color>";
                        float t = Mathf.Clamp01(line.Tension);
                        chip.Fill.anchorMax = new Vector2(Mathf.Max(0.04f, t), 1f);
                        chip.FillImage.color = strained ? Strain : Color.Lerp(Slack, Taut, t);
                    }
                }
                for (int k = c; k < row.Chips.Count; k++)
                    if (row.Chips[k].Root.activeSelf) row.Chips[k].Root.SetActive(false);
            }
        }

        private static readonly RigLineKind[] KindOrder = { RigLineKind.Halyard, RigLineKind.BracePort, RigLineKind.BraceStarboard };

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        // ---- Construction (runtime, no prefab) -------------------------------------------------
        private void Build()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) gameObject.layer = uiLayer;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // Panel: top-right, grows downward with its content.
            var bg = NewGraphic<Image>("Panel", transform);
            bg.color = new Color(0.04f, 0.06f, 0.09f, 0.72f);
            _panel = bg.rectTransform;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(1f, 1f);
            _panel.sizeDelta = new Vector2(390f, 10f);
            var layout = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 10);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = bg.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _header = NewText("Header", _panel, 18, TextAnchor.MiddleLeft);
            Height(_header.gameObject, 22f);

            // Dial + wind / rudder text side by side.
            var dialRow = NewRect("DialRow", _panel);
            Height(dialRow.gameObject, 132f);

            _dial = NewRect("Dial", dialRow);
            _dial.anchorMin = _dial.anchorMax = new Vector2(0f, 0.5f);
            _dial.pivot = new Vector2(0.5f, 0.5f);
            _dial.anchoredPosition = new Vector2(64f, 0f);
            _dial.sizeDelta = new Vector2(124f, 124f);

            var disk = NewGraphic<Image>("Disk", _dial);
            disk.sprite = Sprites.Disk;
            disk.color = new Color(0.1f, 0.2f, 0.3f, 0.55f);
            Size(disk.rectTransform, 124f, 124f);
            var ring = NewGraphic<Image>("Ring", _dial);
            ring.sprite = Sprites.Ring;
            ring.color = new Color(0.8f, 0.86f, 0.92f, 0.65f);
            Size(ring.rectTransform, 124f, 124f);
            string[] points = { "С", "В", "Ю", "З" };
            for (int i = 0; i < 4; i++)
            {
                var mark = NewText("Compass" + points[i], _dial, i == 0 ? 13 : 11, TextAnchor.MiddleCenter);
                mark.text = i == 0 ? "<b>С</b>" : points[i];
                mark.color = i == 0 ? new Color(1f, 0.45f, 0.4f, 0.95f) : new Color(1f, 1f, 1f, 0.6f);
                Size(mark.rectTransform, 20f, 16f);
                _compass[i] = mark.rectTransform;
            }

            // Everything fixed to the hull lives in the ship frame, which turns with the heading.
            _shipFrame = NewRect("ShipFrame", _dial);
            _shipFrame.sizeDelta = Vector2.zero;

            var hull = NewGraphic<Image>("Hull", _shipFrame);
            hull.sprite = Sprites.Hull;
            hull.color = new Color(0.55f, 0.38f, 0.22f, 0.95f);
            Size(hull.rectTransform, 22f, 66f);
            // Side lights: port red, starboard green — tell the sides apart whichever way she points.
            var portLight = NewGraphic<Image>("PortLight", _shipFrame);
            portLight.sprite = Sprites.Disk;
            portLight.color = new Color(1f, 0.3f, 0.25f, 0.95f);
            Size(portLight.rectTransform, 6f, 6f);
            portLight.rectTransform.anchoredPosition = new Vector2(-13f, 6f);
            var starLight = NewGraphic<Image>("StarboardLight", _shipFrame);
            starLight.sprite = Sprites.Disk;
            starLight.color = new Color(0.35f, 1f, 0.45f, 0.95f);
            Size(starLight.rectTransform, 6f, 6f);
            starLight.rectTransform.anchoredPosition = new Vector2(13f, 6f);

            var blade = NewRect("RudderPivot", _shipFrame);
            blade.anchoredPosition = new Vector2(0f, -32f);
            blade.sizeDelta = Vector2.zero;
            _rudderBlade = blade;
            var bladeImg = NewGraphic<Image>("Blade", blade);
            bladeImg.sprite = Sprites.Box;
            bladeImg.color = Gold;
            bladeImg.rectTransform.pivot = new Vector2(0.5f, 1f);
            bladeImg.rectTransform.anchoredPosition = Vector2.zero;
            bladeImg.rectTransform.sizeDelta = new Vector2(3f, 11f);

            _apparentArrow = NewArrow("ApparentWind", Sky, 12f);
            _trueArrow = NewArrow("TrueWind", Gold, 18f);

            var right = NewRect("Readouts", dialRow);
            right.anchorMin = new Vector2(0f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.offsetMin = new Vector2(134f, 0f);
            right.offsetMax = Vector2.zero;
            _windText = NewText("Wind", right, 14, TextAnchor.UpperLeft);
            _windText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(_windText.rectTransform);
            _windText.rectTransform.offsetMin = new Vector2(0f, 52f);

            _helmText = NewText("Helm", right, 14, TextAnchor.UpperLeft);
            _helmText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var hr = _helmText.rectTransform;
            hr.anchorMin = new Vector2(0f, 0f);
            hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(0f, 0f);
            hr.anchoredPosition = new Vector2(0f, 10f);
            hr.sizeDelta = new Vector2(0f, 40f);

            var rudderBg = NewGraphic<Image>("RudderBar", right);
            rudderBg.color = new Color(1f, 1f, 1f, 0.15f);
            var rb = rudderBg.rectTransform;
            rb.anchorMin = new Vector2(0f, 0f);
            rb.anchorMax = new Vector2(1f, 0f);
            rb.pivot = new Vector2(0.5f, 0f);
            rb.anchoredPosition = new Vector2(0f, 2f);
            rb.sizeDelta = new Vector2(-4f, 5f);
            _rudderFillImage = NewGraphic<Image>("Fill", rb);
            _rudderFill = _rudderFillImage.rectTransform;
            _rudderFill.offsetMin = _rudderFill.offsetMax = Vector2.zero;
            _rudderFill.anchorMin = _rudderFill.anchorMax = new Vector2(0.5f, 0f);
            var centre = NewGraphic<Image>("Centre", rb);
            centre.color = new Color(1f, 1f, 1f, 0.7f);
            centre.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            centre.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            centre.rectTransform.sizeDelta = new Vector2(2f, 4f);

            // Sails (rows created as the ship's sails are found).
            _sailsRoot = NewRect("Sails", _panel);
            var sl = _sailsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            sl.spacing = 3f;
            sl.childControlWidth = true;
            sl.childControlHeight = true;
            sl.childForceExpandWidth = true;
            sl.childForceExpandHeight = false;
        }

        private SailRow NewSailRow(int index)
        {
            var row = new SailRow();
            RectTransform root = NewRect($"Sail{index}", _sailsRoot);
            var v = root.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 1f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            row.Root = root.gameObject;

            row.Title = NewText("Title", root, 15, TextAnchor.MiddleLeft);
            Height(row.Title.gameObject, 20f);

            RectTransform chips = NewRect("Lines", root);
            var h = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            Height(chips.gameObject, 22f);
            row.ChipsRoot = chips.gameObject;
            return row;
        }

        private Chip NewChip(Transform parent, int index)
        {
            var chip = new Chip();
            var bg = NewGraphic<Image>($"Chip{index}", parent);
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            chip.Root = bg.gameObject;

            chip.Label = NewText("Label", bg.rectTransform, 13, TextAnchor.UpperLeft);
            Stretch(chip.Label.rectTransform);
            chip.Label.rectTransform.offsetMin = new Vector2(3f, 6f);
            chip.Label.rectTransform.offsetMax = new Vector2(-2f, 0f);

            var barBg = NewGraphic<Image>("Bar", bg.rectTransform);
            barBg.color = new Color(0f, 0f, 0f, 0.45f);
            var rt = barBg.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(-6f, 4f);
            chip.FillImage = NewGraphic<Image>("Fill", rt);
            chip.Fill = chip.FillImage.rectTransform;
            chip.Fill.anchorMin = Vector2.zero;
            chip.Fill.anchorMax = new Vector2(0.5f, 1f);
            chip.Fill.offsetMin = chip.Fill.offsetMax = Vector2.zero;
            return chip;
        }

        private RectTransform NewArrow(string name, Color color, float size)
        {
            var img = NewGraphic<Image>(name, _dial);
            img.sprite = Sprites.Arrow;
            img.color = color;
            Size(img.rectTransform, size * 0.8f, size);
            return img.rectTransform;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return (RectTransform)go.transform;
        }

        private static T NewGraphic<T>(string name, Transform parent) where T : Graphic
        {
            RectTransform rt = NewRect(name, parent);
            var g = rt.gameObject.AddComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        private Text NewText(string name, Transform parent, int size, TextAnchor anchor)
        {
            Text t = NewGraphic<Text>(name, parent);
            t.font = _font;
            t.fontSize = size;
            t.alignment = anchor;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = Color.white;
            return t;
        }

        private static void Height(GameObject go, float h)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = h;
            le.preferredHeight = h;
        }

        private static void Size(RectTransform rt, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ---- Procedural sprites (no texture assets) ---------------------------------------------
        private static class Sprites
        {
            private static Sprite _disk, _ring, _arrow, _hull, _box;

            public static Sprite Disk => _disk != null ? _disk : _disk = Make(128, 128, (x, y) => Circle(x, y, 0.98f, 1f));
            public static Sprite Ring => _ring != null ? _ring : _ring = Make(128, 128, (x, y) => Circle(x, y, 0.98f, 1f) * (1f - Circle(x, y, 0.94f, 1f)));
            public static Sprite Box => _box != null ? _box : _box = Make(4, 4, (x, y) => 1f);

            // Arrow pointing up (+y): a head over a short shaft.
            public static Sprite Arrow => _arrow != null ? _arrow : _arrow = Make(32, 40, (x, y) =>
            {
                float u = Mathf.Abs(x * 2f - 1f);
                if (y > 0.45f) return Edge((1f - y) / 0.55f - u, 0.04f);
                return Edge(0.22f - u, 0.04f);
            });

            // Hull from above, bow at the top.
            public static Sprite Hull => _hull != null ? _hull : _hull = Make(32, 96, (x, y) =>
            {
                float u = Mathf.Abs(x * 2f - 1f);
                float w = y > 0.62f
                    ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((y - 0.62f) / 0.38f, 2f)))
                    : Mathf.Lerp(0.72f, 1f, Mathf.Sqrt(Mathf.Clamp01(y / 0.62f)));
                if (y < 0.03f) return 0f;
                return Edge(w - u, 0.06f);
            });

            private static float Circle(float x, float y, float r, float soft)
            {
                float dx = x * 2f - 1f, dy = y * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01((r - d) / 0.02f * soft);
            }

            private static float Edge(float signedDistance, float softness) => Mathf.Clamp01(signedDistance / softness);

            private static Sprite Make(int w, int h, System.Func<float, float, float> alpha)
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a = Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}
#endif
