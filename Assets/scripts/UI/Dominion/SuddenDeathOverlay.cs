using TMPro;
using UnityEngine;
using Overpower.Dominion;

namespace Overpower.UI
{
    /// <summary>
    /// Dominion Task 9, the sudden-death words (board DomSudden). When sudden death starts a banner shows under the round bar: "SUDDEN DEATH" and
    /// "No respawns · stay inside the circle · last team standing wins", in full for Banner Seconds after the circle's start was written (a
    /// restarted sudden death shows it again), then it shrinks to a small red "SUDDEN DEATH" line in the same place for the rest of the match.
    /// "CIRCLE SHRINKS · 0:41" sits under the minimap until the circle stops. The circle's numbers come from SuddenDeathZone (Task 8), so every
    /// screen counts the same. Words, sizes and colours are UiTheme fields.
    /// </summary>
    public sealed class SuddenDeathOverlay
    {
        private readonly LobbyUiKit kit;
        private readonly UiTheme theme;
        private readonly Transform parent;
        private RectTransform banner, small, shrinkRoot;
        private TextMeshProUGUI shrink;
        private string shownShrink;

        public bool BannerShowing => banner != null && banner.gameObject.activeSelf;
        public bool SmallShowing => small != null && small.gameObject.activeSelf;
        public bool ShrinkShowing => shrinkRoot != null && shrinkRoot.gameObject.activeSelf;
        public string ShrinkText => shrink != null ? shrink.text : "";
        public string BannerTitle { get; private set; } = "";
        public string BannerRules { get; private set; } = "";

        public SuddenDeathOverlay(LobbyUiKit kit, Transform parent)
        {
            this.kit = kit;
            theme = kit.Theme;
            this.parent = parent;
        }

        public void Hide()
        {
            if (banner != null) banner.gameObject.SetActive(false);
            if (small != null) small.gameObject.SetActive(false);
            if (shrinkRoot != null) shrinkRoot.gameObject.SetActive(false);
        }

        /// <summary>One frame during sudden death. sinceStartMs is how long ago the circle's start (dSd) was written; secondsUntilStopped is the circle's own count.</summary>
        public void Refresh(int sinceStartMs, float secondsUntilStopped)
        {
            if (banner == null) Build();
            bool full = DominionHudText.BannerIsFull(sinceStartMs, theme.dominionBannerSeconds);
            if (banner.gameObject.activeSelf != full) banner.gameObject.SetActive(full);
            if (small.gameObject.activeSelf == full) small.gameObject.SetActive(!full);

            string text = DominionHudText.ShrinkLine(theme.dominionShrinkFormat, secondsUntilStopped);
            bool showShrink = text.Length > 0;
            if (shrinkRoot.gameObject.activeSelf != showShrink) shrinkRoot.gameObject.SetActive(showShrink);
            if (showShrink && text != shownShrink) { shrink.text = text; shownShrink = text; }
        }

        public void Destroy()
        {
            if (banner != null) Object.Destroy(banner.gameObject);
            if (small != null) Object.Destroy(small.gameObject);
            if (shrinkRoot != null) Object.Destroy(shrinkRoot.gameObject);
            banner = small = shrinkRoot = null;
        }

        private void Build()
        {
            // The full banner: a dark box with the big red words and the rules under them.
            LobbyBox box = kit.Box(parent, "Sudden Death Banner", theme.dominionCardFill, theme.dominionCardRadius, Color.clear, 0f);
            box.Fill.raycastTarget = false;
            banner = box.Outer;
            banner.anchorMin = banner.anchorMax = banner.pivot = new Vector2(0.5f, 1f);
            banner.anchoredPosition = new Vector2(0f, -theme.dominionBannerTop);
            banner.sizeDelta = new Vector2(theme.dominionBannerWidth, theme.dominionBannerTitleSize * 1.5f + theme.dominionBannerRulesSize * 1.5f + 30f);
            BannerTitle = theme.dominionBannerTitle;
            BannerRules = theme.dominionBannerRules;
            TextMeshProUGUI title = kit.Text(banner, "Title", BannerTitle, kit.Display, theme.dominionBannerTitleSize, theme.suddenDeathColor, TextAlignmentOptions.Midline, 1.5f);
            title.overflowMode = TextOverflowModes.Overflow;
            Place(title.rectTransform, 12f, theme.dominionBannerTitleSize * 1.5f);
            TextMeshProUGUI rules = kit.Text(banner, "Rules", BannerRules, kit.Body, theme.dominionBannerRulesSize, theme.lobbyOffWhiteColor, TextAlignmentOptions.Midline);
            rules.overflowMode = TextOverflowModes.Overflow;
            Place(rules.rectTransform, 12f + theme.dominionBannerTitleSize * 1.5f, theme.dominionBannerRulesSize * 1.5f);

            // The small line that stays after the banner.
            LobbyBox chip = kit.Box(parent, "Sudden Death Small", theme.dominionCardFill, theme.dominionCardRadius, Color.clear, 0f);
            chip.Fill.raycastTarget = false;
            small = chip.Outer;
            small.anchorMin = small.anchorMax = small.pivot = new Vector2(0.5f, 1f);
            small.anchoredPosition = new Vector2(0f, -theme.dominionBannerTop);
            small.sizeDelta = new Vector2(theme.dominionBannerSmallSize * 9f, theme.dominionBannerSmallSize * 1.5f + 12f);
            TextMeshProUGUI chipText = kit.Text(small, "Title", BannerTitle, kit.Display, theme.dominionBannerSmallSize, theme.suddenDeathColor, TextAlignmentOptions.Midline, 1f);
            chipText.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Stretch(chipText.rectTransform);

            // The circle countdown under the minimap.
            var go = new GameObject("Circle Countdown", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            shrinkRoot = (RectTransform)go.transform;
            DominionHud.PlaceUnderMinimap(shrinkRoot, theme, theme.dominionShrinkSize * 1.5f);
            shrink = kit.Text(shrinkRoot, "Line", "", kit.Display, theme.dominionShrinkSize, theme.suddenDeathColor, TextAlignmentOptions.Midline);
            shrink.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Stretch(shrink.rectTransform);
            banner.gameObject.SetActive(false);
            small.gameObject.SetActive(false);
            shrinkRoot.gameObject.SetActive(false);
            shownShrink = null;
        }

        private static void Place(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, -top);
        }
    }
}
