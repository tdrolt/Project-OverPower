using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>One Dominion round as the master's notes tell it (DominionMarkerNotes writes them).</summary>
    public sealed class DominionRound
    {
        public int Number;
        public double? BreakStartT;
        public double? StartT;
        public double? EndT;
        public double? OvertimeStartT;
        public int[] OvertimeTeams = Array.Empty<int>();
        /// <summary>The team that took the round, the teams that shared it, or none (tied, cut short, or still to be settled).</summary>
        public int[] Winners = Array.Empty<int>();
        /// <summary>One of the DominionRoundTimeline outcome words.</summary>
        public string Outcome = "";
        /// <summary>Each team's points at the round's end, by team id.</summary>
        public Dictionary<int, int> Points = new Dictionary<int, int>();
    }

    /// <summary>A sudden death: the match's own (after the rounds), or one round's (a shared overtime that would have handed out the match).</summary>
    public sealed class DominionSuddenDeath
    {
        public double StartT;
        public int[] Teams = Array.Empty<int>();
        public int Replays;
        public double? EndT;
        public int Winner = -1;
        /// <summary>The round it was played for; 0 for the match's own.</summary>
        public int ForRound;
    }

    /// <summary>A bounty (Zone the zone taken) or a centre payout (Zone -1; Team -1 when the centre paid nobody).</summary>
    public sealed class DominionPayout
    {
        public double T;
        public int Zone = -1;
        public int Team = -1;
        public int Points;
    }

    /// <summary>
    /// A Dominion match read back from the master's marker notes: the rounds, overtime, sudden death, the winner, the bounties and the centre's
    /// payouts. Pure text reading; notes that are not Dominion's are ignored, and the same note at the same instant twice (two masters around a
    /// handover) counts once.
    /// </summary>
    public sealed class DominionRoundTimeline
    {
        public const string Won = "won";
        public const string Tied = "tied";
        public const string Shared = "shared";
        public const string CutShort = "cut short";
        public const string ToSuddenDeath = "sudden death";
        public const string WonInSuddenDeath = "won in sudden death";

        public readonly List<DominionRound> Rounds = new List<DominionRound>();
        public DominionSuddenDeath SuddenDeath;
        public double? OverT;
        public int Winner = -1;
        public readonly Dictionary<int, int> Wins = new Dictionary<int, int>();
        public readonly List<DominionPayout> Bounties = new List<DominionPayout>();
        public readonly List<DominionPayout> CentrePayouts = new List<DominionPayout>();

        public static DominionRoundTimeline From(IEnumerable<(double T, string Note)> notes)
        {
            var timeline = new DominionRoundTimeline();
            if (notes == null) return timeline;
            var seen = new HashSet<(double, string)>();
            foreach ((double t, string note) in notes.OrderBy(n => n.T))
            {
                if (note == null || !note.StartsWith("dominion ", StringComparison.Ordinal)) continue;
                if (!seen.Add((t, note))) continue;
                timeline.Read(t, note.Split(' '));
            }
            timeline.Rounds.Sort((a, b) => a.Number.CompareTo(b.Number));
            return timeline;
        }

        /// <summary>How the match ended, in the report's words: "others left", "sudden death", "two round wins" ("N round wins" for another
        /// target), or "lead after round N". "" while no winner is known.</summary>
        public string HowEnded(int roundsToWin)
        {
            if (Winner < 0) return "";
            if (Rounds.Any(r => r.Outcome == CutShort)) return "others left";
            if (SuddenDeath != null && SuddenDeath.ForRound == 0 && SuddenDeath.Winner == Winner) return "sudden death";
            int wins = Wins.TryGetValue(Winner, out int w) ? w : 0;
            if (roundsToWin > 0 && wins >= roundsToWin)
                return roundsToWin == 2 ? "two round wins" : roundsToWin.ToString(CultureInfo.InvariantCulture) + " round wins";
            int last = Rounds.Count > 0 ? Rounds.Max(r => r.Number) : 0;
            return "lead after round " + last.ToString(CultureInfo.InvariantCulture);
        }

        private DominionRound RoundOf(int number)
        {
            DominionRound round = Rounds.FirstOrDefault(r => r.Number == number);
            if (round != null) return round;
            round = new DominionRound { Number = number };
            Rounds.Add(round);
            return round;
        }

        private void Read(double t, string[] w)
        {
            // w[0] is "dominion".
            if (Is(w, 1, "break") && Is(w, 2, "start") && Is(w, 3, "before") && Is(w, 4, "round") && TryInt(w, 5, out int next))
            {
                RoundOf(next).BreakStartT = t;
                return;
            }
            if (Is(w, 1, "round") && TryInt(w, 2, out int number))
            {
                ReadRound(t, number, w);
                return;
            }
            if (Is(w, 1, "sudden") && Is(w, 2, "death") && Is(w, 4, "teams"))
            {
                int[] teams = Teams(w, 5);
                if (Is(w, 3, "start"))
                {
                    DominionRound pending = Rounds.LastOrDefault(r => r.Outcome == ToSuddenDeath && r.Winners.Length == 0);
                    SuddenDeath = new DominionSuddenDeath { StartT = t, Teams = teams, ForRound = pending != null ? pending.Number : 0 };
                }
                else if (Is(w, 3, "replay") && SuddenDeath != null)
                {
                    SuddenDeath.Replays++;
                    SuddenDeath.Teams = teams;
                }
                return;
            }
            if (Is(w, 1, "match") && Is(w, 2, "over") && Is(w, 3, "winner") && Is(w, 4, "team") && TryInt(w, 5, out int winner))
            {
                OverT = t;
                Winner = winner;
                Wins.Clear();
                foreach (KeyValuePair<int, int> pair in PerTeam(w, 6, "wins"))
                    Wins[pair.Key] = pair.Value;
                if (SuddenDeath != null && !SuddenDeath.EndT.HasValue)
                {
                    SuddenDeath.EndT = t;
                    SuddenDeath.Winner = winner;
                }
                return;
            }
            if (Is(w, 1, "bounty") && Is(w, 2, "zone") && TryInt(w, 3, out int zone) && Is(w, 4, "team") && TryInt(w, 5, out int team) && TryPlus(w, 6, out int points))
            {
                Bounties.Add(new DominionPayout { T = t, Zone = zone, Team = team, Points = points });
                return;
            }
            if (Is(w, 1, "centre") && Is(w, 2, "payout"))
            {
                if (Is(w, 3, "nobody"))
                    CentrePayouts.Add(new DominionPayout { T = t, Team = -1, Points = 0 });
                else if (Is(w, 3, "team") && TryInt(w, 4, out int paid) && TryPlus(w, 5, out int lump))
                    CentrePayouts.Add(new DominionPayout { T = t, Team = paid, Points = lump });
            }
        }

        private void ReadRound(double t, int number, string[] w)
        {
            DominionRound round = RoundOf(number);
            if (Is(w, 3, "start"))
            {
                round.StartT = t;
                return;
            }
            if (Is(w, 3, "overtime") && Is(w, 4, "start") && Is(w, 5, "teams"))
            {
                round.OvertimeStartT = t;
                round.OvertimeTeams = Teams(w, 6);
                return;
            }
            if (Is(w, 3, "won") && Is(w, 4, "in") && Is(w, 5, "sudden") && Is(w, 6, "death") && Is(w, 7, "by") && Is(w, 8, "team") && TryInt(w, 9, out int sdWinner))
            {
                round.Outcome = WonInSuddenDeath;
                round.Winners = new[] { sdWinner };
                round.Points = PerTeam(w, 10, "points");
                if (SuddenDeath != null && !SuddenDeath.EndT.HasValue)
                {
                    SuddenDeath.EndT = t;
                    SuddenDeath.Winner = sdWinner;
                }
                return;
            }
            if (!Is(w, 3, "end")) return;

            round.EndT = t;
            if (Is(w, 4, "winner") && Is(w, 5, "team") && TryInt(w, 6, out int winner))
            {
                round.Outcome = Won;
                round.Winners = new[] { winner };
                round.Points = PerTeam(w, 7, "points");
            }
            else if (Is(w, 4, "tied"))
            {
                round.Outcome = Tied;
                round.Points = PerTeam(w, 5, "points");
            }
            else if (Is(w, 4, "cut") && Is(w, 5, "short"))
            {
                round.Outcome = CutShort;
                round.Points = PerTeam(w, 6, "points");
            }
            else if (Is(w, 4, "shared") && Is(w, 5, "teams"))
            {
                int[] sharers = Teams(w, 6);
                bool toSuddenDeath = Is(w, 7, "sudden");
                round.Outcome = toSuddenDeath ? ToSuddenDeath : Shared;
                round.Winners = toSuddenDeath ? Array.Empty<int>() : sharers;
                round.Points = PerTeam(w, toSuddenDeath ? 12 : 7, "points");
            }
        }

        // ---------------------------------------------------------------- words

        private static bool Is(string[] w, int i, string word) => i < w.Length && w[i] == word;

        private static bool TryInt(string[] w, int i, out int value)
        {
            value = 0;
            return i < w.Length && int.TryParse(w[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryPlus(string[] w, int i, out int value)
        {
            value = 0;
            return i < w.Length && w[i].StartsWith("+", StringComparison.Ordinal)
                && int.TryParse(w[i].Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>"0,1" at w[i] as team ids (an empty or missing list reads none).</summary>
        private static int[] Teams(string[] w, int i)
        {
            if (i >= w.Length || w[i].Length == 0) return Array.Empty<int>();
            var teams = new List<int>();
            foreach (string part in w[i].Split(','))
                if (int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int team)) teams.Add(team);
            return teams.ToArray();
        }

        /// <summary>From w[i] = <paramref name="label"/>: the " team0 120 team1 80" pairs that follow.</summary>
        private static Dictionary<int, int> PerTeam(string[] w, int i, string label)
        {
            var values = new Dictionary<int, int>();
            if (!Is(w, i, label)) return values;
            for (int k = i + 1; k + 1 < w.Length; k += 2)
            {
                if (!w[k].StartsWith("team", StringComparison.Ordinal)) break;
                if (!int.TryParse(w[k].Substring(4), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int team)) break;
                if (!int.TryParse(w[k + 1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)) break;
                values[team] = value;
            }
            return values;
        }
    }
}
