using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Overpower.Lobby;

namespace Overpower.Dominion
{
    /// <summary>The words the break card uses to say what the next round opens in the shop (all from UiTheme).</summary>
    public readonly struct OpensTexts
    {
        public readonly string Format;          // "Round {0} opens: {1}"
        public readonly string FirstRoundLine;  // round 1 opens no weapon or armour tier, only the free abilities
        public readonly string WeaponFamily;    // "a weapon family"
        public readonly string WeaponUpgrade;   // "a weapon upgrade"
        public readonly string ArmorOne;        // "one armor upgrade"
        public readonly string ArmorMore;       // "{0} armor upgrades"
        public readonly string And;             // " and "
        public readonly string Nothing;         // "nothing new"

        public OpensTexts(string format, string firstRoundLine, string weaponFamily, string weaponUpgrade, string armorOne, string armorMore, string and, string nothing)
        {
            Format = format; FirstRoundLine = firstRoundLine; WeaponFamily = weaponFamily; WeaponUpgrade = weaponUpgrade;
            ArmorOne = armorOne; ArmorMore = armorMore; And = and; Nothing = nothing;
        }
    }

    /// <summary>
    /// The words and numbers of Dominion's round HUD, break card, centre countdown, sudden-death banner and result, as pure functions:
    /// the drawing code in UI/Dominion only places what these return. Formats come from UiTheme (a broken format falls back to the raw text, never
    /// an exception every frame). Times are the room's server clock in ms (an int that wraps), compared as unchecked(a - b).
    /// </summary>
    public static class DominionHudText
    {
        /// <summary>Whole seconds left until the stage end, rounded up (so the last second reads 1, never 0 until it is over). 0 when there is no end
        /// (endMs 0, sudden death), when the server clock has not synced (nowMs 0), or when it has passed.</summary>
        public static int SecondsLeft(int endMs, int nowMs)
        {
            if (endMs == 0 || nowMs == 0) return 0;
            int remaining = unchecked(endMs - nowMs);
            return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining / 1000.0);
        }

