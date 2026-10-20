using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Gives the player prefab's YOU WIN / YOU LOSE panels the card look at runtime (MatchUI.Awake): a rounded dark card in the middle of the
    /// screen, the title in the display font at its top, the purple Quit / lobby-list button at its bottom, the canvas scaled like the HUD.
    /// Every object stays where the prefab has it (MatchUI, QuitButton and SpectateView find them by reference and by name); only looks change,
    /// so nothing in the prefab is edited. The panel's one Image is the whole card (fill and edge), so SpectateView's bottom strip still hides it
    /// by that Image.
    /// </summary>
    public static class ResultPanelSkin
    {
        /// <param name="buttonRows">The buttons stacked at the card's bottom: 2 on the lose card, where SpectateView puts Spectate over Quit.</param>
        public static void Apply(GameObject panel, UiTheme theme, int buttonRows)
        {
            if (panel == null || theme == null) return;
            Canvas canvas = panel.GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                canvas.sortingOrder = UiLayers.ResultPanels;
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = theme.referenceResolution;
                    scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
                }
            }

            var rect = (RectTransform)panel.transform;
            Vector2 padding = theme.spectatorResultCardPadding;
            float titleHeight = theme.resultPanelTitleSize * 1.5f;
            int rows = Mathf.Max(1, buttonRows);
            var size = new Vector2(theme.resultPanelCardWidth, 2f * padding.y + titleHeight + theme.spectatorResultCardGap
                + rows * theme.dominionBreakButtonHeight + (rows - 1) * SpectateView.ButtonGap);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            Image fill = panel.GetComponent<Image>();
            if (fill != null)
            {
                LobbyUiKit.Round(fill, theme.dominionCardRadius);
                // The pop-up cards are this fill over an opaque border box; one Image cannot stack the two, so it takes their result, opaque,
                // and the Outline draws the edge (both go when SpectateView switches the Image off).
                fill.color = OpaqueOver(theme.spectatorResultCardColor, theme.lobbyCardBorderColor);
                Outline edge = panel.GetComponent<Outline>();
                if (edge == null) edge = panel.AddComponent<Outline>();
                edge.effectColor = theme.lobbyCardBorderColor;
                edge.effectDistance = new Vector2(theme.lobbyBorderWidth, -theme.lobbyBorderWidth);
                edge.useGraphicAlpha = false;
            }

            foreach (Transform child in panel.transform)
            {
                var childRect = (RectTransform)child;
                Button button = child.GetComponent<Button>();
                if (button != null)
                {
                    PopupCard.SkinPrimary(theme, button, theme.resultPanelButtonWidth);
                    childRect.anchoredPosition = new Vector2(0f, -(size.y * 0.5f - padding.y - theme.dominionBreakButtonHeight * 0.5f));
                    continue;
                }
                TMP_Text title = child.GetComponent<TMP_Text>();
                if (title == null) continue;
                if (theme.lobbyDisplayFont != null) title.font = theme.lobbyDisplayFont;
                title.fontSize = theme.resultPanelTitleSize;
                title.color = theme.lobbyOffWhiteColor;
                title.fontStyle = FontStyles.UpperCase;
                title.alignment = TextAlignmentOptions.Midline;
                title.enableWordWrapping = false;
                title.raycastTarget = false;
                childRect.sizeDelta = new Vector2(size.x - 2f * padding.x, titleHeight);
                childRect.anchoredPosition = new Vector2(0f, size.y * 0.5f - padding.y - titleHeight * 0.5f);
            }
        }

        /// <summary>The colour a see-through fill shows over an opaque colour, blended as the linear-space renderer blends it.</summary>
        private static Color OpaqueOver(Color fill, Color under)
        {
            Color mixed = Color.Lerp(under.linear, fill.linear, fill.a).gamma;
            mixed.a = 1f;
            return mixed;
        }
    }
}
