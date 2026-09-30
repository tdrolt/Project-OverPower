using TMPro;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Tudor's D10: the tips panel on the name screen (keys, overheat and the Vent, ability charges, the game mode
    /// in two lines). Built in code from UiTheme, once, under the name screen's own panel - so it goes away with
    /// that panel when you join, and never sits on the name box or the Join button (it starts below the button).
    /// Every text is a UiTheme field; the labels are plain text with no raycast target, so they never take a click.
    /// </summary>
    public static class NameScreenTips
    {
        /// <summary>Builds the two columns under <paramref name="joinButton"/>, inside the same parent, and returns
        /// the root object (null when there is no theme or button to build from).</summary>
        public static GameObject Build(RectTransform joinButton, UiTheme theme, TMP_FontAsset font)
        {
            if (joinButton == null || theme == null)
                return null;

            var root = new GameObject("Name Screen Tips", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(joinButton.parent, false);
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            // Top edge sits nameTipsGapBelowJoin under the Join button's bottom edge.
            float top = joinButton.anchoredPosition.y - joinButton.rect.height * joinButton.pivot.y - theme.nameTipsGapBelowJoin;
            rootRect.anchoredPosition = new Vector2(0f, top);
            rootRect.sizeDelta = Vector2.zero;

            float half = theme.nameTipsColumnGap * 0.5f;
            AddColumn(rootRect, "Keys", theme.nameTipsKeysText, theme, font, new Vector2(1f, 1f), -half, theme.nameTipsKeysWidth);
            AddColumn(rootRect, "Rules",
                theme.nameTipsOverheatText + "\n\n" + theme.nameTipsChargesText + "\n\n" + theme.nameTipsModeText,
                theme, font, new Vector2(0f, 1f), half, theme.nameTipsRulesWidth);
            return root;
        }

        private static void AddColumn(RectTransform parent, string label, string text, UiTheme theme, TMP_FontAsset font,
                                      Vector2 pivot, float x, float width)
        {
            var go = new GameObject(label, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = pivot;
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, 0f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
                tmp.font = font;
            tmp.text = text;
            tmp.fontSize = theme.nameTipsFontSize;
            tmp.color = theme.nameTipsColor;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;
            tmp.richText = true;
            var fitter = go.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        }
    }
}