        /// <summary>m:ss for a clock: 102 -> "1:42", 58 -> "0:58", 180 -> "3:00". Negative reads as 0:00.</summary>
        public static string Clock(int seconds)
        {
            seconds = Math.Max(0, seconds);
            return (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>"ROUND 2 OF 3" from a format with {0} the round and {1} the number of rounds.</summary>
        public static string RoundLabel(string format, int round, int maxRounds) => Fmt(format, round, maxRounds);

        /// <summary>The big line of the break card: "PURPLE WINS" for the round's single leader (team name capitalised), or the tied text when two
        /// teams share the top (or nobody scored). winner is DominionRules.RoundWinner of the round's points: -1 = tied.</summary>
        public static string BreakHeadline(int winner, string[] teamNames, string winsFormat, string tiedText) =>
            winner < 0 ? tiedText : Fmt(winsFormat, LobbyRoomRules.TeamName(teamNames, winner).ToUpperInvariant());

        /// <summary>Team names in capitals, each in its colour (rich text, colourHex[team] as RRGGBB; null = bare), joined by <paramref name="separator"/>
        /// with <paramref name="lastSeparator"/> before the last: "WHITE, PURPLE and CYAN".</summary>
        public static string TeamNameList(int[] teams, string[] teamNames, string[] colourHex, string separator, string lastSeparator)
        {
            var sb = new StringBuilder();
            int count = teams != null ? teams.Length : 0;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(i == count - 1 ? lastSeparator : separator);
                sb.Append(Coloured(LobbyRoomRules.TeamName(teamNames, teams[i]).ToUpperInvariant(), colourHex, teams[i]));
            }
            return sb.ToString();
        }

        /// <summary>The line under "SHARED ROUND": {0} in the format is the winners' names (TeamNameList).</summary>
        public static string SharedRoundLine(string format, int[] winners, string[] teamNames, string[] colourHex, string separator, string lastSeparator) =>
            Fmt(format, TeamNameList(winners, teamNames, colourHex, separator, lastSeparator));

        /// <summary>The match score in round wins. Two teams: "WHITE 1 - 1 PURPLE" (a name on the outer side of its number); three or more: each number
        /// keeps its own name ("WHITE 1 - PURPLE 1 - CYAN 0") so no number is left guessing. Each part is in its team's colour.</summary>
        public static string MatchScoreLine(int[] teams, int[] wins, string[] teamNames, string[] colourHex, string dash)
        {
            var parts = new List<string>();
            if (teams == null) return "";
            for (int i = 0; i < teams.Length; i++)
            {
                string name = LobbyRoomRules.TeamName(teamNames, teams[i]).ToUpperInvariant();
                string number = (teams[i] >= 0 && wins != null && teams[i] < wins.Length ? wins[teams[i]] : 0).ToString(CultureInfo.InvariantCulture);
                bool nameAfter = teams.Length == 2 && i == 1;
                parts.Add(Coloured(nameAfter ? number + " " + name : name + " " + number, colourHex, teams[i]));
            }
            return string.Join(dash, parts);
        }

        /// <summary>The teams one round win from the match (roundsToWin - 1), lowest first. A team already at the target has won, not reached match point.</summary>
        public static int[] MatchPointTeams(int[] teams, int[] wins, int roundsToWin)
        {
            var result = new List<int>();
            if (teams != null)
                foreach (int team in teams)
                    if (roundsToWin >= 2 && wins != null && team >= 0 && team < wins.Length && wins[team] == roundsToWin - 1) result.Add(team);
            result.Sort();
            return result.ToArray();
        }

        /// <summary>"MATCH POINT: WHITE, PURPLE" from a format with {0} the names; empty when no team is on match point.</summary>
        public static string MatchPointLine(string format, int[] pointTeams, string[] teamNames, string[] colourHex, string separator) =>
            pointTeams == null || pointTeams.Length == 0 ? "" : Fmt(format, TeamNameList(pointTeams, teamNames, colourHex, separator, separator));

        /// <summary>A pop that starts and ends at 1 and peaks at <paramref name="peak"/> halfway through <paramref name="duration"/> seconds; 1 outside it.</summary>
        public static float PulseScale(float elapsed, float duration, float peak)
        {
            if (duration <= 0f || elapsed <= 0f || elapsed >= duration) return 1f;
            return 1f + (peak - 1f) * (float)Math.Sin(Math.PI * elapsed / duration);
        }

        public enum PulseStep { Stop, Keep, Start }

        /// <summary>What a redraw of the break card does to the pulse: a card that is not a shared round stops it, a shared round that already pulsed
        /// (<paramref name="pulsedRound"/>) keeps it as it is, and the first draw of a shared round starts it.</summary>
        public static PulseStep PulseStepOnRedraw(bool shared, int round, int pulsedRound) =>
            !shared ? PulseStep.Stop : round == pulsedRound ? PulseStep.Keep : PulseStep.Start;

        /// <summary>Does this round-win dot pulse when the shared-round card appears: the newest win of a team that shared the round (dot number
        /// <paramref name="slot"/>, 0-based, of a team that now has <paramref name="wins"/> wins).</summary>
        public static bool PulsesDot(int[] winners, int team, int slot, int wins)
        {
            if (winners == null || slot != wins - 1) return false;
            foreach (int w in winners) if (w == team) return true;
            return false;
        }

        private static string Coloured(string text, string[] colourHex, int team) =>
            colourHex != null && team >= 0 && team < colourHex.Length && !string.IsNullOrEmpty(colourHex[team]) ? "<color=#" + colourHex[team] + ">" + text + "</color>" : text;

        /// <summary>What the break's last line says. Until the last bigFromSeconds seconds it is the small card line ("ROUND 2 STARTS IN 14",
        /// big = false); from then on it is the big centre line ("Round 2 starts in 5…", big = true). {0} = the round, {1} = the seconds left.</summary>
        public static string BreakCountdown(string smallFormat, string bigFormat, int round, int secondsLeft, int bigFromSeconds, out bool big)
        {
            big = IsBigCountdown(secondsLeft, bigFromSeconds);
            return Fmt(big ? bigFormat : smallFormat, round, Math.Max(0, secondsLeft));
        }

        /// <summary>True for the last bigFromSeconds seconds of the break (and never at 0 seconds left: that is the round starting).</summary>
        public static bool IsBigCountdown(int secondsLeft, int bigFromSeconds) => secondsLeft > 0 && secondsLeft <= Math.Max(0, bigFromSeconds);

        /// <summary>What the next round opens in the shop, read off the same tables the shop uses (DominionConfig): what round <paramref name="round"/>
        /// allows that the round before did not. Round 1 has its own line (only the free abilities). A round that opens nothing new says so.
        /// The weapon part is a family when the tree opens to depth 1, an upgrade at depth 2 and up; the armour part counts the new upgrades.</summary>
        public static string OpensLine(int round, int[] depthByRound, int[] armorByRound, OpensTexts t)
        {
            if (round <= 1) return t.FirstRoundLine;
            int depthNow = Table(depthByRound, round), depthBefore = Table(depthByRound, round - 1);
            int armorNow = Table(armorByRound, round), armorBefore = Table(armorByRound, round - 1);
            var parts = new List<string>();
            if (depthNow > depthBefore) parts.Add("<b>" + (depthNow >= 2 ? t.WeaponUpgrade : t.WeaponFamily) + "</b>");
            int newArmor = armorNow - armorBefore;
            if (newArmor == 1) parts.Add("<b>" + t.ArmorOne + "</b>");
            else if (newArmor > 1) parts.Add("<b>" + Fmt(t.ArmorMore, newArmor) + "</b>");
            string what = parts.Count == 0 ? t.Nothing : string.Join(t.And, parts);
            return Fmt(t.Format, round, what);
        }

        private static int Table(int[] table, int round)
        {
            if (table == null || table.Length == 0) return 0;
            return table[Math.Min(Math.Max(round, 1), table.Length) - 1];
        }

        /// <summary>Does the centre countdown show: a 3v3v3 match with a payout time written and a synced clock. In an overtime it hides when the next payout falls
        /// after the overtime's end (the round is decided by then, so the label would count down to a payout that is never paid).</summary>
        public static bool CentreLabelShown(bool threeTeams, DominionStage stage, int centreMs, int stageEndMs, int nowMs)
        {
            if (!threeTeams || centreMs == 0 || nowMs == 0) return false;
            // A payout exactly at the end is still paid (the buzzer's payout, A18); one after it never is.
            return stage != DominionStage.Overtime || unchecked(centreMs - stageEndMs) <= 0;
        }

        /// <summary>"CENTRE +200 IN 12": {0} = the payout, {1} = seconds to it.</summary>
        public static string CentreLine(string format, int points, int secondsLeft) => Fmt(format, points, Math.Max(0, secondsLeft));

        /// <summary>"Cyan holds it", or "Nobody holds it" when the centre is neutral (holder -1).</summary>
        public static string HolderLine(int holder, string[] teamNames, string holdsFormat, string nobodyText) =>
            holder < 0 ? nobodyText : Fmt(holdsFormat, LobbyRoomRules.TeamName(teamNames, holder));

        /// <summary>"CIRCLE SHRINKS · 0:41" under the minimap while the circle still moves; empty once it has stopped (no seconds left).</summary>
        public static string ShrinkLine(string format, float secondsUntilStopped)
        {
            if (secondsUntilStopped <= 0f) return "";
            return Fmt(format, Clock((int)Math.Ceiling(secondsUntilStopped)));
        }

        /// <summary>The sudden-death banner shows in full for the first seconds after the circle's start was written, then gives way to the small
        /// line. sinceStartMs is how long ago dSd was written (negative = the get-ready beat is still on, which also shows it in full).</summary>
        public static bool BannerIsFull(int sinceStartMs, float fullSeconds) => sinceStartMs < (int)Math.Round(Math.Max(0f, fullSeconds) * 1000f);

        // ---------------------------------------------------------------- the points flash

        /// <summary>The team a centre payout just went to, seen as a change between two reads of the room: the centre's next-payout time moved
        /// (dCtr: prev -> now, both written) and the centre's holder (its owner on this client's copy of the territory, -1 = not known) was paid, i.e.
        /// its points rose by at least the payout. Only the holder flashes: another team's zones can rise by more than the payout in the same write.
        /// When the holder is not known here (this client's territory copy lags the room) the team with the biggest rise of at least the payout is
        /// taken instead. -1 when nothing like that happened (first read, no change, payout of 0, or the payout went to nobody). A joiner's first read
        /// never flashes (prevCentreMs 0).</summary>
        public static int CentrePayoutTeam(int prevCentreMs, int centreMs, int[] prevPoints, int[] points, int centrePoints, int holder)
        {
            if (centrePoints <= 0 || prevCentreMs == 0 || centreMs == 0 || prevCentreMs == centreMs || prevPoints == null || points == null) return -1;
            if (holder >= 0)
                return holder < points.Length && holder < prevPoints.Length && points[holder] - prevPoints[holder] >= centrePoints ? holder : -1;
            int best = -1, bestRise = centrePoints - 1;
            for (int team = 0; team < points.Length && team < prevPoints.Length; team++)
            {
                int rise = points[team] - prevPoints[team];
                if (rise > bestRise) { best = team; bestRise = rise; }
            }
            return best;
        }

        /// <summary>"+200 CYAN": {0} = the points, {1} = the team's name in capitals.</summary>
        public static string FlashText(string format, int points, string[] teamNames, int team) =>
            Fmt(format, points, LobbyRoomRules.TeamName(teamNames, team).ToUpperInvariant());

        // ---------------------------------------------------------------- the result

        /// <summary>"PURPLE WINS 2–1". The score is the winner's round wins first, then the other teams' wins, highest first, joined by the separator
        /// (3v3v3 reads 2–1–0). A match decided in sudden death (nobody reached the wins to take it, so the circle did) has no score: its own
        /// format ("PURPLE WINS IN SUDDEN DEATH").</summary>
        public static string ResultHeadline(int winner, int[] wins, int[] teamsInMatch, string[] teamNames, string format, string suddenDeathFormat,
                                            string separator, bool wonInSuddenDeath)
        {
            string name = LobbyRoomRules.TeamName(teamNames, winner).ToUpperInvariant();
            return wonInSuddenDeath ? Fmt(suddenDeathFormat, name) : Fmt(format, name, ScoreLine(winner, wins, teamsInMatch, separator));
        }

        /// <summary>The winner's round wins, then the others' highest first: "2–1" in 2v2, "2–1–0" in 3v3v3.</summary>
        public static string ScoreLine(int winner, int[] wins, int[] teamsInMatch, string separator)
        {
            var others = new List<int>();
            if (teamsInMatch != null)
                foreach (int team in teamsInMatch)
                    if (team != winner) others.Add(WinsOf(wins, team));
            others.Sort((a, b) => b.CompareTo(a));
            var text = new StringBuilder(WinsOf(wins, winner).ToString(CultureInfo.InvariantCulture));
            foreach (int w in others) text.Append(separator).Append(w.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static int WinsOf(int[] wins, int team) => wins != null && team >= 0 && team < wins.Length ? wins[team] : 0;

        /// <summary>True when the match was settled by sudden death: the room holds a circle start (dSd, non-zero) and the winner did not reach the wins
        /// to take it. Both are needed: a match that ended 1-0-0 because the others left (A7), or with the last team standing mid-round (A3), has fewer
        /// wins than the match needs but no circle; a round won in its own sudden death (A65) that brings the winner to the target shows the score.
        /// A round's sudden death that leads into a break removes dSd, so a later plain win is never called sudden death.</summary>
        public static bool WonInSuddenDeath(int winner, int suddenDeathMs, int[] wins, int roundsToWin) =>
            winner >= 0 && suddenDeathMs != 0 && WinsOf(wins, winner) < roundsToWin;

        /// <summary>"DOMINION 3v3v3" / "DOMINION 2v2" from a format with {0} = the size.</summary>
        public static string ModeLine(string format, int teamCount) => Fmt(format, teamCount >= 3 ? "3v3v3" : "2v2");

        // ---------------------------------------------------------------- safe formatting

        private static string Fmt(string format, params object[] args)
        {
            if (format == null) return "";
            try { return string.Format(CultureInfo.InvariantCulture, format, args); }
            catch (FormatException) { return format; }
        }
    }

    /// <summary>
    /// Each round's final points, kept in the room as dHist (an int[] of rounds x TeamSlots, flattened): the break clears dPts at the next round's
    /// start, so the result table could not be built from the room without it. The master appends one round's points in the same write that scores
    /// the round. Pure helpers: appending, reading a cell, and who won a round (the same DominionRules.RoundWinner the master used).
    /// </summary>
    public static class DominionHistory
    {
        /// <summary>The history with one more round: the old values, then the round's points padded or cut to the team slots. Never edits the
        /// input (a Photon array is shared).</summary>
        public static int[] Append(int[] history, int[] roundPoints)
        {
            int old = history != null ? history.Length - history.Length % DominionKeys.TeamSlots : 0;
            var result = new int[old + DominionKeys.TeamSlots];
            if (history != null) Array.Copy(history, result, old);
            for (int team = 0; team < DominionKeys.TeamSlots; team++)
                result[old + team] = roundPoints != null && team < roundPoints.Length ? roundPoints[team] : 0;
            return result;
        }

        /// <summary>dHistW value for a round nobody won because the match ended in it: the others left and the last team standing took the match. A tied
        /// round that was scored is -1 instead, so the match log can tell the two apart.</summary>
        public const int CutShort = -2;

        /// <summary>A dHistW entry at or above this is a SHARED round (A50: an overtime that ran out): the flag plus a bit per winning team (bit n = team n).
        /// Below it the entry is the old one: a team id, -1 for a tied round, CutShort for a cut-short one, so a room written before overtime reads as it did.</summary>
        public const int SharedFlag = 8;

        /// <summary>The dHistW entry for a round's winners: none = -1 (tied), one = its team id, several = SharedFlag plus a bit per team.</summary>
        public static int EncodeWinners(int[] winners)
        {
            if (winners == null || winners.Length == 0) return -1;
            if (winners.Length == 1) return winners[0];
            int mask = 0;
            foreach (int team in winners)
                if (team >= 0 && team < DominionKeys.TeamSlots) mask |= 1 << team;
            return SharedFlag | mask;
        }

        /// <summary>The teams a dHistW entry names, lowest first: a shared entry's teams, a sudden-death entry's or a team id's one team, nobody for a tie (-1) or a cut-short round.</summary>
        public static int[] DecodeWinners(int entry)
        {
            var teams = new List<int>();
            if (WonInSuddenDeath(entry)) teams.Add(entry - SuddenDeathFlag);
            else if (entry >= SharedFlag)
            {
                for (int team = 0; team < DominionKeys.TeamSlots; team++)
                    if ((entry & (1 << team)) != 0) teams.Add(team);
            }
            else if (entry >= 0) teams.Add(entry);
            return teams.ToArray();
        }

        /// <summary>The winners list with one more round (an EncodeWinners entry, or CutShort for a cut-short round). Never edits the input.</summary>
        public static int[] AppendWinner(int[] winners, int winner)
        {
            int old = winners != null ? winners.Length : 0;
            var result = new int[old + 1];
            if (winners != null) Array.Copy(winners, result, old);
            result[old] = winner;
            return result;
        }

        /// <summary>A dHistW entry at or above this is a round won in its own sudden death (A65): the flag plus the winning team's id. Above every shared
        /// entry (SharedFlag plus three team bits tops out at 15), so the two never overlap.</summary>
        public const int SuddenDeathFlag = 16;

        /// <summary>The dHistW entry for a round its sudden death decided: one winner, marked so the break card can say how it was won.</summary>
        public static int EncodeSuddenDeathWinner(int team) => SuddenDeathFlag + team;

        /// <summary>True for a dHistW entry of a round won in its own sudden death.</summary>
        public static bool WonInSuddenDeath(int entry) => entry >= SuddenDeathFlag;

        /// <summary>Was the round the break card is about (see WinnersOfFinishedRound) won in its own sudden death.</summary>
        public static bool FinishedRoundWonInSuddenDeath(DominionRoomState state) =>
            state.Round >= 2 && state.HistoryWinners != null && state.Round - 2 < state.HistoryWinners.Length && WonInSuddenDeath(state.HistoryWinners[state.Round - 2]);

        public static int RoundCount(int[] history) => history == null ? 0 : history.Length / DominionKeys.TeamSlots;

        /// <summary>A team's points in a round (0-based round); 0 for a cell that is not there.</summary>
        public static int PointsOf(int[] history, int round, int team)
        {
            if (history == null || round < 0 || team < 0 || team >= DominionKeys.TeamSlots) return 0;
            int index = round * DominionKeys.TeamSlots + team;
            return index < history.Length ? history[index] : 0;
        }

        /// <summary>The teams that won a round (0-based) as the room recorded it (dHistW): one, several for a shared round, none for a tied or cut-short one.
        /// A room with no recorded entry for that round (from before dHistW) falls back to the points leader.</summary>
        public static int[] WinnersOfRound(int[] history, int[] winners, int round)
        {
            if (winners != null && round >= 0 && round < winners.Length) return DecodeWinners(winners[round]);
            int leader = WinnerOfRound(history, round);
            return leader >= 0 ? new[] { leader } : new int[0];
        }

        /// <summary>The teams that won the round the break card is about. The break after round N has the room's Round = N + 1, and round N is the history's
        /// 0-based entry N - 1, which is Round - 2. The first break (Round 1) follows no round, so it names nobody. One pure answer for the HUD host, so the
        /// "minus 2" is tested and not an inline sum.</summary>
        public static int[] WinnersOfFinishedRound(DominionRoomState state) =>
            state.Round < 2 ? new int[0] : WinnersOfRound(state.History, state.HistoryWinners, state.Round - 2);

        /// <summary>The team that won a round (0-based) on its points, or -1 for a tied round (nobody gets bold in the table).</summary>
        public static int WinnerOfRound(int[] history, int round)
        {
            if (round < 0 || round >= RoundCount(history)) return -1;
            var points = new int[DominionKeys.TeamSlots];
            for (int team = 0; team < points.Length; team++) points[team] = PointsOf(history, round, team);
            return DominionRules.RoundWinner(points);
        }
    }
}
