using System.Collections.Generic;
using System.Text;
using RumOverboard.StateMachine;
using UnityEngine;
using UnityEngine.UI;

namespace RumOverboard.Gameplay.UI
{
    /// <summary>
    /// Local-only hint layer: crosshair, the look-at interaction prompt and a contextual controls
    /// panel driven by <see cref="ControlHintsConfig"/>.
    ///
    /// It can never get in the way of a raycast:
    ///   • its own Canvas has NO GraphicRaycaster and a CanvasGroup with blocksRaycasts = false;
    ///   • every Graphic is created with raycastTarget = false;
    ///   • nothing here has a Collider, and it's on the UI layer, which the view ray excludes.
    /// The interaction ray is a physics query from the camera, so screen UI can't occlude it either.
    /// </summary>
    public sealed class HintOverlay : MonoBehaviour
    {
        public static HintOverlay Instance { get; private set; }

        [SerializeField] private ControlHintsConfig config;
        [SerializeField] private int sortingOrder = 50;
        [SerializeField] private Color crosshairIdle = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] private Color crosshairActive = new Color(1f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color crosshairBlocked = new Color(1f, 0.35f, 0.3f, 0.9f);

        private Image _crosshair;
        private GameObject _promptRoot;
        private Text _promptKey;
        private Text _promptText;
        private Text _controls;
        private Image _drunkFill;
        private Text _drunkLabel;
        private float _lastDrunk = -1f;
        private readonly StringBuilder _sb = new();

        private string _lastPrompt;
        private bool _lastAvailable;
        private PlayerState _lastState = (PlayerState)uint.MaxValue;
        private bool _visible = true;

        public ControlHintsConfig Config
        {
            get
            {
                if (config == null)
                    config = ControlHintsConfig.CreateDefault();
                return config;
            }
            set => config = value;
        }

        public static HintOverlay Ensure()
        {
            if (Instance != null)
                return Instance;
            var existing = FindAnyObjectByType<HintOverlay>();
            if (existing != null)
                return existing;
            return new GameObject("HintOverlay").AddComponent<HintOverlay>();
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
            SetPrompt(null, false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_crosshair != null) _crosshair.enabled = visible;
            if (_controls != null) _controls.enabled = visible;
            if (_drunkFill != null) _drunkFill.transform.parent.parent.gameObject.SetActive(visible);
            if (!visible) SetPrompt(null, false);
        }

        /// <summary>Look-at prompt. <paramref name="text"/> null hides it.</summary>
        public void SetPrompt(string text, bool available)
        {
            if (!_visible) text = null;
            if (text == _lastPrompt && available == _lastAvailable && _promptRoot != null)
                return;
            _lastPrompt = text;
            _lastAvailable = available;

            bool show = !string.IsNullOrEmpty(text);
            _promptRoot.SetActive(show);
            if (show)
            {
                _promptKey.text = available ? Config.InteractKey : "—";
                _promptText.text = text;
                _promptText.color = available ? Color.white : new Color(1f, 0.6f, 0.55f);
            }

            if (_crosshair != null)
                _crosshair.color = !show ? crosshairIdle : available ? crosshairActive : crosshairBlocked;
        }

        /// <summary>Drunkenness 0..1 → bottom-left meter.</summary>
        public void SetDrunkenness(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Abs(value - _lastDrunk) < 0.002f || _drunkFill == null)
                return;
            _lastDrunk = value;
            _drunkFill.rectTransform.anchorMax = new Vector2(value, 1f);
            _drunkFill.color = Color.Lerp(new Color(0.45f, 0.85f, 0.4f), new Color(0.95f, 0.3f, 0.2f), value);
            _drunkLabel.text = $"НАБУХАННОСТЬ  {Mathf.RoundToInt(value * 100f)}%";
        }

