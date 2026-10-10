using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Overpower.Dominion
{
    /// <summary>The telemetry marker notes of a Dominion match: the points, bounties and the centre (per tick), and the story round by round (round
    /// start, round end with the points, break start, sudden death start and replays, match over). Pure strings; the director drops them.</summary>
    public static class DominionMarkerNotes
    {
        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

        public static string Bounty(int zone, int team, int points) => "dominion bounty zone " + N(zone) + " team " + N(team) + " +" + N(points);

        /// <summary>team -1 = nobody was holding the centre.</summary>
        public static string CentrePayout(int team, int points) =>
            team < 0 ? "dominion centre payout nobody" : "dominion centre payout team " + N(team) + " +" + N(points);

        public static string RoundStart(int round) => "dominion round " + N(round) + " start";

        /// <summary>winner -1 = the round was tied (it counts for nobody). The points are listed for the teams of the match only (a missing slot reads 0).</summary>
        public static string RoundEnd(int round, int winner, int[] teams, int[] points) =>
            "dominion round " + N(round) + " end " + (winner < 0 ? "tied" : "winner team " + N(winner)) + " points" + PerTeam(teams, points);

        /// <summary>The round's clock ran out within the lead: these teams play the extra minute.</summary>
        public static string OvertimeStart(int round, int[] teams) => "dominion round " + N(round) + " overtime start teams " + TeamList(teams);

        /// <summary>An overtime ran out with nobody a lead ahead: every team in sharedTeams got a round win. The points are listed for the match's teams.</summary>
        public static string RoundEndShared(int round, int[] sharedTeams, int[] teams, int[] points) =>
            "dominion round " + N(round) + " end shared teams " + TeamList(sharedTeams) + " points" + PerTeam(teams, points);

        /// <summary>A round that ended without being scored: the others left and the last team standing took the match, so no team's wins went up.</summary>
        public static string RoundCutShort(int round, int[] teams, int[] points) =>
            "dominion round " + N(round) + " end cut short points" + PerTeam(teams, points);

        /// <summary>The team whose wins went up between two readings (-1 when none did). A missing array reads as no wins.</summary>
        public static int TeamWhoseWinsWentUp(int[] before, int[] after)
        {
            if (after == null) return -1;
            for (int team = 0; team < after.Length; team++)
                if (after[team] > (before != null && team < before.Length ? before[team] : 0)) return team;
            return -1;
        }

        /// <summary>The break that leads to <paramref name="nextRound"/> (the one before round 1 included).</summary>
        public static string BreakStart(int nextRound) => "dominion break start before round " + N(nextRound);

        /// <summary>An overtime ran out shared, but a round win each would have handed out the match (A65): the sharers play sudden death for the round.</summary>
        public static string RoundToSuddenDeath(int round, int[] sharedTeams, int[] teams, int[] points) =>
            "dominion round " + N(round) + " end shared teams " + TeamList(sharedTeams) + " sudden death for the round points" + PerTeam(teams, points);

        /// <summary>A round's own sudden death is over: its winner alone took the round win.</summary>
        public static string RoundWonInSuddenDeath(int round, int winner, int[] teams, int[] points) =>
            "dominion round " + N(round) + " won in sudden death by team " + N(winner) + " points" + PerTeam(teams, points);

        public static string SuddenDeathStart(int[] teams) => "dominion sudden death start teams " + TeamList(teams);

        /// <summary>Everyone fell in the same instant: the circle starts over, between these teams.</summary>
        public static string SuddenDeathReplay(int[] teams) => "dominion sudden death replay teams " + TeamList(teams);

        public static string MatchOver(int winner, int[] teams, int[] wins) =>
            "dominion match over winner team " + N(winner) + " wins" + PerTeam(teams, wins);

        /// <summary>The markers to drop when the room goes from (prevRound, prevStage, prevSuddenDeathMs) to <paramref name="room"/>, in the order the
        /// story happened. Only the master drops them (every client sees the same edge, and the report merges every client's file, so one writer keeps
        /// each event once); <paramref name="teamsInMatch"/> names the teams that count in the lines.</summary>
        public static List<string> ForEdge(bool isMaster, int prevRound, DominionStage prevStage, int prevSuddenDeathMs, DominionRoomState room, int[] teamsInMatch, int[] prevWins)
        {
            var notes = new List<string>();
            if (!isMaster) return notes;

            // A round that stopped being a round: scored (its points are still in the room through the break) or cut short by the others leaving.
            // The master's own record of the round (dHistW) says which, the same record the result table bolds from. A room without the record
            // (from before it existed) is read from the wins: whose went up won it, a scored round where nobody's did was tied, an Over where nobody's did was cut short.
            if (DominionRules.IsRoundPlay(prevStage) && room.Stage == DominionStage.SuddenDeath && room.SuddenDeathRound == prevRound && prevRound >= 1)
                notes.Add(RoundToSuddenDeath(prevRound, room.SuddenDeathTeams ?? teamsInMatch, teamsInMatch, room.Points));
            else if (DominionRules.IsRoundPlay(prevStage) && !DominionRules.IsRoundPlay(room.Stage) && prevRound >= 1)
            {
                int recorded = room.HistoryWinners != null && prevRound - 1 < room.HistoryWinners.Length ? room.HistoryWinners[prevRound - 1] : int.MinValue;
                if (recorded >= DominionHistory.SharedFlag && !DominionHistory.WonInSuddenDeath(recorded)) notes.Add(RoundEndShared(prevRound, DominionHistory.DecodeWinners(recorded), teamsInMatch, room.Points));
                else if (recorded >= 0) notes.Add(RoundEnd(prevRound, recorded, teamsInMatch, room.Points));
                else if (recorded == DominionHistory.CutShort) notes.Add(RoundCutShort(prevRound, teamsInMatch, room.Points));
                else if (recorded == -1) notes.Add(RoundEnd(prevRound, -1, teamsInMatch, room.Points));
                else
                {
                    int winner = TeamWhoseWinsWentUp(prevWins, room.Wins);
                    if (winner >= 0) notes.Add(RoundEnd(prevRound, winner, teamsInMatch, room.Points));
                    else if (room.Stage == DominionStage.Over) notes.Add(RoundCutShort(prevRound, teamsInMatch, room.Points));
                    else notes.Add(RoundEnd(prevRound, -1, teamsInMatch, room.Points));
                }
            }

            // A round's own sudden death ended: the only sudden-death verdict that raises a team's wins (unknown without the wins before it).
            int roundSuddenDeathWinner = prevStage == DominionStage.SuddenDeath && prevWins != null ? TeamWhoseWinsWentUp(prevWins, room.Wins) : -1;
            if (roundSuddenDeathWinner >= 0) notes.Add(RoundWonInSuddenDeath(prevRound, roundSuddenDeathWinner, teamsInMatch, room.Points));

            switch (room.Stage)
            {
                case DominionStage.Break:
                    if (prevStage != DominionStage.Break) notes.Add(BreakStart(room.Round));
                    break;
                case DominionStage.Round:
                    if (prevStage == DominionStage.Break) notes.Add(RoundStart(room.Round));
                    break;
                case DominionStage.Overtime:
                    if (prevStage == DominionStage.Round) notes.Add(OvertimeStart(room.Round, room.OvertimeTeams ?? teamsInMatch));
                    break;
                case DominionStage.SuddenDeath:
                    if (DominionRoomWrites.IsSuddenDeathStart(prevStage, prevSuddenDeathMs, room.Stage, room.SuddenDeathMs))
                    {
                        int[] playing = room.SuddenDeathTeams ?? teamsInMatch;
                        notes.Add(prevStage == DominionStage.SuddenDeath && roundSuddenDeathWinner < 0 ? SuddenDeathReplay(playing) : SuddenDeathStart(playing));
                    }
                    break;
                case DominionStage.Over:
                    if (prevStage != DominionStage.Over) notes.Add(MatchOver(room.Winner, teamsInMatch, room.Wins));
                    break;
            }
            return notes;
        }

        // " team0 120 team1 80": one entry per team of the match; a missing slot reads 0.
        private static string PerTeam(int[] teams, int[] values)
        {
            var sb = new StringBuilder();
            if (teams == null) return "";
            foreach (int team in teams)
                sb.Append(" team").Append(N(team)).Append(' ').Append(N(values != null && team >= 0 && team < values.Length ? values[team] : 0));
            return sb.ToString();
        }

        private static string TeamList(int[] teams) => teams == null ? "" : string.Join(",", System.Array.ConvertAll(teams, N));
    }
}
