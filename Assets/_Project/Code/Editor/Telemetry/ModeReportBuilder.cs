using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>
    /// Log -> ModeReport: which mode the match was, its players and bots, its tabs and their sections, the settings that matter for the mode, and
    /// the mode's own sections (Dominion's rounds, points race, zones, respawn shields, builds and sudden death; Conquest's centre scan and map
    /// shrink). No IO. The decisions are the pure rules it asks: ReportModes, ReportMaps, ReportFormat, DominionRoundTimeline.
    /// </summary>
    public static class ModeReportBuilder
    {
        private const double DefaultSampleIntervalSeconds = 5.0;

        /// <summary>The mode the log was played in (ReportModes.Resolve over the sessions, the folder's name and the log's own notes).</summary>
        public static ReportMode ModeOf(TelemetryLog log)
        {
            if (log == null) return ReportModes.Resolve(null, null, false, 0);
            bool dominionNotes = log.Events.Any(e => e.Name == TelemetryKeys.Marker && (e.Data[TelemetryKeys.Note]?.ToString() ?? "").StartsWith("dominion ", StringComparison.Ordinal));
            int teamsSeen = log.Sessions.Where(s => !s.Spectator && s.Team >= 0).Select(s => s.Team).Distinct().Count();
            string folderName = string.IsNullOrEmpty(log.Folder) ? "" : Path.GetFileName(log.Folder.TrimEnd('/', '\\'));
            return ReportModes.Resolve(log.Sessions.Select(s => s.Mode), folderName, dominionNotes, teamsSeen);
        }

        public static ModeReport Build(TelemetryLog log, ReportMode mode, ReportSet set)
        {
            ReportMap map = ReportModes.MapOf(mode);
            var report = new ModeReport
            {
                Mode = mode,
                ModeKey = ReportModes.Key(mode),
                ModeName = ReportModes.DisplayName(mode),
                Dominion = ReportModes.IsDominion(mode),
                MapName = ReportMaps.Name(map),
                Teams = ReportModes.Teams(mode),
                ShowsOverPower = ReportModes.ShowsOverPower(mode),
                ShowsGold = ReportModes.ShowsGold(mode),
            };
            report.TeamSize = log != null ? log.Sessions.Select(s => s.TeamSize).FirstOrDefault(n => n > 0) : 0;
            if (report.TeamSize <= 0) report.TeamSize = ReportModes.DefaultTeamSize(mode);
            if (log == null)
            {
                report.Title = report.ModeName;
                report.PlayerCount = ReportFormat.PlayerCount(0, 0);
                report.Tabs.Add(new ReportTab { Key = "whole-match", Label = "Whole match", Sections = ConquestSections(true, report.ShowsGold) });
                return report;
            }

            var ctx = new Context(log, map);
            PhaseTimeline timeline = PhaseTimeline.From(log);
            report.LiveSeconds = timeline.LiveSeconds;
            report.LengthSeconds = Math.Max(0, timeline.WholeMatch.End - timeline.WholeMatch.Start);

            BuildRoster(ctx, report);
            report.MissingLogs = MissingLogsLine(set);

            if (report.Dominion)
                BuildDominion(ctx, report, timeline, mode);
            else
                BuildConquest(ctx, report, timeline, mode);

            report.Settings = report.Dominion ? DominionSettings(log, mode, report.TeamSize) : ConquestSettings(log, report.TeamSize);
            return report;
        }

        // ==================================================================== shared reading

        /// <summary>The log read once into the shapes every part below needs.</summary>
        private sealed class Context
        {
            public readonly TelemetryLog Log;
            public readonly ReportMap Map;
            public readonly Dictionary<string, int> FileActor = new Dictionary<string, int>();
            public readonly Dictionary<int, int> TeamOf = new Dictionary<int, int>();
            public readonly Dictionary<int, double> LastEventOf = new Dictionary<int, double>();
            public readonly double SampleInterval;

            public Context(TelemetryLog log, ReportMap map)
            {
                Log = log;
                Map = map;
                foreach (TelemetrySession s in log.Sessions)
                    if (!FileActor.ContainsKey(s.File)) FileActor[s.File] = s.Actor;
                foreach (TelemetryEvent e in log.Events)
                {
                    int actor = ActorOf(e);
                    if (actor < 0) continue;
                    if (e.T >= 0) LastEventOf[actor] = Math.Max(LastEventOf.TryGetValue(actor, out double last) ? last : 0, e.T);
                    if (e.Name == TelemetryKeys.Sample && !TeamOf.ContainsKey(actor))
                    {
                        int team = ReadInt(e.Data, TelemetryKeys.Team, -1);
                        if (team >= 0) TeamOf[actor] = team;
                    }
                }
                foreach (TelemetrySession s in log.Sessions)
                    if (!TeamOf.ContainsKey(s.Actor) && s.Team >= 0) TeamOf[s.Actor] = s.Team;

                SampleInterval = DefaultSampleIntervalSeconds;
                foreach (TelemetrySession s in log.Sessions)
                {
                    double? v = (s.Tuning?["telemetry"] as JObject)?["sampleIntervalSeconds"]?.ToObject<double?>();
                    if (v.HasValue && v.Value > 0) { SampleInterval = v.Value; break; }
                }
            }

            public int ActorOf(TelemetryEvent e) => FileActor.TryGetValue(e.File, out int a) ? a : -1;

            public int Team(int actor) => TeamOf.TryGetValue(actor, out int t) ? t : -1;
        }

        private static void BuildRoster(Context ctx, ModeReport report)
        {
            var botBuilds = new Dictionary<int, int>();
            var botActors = new HashSet<int>();
            foreach (TelemetryEvent e in ctx.Log.Events)
            {
                if (e.Name == TelemetryKeys.Marker && LobbyMarkerNotes.TryReadBot(e.Data[TelemetryKeys.Note]?.ToString(), out int botActor))
                    botActors.Add(botActor);
                else if (e.Name == TelemetryKeys.Join && (e.Data[TelemetryKeys.Bot]?.ToObject<bool?>() ?? false))
                {
                    int actor = ReadInt(e.Data, TelemetryKeys.Actor, -1);
                    botActors.Add(actor);
                    int build = ReadInt(e.Data, TelemetryKeys.BotBuild, -1);
                    if (build >= 0) botBuilds[actor] = build;
                }
            }

            foreach (TelemetrySession s in ctx.Log.Sessions)
            {
                if (s.Spectator || report.Roster.Any(r => r.Actor == s.Actor)) continue;
                bool bot = s.Bot || botActors.Contains(s.Actor);
                int build = s.BotBuild >= 0 ? s.BotBuild : (botBuilds.TryGetValue(s.Actor, out int b) ? b : -1);
                report.Roster.Add(new RosterEntry
                {
                    Actor = s.Actor,
                    Nick = s.Nick ?? "",
                    Team = ctx.Team(s.Actor),
                    Bot = bot,
                    Build = bot ? ReportFormat.BotBuildName(build) : "",
                });
            }
            report.Roster.Sort((a, b) => a.Team != b.Team ? a.Team.CompareTo(b.Team) : a.Actor.CompareTo(b.Actor));
            int bots = report.Roster.Count(r => r.Bot);
            report.PlayerCount = ReportFormat.PlayerCount(report.Roster.Count - bots, bots);
        }

        private static string MissingLogsLine(ReportSet set)
        {
            List<LogCoverageRow> coverage = set?.WholeMatch?.Header?.LogCoverage;
            if (coverage == null) return "";
            List<string> names = coverage.Where(r => !r.FilePresent && !r.JoinedAndLeftBeforeLoggingStarted)
                                         .Select(r => string.IsNullOrEmpty(r.Nick) ? "a player who left no name" : r.Nick).ToList();
            if (names.Count == 0) return "";
            return "No log from " + string.Join(", ", names) + ": what only their own log records (damage taken, deaths, purchases) is missing.";
        }

        // ==================================================================== Conquest

        private static List<string> ConquestSections(bool wholeMatch, bool gold)
        {
            var sections = new List<string> { "header" };
            if (gold) sections.Add("economy");
            sections.AddRange(new[] { "territory", "combat", "map", "players", "markers" });
            if (wholeMatch) sections.Add("bugs");
            return sections;
        }

        private static void BuildConquest(Context ctx, ModeReport report, PhaseTimeline timeline, ReportMode mode)
        {
            report.Tabs.Add(new ReportTab { Key = "whole-match", Label = "Whole match", Start = timeline.WholeMatch.Start, End = timeline.WholeMatch.End, Sections = ConquestSections(true, report.ShowsGold) });
            if (ReportModes.HasPhaseTabs(mode))
            {
                report.Tabs.Add(new ReportTab { Key = "phase-1", Label = "Phase 1: 3 teams", Start = timeline.Phase1.Start, End = timeline.Phase1.End, Sections = ConquestSections(false, report.ShowsGold) });
                TimeWindow phase2 = timeline.Phase2;
                report.Tabs.Add(new ReportTab
                {
                    Key = "phase-2", Label = "Phase 2: 2 teams",
                    Start = phase2 != null ? phase2.Start : timeline.MatchLength, End = phase2 != null ? phase2.End : timeline.MatchLength,
                    Sections = ConquestSections(false, report.ShowsGold),
                });
            }

            var data = new ConquestData();
            var waves = new Dictionary<double, List<(int Holder, int Caught)>>();
            foreach (TelemetryEvent e in ctx.Log.Events)
            {
                if (e.Name == TelemetryKeys.Scan)
                {
                    double start = ReadDouble(e.Data, TelemetryKeys.WaveStartT, -1);
                    if (start < 0) continue;
                    double key = Math.Round(start, 1);
                    if (!waves.TryGetValue(key, out var lines)) waves[key] = lines = new List<(int, int)>();
                    lines.Add((ReadInt(e.Data, TelemetryKeys.Team, -1), ReadInt(e.Data, TelemetryKeys.Caught, -1)));
                }
                else if (e.Name == TelemetryKeys.Shrink && data.Shrink == null)
                {
                    data.Shrink = new ShrinkInfo
                    {
                        T = e.T,
                        Team = ReadInt(e.Data, TelemetryKeys.Team, -1),
                        Zones = (e.Data[TelemetryKeys.Zones] as JArray)?.Where(z => z.Type != JTokenType.Null).Select(z => z.ToObject<int>()).ToArray() ?? Array.Empty<int>(),
                    };
                }
                else if (e.Name == TelemetryKeys.Phase && ReadInt(e.Data, TelemetryKeys.PhaseNumber, 1) >= (int)Overpower.Match.MatchPhase.Over)
                {
                    var remain = e.Data[TelemetryKeys.TeamsRemaining] as JArray;
                    if (remain != null && remain.Count == 1 && remain[0].Type != JTokenType.Null) data.Winner = remain[0].ToObject<int>();
                }
            }
            foreach (var pair in waves.OrderBy(p => p.Key))
            {
                int holder = pair.Value.GroupBy(l => l.Holder).OrderByDescending(g => g.Count()).First().Key;
                data.ScanWaves.Add(new ScanWave { StartT = pair.Key, Holder = holder, Caught = pair.Value.Max(l => l.Caught) });
            }
            report.ConquestReport = data;

            if (mode == ReportMode.Conquest3v3)
            {
                int closedTeam = data.Shrink != null && data.Shrink.Team >= 0 ? data.Shrink.Team : 3 - report.Teams.Sum();
                IEnumerable<int> closed = data.Shrink != null && data.Shrink.Zones.Length > 0 ? data.Shrink.Zones : ReportMaps.ClosedZones(closedTeam);
                report.HiddenZones = closed.ToList();
            }

            report.Title = data.Winner >= 0 ? report.ModeName + ", " + ArenaNames.TeamName(data.Winner) + " wins" : report.ModeName;
        }

        private static List<string[]> ConquestSettings(TelemetryLog log, int teamSize)
        {
            var rows = new List<string[]> { new[] { "Team size", teamSize.ToString(CultureInfo.InvariantCulture) + " per team" } };
            JObject tuning = PrimaryTuning(log);
            JObject territory = tuning?["territory"] as JObject;
            JObject gameplay = tuning?["gameplay"] as JObject;
            if (territory == null && gameplay == null)
            {
                rows.Add(new[] { "Settings", "not in this log" });
                return rows;
            }
            if (territory?["startingGold"] != null) rows.Add(new[] { "Starting gold", Int(territory["startingGold"]) });
            if (territory?["tiers"] is JArray tiers)
            {
                rows.Add(new[] { "Income per zone", TierList(tiers, "teamGoldPerSecond", v => v + "/s") });
                rows.Add(new[] { "Capture bounty", TierList(tiers, "captureBounty", v => v) + (territory["bountyHoldSeconds"] != null ? " (after holding " + ReportFormat.Length(Dbl(territory["bountyHoldSeconds"])) + ")" : "") });
                rows.Add(new[] { "Capture time", TierList(tiers, "captureSeconds", v => ReportFormat.Length(double.Parse(v, CultureInfo.InvariantCulture))) });
            }
            if (gameplay?["respawnBaseSeconds"] != null)
                rows.Add(new[] { "Respawn wait", ReportFormat.Length(Dbl(gameplay["respawnBaseSeconds"])) + " + " + ReportFormat.Length(Dbl(gameplay["respawnPerDeathSeconds"])) +
                                                  " per death, at most " + ReportFormat.Length(Dbl(gameplay["respawnMaxSeconds"])) });
            return rows;
        }

        // ==================================================================== Dominion

        private static void BuildDominion(Context ctx, ModeReport report, PhaseTimeline timeline, ReportMode mode)
        {
            DominionRoundTimeline rounds = DominionRoundTimeline.From(ctx.Log.Events
                .Where(e => e.Name == TelemetryKeys.Marker)
                .Select(e => (e.T, e.Data[TelemetryKeys.Note]?.ToString() ?? "")));
            double matchEnd = rounds.OverT ?? timeline.MatchLength;
            int roundsToWin = (PrimaryTuning(ctx.Log)?["dominion"] as JObject)?["roundsToWin"]?.ToObject<int?>() ?? 2;

            var data = new DominionData
            {
                Winner = rounds.Winner,
                Wins = new Dictionary<int, int>(rounds.Wins),
                HowEnded = rounds.HowEnded(roundsToWin),
                HasCentre = ReportModes.HasCentrePayouts(mode),
                Bounties = rounds.Bounties,
                CentrePayouts = rounds.CentrePayouts,
            };
            report.DominionReport = data;

            var whole = new ReportTab { Key = "whole-match", Label = "Whole match", Start = timeline.WholeMatch.Start, End = timeline.WholeMatch.End };
            whole.Sections.AddRange(new[] { "result", "points", "zones", "fights", "map", "builds", "players" });
            if (rounds.SuddenDeath != null) whole.Sections.Add("sudden-death");
            whole.Sections.AddRange(new[] { "markers", "bugs" });
            report.Tabs.Add(whole);

            foreach (DominionRound round in rounds.Rounds)
            {
                if (!round.StartT.HasValue) continue;
                double end = round.EndT ?? matchEnd;
                var tab = new ReportTab
                {
                    Key = "round-" + round.Number.ToString(CultureInfo.InvariantCulture),
                    Label = "Round " + round.Number.ToString(CultureInfo.InvariantCulture),
                    Start = round.StartT.Value, End = Math.Max(round.StartT.Value, end), TimesFromStart = true,
                };
                tab.Sections.AddRange(new[] { "points", "zones", "fights", "map", "builds", "players" });
                report.Tabs.Add(tab);
            }
            if (rounds.SuddenDeath != null)
            {
                double end = rounds.SuddenDeath.EndT ?? matchEnd;
                var tab = new ReportTab { Key = "sudden-death", Label = "Sudden death", Start = rounds.SuddenDeath.StartT, End = Math.Max(rounds.SuddenDeath.StartT, end), TimesFromStart = true };
                tab.Sections.AddRange(new[] { "sudden-death", "fights", "map", "players" });
                report.Tabs.Add(tab);
            }

            foreach (ReportTab tab in report.Tabs)
                if (tab.Key != "whole-match")
                    report.Scopes[tab.Key] = TelemetryAggregator.Build(ctx.Log, new TimeWindow(tab.Start, tab.End, true));

            int[] teams = report.Teams;
            foreach (DominionRound round in rounds.Rounds)
            {
                if (!round.StartT.HasValue && !round.EndT.HasValue) continue;
                double start = round.StartT ?? round.EndT.Value;
                double end = round.EndT ?? matchEnd;
                List<int> ordered = teams.OrderByDescending(t => round.Points.TryGetValue(t, out int p) ? p : 0).ToList();
                int top = ordered.Count > 0 && round.Points.TryGetValue(ordered[0], out int p0) ? p0 : 0;
                int second = ordered.Count > 1 && round.Points.TryGetValue(ordered[1], out int p1) ? p1 : 0;
                data.Rounds.Add(new RoundRow
                {
                    Number = round.Number,
                    Winners = round.Winners,
                    Outcome = round.Outcome,
                    Points = round.Points,
                    Margin = top - second,
                    StartT = round.StartT,
                    EndT = round.EndT,
                    LengthSeconds = Math.Max(0, end - start),
                    Overtime = round.OvertimeStartT.HasValue,
                    OvertimeSeconds = round.OvertimeStartT.HasValue ? Math.Max(0, end - round.OvertimeStartT.Value) : 0,
                });
                if (round.StartT.HasValue)
                {
                    data.Races.Add(Race(ctx, round, start, end, teams, rounds));
                    AddZones(ctx, report, data, round.Number, start, end);
                    AddBuilds(ctx, report, data, round.Number, start, end);
                }
            }

            foreach (TelemetryEvent e in ctx.Log.Events)
            {
                if (e.Name != TelemetryKeys.Shield || (e.Data[TelemetryKeys.State]?.ToString() ?? "") != "down") continue;
                int actor = ctx.ActorOf(e);
                data.Shields.Add(new ShieldEnd
                {
                    T = e.T, Actor = actor, Team = ctx.Team(actor),
                    Reason = e.Data[TelemetryKeys.Reason]?.ToString() ?? "",
                    Blocked = ReadInt(e.Data, TelemetryKeys.Blocked, 0),
                });
            }

            foreach (ReportTab tab in report.Tabs)
                AddPlayerExtras(ctx, report, data, tab);

            if (rounds.SuddenDeath != null)
                data.SuddenDeath = SuddenDeath(ctx, rounds.SuddenDeath, rounds.SuddenDeath.EndT ?? matchEnd);

            if (data.Winner >= 0)
            {
                string score = string.Join("-", teams.Select(t => data.Wins.TryGetValue(t, out int w) ? w : 0).OrderByDescending(w => w)
                                                      .Select(w => w.ToString(CultureInfo.InvariantCulture)));
                report.Title = report.ModeName + ", " + ArenaNames.TeamName(data.Winner) + " wins " + score;
            }
            else
            {
                report.Title = report.ModeName + ", no winner recorded";
            }
        }

        private static PointsRace Race(Context ctx, DominionRound round, double start, double end, int[] teams, DominionRoundTimeline rounds)
        {
            var race = new PointsRace { Round = round.Number, StartT = start, EndT = end };
            var series = teams.ToDictionary(t => t, t => new PointsSeries { Team = t });
            foreach (PointsSeries s in series.Values) s.Points.Add(new[] { 0.0, 0.0 });
            var lastLogged = new Dictionary<int, int>();

            foreach (TelemetryEvent e in ctx.Log.Events)
            {
                if (e.Name != TelemetryKeys.Points || e.T < start || e.T > end) continue;
                if (ReadInt(e.Data, TelemetryKeys.Round, round.Number) != round.Number) continue;
                if (!(e.Data[TelemetryKeys.TeamPoints] is JArray points)) continue;
                race.Logged = true;
                foreach (int team in teams)
                {
                    if (team < 0 || team >= points.Count || points[team].Type == JTokenType.Null) continue;
                    int value = points[team].ToObject<int>();
                    series[team].Points.Add(new[] { e.T - start, value });
                    lastLogged[team] = value;
                }
            }

            foreach (int team in teams)
            {
                int final = round.Points.TryGetValue(team, out int p) ? p : (lastLogged.TryGetValue(team, out int l) ? l : 0);
                series[team].Points.Add(new[] { end - start, (double)final });
                int bounties = rounds.Bounties.Where(b => b.Team == team && b.T >= start && b.T <= end).Sum(b => b.Points);
                int centre = rounds.CentrePayouts.Where(c => c.Team == team && c.T >= start && c.T <= end).Sum(c => c.Points);
                race.Sources.Add(new PointsSource { Team = team, Bounties = bounties, Centre = centre, Total = final, Zones = Math.Max(0, final - bounties - centre) });
            }
            race.Series.AddRange(series.Values);

            foreach (DominionPayout b in rounds.Bounties.Where(b => b.T >= start && b.T <= end))
                race.Events.Add(new RaceEvent { At = b.T - start, Team = b.Team, Kind = "bounty", Points = b.Points });
            foreach (DominionPayout c in rounds.CentrePayouts.Where(c => c.Team >= 0 && c.T >= start && c.T <= end))
                race.Events.Add(new RaceEvent { At = c.T - start, Team = c.Team, Kind = "centre", Points = c.Points });
            return race;
        }

        private static void AddZones(Context ctx, ModeReport report, DominionData data, int round, double start, double end)
        {
            if (!report.Scopes.TryGetValue("round-" + round.ToString(CultureInfo.InvariantCulture), out ReportTables tables)) return;
            var holds = new Dictionary<(int Zone, int Team), double>();
            var captures = new Dictionary<int, int>();
            var first = new Dictionary<int, double>();
            foreach (OwnershipRow row in tables.Ownership)
            {
                if (ReportMaps.IsSpawn(ctx.Map, row.Zone) || row.Team < 0) continue;
                holds[(row.Zone, row.Team)] = (holds.TryGetValue((row.Zone, row.Team), out double s) ? s : 0) + row.Duration;
                if (row.From <= start + 0.5) continue; // a holding the round only clipped, not a capture made in it
                captures[row.Zone] = (captures.TryGetValue(row.Zone, out int n) ? n : 0) + 1;
                if (!first.TryGetValue(row.Zone, out double f) || row.From - start < f) first[row.Zone] = row.From - start;
            }
            foreach (var pair in holds.OrderBy(p => p.Key.Zone).ThenBy(p => p.Key.Team))
                data.ZoneHolds.Add(new ZoneHoldRow { Round = round, Zone = pair.Key.Zone, Team = pair.Key.Team, Seconds = pair.Value });
            for (int zone = 0; zone < ReportMaps.ZoneCount(ctx.Map); zone++)
            {
                if (ReportMaps.IsSpawn(ctx.Map, zone)) continue;
                data.ZoneRounds.Add(new ZoneRoundRow
                {
                    Round = round, Zone = zone, RoundSeconds = Math.Max(0, end - start),
                    Captures = captures.TryGetValue(zone, out int c) ? c : 0,
                    FirstCaptureAfter = first.TryGetValue(zone, out double f) ? f : (double?)null,
                });
            }
        }

        private static void AddBuilds(Context ctx, ModeReport report, DominionData data, int round, double start, double end)
        {
            var byActor = new Dictionary<int, Dictionary<(int, int, int, int, int, int), (int Count, double Last)>>();
            foreach (TelemetryEvent e in ctx.Log.Events)
            {
                if (e.Name != TelemetryKeys.Sample || e.T < start || e.T > end) continue;
                if (!(e.Data[TelemetryKeys.Alive]?.ToObject<bool?>() ?? true)) continue;
                int actor = ctx.ActorOf(e);
                if (actor < 0) continue;
                var key = (ReadInt(e.Data, TelemetryKeys.Weapon, -1), ReadInt(e.Data, TelemetryKeys.Attachment, -1), ReadInt(e.Data, TelemetryKeys.Mobility, -1),
                           ReadInt(e.Data, TelemetryKeys.Ultimate, -1), ReadInt(e.Data, TelemetryKeys.AbsorbLevel, 0), ReadInt(e.Data, TelemetryKeys.RechargeLevel, 0));
                if (!byActor.TryGetValue(actor, out var counts)) byActor[actor] = counts = new Dictionary<(int, int, int, int, int, int), (int, double)>();
                counts[key] = (counts.TryGetValue(key, out var seen) ? seen.Count + 1 : 1, e.T);
            }
            foreach (RosterEntry player in report.Roster)
            {
                if (!byActor.TryGetValue(player.Actor, out var counts)) continue;
                var held = counts.OrderByDescending(p => p.Value.Count).ThenByDescending(p => p.Value.Last).First().Key;
                data.Builds.Add(new BuildRow
                {
                    Round = round, Actor = player.Actor, Team = player.Team,
                    Weapon = held.Item1, Attachment = held.Item2, Mobility = held.Item3, Ultimate = held.Item4, Absorb = held.Item5, Recharge = held.Item6,
                });
            }
        }

        /// <summary>Per player, on one tab: seconds on a scoring zone while alive (from the samples, each counted until the next one, a gap
        /// counted at most two intervals) and seconds dead (from each death to the next respawn, or the end of their log).</summary>
        private static void AddPlayerExtras(Context ctx, ModeReport report, DominionData data, ReportTab tab)
        {
            var window = new TimeWindow(tab.Start, tab.End, true);
            var onZone = new Dictionary<int, double>();
            var lastSample = new Dictionary<int, (double T, bool Counts)>();
            var deathAt = new Dictionary<int, double>();
            var dead = new Dictionary<int, double>();

            void AddOnZone(int actor, double from, double to)
            {
                if (window.Clip(from, to, out double a, out double b))
                    onZone[actor] = (onZone.TryGetValue(actor, out double s) ? s : 0) + (b - a);
            }
            void AddDead(int actor, double from, double to)
            {
                if (window.Clip(from, to, out double a, out double b))
                    dead[actor] = (dead.TryGetValue(actor, out double s) ? s : 0) + (b - a);
            }

            foreach (TelemetryEvent e in ctx.Log.Events)
            {
                int actor = ctx.ActorOf(e);
                if (actor < 0 || e.T < 0) continue;
                if (e.Name == TelemetryKeys.Sample)
                {
                    if (lastSample.TryGetValue(actor, out var prev) && prev.Counts)
                        AddOnZone(actor, prev.T, Math.Min(e.T, prev.T + 2 * ctx.SampleInterval));
                    int zone = ReadInt(e.Data, TelemetryKeys.Zone, -1);
                    bool alive = e.Data[TelemetryKeys.Alive]?.ToObject<bool?>() ?? true;
                    lastSample[actor] = (e.T, alive && zone >= 0 && !ReportMaps.IsSpawn(ctx.Map, zone));
                }
                else if (e.Name == TelemetryKeys.Death)
                {
                    deathAt[actor] = e.T;
                }
                else if (e.Name == TelemetryKeys.Respawn && deathAt.TryGetValue(actor, out double died))
                {
                    AddDead(actor, died, e.T);
                    deathAt.Remove(actor);
                }
            }
            foreach (var pair in lastSample)
                if (pair.Value.Counts)
                    AddOnZone(pair.Key, pair.Value.T, pair.Value.T + Math.Min(ctx.SampleInterval, Math.Max(0, (ctx.LastEventOf.TryGetValue(pair.Key, out double l) ? l : pair.Value.T) - pair.Value.T)));
            foreach (var pair in deathAt)
                AddDead(pair.Key, pair.Value, ctx.LastEventOf.TryGetValue(pair.Key, out double l) ? Math.Max(l, pair.Value) : pair.Value);

            foreach (RosterEntry player in report.Roster)
                data.PlayerExtras.Add(new PlayerExtra
                {
                    Scope = tab.Key, Actor = player.Actor,
                    ScoringZoneSeconds = onZone.TryGetValue(player.Actor, out double z) ? z : 0,
                    DeadSeconds = dead.TryGetValue(player.Actor, out double d) ? d : 0,
                });
        }

        private static SuddenDeathData SuddenDeath(Context ctx, DominionSuddenDeath sd, double end)
        {
            var data = new SuddenDeathData
            {
                StartT = sd.StartT, EndT = sd.EndT, LengthSeconds = Math.Max(0, end - sd.StartT),
                Teams = sd.Teams, Replays = sd.Replays, Winner = sd.Winner, ForRound = sd.ForRound,
            };
            var lethalCircle = new List<(int Victim, double T)>();
            var damage = new Dictionary<int, double>();
            foreach (TelemetryEvent e in ctx.Log.Events)
            {
                if (e.T < sd.StartT || e.T > end) continue;
                if (e.Name == TelemetryKeys.Hit && (e.Data[TelemetryKeys.Source]?.ToString() ?? "") == "SuddenDeath")
                {
                    int victim = ReadInt(e.Data, TelemetryKeys.Victim, ctx.ActorOf(e));
                    damage[victim] = (damage.TryGetValue(victim, out double d) ? d : 0) + ReadDouble(e.Data, TelemetryKeys.Raw, 0);
                    if (e.Data[TelemetryKeys.Lethal]?.ToObject<bool?>() ?? false) lethalCircle.Add((victim, e.T));
                }
            }
            foreach (TelemetryEvent e in ctx.Log.Events)
            {
                if (e.Name != TelemetryKeys.Death || e.T < sd.StartT || e.T > end) continue;
                int victim = ctx.ActorOf(e);
                int killer = ReadInt(e.Data, TelemetryKeys.Killer, -1);
                if (killer >= 0) data.PlayerKills.Add(new[] { victim, killer });
                else if (lethalCircle.Any(h => h.Victim == victim && Math.Abs(h.T - e.T) <= 1.0)) data.CircleKills.Add(victim);
            }
            foreach (var pair in damage)
                data.CircleDamage[pair.Key] = (int)Math.Round(pair.Value, MidpointRounding.AwayFromZero);
            return data;
        }

        private static List<string[]> DominionSettings(TelemetryLog log, ReportMode mode, int teamSize)
        {
            var rows = new List<string[]> { new[] { "Team size", teamSize.ToString(CultureInfo.InvariantCulture) + " per team" } };
            JObject d = PrimaryTuning(log)?["dominion"] as JObject;
            if (d == null)
            {
                rows.Add(new[] { "Dominion settings", "not in this log (logged from the next build on)" });
                return rows;
            }
            bool twoTeams = ReportModes.TeamCount(mode) == 2;
            rows.Add(new[] { "Rounds", "best of " + Int(d["maxRounds"]) + ", " + Int(d["roundsToWin"]) + " round wins take the match" });
            rows.Add(new[] { "Round length", ReportFormat.Length(Dbl(d["roundSeconds"])) + ", with a " + ReportFormat.Length(Dbl(d["breakSeconds"])) + " break before each" });
            rows.Add(new[] { "Overtime", ReportFormat.Length(Dbl(d["overtimeSeconds"])) + ", ends at a " + Int(d["overtimeLeadPoints"]) + " point lead" });
            if (d["pointsPerZonePerSecond"] is JArray perTier)
            {
                var parts = new List<string>();
                for (int i = 0; i < perTier.Count; i++)
                    if (perTier[i].Type != JTokenType.Null && perTier[i].ToObject<int>() != 0)
                        parts.Add("Tier " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + perTier[i].ToObject<int>().ToString(CultureInfo.InvariantCulture) + "/s");
                rows.Add(new[] { "Points per zone", parts.Count > 0 ? string.Join(", ", parts) : "none" });
            }
            rows.Add(new[] { "Bounty", Int(d["bountyPoints"]) + " points for taking a zone held " + ReportFormat.Length(Dbl(d["bountyHoldSeconds"])) });
            if (!twoTeams)
                rows.Add(new[] { "Centre payout", Int(d["centrePayoutPoints"]) + " points every " + ReportFormat.Length(Dbl(d["centrePayoutIntervalSeconds"])) +
                                                   ", the first " + ReportFormat.Length(Dbl(d["centreFirstPayoutSeconds"])) + " into the round" });
            rows.Add(new[] { "Respawn wait", ReportFormat.Length(Dbl(d[twoTeams ? "respawnSeconds2v2" : "respawnSeconds3v3v3"])) });
            rows.Add(new[] { "Respawn shield", ReportFormat.Length(Dbl(d["shieldSeconds"])) });
            rows.Add(new[] { "Sudden-death circle", Whole(d[twoTeams ? "suddenDeathStartRadius2v2" : "suddenDeathStartRadius3v3v3"]) + " m to " +
                                                    Whole(d[twoTeams ? "finalRadius2v2" : "finalRadius3v3v3"]) + " m over " + ReportFormat.Length(Dbl(d["suddenDeathShrinkSeconds"])) +
                                                    ", " + Whole(d["damagePerSecondOutside"]) + " damage a second outside" });
            if (d["weaponDepthByRound"] is JArray depth && d["armorUpgradesByRound"] is JArray armour && depth.Count > 0 && armour.Count > 0)
            {
                var parts = new List<string>();
                for (int i = 0; i < Math.Max(depth.Count, armour.Count); i++)
                {
                    int weapons = i < depth.Count ? depth[i].ToObject<int>() : depth[depth.Count - 1].ToObject<int>();
                    int upgrades = i < armour.Count ? armour[i].ToObject<int>() : armour[armour.Count - 1].ToObject<int>();
                    string guns = weapons <= 0 ? "starting gun only" : weapons == 1 ? "weapon families" : "weapon upgrades";
                    parts.Add("round " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + guns + ", " + upgrades.ToString(CultureInfo.InvariantCulture) +
                              (upgrades == 1 ? " armour upgrade" : " armour upgrades"));
                }
                rows.Add(new[] { "Shop by round", string.Join("; ", parts) });
            }
            return rows;
        }

        // ==================================================================== small readers

        /// <summary>The master's session's tuning (any session's when no master's has one).</summary>
        private static JObject PrimaryTuning(TelemetryLog log)
        {
            if (log == null) return null;
            TelemetrySession primary = log.Sessions.FirstOrDefault(s => s.IsMaster && s.Tuning != null) ?? log.Sessions.FirstOrDefault(s => s.Tuning != null);
            return primary?.Tuning;
        }

        private static string TierList(JArray tiers, string field, Func<string, string> format)
        {
            var parts = new List<string>();
            for (int i = 0; i < tiers.Count; i++)
            {
                JToken value = (tiers[i] as JObject)?[field];
                if (value == null || value.Type == JTokenType.Null) continue;
                double v = value.ToObject<double>();
                if (v == 0) continue;
                parts.Add("Tier " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + format(v.ToString("0.##", CultureInfo.InvariantCulture)));
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        private static string Int(JToken token) => token == null || token.Type == JTokenType.Null ? "?" : token.ToObject<int>().ToString(CultureInfo.InvariantCulture);

        private static string Whole(JToken token) => token == null || token.Type == JTokenType.Null ? "?" : ReportFormat.Whole(token.ToObject<double>());

        private static double Dbl(JToken token) => token == null || token.Type == JTokenType.Null ? 0 : token.ToObject<double>();

        private static int ReadInt(JObject data, string key, int fallback)
        {
            JToken token = data[key];
            return token != null && token.Type != JTokenType.Null ? token.ToObject<int>() : fallback;
        }

        private static double ReadDouble(JObject data, string key, double fallback)
        {
            JToken token = data[key];
            return token != null && token.Type != JTokenType.Null ? token.ToObject<double>() : fallback;
        }
    }
}
