using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The card look of the match's pop-ups (the Escape pop-up, Connection lost, the saved-log box, the death card): LobbyUiKit's rounded dark
    /// card, lobby fonts, a purple main button and an outlined second one, the look of the round bar, break card and result card. Builders only;
    /// each screen keeps its own behaviour. The canvas is built here, not with LobbyUiKit.CreateCanvas, which also makes an EventSystem when it finds
    /// none: these are built in Awake, before the scene's own EventSystem is enabled.
    /// </summary>
    public static class PopupCard
    {
        private static UiTheme fallbackTheme;

        /// <summary>The theme, or a default one (built-in colours, TextMeshPro's default font) so a screen with a missing theme still draws.</summary>
        public static UiTheme ThemeOrDefault(UiTheme theme)
        {
            if (theme != null) return theme;
            if (fallbackTheme == null)
            {
                fallbackTheme = ScriptableObject.CreateInstance<UiTheme>();
                fallbackTheme.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallbackTheme;
        }

        public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder, UiTheme theme, bool clickable)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            if (clickable) go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>A rounded dark card whose column of contents sets its height; the caller anchors and places it.</summary>
        public static RectTransform Card(LobbyUiKit kit, Transform parent, string name, float width, Vector2 padding, float gap, TextAnchor align = TextAnchor.UpperCenter)
        {
            UiTheme t = kit.Theme;
            LobbyBox box = kit.Box(parent, name, t.spectatorResultCardColor, t.dominionCardRadius, t.lobbyCardBorderColor, t.lobbyBorderWidth);
            RectTransform card = box.Outer;
            card.sizeDelta = new Vector2(width, 0f);
            VerticalLayoutGroup column = card.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = align;
            column.spacing = gap;
            column.padding = LobbyUiKit.Pad(padding.x, padding.x, padding.y, padding.y);
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            ContentSizeFitter fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return card;
        }

        /// <summary>A line of text in the card's column, its height set to fit its own (wrapped) text.</summary>
        public static TextMeshProUGUI Line(LobbyUiKit kit, Transform card, string name, string text, TMP_FontAsset font, float size, Color colour, bool wrap)
        {
            TextMeshProUGUI label = kit.Text(card, name, text, font, size, colour, TextAlignmentOptions.Midline, 0f, wrap);
            label.overflowMode = TextOverflowModes.Overflow;
            if (!wrap) LobbyUiKit.Size(label.gameObject, -1f, size * 1.5f);
            return label;
        }

        /// <summary>A centred row for buttons: a button straight in the card's column would be stretched to the card's full width.</summary>
        public static Transform ButtonRow(Transform card, float gap)
        {
            HorizontalLayoutGroup row = LobbyUiKit.HGroup(card, "Buttons", gap, TextAnchor.MiddleCenter);
            return row.transform;
        }

        /// <summary>The main action: filled purple, the Create button's look.</summary>
        public static LobbyButton Primary(LobbyUiKit kit, Transform row, string label, UnityAction onClick) =>
            Button(kit, row, label, kit.Theme.lobbyPurpleColor, kit.Theme.lobbyPurpleColor, 0f, onClick);

        /// <summary>The second action: the card's own colour with an outline.</summary>
        public static LobbyButton Secondary(LobbyUiKit kit, Transform row, string label, UnityAction onClick) =>
            Button(kit, row, label, kit.Theme.lobbyPanelColor, kit.Theme.lobbyBorderColor, kit.Theme.lobbyBorderWidth, onClick);

        private static LobbyButton Button(LobbyUiKit kit, Transform row, string label, Color fill, Color border, float borderWidth, UnityAction onClick)
        {
            UiTheme t = kit.Theme;
            LobbyButton button = kit.MakeButton(row, label, label, kit.Display, t.dominionBreakButtonSize, t.lobbyOffWhiteColor, fill, t.lobbyCornerRadius,
                border, borderWidth, t.lobbyButtonSpacing);
            LobbyUiKit.Size(button.Root, -1f, t.dominionBreakButtonHeight);
            LobbyUiKit.WidenToLabel(button, t.dominionBreakButtonPadding);
            if (onClick != null) button.Button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>A volume-style slider in the card's colours: a dark rounded track, a purple fill, an off-white knob. Only the knob and the track
        /// take the mouse.</summary>
        public static Slider MakeSlider(LobbyUiKit kit, Transform parent, string name, float height)
        {
            UiTheme t = kit.Theme;
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            LobbyUiKit.Size(root, -1f, height, 1f);

            float track = height * 0.3f;
            Image background = Child(root.transform, "Track", t.lobbyDarkColor, track * 0.5f);
            RectTransform backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(0f, track);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(root.transform, false);
            RectTransform fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRect.sizeDelta = new Vector2(-height, track);
            Image fill = Child(fillArea.transform, "Fill", t.lobbyPurpleColor, track * 0.5f);
            fill.raycastTarget = false;
            fill.rectTransform.sizeDelta = new Vector2(height, 0f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(root.transform, false);
            RectTransform handleAreaRect = (RectTransform)handleArea.transform;
            LobbyUiKit.Stretch(handleAreaRect);
            handleAreaRect.offsetMin = new Vector2(height * 0.5f, 0f);
            handleAreaRect.offsetMax = new Vector2(-height * 0.5f, 0f);
            Image handle = Child(handleArea.transform, "Handle", t.lobbyOffWhiteColor, height * 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(height, 0f);

            Slider slider = root.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }

        private static Image Child(Transform parent, string name, Color colour, float radius)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            Image image = go.GetComponent<Image>();
            LobbyUiKit.Round(image, radius);
            image.color = colour;
            return image;
        }

        /// <summary>Gives a button built elsewhere (a prefab's Quit button) the main-button look: rounded purple fill, off-white display font.</summary>
        public static void SkinPrimary(UiTheme t, Button button, float width)
        {
            Image fill = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (fill != null)
            {
                LobbyUiKit.Round(fill, t.lobbyCornerRadius);
                fill.color = t.lobbyPurpleColor;
            }
            ColorBlock colours = ColorBlock.defaultColorBlock;
            colours.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colours.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colours.fadeDuration = 0.08f;
            button.colors = colours;
            ((RectTransform)button.transform).sizeDelta = new Vector2(width, t.dominionBreakButtonHeight);
            foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.lobbyDisplayFont != null) label.font = t.lobbyDisplayFont;
                label.fontSize = t.dominionBreakButtonSize;
                label.color = t.lobbyOffWhiteColor;
                label.characterSpacing = LobbyUiKit.SpacingFor(t.lobbyButtonSpacing, t.dominionBreakButtonSize);
                label.alignment = TextAlignmentOptions.Midline;
            }
        }
    }
}
