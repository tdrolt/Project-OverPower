using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Match;

namespace Overpower.UI
{
    /// <summary>Task 5b-2 (D5): the shop's hover tooltip. Rest the pointer on a weapon node or ability card for
    /// Loadout Tooltip Delay Seconds and its description appears next to the cursor; move off and it goes at once;
    /// move to another item and the wait starts over. The bottom hover strip stays as it was. The timing rule is
    /// Overpower.Match.HoverTooltipTimer (unit tested); this file is the small view around it.</summary>
    public partial class LoadoutScreen
    {
        private readonly HoverTooltipTimer tooltipTimer = new HoverTooltipTimer();
        private System.Func<string> tooltipTextSource;
        private Vector2 tooltipPointerScreen;
        private GameObject tooltipRoot;
        private RectTransform tooltipRect;
        private TextMeshProUGUI tooltipLabel;

        /// <summary>True while the tooltip is on screen.</summary>
        public bool TooltipVisible => tooltipRoot != null && tooltipRoot.activeSelf;

        /// <summary>The pointer is on an item (entered it or moved over it).</summary>
        private void TooltipPointerAt(string key, System.Func<string> textSource, Vector2 screenPosition)
        {
            tooltipPointerScreen = screenPosition;
            if (tooltipTimer.Target != key)
            {
                tooltipTimer.Enter(key);
                tooltipTextSource = textSource;
            }
        }

        private void TooltipPointerLeft(string key)
        {
            tooltipTimer.Exit(key);
            if (tooltipTimer.Target == null && tooltipRoot != null)
                tooltipRoot.SetActive(false); // Moving off hides it at once, not on the next frame.
        }

        private void HideTooltip()
        {
            tooltipTimer.Reset();
            tooltipTextSource = null;
            if (tooltipRoot != null)
                tooltipRoot.SetActive(false);
        }

        /// <summary>Advances the hover clock and shows or hides the tooltip. Called from Update while the shop is open.</summary>
        private void TickTooltip(float deltaSeconds)
        {
            tooltipTimer.Tick(deltaSeconds);
            string text = tooltipTimer.Target != null && tooltipTextSource != null ? tooltipTextSource() : null;
            bool show = tooltipTimer.IsShown(theme.loadoutTooltipDelaySeconds) && !string.IsNullOrEmpty(text);
            if (!show)
            {
                if (tooltipRoot != null && tooltipRoot.activeSelf)
                    tooltipRoot.SetActive(false);
                return;
            }

            EnsureTooltip();
            if (!tooltipRoot.activeSelf || tooltipLabel.text != text)
            {
                tooltipLabel.text = text;
                float pad = theme.loadoutPanelPadding * 0.5f;
                float maxTextWidth = theme.loadoutTooltipMaxWidth - 2f * pad;
                float textWidth = Mathf.Min(tooltipLabel.GetPreferredValues(text, 10000f, 10000f).x, maxTextWidth);
                float textHeight = tooltipLabel.GetPreferredValues(text, textWidth, 10000f).y;
                tooltipRect.sizeDelta = new Vector2(textWidth + 2f * pad, textHeight + 2f * pad);
                tooltipRoot.SetActive(true);
            }
            PlaceTooltip();
        }

        private void PlaceTooltip()
        {
            var canvas = screenRoot.GetComponent<Canvas>();
            var canvasRect = (RectTransform)screenRoot.transform;
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, tooltipPointerScreen, cam, out Vector2 local))
                return;

            Vector2 size = tooltipRect.sizeDelta;
            Vector2 half = canvasRect.rect.size * 0.5f;
            // Top-left corner just below-right of the cursor; flip above / left when it would leave the screen.
            Vector2 offset = theme.loadoutTooltipOffset;
            float flipGap = theme.loadoutTooltipFlipGap;
            float x = local.x + offset.x;
            float y = local.y + offset.y;
            if (x + size.x > half.x) x = local.x - flipGap - size.x;
            if (y - size.y < -half.y) y = local.y + flipGap + size.y;
            x = Mathf.Clamp(x, -half.x, Mathf.Max(-half.x, half.x - size.x));
            y = Mathf.Clamp(y, Mathf.Min(half.y, -half.y + size.y), half.y);
            tooltipRect.anchoredPosition = new Vector2(x, y);
        }

        private void EnsureTooltip()
        {
            if (tooltipRoot != null)
                return;

            tooltipRoot = new GameObject("Shop Tooltip", typeof(RectTransform));
            tooltipRoot.transform.SetParent(screenRoot.transform, false); // Last child of the canvas: on top of the panel.
            tooltipRect = tooltipRoot.GetComponent<RectTransform>();
            tooltipRect.anchorMin = tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            tooltipRect.pivot = new Vector2(0f, 1f);
            Image background = tooltipRoot.AddComponent<Image>();
            background.color = theme.loadoutTooltipBackgroundColor;
            background.raycastTarget = false; // Never steals the pointer from the item under it (that would flicker it away).

            float pad = theme.loadoutPanelPadding * 0.5f;
            tooltipLabel = AddLabel(tooltipRoot.transform, "", theme.smallTextSize, FontStyles.Normal);
            tooltipLabel.alignment = TextAlignmentOptions.TopLeft;
            RectTransform labelRt = tooltipLabel.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(pad, pad);
            labelRt.offsetMax = new Vector2(-pad, -pad);
            tooltipRoot.SetActive(false);
        }
    }
}
