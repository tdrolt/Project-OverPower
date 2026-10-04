using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using Overpower.Dominion;

namespace Overpower.UI
{
    /// <summary>
    /// Dominion Task 9, the match result (board DomResult): "DOMINION 2v2", "PURPLE WINS 2–1" in the winner's colour, a table of points per round
    /// with each round's winner in bold (a tied round has none), a note, and Back to the lobby list. It is the lobby look from
    /// LobbyUiKit.ResultCard (so it matches the spectator card), with the table built between the title and the button, and it replaces Conquest's
    /// YOU WIN / YOU LOSE panel in Dominion only. Players get it through MatchUI.ShowMatchResult and spectators through SpectatorSeatView; both
    /// ask DominionHud, which builds it from the room (the round wins and dHist, each round's points). Words, sizes and colours are UiTheme fields.
    /// </summary>
    public sealed class DominionResultPanel
    {
        private readonly LobbyUiKit kit;
        private readonly UiTheme theme;
        private readonly Transform canvas;
        private RectTransform card;
        private LobbyButton back;

        public bool IsShowing => card != null && card.gameObject.activeSelf;
        public string ModeText { get; private set; } = "";
        public string HeadlineText { get; private set; } = "";
        /// <summary>The table as drawn, one entry per row, cells joined by "|" and the round winner's cell wrapped in *…*; recorders and checks read it.</summary>
        public List<string> TableRows { get; } = new List<string>();
        public LobbyButton BackButton => back;

        public DominionResultPanel(LobbyUiKit kit, Transform canvas)
        {
            this.kit = kit;
            theme = kit.Theme;
            this.canvas = canvas;
        }

        public void Show(int winner, int[] teams, int[] wins, int[] history, int roundsToWin, string[] teamNames, Action onBack)
        {
            if (IsShowing) return;
            ModeText = DominionHudText.ModeLine(theme.dominionResultModeFormat, teams.Length);
            HeadlineText = DominionHudText.ResultHeadline(winner, wins, teams, teamNames, theme.dominionResultHeadlineFormat, theme.dominionResultSuddenFormat,
                theme.dominionResultScoreSeparator, DominionHudText.WonInSuddenDeath(winner, wins, roundsToWin));
            TableRows.Clear();

            back = kit.ResultCard(canvas, HeadlineText, Pick(theme.dominionTeamTextColors, winner), theme.resultButtonLobbyList, out card,
                ModeText, theme.dominionResultModeSize, theme.dominionMutedColor,
                body => BuildBody(body, teams, history, teamNames), theme.dominionResultHeadlineSize, theme.dominionResultWidth);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(0f, theme.dominionResultRaise); // raised clear of the saved-log box in the bottom left
            if (onBack != null) back.Button.onClick.AddListener(() => onBack());
        }

        public void Destroy()
        {
            if (card != null) UnityEngine.Object.Destroy(card.gameObject);
            card = null;
        }

        private void BuildBody(Transform body, int[] teams, int[] history, string[] teamNames)
        {
            int rounds = DominionHistory.RoundCount(history);
            float size = theme.dominionResultTableSize;
            float rowHeight = theme.dominionResultRowHeight;

            // The header row: an empty corner, then "Round 1", "Round 2"…
            HorizontalLayoutGroup header = null;
            var headerCells = new List<string> { "" };
            header = Row(body, "Header");
            Cell(header.transform, "", kit.Body, size, theme.dominionMutedColor, theme.dominionResultNameWidth, rowHeight, TextAlignmentOptions.MidlineLeft);
            for (int r = 0; r < rounds; r++)
            {
                string label = string.Format(System.Globalization.CultureInfo.InvariantCulture, theme.dominionResultRoundFormat, r + 1);
                headerCells.Add(label);
                Cell(header.transform, label, kit.Body, size, theme.dominionMutedColor, theme.dominionResultCellWidth, rowHeight, TextAlignmentOptions.Midline);
            }
            TableRows.Add(string.Join("|", headerCells));

            foreach (int team in teams)
            {
                HorizontalLayoutGroup row = Row(body, "Team " + team);
                string name = Overpower.Lobby.LobbyRoomRules.TeamName(teamNames, team);
                var cells = new List<string> { name };
                Cell(row.transform, name, kit.Bold, size, Pick(theme.dominionTeamTextColors, team), theme.dominionResultNameWidth, rowHeight, TextAlignmentOptions.MidlineLeft);
                for (int r = 0; r < rounds; r++)
                {
                    bool won = DominionHistory.WinnerOfRound(history, r) == team; // a tied round has no winner, so no cell is bold
                    string points = DominionHistory.PointsOf(history, r, team).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    cells.Add(won ? "*" + points + "*" : points);
                    Cell(row.transform, points, won ? kit.Bold : kit.Body, size, theme.lobbyOffWhiteColor, theme.dominionResultCellWidth, rowHeight, TextAlignmentOptions.Midline);
                }
                TableRows.Add(string.Join("|", cells));
            }

            TextMeshProUGUI note = kit.Text(body, "Note", theme.dominionResultNote, kit.Body, theme.dominionResultNoteSize, theme.dominionDimColor, TextAlignmentOptions.Midline);
            note.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Size(note.gameObject, -1f, theme.dominionResultNoteSize * 1.5f);
        }

        private HorizontalLayoutGroup Row(Transform parent, string name)
        {
            HorizontalLayoutGroup row = LobbyUiKit.HGroup(parent, name, 16f, TextAnchor.MiddleCenter);
            row.childForceExpandWidth = false;
            return row;
        }

        private void Cell(Transform row, string text, TMP_FontAsset font, float size, Color colour, float width, float height, TextAlignmentOptions align)
        {
            TextMeshProUGUI label = kit.Text(row, "Cell", text, font, size, colour, align);
            label.overflowMode = TextOverflowModes.Overflow;
            LobbyUiKit.Size(label.gameObject, width, height);
        }

        private static Color Pick(Color[] colours, int index) =>
            colours != null && colours.Length > 0 ? colours[Mathf.Clamp(index, 0, colours.Length - 1)] : Color.white;
    }
}