        private void BuildDrunkMeter(Font font)
        {
            var root = new GameObject("DrunkMeter", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(24f, 24f);
            rt.sizeDelta = new Vector2(320f, 56f);

            _drunkLabel = NewText("Label", root.transform, font, 18, TextAnchor.UpperLeft);
            _drunkLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            _drunkLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _drunkLabel.rectTransform.pivot = new Vector2(0f, 1f);
            _drunkLabel.rectTransform.sizeDelta = new Vector2(0f, 24f);
            _drunkLabel.rectTransform.anchoredPosition = Vector2.zero;
            _drunkLabel.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);

            var back = NewGraphic<Image>("Back", root.transform);
            back.color = new Color(0f, 0f, 0f, 0.5f);
            back.rectTransform.anchorMin = Vector2.zero;
            back.rectTransform.anchorMax = new Vector2(1f, 0f);
            back.rectTransform.pivot = Vector2.zero;
            back.rectTransform.sizeDelta = new Vector2(0f, 20f);
            back.rectTransform.anchoredPosition = Vector2.zero;

            _drunkFill = NewGraphic<Image>("Fill", back.transform);
            _drunkFill.rectTransform.anchorMin = Vector2.zero;
            _drunkFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            _drunkFill.rectTransform.offsetMin = new Vector2(2f, 2f);
            _drunkFill.rectTransform.offsetMax = new Vector2(-2f, -2f);
            SetDrunkenness(0f);
        }

        public void SetState(PlayerState active)
        {
            if (active == _lastState)
                return;
            _lastState = active;

            _sb.Clear();
            IReadOnlyList<ControlHint> hints = Config.HintsFor(active);
            if (hints != null)
                for (int i = 0; i < hints.Count; i++)
                {
                    if (i > 0) _sb.Append('\n');
                    _sb.Append("<b>[").Append(hints[i].Key).Append("]</b>  ").Append(hints[i].Text);
                }
            _controls.text = _sb.ToString();
        }

        // ---- UI construction (runtime, no prefab needed) --------------------------------------
        private void Build()
        {
            gameObject.layer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : gameObject.layer;

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Crosshair dot.
            _crosshair = NewGraphic<Image>("Crosshair", transform);
            Place(_crosshair.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
            _crosshair.color = crosshairIdle;

            // Prompt: [E] text, just below the crosshair.
            _promptRoot = new GameObject("Prompt", typeof(RectTransform));
            _promptRoot.transform.SetParent(transform, false);
            Place((RectTransform)_promptRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(640f, 46f));

            var bg = NewGraphic<Image>("Bg", _promptRoot.transform);
            Stretch(bg.rectTransform);
            bg.color = new Color(0f, 0f, 0f, 0.45f);

            var keyBg = NewGraphic<Image>("KeyBg", _promptRoot.transform);
            Place(keyBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(40f, 34f));
            keyBg.color = new Color(1f, 0.85f, 0.3f, 0.95f);

            _promptKey = NewText("Key", keyBg.transform, font, 22, TextAnchor.MiddleCenter);
            Stretch(_promptKey.rectTransform);
            _promptKey.color = Color.black;
            _promptKey.fontStyle = FontStyle.Bold;

            _promptText = NewText("Text", _promptRoot.transform, font, 24, TextAnchor.MiddleLeft);
            Stretch(_promptText.rectTransform);
            _promptText.rectTransform.offsetMin = new Vector2(62f, 0f);

            // Controls panel, bottom-left.
            _controls = NewText("Controls", transform, font, 20, TextAnchor.LowerLeft);
            _controls.supportRichText = true;
            _controls.color = new Color(1f, 1f, 1f, 0.85f);
            RectTransform cr = _controls.rectTransform;
            cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0f, 0f);
            cr.anchoredPosition = new Vector2(24f, 96f); // above the drunkenness meter
            cr.sizeDelta = new Vector2(520f, 220f);
            var shadow = _controls.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);

            BuildDrunkMeter(font);
        }

        private static T NewGraphic<T>(string name, Transform parent) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var g = go.AddComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        private static Text NewText(string name, Transform parent, Font font, int size, TextAnchor anchor)
        {
            Text t = NewGraphic<Text>(name, parent);
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = Color.white;
            return t;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x == 0f ? 0f : 0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
