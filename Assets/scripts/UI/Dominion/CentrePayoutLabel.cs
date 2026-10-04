using TMPro;
using UnityEngine;
using Overpower.Dominion;

namespace Overpower.UI
{
    /// <summary>
    /// Dominion Task 9, the centre countdown under the minimap in a 3v3v3 match (board DomCentre): "CENTRE +200 IN 12" and who holds it ("Cyan holds
    /// it" in their colour, "Nobody holds it" in grey). It is the place a Conquest match shows its scan countdown (Conquest is unchanged; the scan
    /// does not run in Dominion). Shown only while a round is on and the centre has a payout time written. Words and sizes are UiTheme fields.
    /// </summary>
    public sealed class CentrePayoutLabel
    {
        private readonly LobbyUiKit kit;
        private readonly UiTheme theme;
        private readonly Transform parent;
        private RectTransform root;
        private TextMeshProUGUI line, holder;
        private string shownLine, shownHolder;
        private int shownHolderTeam = -2;

        public bool IsShowing => root != null && root.gameObject.activeSelf;
        public string LineText => line != null ? line.text : "";
        public string HolderText => holder != null ? holder.text : "";

        public CentrePayoutLabel(LobbyUiKit kit, Transform parent)
        {
            this.kit = kit;
            theme = kit.Theme;
            this.parent = parent;
        }

        public void SetVisible(bool visible)
        {
            if (root != null && root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }

        public void Refresh(int points, int secondsLeft, int holderTeam, string[] teamNames)
        {
            if (root == null) Build();
            SetVisible(true);
            string text = DominionHudText.CentreLine(theme.dominionCentreFormat, points, secondsLeft);
            if (text != shownLine) { line.text = text; shownLine = text; }
            string who = DominionHudText.HolderLine(holderTeam, teamNames, theme.dominionCentreHoldsFormat, theme.dominionCentreNobodyText);
            if (who != shownHolder || holderTeam != shownHolderTeam)
            {
                holder.text = who;
                holder.color = holderTeam < 0 ? theme.dominionDimColor : Pick(theme.dominionTeamTextColors, holderTeam);
                shownHolder = who;
                shownHolderTeam = holderTeam;
            }
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }

        private void Build()
        {
            var go = new GameObject("Centre Countdown", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            root = (RectTransform)go.transform;
            DominionHud.PlaceUnderMinimap(root, theme, theme.dominionCentreSize * 1.5f + theme.dominionCentreHolderSize * 1.5f);
            line = kit.Text(root, "Line", "", kit.Display, theme.dominionCentreSize, theme.lobbyOffWhiteColor, TextAlignmentOptions.Midline, 0f, false, true);
            line.overflowMode = TextOverflowModes.Overflow;
            Place(line.rectTransform, 0f, theme.dominionCentreSize * 1.5f);
            holder = kit.Text(root, "Holder", "", kit.Bold, theme.dominionCentreHolderSize, theme.dominionDimColor, TextAlignmentOptions.Midline);
            holder.overflowMode = TextOverflowModes.Overflow;
            Place(holder.rectTransform, theme.dominionCentreSize * 1.5f, theme.dominionCentreHolderSize * 1.5f);
            shownLine = shownHolder = null;
            shownHolderTeam = -2;
        }

        private static void Place(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, -top);
        }

        private static Color Pick(Color[] colours, int index) =>
            colours != null && colours.Length > 0 ? colours[Mathf.Clamp(index, 0, colours.Length - 1)] : Color.white;
    }
}
