using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>One button of the lobby screens: its parts, and the look it has when it can and cannot be pressed.</summary>
    public sealed class LobbyButton
    {
        public Button Button;
        public Image Fill;
        public Image Border;
        public TMP_Text Label;
        public Color EnabledFill, EnabledText, DisabledFill, DisabledText;

        public GameObject Root => Button.gameObject;
        public RectTransform Rect => (RectTransform)Button.transform;

        /// <summary>Greys the button out (its disabled look) or gives it back its own colours.</summary>
        public void SetEnabled(bool on)
        {
            Button.interactable = on;
            Fill.color = on ? EnabledFill : DisabledFill;
            Label.color = on ? EnabledText : DisabledText;
        }

        /// <summary>Presses the button the way a click does (what checks and drivers call).</summary>
        public void Press()
        {
            if (Button.interactable) Button.onClick.Invoke();
        }
    }

    /// <summary>A box with an outline: Outer is the thing placed in a layout, Inner is where its contents go.</summary>
    public sealed class LobbyBox
    {
        public RectTransform Outer;
        public RectTransform Inner;
        public Image Fill;
        public Image Border;
    }

    /// <summary>
    /// The shared builders of the lobby screens (lobby Task 9): the name screen, the lobby list and the create screen are code-built
    /// uGUI + TextMeshPro like MatchStartPanel, so the scene needs no objects for them. One kit per screen set, made from the
    /// UiTheme: it makes the screen canvas, rounded boxes with an outline, text in the lobby fonts, buttons, the name box, the
    /// outlined title. Sizes are in reference pixels (the boards' pixels x 1.5 at 1920 x 1080).
    ///
    /// The canvas is a ScreenSpaceOverlay with a GraphicRaycaster: a screen you click needs one (a world-space canvas must not have one,
    /// trap 20). Its scaler takes the reference resolution and match value straight from the theme (the ThemedCanvasScaler component
    /// is for scene-built canvases whose theme is assigned in the Inspector; a canvas built here has no Inspector to assign it in).
    /// </summary>
    public sealed class LobbyUiKit
    {
        public readonly UiTheme Theme;

        /// <summary>Above the match UI (0 to -21), below the Escape pop-up (700) and the connection-lost panel (900).</summary>
        public const int CanvasSortingOrder = 100;

        private readonly List<Material> materialInstances = new List<Material>();

        public LobbyUiKit(UiTheme theme) => Theme = theme;

        /// <summary>Destroys the text materials this kit made for outlined text. Call when the screens go.</summary>
        public void Dispose()
        {
            foreach (Material m in materialInstances)
                if (m != null) Object.Destroy(m);
            materialInstances.Clear();
        }

        // ---- fonts ----

        public TMP_FontAsset Display => Theme.lobbyDisplayFont;
        public TMP_FontAsset Body => Theme.lobbyBodyFont;
        public TMP_FontAsset Bold => Theme.lobbyBoldFont != null ? Theme.lobbyBoldFont : Theme.lobbyBodyFont;

        // ---- the canvas ----

        public Canvas CreateCanvas(Transform parent, string name)
        {
            EnsureEventSystem();
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Theme.referenceResolution;
            scaler.matchWidthOrHeight = Theme.matchWidthOrHeight;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ---- rounded boxes ----

        private const int SpriteSize = 96;
        private const int SpriteRadius = 32;
        private static Sprite rounded;

        /// <summary>A white rounded square that stretches by its corners (9-sliced); every box is this sprite tinted.</summary>
        private static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false)
                {
                    name = "Lobby Rounded Box",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var pixels = new Color32[SpriteSize * SpriteSize];
                float half = SpriteSize * 0.5f;
                for (int y = 0; y < SpriteSize; y++)
                {
                    for (int x = 0; x < SpriteSize; x++)
                    {
                        float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - SpriteRadius), 0f);
                        float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - SpriteRadius), 0f);
                        float distance = Mathf.Sqrt(dx * dx + dy * dy) - SpriteRadius;
                        pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(0.5f - distance)));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                rounded = Sprite.Create(texture, new Rect(0, 0, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                    new Vector4(SpriteRadius, SpriteRadius, SpriteRadius, SpriteRadius));
                rounded.hideFlags = HideFlags.HideAndDontSave;
                return rounded;
            }
        }

        private static Sprite dashed;
        private const int DashThickness = 5;   // texture pixels: with a 9 px corner radius this draws a line about 1.4 reference pixels thick
        private const int DashLength = 16;     // texture pixels of a 32 pixel edge piece: half dash, half gap

        /// <summary>A rounded OUTLINE (nothing inside) whose straight edges are dashed. Its edge pieces repeat along a long edge (Image type Tiled),
        /// so one small sprite dashes a box of any size; the corners are drawn solid.</summary>
        private static Sprite DashedRounded
        {
            get
            {
                if (dashed != null) return dashed;
                var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false)
                {
                    name = "Lobby Dashed Box",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var pixels = new Color32[SpriteSize * SpriteSize];
                float half = SpriteSize * 0.5f;
                int edgeStart = SpriteRadius, edgeEnd = SpriteSize - SpriteRadius;
                for (int y = 0; y < SpriteSize; y++)
                {
                    for (int x = 0; x < SpriteSize; x++)
                    {
                        float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - SpriteRadius), 0f);
                        float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - SpriteRadius), 0f);
                        float distance = Mathf.Sqrt(dx * dx + dy * dy) - SpriteRadius; // negative inside the box
                        float alpha = Mathf.Clamp01(0.5f - distance) * Mathf.Clamp01(distance + DashThickness + 0.5f);
                        bool inColumn = x >= edgeStart && x < edgeEnd, inRow = y >= edgeStart && y < edgeEnd;
                        // along a top or bottom edge the piece repeats in x, along a left or right edge in y; the middle of the sprite is empty anyway
                        if (inColumn && !inRow && x - edgeStart >= DashLength) alpha = 0f;
                        if (inRow && !inColumn && y - edgeStart >= DashLength) alpha = 0f;
                        pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * alpha));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                dashed = Sprite.Create(texture, new Rect(0, 0, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                    new Vector4(SpriteRadius, SpriteRadius, SpriteRadius, SpriteRadius));
                dashed.hideFlags = HideFlags.HideAndDontSave;
                return dashed;
            }
        }

        /// <summary>Gives an Image the dashed outline (a see-through box with a dashed line round it) with this corner radius (reference pixels).</summary>
        public static void Dash(Image image, float radius)
        {
            image.sprite = DashedRounded;
            image.type = Image.Type.Tiled;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = SpriteRadius / Mathf.Max(0.01f, radius);
        }

        /// <summary>Gives an Image the rounded-box sprite with this corner radius (reference pixels).</summary>
        public static void Round(Image image, float radius)
        {
            image.sprite = radius > 0.01f ? Rounded : null;
            image.type = radius > 0.01f ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = radius > 0.01f ? SpriteRadius / radius : 1f;
        }

        /// <summary>A rounded box with an outline. With no outline Outer and Inner are the same object.</summary>
        public LobbyBox Box(Transform parent, string name, Color fill, float radius, Color border, float borderWidth)
        {
            var outer = new GameObject(name, typeof(RectTransform), typeof(Image));
            outer.transform.SetParent(parent, false);
            var box = new LobbyBox { Outer = (RectTransform)outer.transform };
            Image outerImage = outer.GetComponent<Image>();
            if (borderWidth <= 0.01f)
            {
                Round(outerImage, radius);
                outerImage.color = fill;
                box.Inner = box.Outer;
                box.Fill = outerImage;
                return box;
            }
            Round(outerImage, radius);
            outerImage.color = border;
            box.Border = outerImage;
            var inner = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            inner.transform.SetParent(outer.transform, false);
            RectTransform innerRect = (RectTransform)inner.transform;
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(borderWidth, borderWidth);
            innerRect.offsetMax = new Vector2(-borderWidth, -borderWidth);
            inner.AddComponent<LayoutElement>().ignoreLayout = true; // when the box lays out its own contents, the fill is not one of them
            Image innerImage = inner.GetComponent<Image>();
            Round(innerImage, Mathf.Max(0f, radius - borderWidth));
            innerImage.color = fill;
            box.Inner = innerRect;
            box.Fill = innerImage;
            return box;
        }

        /// <summary>A flat full-screen colour.</summary>
        public Image Backdrop(Transform parent, string name, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            Image image = go.GetComponent<Image>();
            image.color = colour;
            return image;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ---- layout groups ----

        public static VerticalLayoutGroup VGroup(Transform parent, string name, float spacing, TextAnchor align = TextAnchor.UpperLeft,
            RectOffset padding = null, bool expandWidth = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            VerticalLayoutGroup group = go.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = align;
            group.padding = padding ?? new RectOffset();
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = expandWidth;
            group.childForceExpandHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup HGroup(Transform parent, string name, float spacing, TextAnchor align = TextAnchor.MiddleLeft,
            RectOffset padding = null, bool expandHeight = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            HorizontalLayoutGroup group = go.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = align;
            group.padding = padding ?? new RectOffset();
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = expandHeight;
            return group;
        }

        /// <summary>Sizes a child of a layout group. Anything left at a negative value is not set.</summary>
        public static LayoutElement Size(GameObject go, float width = -1f, float height = -1f, float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            LayoutElement element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (width >= 0f) element.preferredWidth = width;
            if (height >= 0f) element.preferredHeight = height;
            if (flexibleWidth >= 0f) element.flexibleWidth = flexibleWidth;
            if (flexibleHeight >= 0f) element.flexibleHeight = flexibleHeight;
            return element;
        }

        public static GameObject Spacer(Transform parent, string name = "Spacer")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Size(go, 0f, 0f, 1f, 0f);
            return go;
        }

        public static RectOffset Pad(float left, float right, float top, float bottom) =>
            new RectOffset(Mathf.RoundToInt(left), Mathf.RoundToInt(right), Mathf.RoundToInt(top), Mathf.RoundToInt(bottom));

        // ---- text ----

        /// <summary>TextMeshPro's character spacing is in hundredths of the text size: this turns a spacing in reference pixels into it.</summary>
        public static float SpacingFor(float pixels, float textSize) => textSize > 0.01f ? pixels / textSize * 100f : 0f;

        /// <param name="richText">Pass false for text a player typed (a lobby name, a host name): it is drawn as typed, never read for markup.</param>
        /// <remarks>Trap: a line of the display font (Oswald) is about 1.5 times its size tall. A text box shorter than that does not show a smaller
        /// line, it shows no line at all (the ellipsis overflow drops what does not fit vertically), so a heading's box must be at least 1.5 x its size.</remarks>
        public TextMeshProUGUI Text(Transform parent, string name, string text, TMP_FontAsset font, float size, Color colour,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, float spacingPixels = 0f, bool wrap = false, bool richText = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = colour;
            tmp.alignment = align;
            tmp.characterSpacing = SpacingFor(spacingPixels, size);
            // Kerning and letter spacing together put a gap after the P of PROJECT in these fonts: spaced capitals are set without kerning.
            if (spacingPixels > 0.01f) tmp.enableKerning = false;
            tmp.enableWordWrapping = wrap;
            tmp.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            tmp.richText = richText;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>OVERPOWER: off-white letters with an orange outline (TextMeshPro's outline on a copy of the font's material).</summary>
        public TextMeshProUGUI OutlinedTitle(Transform parent, string name, string text, float size)
        {
            TextMeshProUGUI title = Text(parent, name, text, Display, size, Theme.lobbyOffWhiteColor, TextAlignmentOptions.Midline);
            // A material with the outline switched on exists as an asset so the outline shader variant is in the build; the colour and width come from the theme.
            if (Theme.lobbyTitleMaterial != null) title.fontSharedMaterial = Theme.lobbyTitleMaterial;
            title.outlineColor = Theme.lobbyTitleOutlineColor;
            title.outlineWidth = Theme.lobbyTitleOutlineWidth;
            if (title.fontMaterial != null) materialInstances.Add(title.fontMaterial);
            return title;
        }

        // ---- buttons ----

        /// <summary>A button with a label. Pass a border colour and width for an outlined one; the fill of an outlined button that should look
        /// see-through is the colour of what it sits on.</summary>
        public LobbyButton MakeButton(Transform parent, string name, string label, TMP_FontAsset font, float textSize, Color textColour, Color fill,
            float radius, Color border, float borderWidth, float spacingPixels = 0f)
        {
            LobbyBox box = Box(parent, name, fill, radius, border, borderWidth);
            Button button = box.Outer.gameObject.AddComponent<Button>();
            button.targetGraphic = box.Fill;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colours = ColorBlock.defaultColorBlock;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colours.selectedColor = Color.white;
            colours.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colours.disabledColor = Color.white; // the disabled look is drawn by SetEnabled, not tinted
            colours.fadeDuration = 0.08f;
            button.colors = colours;
            Navigation none = new Navigation { mode = Navigation.Mode.None };
            button.navigation = none;

            TextMeshProUGUI text = Text(box.Inner, "Label", label, font, textSize, textColour, TextAlignmentOptions.Midline, spacingPixels);
            Stretch((RectTransform)text.transform);
            return new LobbyButton
            {
                Button = button,
                Fill = box.Fill,
                Border = box.Border,
                Label = text,
                EnabledFill = fill,
                EnabledText = textColour,
                DisabledFill = Theme.lobbyDisabledButtonColor,
                DisabledText = Theme.lobbyDimColor,
            };
        }

        /// <summary>Makes a button as wide as its label plus this much on each side (inside a layout group).</summary>
        public static void WidenToLabel(LobbyButton button, float paddingEachSide)
        {
            float labelWidth = button.Label.GetPreferredValues(button.Label.text).x;
            Size(button.Root, labelWidth + paddingEachSide * 2f);
        }

        // ---- the name box ----

        /// <summary>A single-line text box: the typed text, a placeholder, a limit and (optionally) letters and digits only.</summary>
        public TMP_InputField InputBox(Transform parent, string name, string placeholder, float height, float textSize, float paddingX,
            TextAlignmentOptions align, int characterLimit, bool lettersAndDigitsOnly, out LobbyBox box)
        {
            box = Box(parent, name, Theme.lobbyPanelColor, Theme.lobbyCornerRadius, Theme.lobbyBorderColor, Theme.lobbyBorderWidth);
            Size(box.Outer.gameObject, -1f, height);

            var areaGo = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            areaGo.transform.SetParent(box.Inner, false);
            RectTransform area = (RectTransform)areaGo.transform;
            Stretch(area);
            area.offsetMin = new Vector2(paddingX, 0f);
            area.offsetMax = new Vector2(-paddingX, 0f);

            TextMeshProUGUI hint = Text(area, "Placeholder", placeholder, Body, textSize, Theme.lobbyDimColor, align);
            Stretch((RectTransform)hint.transform);
            TextMeshProUGUI typed = Text(area, "Text", "", Body, textSize, Theme.lobbyOffWhiteColor, align);
            Stretch((RectTransform)typed.transform);

            TMP_InputField field = box.Inner.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = box.Fill;
            field.textViewport = area;
            field.textComponent = typed;
            field.placeholder = hint;
            field.fontAsset = Body;
            field.pointSize = textSize;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = characterLimit;
            field.characterValidation = lettersAndDigitsOnly ? TMP_InputField.CharacterValidation.Alphanumeric : TMP_InputField.CharacterValidation.None;
            field.customCaretColor = true;
            field.caretColor = Theme.lobbyOffWhiteColor;
            field.caretWidth = 3;
            field.selectionColor = new Color(Theme.lobbyCyanColor.r, Theme.lobbyCyanColor.g, Theme.lobbyCyanColor.b, 0.35f);
            field.onFocusSelectAll = false;
            field.transition = Selectable.Transition.None;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            return field;
        }
    }
}
