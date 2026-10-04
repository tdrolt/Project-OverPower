using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The result card a spectator sees when the match ends (lobby Task 15b, spec section 11): who won, and the same "Back to the lobby list"
    /// button the players' result screen has. A spectator has no body, so none of the MatchUI panels (which live on the player prefab) can
    /// show; this is built in code on its own overlay canvas like SpectatorBar, above the bar. The saved-log box (MatchLogZip) is shown by
    /// the zip that runs when the card goes up, exactly as for a player. Texts, colours and sizes are UiTheme fields (Spectator bar section).
    /// </summary>
    public sealed class SpectatorResultCard : MonoBehaviour
    {
        private UiTheme theme;
        private Action onBack;
        private Material textMaterial;

        /// <summary>The title as shown ("Purple wins the match"); checks read it.</summary>
        public string Title { get; private set; } = "";

        /// <summary>The button's label ("Back to the lobby list").</summary>
        public string ButtonText { get; private set; } = "";

        /// <summary>The card's button, for checks that press it through its onClick.</summary>
        public Button BackButton { get; private set; }

        public static SpectatorResultCard Create(Transform parent, UiTheme theme, string title, Color teamColor, Action onBack)
        {
            var go = new GameObject("Spectator Result Card", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SpectatorResultCard card = go.AddComponent<SpectatorResultCard>();
            card.theme = theme;
            card.onBack = onBack;
            card.Title = title;
            card.ButtonText = theme.resultButtonLobbyList;
            card.Build(teamColor);
            return card;
        }

        private void OnDestroy()
        {
            if (textMaterial != null)
                Destroy(textMaterial);
        }

        private void Build(Color teamColor)
        {
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            GameObject canvasGo = new GameObject("Spectator Result Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 5; // over the spectator bar (-10), under the saved-log box (10)
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject panel = new GameObject("Card", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            RectTransform rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -theme.spectatorResultCardTop);
            panel.GetComponent<Image>().color = theme.spectatorResultCardColor;
            VerticalLayoutGroup column = panel.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.MiddleCenter;
            column.spacing = 24f;
            column.padding = new RectOffset(Mathf.RoundToInt(theme.spectatorResultCardPadding.x), Mathf.RoundToInt(theme.spectatorResultCardPadding.x),
                Mathf.RoundToInt(theme.spectatorResultCardPadding.y), Mathf.RoundToInt(theme.spectatorResultCardPadding.y));
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = column.childForceExpandHeight = false;
            ContentSizeFitter fit = panel.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Label(panel.transform, "Title", Title, theme.spectatorResultTitleSize, teamColor, FontStyles.Bold);

            GameObject button = new GameObject("Back Button", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(panel.transform, false);
            button.GetComponent<Image>().color = theme.matchStartButtonColor;
            BackButton = button.GetComponent<Button>();
            BackButton.navigation = new Navigation { mode = Navigation.Mode.None };
            BackButton.onClick.AddListener(() => onBack?.Invoke());
            LayoutElement size = button.AddComponent<LayoutElement>();
            size.preferredWidth = theme.matchStartButtonSize.x;
            size.preferredHeight = theme.matchStartButtonSize.y;
            HorizontalLayoutGroup centre = button.AddComponent<HorizontalLayoutGroup>();
            centre.childAlignment = TextAnchor.MiddleCenter;
            centre.childControlWidth = centre.childControlHeight = true;
            centre.childForceExpandWidth = centre.childForceExpandHeight = false;
            Label(button.transform, "Text", ButtonText, theme.bodyTextSize, theme.textColor, FontStyles.Normal);
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
            tmp.alignment = TextAlignmentOptions.Center;
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
    }
}
