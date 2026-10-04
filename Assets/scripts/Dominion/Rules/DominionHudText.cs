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
    /// The words and numbers of Dominion's round HUD, break card, centre countdown, sudden-death banner and result (Task 9), as pure functions:
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
        /// (dCtr: prev -> now, both written) and one team's points rose by at least the payout. -1 when nothing like that happened (first read,
        /// no change, payout of 0, or the payout went to nobody). A joiner's first read never flashes (prevCentreMs 0).</summary>
        public static int CentrePayoutTeam(int prevCentreMs, int centreMs, int[] prevPoints, int[] points, int centrePoints)
        {
            if (centrePoints <= 0 || prevCentreMs == 0 || centreMs == 0 || prevCentreMs == centreMs || prevPoints == null || points == null) return -1;
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

        /// <summary>True when the match was settled by sudden death: every round was played and nobody reached the wins needed.</summary>
        public static bool WonInSuddenDeath(int winner, int[] wins, int roundsToWin) => winner >= 0 && WinsOf(wins, winner) < roundsToWin;

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

        /// <summary>How many rounds the history holds.</summary>
        public static int RoundCount(int[] history) => history == null ? 0 : history.Length / DominionKeys.TeamSlots;

        /// <summary>A team's points in a round (0-based round); 0 for a cell that is not there.</summary>
        public static int PointsOf(int[] history, int round, int team)
        {
            if (history == null || round < 0 || team < 0 || team >= DominionKeys.TeamSlots) return 0;
            int index = round * DominionKeys.TeamSlots + team;
            return index < history.Length ? history[index] : 0;
        }

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
