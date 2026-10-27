using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RumOverboard.Frontend
{
    /// <summary>Tiny code-only uGUI builder (no prefabs) for the frontend screens.</summary>
    public static class UiKit
    {
        public static readonly Color Backdrop = new(0.03f, 0.06f, 0.1f, 1f);
        public static readonly Color Dim = new(0f, 0f, 0f, 0.6f);
        public static readonly Color Card = new(0.08f, 0.12f, 0.18f, 0.97f);
        public static readonly Color Row = new(0.12f, 0.17f, 0.24f, 1f);
        public static readonly Color Field = new(0.05f, 0.08f, 0.12f, 1f);
        public static readonly Color Gold = new(0.93f, 0.72f, 0.3f, 1f);
        public static readonly Color TextColor = new(0.92f, 0.94f, 0.96f, 1f);
        public static readonly Color Muted = new(0.6f, 0.66f, 0.74f, 1f);
        public static readonly Color Danger = new(0.95f, 0.45f, 0.4f, 1f);
        public static readonly Color Good = new(0.45f, 0.85f, 0.5f, 1f);
        public static readonly Color ButtonColor = new(0.17f, 0.26f, 0.37f, 1f);
        public static readonly Color PrimaryColor = new(0.72f, 0.5f, 0.16f, 1f);
        public static readonly Color DangerButton = new(0.55f, 0.18f, 0.16f, 1f);

        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Panel(Transform parent, Color color, string name = "Panel")
        {
            RectTransform rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>Centered card that sizes itself to its vertical content.</summary>
        public static RectTransform CardPanel(Transform parent, float width, string name = "Card")
        {
            Image img = Panel(parent, Card, name);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, 100f);
            VStack(rt, 14f, 36);
            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        public static VerticalLayoutGroup VStack(Component target, float spacing, int padding)
        {
            var v = target.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static RectTransform HStack(Transform parent, float spacing, float height = -1f, string name = "Row")
        {
            RectTransform rt = Rect(name, parent);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            if (height > 0f)
                Size(rt, -1f, height);
            return rt;
        }

        public static LayoutElement Size(Component c, float width = -1f, float height = -1f, float flexWidth = -1f)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null)
                le = c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) le.preferredWidth = width;
            if (height >= 0f)
            {
                le.preferredHeight = height;
                le.minHeight = height;
            }
            if (flexWidth >= 0f) le.flexibleWidth = flexWidth;
            return le;
        }

        public static Text Label(Transform parent, string text, int size = 26, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = Rect("Label", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color ?? TextColor;
            t.alignment = align;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(Transform parent, string label, Action onClick, Color? color = null,
            float height = 58f, float width = -1f, int fontSize = 26)
        {
            Image img = Panel(parent, color ?? ButtonColor, "Button: " + label);
            var button = img.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            button.colors = colors;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            Text t = Label(img.transform, label, fontSize, TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform, 6f);
            Size(img, width, height, width < 0f ? 1f : 0f);
            return button;
        }

        public static void SetLabel(Button button, string label)
        {
            Text t = button != null ? button.GetComponentInChildren<Text>(true) : null;
            if (t != null)
                t.text = label;
        }

        public static InputField Input(Transform parent, string placeholder, string value, int maxLength,
            float height = 54f, float width = -1f)
        {
            Image img = Panel(parent, Field, "Input");
            var field = img.gameObject.AddComponent<InputField>();
            Text text = Label(img.transform, string.Empty, 26);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(text.rectTransform, 12f);
            Text ph = Label(img.transform, placeholder, 24, Muted, TextAnchor.MiddleLeft, FontStyle.Italic);
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(ph.rectTransform, 12f);
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = maxLength;
            field.lineType = InputField.LineType.SingleLine;
            field.text = value ?? string.Empty;
            Size(img, width, height, width < 0f ? 1f : 0f);
            return field;
        }

        public static ScrollRect ScrollList(Transform parent, float height, out RectTransform content)
        {
            Image bg = Panel(parent, new Color(0f, 0f, 0f, 0.25f), "List");
            bg.gameObject.AddComponent<RectMask2D>();
            Size(bg, -1f, height);
            var scroll = bg.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            content = Rect("Content", bg.transform);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            VStack(content, 8f, 8);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = bg.rectTransform;
            scroll.content = content;
            return scroll;
        }

        public static void Spacer(Transform parent, float height)
        {
            RectTransform rt = Rect("Spacer", parent);
            Size(rt, -1f, height);
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject child = t.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        public static bool IsTyping()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.TryGetComponent(out InputField f) && f.isFocused;
        }
    }
}
