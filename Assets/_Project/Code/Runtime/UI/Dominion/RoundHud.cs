using TMPro;
using UnityEngine;
using Overpower.Dominion;

namespace Overpower.UI
{
    /// <summary>
    /// The round clock at the top centre during a round: the small round heading over the clock, nothing else (the points and the round-win dots
    /// are on ScoreBars, top right). "+200 CYAN" flashes under it when the centre pays out. DominionHudText builds the words; sizes, colours and
    /// words are UiTheme fields (Dominion HUD). It draws only what DominionHud hands it each frame and rewrites a text only when its value changed.
    /// </summary>
    public sealed class RoundHud
    {
        private readonly LobbyUiKit kit;
        private readonly UiTheme theme;
        private readonly Transform parent;
        private RectTransform root;
        private TextMeshProUGUI roundLabel, clockLabel, flashLabel;
        private int builtTeamCount;
        private float flashUntil;
        // The whole numbers the labels were last built from: a label's words are formatted only when one moved.
        private bool roundDrawn, clockDrawn;
        private int drawnRoundNumber, drawnMaxRounds, drawnClockSeconds;
        private bool shownSudden, shownOvertime;

        /// <summary>The words the round label and the clock slot show now. Recorders and wiring tests read them.</summary>
        public string RoundText => roundLabel != null ? roundLabel.text : "";
        public string ClockText => clockLabel != null ? clockLabel.text : "";
        public string FlashTextShown => flashLabel != null && flashLabel.gameObject.activeSelf ? flashLabel.text : "";
        public bool IsShowing => root != null && root.gameObject.activeSelf;
        public RectTransform Root => root;

        public RoundHud(LobbyUiKit kit, Transform parent)
        {
            this.kit = kit;
            theme = kit.Theme;
            this.parent = parent;
        }

        public void SetVisible(bool visible)
        {
            if (root != null && root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }

        /// <summary>teams are the match's team ids in order (2 or 3; three use the 3v3v3 clock size); clockSeconds is the round clock (ignored in sudden
        /// death), or the overtime's own clock when overtime is on: the small heading then says OVERTIME instead of the round.</summary>
        public void Refresh(int[] teams, int round, int maxRounds, int clockSeconds, bool suddenDeath, bool overtime)
        {
            if (teams == null || teams.Length < 2) { SetVisible(false); return; }
            if (root == null || builtTeamCount != teams.Length) Build(teams);
            SetVisible(true);

            if (!roundDrawn || round != drawnRoundNumber || maxRounds != drawnMaxRounds || overtime != shownOvertime)
            {
                roundLabel.text = overtime ? theme.dominionBarOvertimeText : DominionHudText.RoundLabel(theme.dominionRoundLabelFormat, round, maxRounds);
                roundLabel.color = overtime ? theme.dominionGoldColor : theme.dominionMutedColor;
                roundDrawn = true; drawnRoundNumber = round; drawnMaxRounds = maxRounds; shownOvertime = overtime;
            }

            // In sudden death the clock slot says SUDDEN DEATH whatever the seconds are, so only the mode matters there.
            if (!clockDrawn || suddenDeath != shownSudden || (!suddenDeath && clockSeconds != drawnClockSeconds))
            {
                clockLabel.text = suddenDeath ? theme.dominionBarSuddenText : DominionHudText.Clock(clockSeconds);
                clockLabel.fontSize = suddenDeath ? theme.dominionBarSuddenSize : ClockSize();
                clockLabel.color = suddenDeath ? theme.suddenDeathColor : theme.lobbyOffWhiteColor;
                clockDrawn = true; drawnClockSeconds = clockSeconds;
                shownSudden = suddenDeath;
            }

            if (flashLabel.gameObject.activeSelf)
            {
                float left = flashUntil - Time.unscaledTime;
                if (left <= 0f) flashLabel.gameObject.SetActive(false);
                else flashLabel.alpha = Mathf.Clamp01(left / theme.dominionFlashFadeSeconds);
            }
        }

        /// <summary>The centre paid this team: "+200 CYAN" in the team's colour shows under the clock for Flash Seconds.</summary>
        public void Flash(int team, int pointsPaid, string[] teamNames)
        {
            if (root == null) return;
            flashLabel.text = DominionHudText.FlashText(theme.dominionFlashFormat, pointsPaid, teamNames, team);
            flashLabel.color = TextColour(team);
            flashLabel.alpha = 1f;
            flashLabel.gameObject.SetActive(true);
            flashUntil = Time.unscaledTime + theme.dominionFlashSeconds;
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }

        // ---------------------------------------------------------------- building

        private float ClockSize() => builtTeamCount >= 3 ? theme.dominionClock3Size : theme.dominionClockSize;

        private void Build(int[] teams)
        {
            Destroy();
            builtTeamCount = teams.Length;
            float width = teams.Length >= 3 ? theme.dominionBar3ClockWidth : theme.dominionBarClockWidth;
            float height = theme.dominionBarHeight;

            LobbyBox box = kit.Box(parent, "Round Bar", theme.dominionBarFill, theme.dominionBarRadius, Color.clear, 0f);
            box.Fill.raycastTarget = false;
            root = box.Outer;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -theme.dominionBarTop);
            root.sizeDelta = new Vector2(width, height);
            BuildClock(width, height);

            flashLabel = kit.Text(root, "Centre Flash", "", kit.Display, theme.dominionFlashSize, theme.dominionGoldColor, TextAlignmentOptions.Midline);
            flashLabel.overflowMode = TextOverflowModes.Overflow;
            RectTransform flashRect = flashLabel.rectTransform;
            flashRect.anchorMin = flashRect.anchorMax = flashRect.pivot = new Vector2(0.5f, 1f);
            flashRect.sizeDelta = new Vector2(width * 1.5f, theme.dominionFlashSize * 1.5f);
            flashRect.anchoredPosition = new Vector2(0f, -(height + theme.dominionFlashGap));
            flashLabel.gameObject.SetActive(false);
            roundDrawn = clockDrawn = false;
            shownOvertime = false;
        }

