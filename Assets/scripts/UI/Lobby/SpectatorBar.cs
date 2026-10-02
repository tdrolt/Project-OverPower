using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The bar at the bottom of a spectator seat's screen (lobby Task 6, board 8): "SPECTATING", who is watched in their team's colour (or
    /// "Whole map"), the four keys and a Leave button. Built in code like MatchStartPanel (uGUI + TMP on its own overlay canvas with a
    /// GraphicRaycaster so Leave is clickable); every colour, size and text is a UiTheme field in the "Spectator bar" section. It only
    /// shows what SpectatorSeatView tells it; the keys themselves are read there.
    /// </summary>
    public sealed class SpectatorBar : MonoBehaviour
    {
        private UiTheme theme;
        private Action onLeave;
        private TextMeshProUGUI nameLabel;
        private Material textMaterial;
        private string shownLine = "\u0001"; // never a real line: forces the first Show to write

        /// <summary>What the name line says right now ("Mara - Purple" or "Whole map", without the colour tag). Recorders read it.</summary>
        public string NameLine { get; private set; } = "";

        public static SpectatorBar Create(Transform parent, UiTheme theme, Action onLeave)
        {
            var go = new GameObject("Spectator Bar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SpectatorBar bar = go.AddComponent<SpectatorBar>();
            bar.theme = theme;
            bar.onLeave = onLeave;
            bar.Build();
            return bar;
        }

        private void OnDestroy()
        {
            if (textMaterial != null)
                Destroy(textMaterial);
        }

        /// <summary>The watched player's name and team name in the team's colour; a null name shows the whole-map line instead.</summary>
        public void Show(string playerName, string teamName, Color teamColor)
        {
            string line = playerName == null ? theme.spectatorBarWholeMapName : $"{playerName} · {teamName}";
            if (line == shownLine)
                return;
            shownLine = line;
            NameLine = line;
            nameLabel.text = playerName == null
                ? theme.spectatorBarWholeMapName
                : $"{playerName} <color=#{ColorUtility.ToHtmlStringRGB(teamColor)}>· {teamName}</color>";
        }

        private void Build()
        {
            EnsureEventSystem();

            GameObject canvasGo = new GameObject("Spectator Bar Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = -10;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(canvasGo.transform, false);
            RectTransform barRt = bar.GetComponent<RectTransform>();
            barRt.anchorMin = barRt.anchorMax = barRt.pivot = new Vector2(0.5f, 0f);
            barRt.anchoredPosition = new Vector2(0f, theme.spectatorBarBottom);
            bar.GetComponent<Image>().color = theme.spectatorBarColor;
            HorizontalLayoutGroup row = bar.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = theme.spectatorBarGap;
            row.padding = Padding(theme.spectatorBarPadding);
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            ContentSizeFitter fit = bar.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Who is watched: the caption over the name.
            GameObject who = new GameObject("Watching", typeof(RectTransform));
            who.transform.SetParent(bar.transform, false);
            VerticalLayoutGroup column = who.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.MiddleLeft;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = column.childForceExpandHeight = false;
            Label(who.transform, "Caption", theme.spectatorBarCaption, theme.spectatorBarCaptionSize, theme.spectatorBarCaptionColor, FontStyles.Bold);
            nameLabel = Label(who.transform, "Name", "", theme.spectatorBarNameSize, theme.textColor, FontStyles.Normal);

            for (int i = 0; i < 4; i++)
                BuildKey(bar.transform, Entry(theme.spectatorBarKeyCaps, i), Entry(theme.spectatorBarKeyTexts, i));

            BuildLeave(bar.transform);
        }

        private static string Entry(string[] texts, int index) => texts != null && index < texts.Length ? texts[index] : "";

        private static RectOffset Padding(Vector2 horizontalVertical) =>
            new RectOffset(Mathf.RoundToInt(horizontalVertical.x), Mathf.RoundToInt(horizontalVertical.x),
                           Mathf.RoundToInt(horizontalVertical.y), Mathf.RoundToInt(horizontalVertical.y));

        private void BuildKey(Transform parent, string cap, string text)
        {
            GameObject key = new GameObject("Key " + cap, typeof(RectTransform));
            key.transform.SetParent(parent, false);
            HorizontalLayoutGroup group = key.AddComponent<HorizontalLayoutGroup>();
            group.childAlignment = TextAnchor.MiddleCenter;
            group.spacing = theme.spectatorBarGap * 0.3f;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;

            // The cap: an outline (outer image) with the bar's colour inside it.
            GameObject outline = new GameObject("Cap", typeof(RectTransform), typeof(Image));
            outline.transform.SetParent(key.transform, false);
            outline.GetComponent<Image>().color = theme.spectatorBarKeyCapColor;
            outline.GetComponent<Image>().raycastTarget = false;
            int border = Mathf.RoundToInt(theme.spectatorBarOutline);
            HorizontalLayoutGroup outer = outline.AddComponent<HorizontalLayoutGroup>();
            outer.padding = new RectOffset(border, border, border, border);
            outer.childControlWidth = outer.childControlHeight = true;
            outer.childForceExpandWidth = outer.childForceExpandHeight = false;

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(outline.transform, false);
            Color inner = theme.spectatorBarColor;
            inner.a = 1f;
            fill.GetComponent<Image>().color = inner;
            fill.GetComponent<Image>().raycastTarget = false;
            HorizontalLayoutGroup pad = fill.AddComponent<HorizontalLayoutGroup>();
            pad.padding = Padding(theme.spectatorBarKeyCapPadding);
            pad.childControlWidth = pad.childControlHeight = true;
            pad.childForceExpandWidth = pad.childForceExpandHeight = false;
            Label(fill.transform, "Cap Text", cap, theme.spectatorBarKeyCapSize, theme.textColor, FontStyles.Bold);

            Label(key.transform, "Text", text, theme.spectatorBarKeyTextSize, theme.textColor, FontStyles.Normal);
        }

        private void BuildLeave(Transform parent)
        {
            GameObject outline = new GameObject("Leave Button", typeof(RectTransform), typeof(Image), typeof(Button));
            outline.transform.SetParent(parent, false);
            outline.GetComponent<Image>().color = theme.spectatorBarLeaveOutlineColor;
            Button button = outline.GetComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onLeave?.Invoke());
            LayoutElement size = outline.AddComponent<LayoutElement>();
            size.preferredWidth = theme.spectatorBarLeaveSize.x;
            size.preferredHeight = theme.spectatorBarLeaveSize.y;
            int border = Mathf.RoundToInt(theme.spectatorBarOutline);
            HorizontalLayoutGroup outer = outline.AddComponent<HorizontalLayoutGroup>();
            outer.padding = new RectOffset(border, border, border, border);
            outer.childControlWidth = outer.childControlHeight = true;
            outer.childForceExpandWidth = outer.childForceExpandHeight = true;

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(outline.transform, false);
            Color inner = theme.spectatorBarColor;
            inner.a = 1f;
            fill.GetComponent<Image>().color = inner;
            fill.GetComponent<Image>().raycastTarget = false;
            HorizontalLayoutGroup centre = fill.AddComponent<HorizontalLayoutGroup>();
            centre.childAlignment = TextAnchor.MiddleCenter;
            centre.childControlWidth = centre.childControlHeight = true;
            centre.childForceExpandWidth = centre.childForceExpandHeight = false;
            Label(fill.transform, "Leave Text", theme.spectatorBarLeaveText, theme.spectatorBarKeyTextSize, theme.textColor, FontStyles.Normal);
        }

        private TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color, FontStyles style)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            if (theme.font != null)
                tmp.font = theme.font; // before the material (assigning a font swaps the material)
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.richText = true;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            if (textMaterial == null)
            {
                textMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(textMaterial);
            }
            tmp.fontSharedMaterial = textMaterial;
            return tmp;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            GameObject go = new GameObject("EventSystem", typeof(EventSystem));
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }
}
