using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gamekit3D.Experiment
{
    public static class UIFactory
    {
        // Shared base colour for buttons and toggle boxes — light enough to stay clearly visible
        // against the app's dark page background.
        public static readonly Color DefaultElementColor = new Color(0.4f, 0.42f, 0.5f, 1f);

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        public static GameObject CreatePanelRoot(Transform parent, string name, Color background)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt);

            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.SetParent(rt, false);
            Stretch(bgRt);
            bgGo.GetComponent<Image>().color = background;

            return go;
        }

        public static TextMeshProUGUI CreateText(Transform parent, string content, float fontSize,
            TextAlignmentOptions align, Color? color = null, bool autoSize = false)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = align;
            text.color = color ?? Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;

            if (autoSize)
            {
                // Bind width to the parent (needed for wrapping to have a boundary) and let height
                // grow to fit however many lines the wrapped text ends up needing — without this,
                // a LayoutGroup ancestor sizes this element off its stale single-line height and
                // long text gets visually clipped instead of wrapping onto new lines.
                Stretch(rt);
                var fitter = go.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            return text;
        }

        public static Color Lighten(Color c, float amount)
        {
            return Color.Lerp(c, Color.white, amount);
        }

        public static Color Darken(Color c, float amount)
        {
            return Color.Lerp(c, Color.black, amount);
        }

        public static void ApplyColorBlock(Selectable selectable, Color baseColor)
        {
            var colors = selectable.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Lighten(baseColor, 0.25f);
            colors.pressedColor = Darken(baseColor, 0.25f);
            colors.selectedColor = Lighten(baseColor, 0.1f);
            colors.disabledColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.35f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            selectable.colors = colors;
        }

        public static Button CreateButton(Transform parent, string label, out TextMeshProUGUI labelText,
            Color? bgColor = null)
        {
            var go = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            var baseColor = bgColor ?? DefaultElementColor;
            image.color = baseColor;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            ApplyColorBlock(button, baseColor);

            var textRt = CreateRect(rt, "Label");
            Stretch(textRt);
            labelText = CreateText(textRt, label, 24, TextAlignmentOptions.Center);
            labelText.transform.SetParent(textRt, false);

            // A button placed inside a LayoutGroup with childControlHeight/Width = true has its
            // size driven entirely by its own LayoutElement; without one it collapses to zero.
            SetPreferredSize(go, 160, 50);

            return button;
        }

        public static void SetPreferredSize(GameObject go, float width, float height)
        {
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);

            var le = go.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = go.AddComponent<LayoutElement>();
            }
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.minWidth = width;
            le.minHeight = height;
        }

        public static Toggle CreateToggle(Transform parent, string label, ToggleGroup group,
            out TextMeshProUGUI labelText)
        {
            // A HorizontalLayoutGroup (checkbox fixed-size, label flexible-width) + ContentSizeFitter
            // lets long option labels wrap onto multiple lines and grow the row's height to match —
            // the previous manual-anchor layout had no way to grow past a single fixed-height line,
            // silently clipping long questionnaire options instead of wrapping them.
            var go = new GameObject("Toggle - " + label, typeof(RectTransform), typeof(Image), typeof(Toggle));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);

            // Fully transparent, but still raycastable: makes the whole row clickable, not just the
            // small checkbox.
            var rowImage = go.GetComponent<Image>();
            rowImage.color = new Color(0, 0, 0, 0);

            var rowLayout = go.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12;
            rowLayout.padding = new RectOffset(16, 8, 8, 8);
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bgGo = new GameObject("Box", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(rt, false);
            var bgImage = bgGo.GetComponent<Image>();
            bgImage.color = DefaultElementColor;
            SetPreferredSize(bgGo, 28, 28);

            var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.SetParent(bgGo.transform, false);
            Stretch(checkRt);
            checkRt.offsetMin = new Vector2(4, 4);
            checkRt.offsetMax = new Vector2(-4, -4);
            checkGo.GetComponent<Image>().color = new Color(0.35f, 0.75f, 0.95f, 1f);

            labelText = CreateText(rt, label, 22, TextAlignmentOptions.MidlineLeft);
            AddLayoutElement(labelText.gameObject).flexibleWidth = 1;

            var toggle = go.GetComponent<Toggle>();
            // targetGraphic drives the hover/press visual feedback (tints the checkbox), while the
            // invisible rowImage above merely widens the clickable/raycastable area to the full row.
            toggle.targetGraphic = bgImage;
            toggle.graphic = checkGo.GetComponent<Image>();
            toggle.isOn = false;
            ApplyColorBlock(toggle, bgImage.color);
            if (group != null)
            {
                toggle.group = group;
            }

            return toggle;
        }

        public static TMP_InputField CreateInputField(Transform parent, TMP_InputField.ContentType contentType,
            bool multiline = false)
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f, 1f);

            var textArea = CreateRect(rt, "Text Area");
            Stretch(textArea);
            textArea.offsetMin = new Vector2(10, 6);
            textArea.offsetMax = new Vector2(-10, -6);
            textArea.gameObject.AddComponent<RectMask2D>();

            var placeholder = CreateText(textArea, "...", 20, TextAlignmentOptions.MidlineLeft,
                new Color(1, 1, 1, 0.4f));
            placeholder.name = "Placeholder";
            Stretch(placeholder.GetComponent<RectTransform>());
            placeholder.fontStyle = FontStyles.Italic;

            var textComponent = CreateText(textArea, string.Empty, 20, TextAlignmentOptions.TopLeft);
            textComponent.name = "Text";
            Stretch(textComponent.GetComponent<RectTransform>());

            var input = go.GetComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = textComponent;
            input.placeholder = placeholder;
            input.contentType = contentType;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;

            return input;
        }

        public static Slider CreateSlider(Transform parent, int min, int max, int defaultValue)
        {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(0, 30);

            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.SetParent(rt, false);
            Stretch(bgRt);
            bgRt.offsetMin = new Vector2(0, 10);
            bgRt.offsetMax = new Vector2(0, -10);
            bgGo.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.24f, 1f);

            var fillAreaRt = CreateRect(rt, "Fill Area");
            Stretch(fillAreaRt);
            fillAreaRt.offsetMin = new Vector2(5, 10);
            fillAreaRt.offsetMax = new Vector2(-5, -10);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.SetParent(fillAreaRt, false);
            Stretch(fillRt);
            fillGo.GetComponent<Image>().color = new Color(0.35f, 0.75f, 0.95f, 1f);

            var handleAreaRt = CreateRect(rt, "Handle Slide Area");
            Stretch(handleAreaRt);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.SetParent(handleAreaRt, false);
            handleRt.sizeDelta = new Vector2(24, 0);
            handleGo.GetComponent<Image>().color = Color.white;

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleGo.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = true;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = defaultValue;

            return slider;
        }

        public static Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            var go = new GameObject("Scrollbar Vertical", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(16, 0);
            rt.anchoredPosition = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f, 1f);

            var slidingAreaRt = CreateRect(rt, "Sliding Area");
            Stretch(slidingAreaRt);
            slidingAreaRt.offsetMin = new Vector2(2, 2);
            slidingAreaRt.offsetMax = new Vector2(-2, -2);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.SetParent(slidingAreaRt, false);
            handleRt.sizeDelta = new Vector2(0, 40);
            handleGo.GetComponent<Image>().color = new Color(0.4f, 0.78f, 0.95f, 1f);

            var scrollbar = go.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleGo.GetComponent<Image>();
            ApplyColorBlock(scrollbar, handleGo.GetComponent<Image>().color);

            return scrollbar;
        }

        public static ScrollRect CreateScrollView(Transform parent, out RectTransform content)
        {
            var go = new GameObject("Scroll View", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt);
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var viewportRt = CreateRect(rt, "Viewport");
            Stretch(viewportRt);
            viewportRt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            viewportRt.gameObject.AddComponent<RectMask2D>();

            content = CreateRect(viewportRt, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, 0);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 26;
            layout.padding = new RectOffset(30, 30, 30, 30);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbar = CreateVerticalScrollbar(rt);

            var scrollRect = go.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRt;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 40f;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRect.verticalScrollbarSpacing = -3f;

            return scrollRect;
        }

        public static LayoutElement AddLayoutElement(GameObject go, float preferredHeight = -1,
            float minHeight = -1)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = go.AddComponent<LayoutElement>();
            }
            if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
            if (minHeight >= 0) le.minHeight = minHeight;
            return le;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(GameObject go, float spacing = 12)
        {
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            return layout;
        }

        public static GridLayoutGroup AddGridLayout(GameObject go, Vector2 cellSize, float spacing, int columns)
        {
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = cellSize;
            grid.spacing = new Vector2(spacing, spacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.MiddleCenter;
            return grid;
        }

        public static VerticalLayoutGroup AddVerticalLayout(GameObject go, float spacing = 12)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            return layout;
        }
    }
}