        private void BuildClock(float width, float height)
        {
            LobbyBox clock = kit.Box(root, "Clock", theme.dominionBarClockFill, theme.dominionBarRadius, Color.clear, 0f);
            clock.Fill.raycastTarget = false;
            TopLeft(clock.Outer, 0f, 0f, width, height);

            roundLabel = kit.Text(clock.Outer, "Round", "", kit.Bold, theme.dominionRoundLabelSize, theme.dominionMutedColor, TextAlignmentOptions.Midline, theme.dominionLabelSpacing);
            roundLabel.overflowMode = TextOverflowModes.Overflow;
            RectTransform roundRect = roundLabel.rectTransform;
            roundRect.anchorMin = new Vector2(0f, 1f);
            roundRect.anchorMax = new Vector2(1f, 1f);
            roundRect.pivot = new Vector2(0.5f, 1f);
            roundRect.sizeDelta = new Vector2(0f, theme.dominionRoundLabelSize * 1.5f);
            roundRect.anchoredPosition = new Vector2(0f, -(height * theme.dominionRoundLabelTopShare));

            clockLabel = kit.Text(clock.Outer, "Clock", "0:00", kit.Display, ClockSize(), theme.lobbyOffWhiteColor, TextAlignmentOptions.Midline);
            clockLabel.overflowMode = TextOverflowModes.Overflow;
            RectTransform clockRect = clockLabel.rectTransform;
            clockRect.anchorMin = new Vector2(0f, 1f);
            clockRect.anchorMax = new Vector2(1f, 1f);
            clockRect.pivot = new Vector2(0.5f, 1f);
            clockRect.sizeDelta = new Vector2(0f, ClockSize() * 1.3f);
            clockRect.anchoredPosition = new Vector2(0f, -(height * theme.dominionRoundLabelTopShare + theme.dominionRoundLabelSize * 1.5f));
        }

        // ---------------------------------------------------------------- helpers

        private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private Color TextColour(int team) => Pick(theme.dominionTeamTextColors, team);

        private Color Pick(Color[] colours, int team) => TeamPalette.Of(colours, team, theme.dominionDimColor);
    }
}
