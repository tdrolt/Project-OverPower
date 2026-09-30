using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Data;
using Overpower.Combat;
using Overpower.Match;

namespace Overpower.UI
{
    /// <summary>Task 5b-2 (D5), reworked in Task 13: the shop's hover pop-up. Rest the pointer on a weapon node, an
    /// ability card or an armor row for Loadout Tooltip Delay Seconds (0.5 s) and a small box appears right beside the
    /// cursor, kept fully on screen: the item's name, one line on what it does, and its numbers. Move off and it goes
    /// at once; move to another item and the wait starts over. It replaced the description strip at the bottom of the
    /// screen. The timing rule is Overpower.Match.HoverTooltipTimer, the placement rule Overpower.Match.
    /// ShopPopupPlacement, the text ShopPopupText (all unit tested); this file is the small view around them.</summary>
    public partial class LoadoutScreen
    {
        private readonly HoverTooltipTimer tooltipTimer = new HoverTooltipTimer();
        private System.Func<string> tooltipTextSource;
        private string tooltipText;
        // True when the text must be built again: another item was entered, or the shop refreshed (a price, a level, the
        // equipped item may have changed). Building it is a few string joins, so it is done once per change, not per frame.
        private bool tooltipTextDirty;
        private Vector2 tooltipPointerScreen;
        private GameObject tooltipRoot;
        private RectTransform tooltipRect;
        private TextMeshProUGUI tooltipLabel;

        /// <summary>True while the pop-up is on screen.</summary>
        public bool TooltipVisible => tooltipRoot != null && tooltipRoot.activeSelf;

        /// <summary>The pop-up's box on screen (canvas units, y up from the screen centre) and its text - for checks.</summary>
        public Rect TooltipBox => tooltipRect != null
            ? new Rect(tooltipRect.anchoredPosition.x, tooltipRect.anchoredPosition.y - tooltipRect.sizeDelta.y, tooltipRect.sizeDelta.x, tooltipRect.sizeDelta.y)
            : Rect.zero;

        public string TooltipShownText => tooltipLabel != null ? tooltipLabel.text : "";

        /// <summary>Wires an item (a weapon node, an ability card, an armor row) so resting the pointer on it opens the
        /// pop-up. key names the item (moving to another key restarts the wait); text builds the pop-up's text when it opens.</summary>
        private void AddPopUp(GameObject item, string key, System.Func<string> text)
        {
            HoverRelay hover = item.AddComponent<HoverRelay>();
            hover.OnPointerAt = pos => TooltipPointerAt(key, text, pos);
            hover.OnExit = () => TooltipPointerLeft(key);
        }

        /// <summary>Name + one line + numbers as the pop-up shows them, in the muted numbers colour from UiTheme.</summary>
        private string PopUpText(string name, string description, string numbers) =>
            ShopPopupText.Compose(name, description, numbers, ColorUtility.ToHtmlStringRGB(theme.loadoutTooltipNumbersColor));

        private string WeaponPopUpText(WeaponDefinition def) =>
            PopUpText(def.DisplayName, def.Description, ShopItemNumbers.Weapon(def));

        private string AbilityPopUpText(AbilityDefinition def) =>
            PopUpText(def.DisplayName, def.Description, ShopItemNumbers.Ability(def));

        /// <summary>An armor row's pop-up: what the path does, the level you have, what the next upgrade gives and what
        /// it costs (the same price line the row itself shows).</summary>
        private string ArmorPopUpText(bool absorbRow)
        {
            if (playerHealth == null || armorConfig == null)
                return "";
            int nextPrice = armorConfig.CostFor(playerHealth.AbsorbLevel + playerHealth.RechargeLevel);
            ShopContext ctx = CurrentShopContext();
            string priceLine = ShopPricing.PriceLine(nextPrice, ctx.Check(nextPrice), ctx.Balance);
            string numbers = absorbRow
                ? ArmorPopupText.Numbers(true, armorConfig, playerHealth.AbsorbLevel, playerHealth.RechargeLevel, priceLine,
                    theme.loadoutArmorAbsorbNowFormat, theme.loadoutArmorAbsorbNextFormat, theme.loadoutArmorNoNextText)
                : ArmorPopupText.Numbers(false, armorConfig, playerHealth.AbsorbLevel, playerHealth.RechargeLevel, priceLine,
                    theme.loadoutArmorRechargeNowFormat, theme.loadoutArmorRechargeNextFormat, theme.loadoutArmorNoNextText);
            return absorbRow
                ? PopUpText(theme.loadoutArmorAbsorbTipName, theme.loadoutArmorAbsorbTipText, numbers)
                : PopUpText(theme.loadoutArmorRechargeTipName, theme.loadoutArmorRechargeTipText, numbers);
        }

        /// <summary>The pointer is on an item (entered it or moved over it).</summary>
        private void TooltipPointerAt(string key, System.Func<string> textSource, Vector2 screenPosition)
        {
            tooltipPointerScreen = screenPosition;
            if (tooltipTimer.Target != key)
            {
                tooltipTimer.Enter(key);
                tooltipTextSource = textSource;
                tooltipTextDirty = true;
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
            tooltipText = null;
            tooltipTextDirty = false;
            if (tooltipRoot != null)
                tooltipRoot.SetActive(false);
        }

        /// <summary>Advances the hover clock and shows or hides the pop-up. Called from Update while the shop is open.</summary>
        private void TickTooltip(float deltaSeconds)
        {
            tooltipTimer.Tick(deltaSeconds);
            bool waited = tooltipTimer.Target != null && tooltipTextSource != null && tooltipTimer.IsShown(theme.loadoutTooltipDelaySeconds);
            if (waited && tooltipTextDirty)
            {
                tooltipText = tooltipTextSource();
                tooltipTextDirty = false;
            }

            string text = waited ? tooltipText : null;
            if (string.IsNullOrEmpty(text))
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

            tooltipRect.anchoredPosition = ShopPopupPlacement.TopLeft(local, tooltipRect.sizeDelta, canvasRect.rect.size * 0.5f,
                theme.loadoutTooltipOffset, theme.loadoutTooltipFlipGap);
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
