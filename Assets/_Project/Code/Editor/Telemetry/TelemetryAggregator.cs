using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Pure log -> tables. No IO (TelemetryLog already read the files); CsvReportWriter and HtmlReportWriter only
    /// format what this produces, so the two outputs never disagree. Tested against a hand-written fixture with hand-computed
    /// totals (TelemetryAggregatorTests, TelemetryAggregatorReviewFixesTests).
    ///
    /// Build(log, window) is one pipeline, called up to three times (see BuildSet), once per scope (whole match, Phase 1, Phase
    /// 2). No table builder is duplicated for phases: a DISCRETE event (hit, purchase, death...) is skipped when its t falls
    /// outside the window; a CONTINUOUS integral (sample interval, ownership stint, capture attempt, alive-time tail) is
    /// CLIPPED to it via TimeWindow.Clip. Row types that can straddle the phase-2 transition (ownership, captures, hits,
    /// deaths, purchases, gold/economy buckets) carry a `Phase` tagged against it REGARDLESS of the build's window, and a
    /// stint/attempt crossing it is split in every scope that sees both halves (ClipAndTagPhase).
    ///
    /// Traps: a `capture` line's own state is NEVER trusted for the outcome (it is stateless: a "paused" at progress 1 looks
    /// the same whether completed or interrupted) - only `ownership` changes close an attempt (BuildCaptures). An out-of-range
    /// tier (an `ownership` line can carry tier 0 for an unregistered zone) is skipped and counted (Header.InvalidTierCount).
    /// `underAttack` feeds no table.</summary>
    public static class TelemetryAggregator
    {
        private const int Neutral = -1; // TerritoryMap.Neutral's own value - a zone with no owner.
        // Same sentinel value as Neutral (both mean "no real zone id"), under its own name so a reader of
        // zone_income.csv/economy_by_minute.csv isn't confused about which concept a -1 zone/tier means there.
        private const int UnattributedZone = -1;
        private const double DefaultSampleIntervalSeconds = 5.0;

        private static readonly HashSet<string> CaptureOpenStates =
            new HashSet<string> { "started", "resumed", "drainStarted", "drainResumed" };
        private static readonly HashSet<string> CaptureFreshStartStates = new HashSet<string> { "started", "drainStarted" };
        private static readonly HashSet<string> CapturePauseStates = new HashSet<string> { "paused", "drainPaused" };
        // "completed"/"neutralised" are known (older logs may still carry them) but never drive the state machine - see
        // BuildCaptures. Anything outside this whole set is unknown (counted, not fatal).
        private static readonly HashSet<string> CaptureKnownStates = new HashSet<string>
            { "started", "resumed", "drainStarted", "drainResumed", "paused", "drainPaused", "completed", "neutralised" };

        private sealed class CaptureAttempt
        {
            public int Team;
            public double Start;
            public bool IsDrain;
            public int LastPlayers;
        }

        /// <summary>The whole match. Equivalent to <c>Build(log, PhaseTimeline.From(log).WholeMatch)</c>.</summary>
        public static ReportTables Build(TelemetryLog log) => Build(log, null);

        /// <summary>Builds one scope. A null window means the whole match (every event, every integral in full). A real window
        /// (Phase 1 or Phase 2, from PhaseTimeline) filters every discrete event to it and clips every continuous integral to
        /// it - see the class comment.
        ///
        /// includeWarmupMarkers: Markers, like Bug reports and Console, stay visible over the FULL log (warm-up included) but
        /// are scoped per phase tab (a marker dropped in Phase 2 has no business on the Phase 1 tab); so this only widens the
        /// marker window's START back to 0 for the whole-match build (BuildSet is the only caller that passes true).</summary>
        public static ReportTables Build(TelemetryLog log, TimeWindow window, bool includeWarmupMarkers = false)
        {
            var tables = new ReportTables();
            if (log == null) return tables;

            var fileActor = new Dictionary<string, int>();
            var sessionByActor = new Dictionary<int, TelemetrySession>();
            foreach (TelemetrySession s in log.Sessions)
            {
                fileActor[s.File] = s.Actor;
                if (!sessionByActor.ContainsKey(s.Actor))
                    sessionByActor[s.Actor] = s;
            }

            // The phase timeline gives both this build's default (whole-match) window AND the phase-2 transition instant; every
            // phase-taggable row is split/tagged against the transition regardless of this call's window (see ClipAndTagPhase).
            PhaseTimeline timeline = PhaseTimeline.From(log);
            double matchLength = timeline.MatchLength;
            double? tPhase2 = timeline.TransitionSeconds;
            TimeWindow effectiveWindow = window ?? timeline.WholeMatch;

            // When this build's window lies entirely within ONE phase (a Phase 1- or Phase 2-scoped build) every surviving row
            // belongs to that phase; null when it spans both (the whole-match build), the only case where a straddling minute
            // bucket's absolute start is still the right tag (see BuildGoldTimelineAndEconomy).
            int? forcedPhase = null;
            if (tPhase2.HasValue)
            {
                if (effectiveWindow.End <= tPhase2.Value) forcedPhase = 1;
                else if (effectiveWindow.Start >= tPhase2.Value) forcedPhase = 2;
            }

            // Team from the actor's first sample with tm >= 0, falling back to the session - see EffectiveTeamByActor.
            Dictionary<int, int> effectiveTeam = BuildEffectiveTeam(log, fileActor, sessionByActor);

            // Every player's covered range (their file's first/last real event): time-alive and every sample integral are
            // bounded by THIS, not matchLength.
            Dictionary<int, (double First, double Last)> coverageByActor = BuildCoverageByActor(log, fileActor);

            double sampleInterval = ResolveSampleIntervalSeconds(log);

            var zoneTier = new Dictionary<int, int>();
            // Every raw ownership change per zone, IN TIME ORDER, including transitions to neutral - built from the WHOLE log
            // regardless of window: a sample inside a phase-scoped window still needs the true owner-at-time, even when the
            // change that established it happened before the window. The ownership.csv STINT ROWS (below) are what gets
            // clipped/filtered; this raw history never is.
            var rawChangesByZone = new Dictionary<int, List<(double T, int New)>>();
            BuildOwnership(log, tables.Ownership, zoneTier, rawChangesByZone, matchLength, effectiveWindow, tPhase2);

            TimeWindow markerWindow = includeWarmupMarkers ? new TimeWindow(0, effectiveWindow.End, effectiveWindow.EndInclusive) : effectiveWindow;
            BuildHeader(tables.Header, log, sessionByActor, coverageByActor, effectiveWindow, sampleInterval, markerWindow);
            tables.Header.EliminationFallbackUsed = timeline.UsedEliminationFallback;
            tables.Header.WarmupSeconds = timeline.LiveSeconds;
            tables.Header.NeverWentLive = timeline.HasWarmup && !timeline.WentLive;
            // Unwindowed, like LogCoverage above: the Bug reports and Console sections only render on the whole-match tab (see
            // BugRow).
            BuildBugsAndConsole(log, fileActor, sessionByActor, tPhase2, tables.Header.Bugs, tables.Header.ConsoleByPlayer);
            BuildCaptures(log, rawChangesByZone, zoneTier, matchLength, tables.Header, tables.Captures, effectiveWindow, tPhase2);
            BuildPurchasesAndBlocked(log, fileActor, sessionByActor, effectiveTeam, tables, effectiveWindow, tPhase2);
            BuildHits(log, effectiveTeam, tables.Hits, effectiveWindow, tPhase2);
            BuildGoldTimelineAndEconomy(log, fileActor, sessionByActor, effectiveTeam, zoneTier, rawChangesByZone, matchLength, tables, effectiveWindow, tPhase2, forcedPhase);
            BuildZoneIncome(log, fileActor, effectiveTeam, zoneTier, tables.Ownership, tables.ZoneIncome, effectiveWindow);

            var sampleStats = BuildSampleDerivedStats(log, fileActor, effectiveTeam, rawChangesByZone, coverageByActor, sampleInterval, effectiveWindow);
            BuildWeaponsAndAbilities(log, tables.Hits, sampleStats.EquippedSecondsByWeapon, tables, effectiveWindow);
            BuildPlayersAndDeaths(log, fileActor, sessionByActor, effectiveTeam, coverageByActor, sampleStats, tables, effectiveWindow, tPhase2);

            return tables;
        }

        /// <summary>Builds all three scopes from one log (see ReportSet). The same Build(log, window) runs up to three times,
        /// parameterized only by which window to clip/filter against - no second copy of the table logic.</summary>
        public static ReportSet BuildSet(TelemetryLog log)
        {
            PhaseTimeline timeline = PhaseTimeline.From(log);
            return new ReportSet
            {
                WholeMatch = Build(log, timeline.WholeMatch, includeWarmupMarkers: true),
                Phase1 = Build(log, timeline.Phase1),
                Phase2 = timeline.HasPhase2 ? Build(log, timeline.Phase2) : null,
                LiveSeconds = timeline.LiveSeconds,
                WentLive = timeline.WentLive,
                TransitionSeconds = timeline.TransitionSeconds,
            };
        }

        // ==================================================================== phase helpers

        /// <summary>1 or 2, from a discrete event's t vs the match's phase-2 transition (null transition: everything is Phase
        /// 1).</summary>
        private static int PhaseOf(double t, double? tPhase2) => (tPhase2.HasValue && t >= tPhase2.Value) ? 2 : 1;

        /// <summary>Clips a real [realFrom, realTo) span to the requested window, AND splits it at the phase-2 transition when
        /// the (window-clipped) span straddles it. Used by ownership stints and capture attempts, the two tables whose rows can
        /// span more than one phase. Yields nothing with no overlap, ONE piece when it doesn't straddle tPhase2, TWO when it
        /// does (Phase 1's [from, tPhase2) and Phase 2's [tPhase2, to)).
        ///
        /// One mechanism makes "the whole-match build's rows are ALSO phase-split" and "a phase-scoped build only sees its own
        /// half" the same code path: a phase-scoped window's End IS tPhase2 (Phase 1) or Start IS tPhase2 (Phase 2), so the
        /// window-clip alone yields one correctly-bounded piece; only the whole-match window needs the second yield.</summary>
        private static IEnumerable<(double From, double To, bool ReachedRealEnd, int Phase)> ClipAndTagPhase(
            double realFrom, double realTo, TimeWindow window, double? tPhase2)
        {
            if (!window.Clip(realFrom, realTo, out double from, out double to))
                yield break;

            if (tPhase2.HasValue && from < tPhase2.Value && tPhase2.Value < to)
            {
                yield return (from, tPhase2.Value, false, 1);
                yield return (tPhase2.Value, to, to >= realTo, 2);
            }
            else
            {
                int phase = (tPhase2.HasValue && from >= tPhase2.Value) ? 2 : 1;
                yield return (from, to, to >= realTo, phase);
            }
        }

        // ==================================================================== header

        private static void BuildHeader(ReportHeader header, TelemetryLog log, Dictionary<int, TelemetrySession> sessionByActor,
            Dictionary<int, (double First, double Last)> coverageByActor, TimeWindow window, double sampleInterval, TimeWindow markerWindow = null)
        {
            markerWindow ??= window;
            header.MatchId = log.MatchId;
            // THIS window's duration: the whole match's length for the whole-match window, or a phase's own duration on a
            // phase-scoped build, which lets the HTML compare it against BalanceTargets'
            // Phase1DurationSeconds/Phase2DurationSeconds.
            header.MatchLengthSeconds = window.End - window.Start;
            header.MalformedLineCount = log.MalformedLineCount;
            header.UnknownEventCount = log.UnknownEventCount;
            header.UnreadableFileCount = log.UnreadableFileCount;
            header.NewerSchemaCount = log.NewerSchemaCount;
            header.OtherMatchId = log.OtherMatchId;
            header.OtherMatchFileCount = log.OtherMatchFileCount;

            TelemetrySession primary = null;
            foreach (TelemetrySession s in log.Sessions)
            {
                if (!string.IsNullOrEmpty(s.Commit) && !header.Commits.Contains(s.Commit))
                    header.Commits.Add(s.Commit);
                if (primary == null || (s.IsMaster && !primary.IsMaster))
                    primary = s;
            }

            if (primary?.Tuning != null)
            {
                header.TuningJson = primary.Tuning.ToString(Newtonsoft.Json.Formatting.None);
                header.PlayersPerTeam = primary.Tuning["territory"]?["playersPerTeam"]?.ToObject<int?>() ?? 0;
                header.FreeLoadoutUsed = primary.Tuning["gameplay"]?["freeLoadout"]?.ToObject<bool?>() ?? false;
            }

            foreach (TelemetryEvent e in log.Events)
            {
                // The free-loadout/debug-gold warnings are scoped to THIS window. A Marker uses markerWindow instead: on the
                // whole-match build that is [0, End] (full log, warm-up included - Markers stay visible over the full log like
                // Bug reports/Console), but still scoped per phase tab on a Phase 1/Phase 2 build (a marker dropped in Phase 2
                // has no business on the Phase 1 tab).
                if (e.Name == TelemetryKeys.Marker)
                {
                    if (!markerWindow.Contains(e.T)) continue;
                    // "spectator joined: actor N" is bookkeeping for the log-coverage table, not a moment somebody flagged.
                    if (LobbyMarkerNotes.TryReadSpectator(e.Data[TelemetryKeys.Note]?.ToString(), out _)) continue;
                    header.Markers.Add(new MarkerRow
                    {
                        T = e.T,
                        Actor = ReadInt(e.Data, TelemetryKeys.Actor, -1),
                        Note = e.Data[TelemetryKeys.Note]?.ToString() ?? "",
                    });
                    continue;
                }

                if (!window.Contains(e.T)) continue;

                if (e.Name == TelemetryKeys.GoldEarned && !IsJunkGoldEarned(e.Data))
                {
                    if (ReadInt(e.Data, TelemetryKeys.Debug, 0) > 0)
                        header.DebugGoldUsed = true;
                }
                // OR the tuning flag with any purchase marked free: a mid-match Free Loadout toggle (or a free purchase with no
                // matching tuning read) must still surface the warning.
                else if (e.Name == TelemetryKeys.Purchase && (e.Data[TelemetryKeys.Free]?.ToObject<bool?>() ?? false))
                {
                    header.FreeLoadoutUsed = true;
                }
            }

            // Player coverage: each FILE's first/last real event - a fact about the file, not about any one phase window, so it
            // stays unwindowed even on a phase-scoped build.
            var firstT = new Dictionary<string, double>();
            var lastT = new Dictionary<string, double>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.T < 0) continue;
                if (!firstT.TryGetValue(e.File, out double f) || e.T < f) firstT[e.File] = e.T;
                if (!lastT.TryGetValue(e.File, out double l) || e.T > l) lastT[e.File] = e.T;
            }
            foreach (TelemetrySession s in log.Sessions)
            {
                if (s.Spectator) continue; // a spectator host's file is no player row
                header.Coverage.Add(new PlayerCoverageRow
                {
                    Actor = s.Actor,
                    Nick = s.Nick,
                    FirstT = firstT.TryGetValue(s.File, out double f) ? f : 0,
                    LastT = lastT.TryGetValue(s.File, out double l) ? l : 0,
                });
            }

            // Log coverage: every actor seen ANYWHERE (not just those with their own file), always whole-match regardless of
            // this build's window - see LogCoverageRow.
            BuildLogCoverage(log, sessionByActor, coverageByActor, header.LogCoverage, sampleInterval);
        }

        /// <summary>Every actor seen anywhere in the match (joins, sessions, `hit` attackers/victims, `death` killers/assists)
        /// against which files are present, so the report can say "no log from actor N" instead of silently under-counting
        /// their damage, gold and purchases.</summary>
        private static void BuildLogCoverage(TelemetryLog log, Dictionary<int, TelemetrySession> sessionByActor,
            Dictionary<int, (double First, double Last)> coverageByActor, List<LogCoverageRow> outRows, double sampleInterval)
        {
            var seen = new HashSet<int>(sessionByActor.Keys);
            // A spectator has no match log of their own (only a spectator HOST writes one, and that file is no player row), so
            // "no log from actor N" would be a false alarm. A spectator host's session says so (spec flag); any other spectator
            // is named by the master's "spectator joined" marker (MatchTelemetry writes one when it sees their spec flag).
            var spectators = new HashSet<int>();
            foreach (TelemetrySession s in sessionByActor.Values)
                if (s.Spectator) spectators.Add(s.Actor);
            // A MISSING actor's nick/first-seen/last-seen come from whoever else logged their `join`/`leave` (every client logs
            // every OTHER player's join and leave, even one whose own file never opened).
            var nickByActor = new Dictionary<int, string>();
            var earliestJoinByActor = new Dictionary<int, double>();
            var latestLeaveByActor = new Dictionary<int, double>();

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name == TelemetryKeys.Join)
                {
                    int a = ReadInt(e.Data, TelemetryKeys.Actor, -1);
                    if (a < 0) continue;
                    seen.Add(a);
                    string nick = e.Data[TelemetryKeys.Nick]?.ToString();
                    if (!string.IsNullOrEmpty(nick) && !nickByActor.ContainsKey(a))
                        nickByActor[a] = nick;
                    if (!earliestJoinByActor.TryGetValue(a, out double existingJoin) || e.T < existingJoin)
                        earliestJoinByActor[a] = e.T;
                }
                else if (e.Name == TelemetryKeys.Marker)
                {
                    if (LobbyMarkerNotes.TryReadSpectator(e.Data[TelemetryKeys.Note]?.ToString(), out int spectator))
                        spectators.Add(spectator);
                }
                else if (e.Name == TelemetryKeys.Leave)
                {
                    int a = ReadInt(e.Data, TelemetryKeys.Actor, -1);
                    if (a < 0) continue;
                    if (!latestLeaveByActor.TryGetValue(a, out double existingLeave) || e.T > existingLeave)
                        latestLeaveByActor[a] = e.T;
                }
                else if (e.Name == TelemetryKeys.Hit)
                {
                    int attacker = ReadInt(e.Data, TelemetryKeys.Attacker, -1);
                    int victim = ReadInt(e.Data, TelemetryKeys.Victim, -1);
                    if (attacker >= 0) seen.Add(attacker);
                    if (victim >= 0) seen.Add(victim);
                }
                else if (e.Name == TelemetryKeys.Death)
                {
                    int killer = ReadInt(e.Data, TelemetryKeys.Killer, -1);
                    if (killer >= 0) seen.Add(killer);

                    var assists = e.Data[TelemetryKeys.Assists] as JArray;
                    if (assists != null)
                        foreach (JToken t in assists)
                        {
                            if (t.Type == JTokenType.Null) continue;
                            int assister = t.ToObject<int>();
                            if (assister >= 0) seen.Add(assister);
                        }
                }
            }

            foreach (int actor in seen.OrderBy(a => a))
            {
                if (spectators.Contains(actor)) continue;
                bool filePresent = sessionByActor.TryGetValue(actor, out TelemetrySession session);

                string nick;
                double? firstT, lastT;
                bool joinedAndLeft = false;

                if (filePresent)
                {
                    nick = session.Nick;
                    (double First, double Last) coverage = coverageByActor.TryGetValue(actor, out var c) ? c : (0, 0);
                    firstT = coverage.First;
                    lastT = coverage.Last;
                }
                else
                {
                    nick = nickByActor.GetValueOrDefault(actor, "");
                    bool hasJoin = earliestJoinByActor.TryGetValue(actor, out double joinT);
                    bool hasLeave = latestLeaveByActor.TryGetValue(actor, out double leaveT);
                    firstT = hasJoin ? joinT : (double?)null;
                    lastT = hasLeave ? leaveT : (double?)null;
                    // The span must be genuinely short, not just present: a join->leave gap of minutes still means real,
                    // uncounted gameplay happened (damage, gold, purchases nobody's file recorded), which deserves the harsh
                    // warning, not the soft "gone before logging started" one. Threshold: shorter than one sample interval -
                    // long enough that even a single `sample` line never had a chance to flush before they left.
                    joinedAndLeft = hasJoin && hasLeave && leaveT >= joinT && (leaveT - joinT) < sampleInterval;
                }

                outRows.Add(new LogCoverageRow
                {
                    Actor = actor,
                    Nick = nick,
                    FilePresent = filePresent,
                    FirstT = firstT,
                    LastT = lastT,
                    JoinedAndLeftBeforeLoggingStarted = joinedAndLeft,
                });
            }
        }

        /// <summary>The "Bug reports" and per-player "Console" sections' data, in one pass. A console line carries no actor
        /// field - the FILE it came from is the player (see TelemetryKeys.Console and ActorOf) - so both a bug card's console
        /// window and the per-player grouping read it through fileActor.
        ///
        /// Bug cards: the reporter's OWN chat (never another player's) in [t, t+60], and every client's console lines (any
        /// level, including plain "log" - the one place they're kept) in [t-20, t+5], merged across files and sorted by time. A
        /// "dropped" summary line (State "dropped", no Message) is never a real console line and is skipped everywhere here.
        ///
        /// Per-player Console: every non-"log" line (warning/error/exception/assert), grouped by (actor, level, message) - the
        /// count SUMS each matching line's fold count (n), since one folded line can represent several real repeats, and
        /// First/Last t spans every matching line's first/last-touched time, not just its single `t`.</summary>
        private static void BuildBugsAndConsole(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, TelemetrySession> sessionByActor, double? tPhase2, List<BugRow> outBugs,
            List<ConsolePlayerGroupRow> outConsoleByPlayer)
        {
            var chatByActor = new Dictionary<int, List<(double T, string Text)>>();
            var consoleLines = new List<(double T, int Actor, string Level, string Message, int N, double FirstT, double LastT)>();

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name == TelemetryKeys.Chat)
                {
                    int actor = ReadInt(e.Data, TelemetryKeys.Actor, -1);
                    if (actor < 0) continue;
                    if (!chatByActor.TryGetValue(actor, out var list))
                        chatByActor[actor] = list = new List<(double, string)>();
                    list.Add((e.T, e.Data[TelemetryKeys.Text]?.ToString() ?? ""));
                }
                else if (e.Name == TelemetryKeys.Console)
                {
                    string level = e.Data[TelemetryKeys.State]?.ToString() ?? "";
                    if (level == "dropped") continue; // a summary line, never a real message.
                    int actor = ActorOf(e, fileActor);
                    int n = ReadInt(e.Data, TelemetryKeys.RepeatCount, 1);
                    double firstT = ReadDoubleOrDefault(e.Data, TelemetryKeys.FirstT, e.T);
                    double lastT = ReadDoubleOrDefault(e.Data, TelemetryKeys.LastT, e.T);
                    consoleLines.Add((e.T, actor, level, e.Data[TelemetryKeys.Message]?.ToString() ?? "", n, firstT, lastT));
                }
            }

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Bug) continue;

                int actor = ReadInt(e.Data, TelemetryKeys.Actor, -1);
                TelemetrySession session = sessionByActor.GetValueOrDefault(actor);

                var bug = new BugRow
                {
                    T = e.T,
                    Actor = actor,
                    Nick = session?.Nick ?? "",
                    Team = ReadInt(e.Data, TelemetryKeys.Team, -1),
                    Alive = e.Data[TelemetryKeys.Alive]?.ToObject<bool?>() ?? true,
                    Zone = ReadInt(e.Data, TelemetryKeys.Zone, -1),
                    X = ReadFloat(e.Data, TelemetryKeys.X),
                    Z = ReadFloat(e.Data, TelemetryKeys.Z),
                    Weapon = ReadInt(e.Data, TelemetryKeys.Weapon, -1),
                    Attachment = ReadInt(e.Data, TelemetryKeys.Attachment, -1),
                    Mobility = ReadInt(e.Data, TelemetryKeys.Mobility, -1),
                    Ultimate = ReadInt(e.Data, TelemetryKeys.Ultimate, -1),
                    ScreenshotFile = e.Data[TelemetryKeys.ScreenshotFile]?.ToString() ?? "",
                    Phase = PhaseOf(e.T, tPhase2),
                };

                // The reporter's OWN chat, 0-60s after the mark - never another player's.
                if (chatByActor.TryGetValue(actor, out var chats))
                    foreach (var (t, text) in chats)
                        if (t >= e.T && t <= e.T + 60.0)
                            bug.ChatNotes.Add(text);

                // Every client's console lines, 20s before to 5s after - merged across files, sorted by time below, labelled by
                // whichever player's file each came from.
                foreach (var (t, lineActor, level, message, n, _, _) in consoleLines)
                {
                    if (t < e.T - 20.0 || t > e.T + 5.0) continue;
                    bug.ConsoleWindow.Add(new ConsoleLineRef
                    {
                        T = t,
                        Actor = lineActor,
                        Nick = sessionByActor.GetValueOrDefault(lineActor)?.Nick ?? "",
                        Level = level,
                        Message = message,
                        Count = n,
                    });
                }
                bug.ConsoleWindow.Sort((a, b) => a.T.CompareTo(b.T));

                outBugs.Add(bug);
            }

            // Console section, per player: every level except plain "log" (which only appears inside bug windows - see
            // BugRow.ConsoleWindow), grouped by (actor, level, message), with the summed count and the first/last time.
            var groups = new Dictionary<(int Actor, string Level, string Message), ConsolePlayerGroupRow>();
            foreach (var (t, actor, level, message, n, firstT, lastT) in consoleLines)
            {
                if (level == "log") continue;
                var key = (actor, level, message);
                if (!groups.TryGetValue(key, out ConsolePlayerGroupRow row))
                {
                    row = new ConsolePlayerGroupRow
                    {
                        Actor = actor,
                        Nick = sessionByActor.GetValueOrDefault(actor)?.Nick ?? "",
                        Level = level,
                        Message = message,
                        FirstT = firstT,
                        LastT = lastT,
                    };
                    groups[key] = row;
                }
                row.Count += n;
                if (firstT < row.FirstT) row.FirstT = firstT;
                if (lastT > row.LastT) row.LastT = lastT;
            }
            outConsoleByPlayer.AddRange(groups.Values.OrderBy(r => r.Actor).ThenBy(r => r.FirstT));
        }

        /// <summary>A late joiner's `session` line can be written before the room's player-properties echo carries their team
        /// (tm:-1), which would drop their whole slice from every team-keyed table (economy, zone income, gold gap,
        /// players.csv). Each actor's team is instead read from their first `sample` that reports tm >= 0 - `sample` is written
        /// by the owner every interval, so it catches up moments later - falling back to the session's team only if no sample
        /// ever does.</summary>
        private static Dictionary<int, int> BuildEffectiveTeam(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, TelemetrySession> sessionByActor)
        {
            var result = new Dictionary<int, int>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Sample) continue;
                int actor = ActorOf(e, fileActor);
                if (result.ContainsKey(actor)) continue;
                int tm = ReadInt(e.Data, TelemetryKeys.Team, -1);
                if (tm >= 0) result[actor] = tm;
            }
            foreach (var kv in sessionByActor)
                if (!result.ContainsKey(kv.Key))
                    result[kv.Key] = kv.Value.Team;
            return result;
        }

        private static Dictionary<int, (double First, double Last)> BuildCoverageByActor(TelemetryLog log, Dictionary<string, int> fileActor)
        {
            var firstT = new Dictionary<int, double>();
            var lastT = new Dictionary<int, double>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.T < 0) continue; // t == -1 never counts as covered time.
                int actor = ActorOf(e, fileActor);
                if (!firstT.TryGetValue(actor, out double f) || e.T < f) firstT[actor] = e.T;
                if (!lastT.TryGetValue(actor, out double l) || e.T > l) lastT[actor] = e.T;
            }
            var result = new Dictionary<int, (double, double)>();
            foreach (int actor in firstT.Keys)
                result[actor] = (firstT[actor], lastT[actor]);
            return result;
        }

        /// <summary>The sample interval used to bound the trailing interval and cap gaps. A session line from a real match
        /// carries `telemetry.sampleIntervalSeconds` (in the tuning snapshot); older logs don't, so this falls back to the
        /// shipped default (5s) when the field is absent.</summary>
        private static double ResolveSampleIntervalSeconds(TelemetryLog log)
        {
            foreach (TelemetrySession s in log.Sessions)
            {
                double? v = s.Tuning?["telemetry"]?["sampleIntervalSeconds"]?.ToObject<double?>();
                if (v.HasValue && v.Value > 0) return v.Value;
            }
            return DefaultSampleIntervalSeconds;
        }

        // ==================================================================== ownership

        private static void BuildOwnership(TelemetryLog log, List<OwnershipRow> outStints, Dictionary<int, int> zoneTier,
                                            Dictionary<int, List<(double T, int New)>> rawChangesByZone, double matchLength,
                                            TimeWindow window, double? tPhase2)
        {
            var dedupe = new HashSet<(int Zone, int New, int Since)>();
            var changesByZone = new Dictionary<int, List<(double T, int Tier, int Old, int New)>>();

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Ownership) continue;

                int zone = ReadInt(e.Data, TelemetryKeys.Zone, -1);
                int tier = ReadInt(e.Data, TelemetryKeys.Tier, 0);
                int oldOwner = ReadInt(e.Data, TelemetryKeys.OldOwner, Neutral);
                int newOwner = ReadInt(e.Data, TelemetryKeys.NewOwner, Neutral);
                int since = ReadInt(e.Data, TelemetryKeys.HeldSince, 0);

                if (!dedupe.Add((zone, newOwner, since)))
                    continue; // The exact same change, logged by a second master around a handover.

                zoneTier[zone] = tier;

                if (!changesByZone.TryGetValue(zone, out var list))
                    changesByZone[zone] = list = new List<(double, int, int, int)>();
                list.Add((e.T, tier, oldOwner, newOwner));

                if (!rawChangesByZone.TryGetValue(zone, out var raw))
                    rawChangesByZone[zone] = raw = new List<(double, int)>();
                raw.Add((e.T, newOwner));
            }

            foreach (var kv in changesByZone.OrderBy(kv => kv.Key))
            {
                int zone = kv.Key;
                var changes = kv.Value; // already in time order (log.Events is globally t-sorted)
                for (int i = 0; i < changes.Count; i++)
                {
                    var (t, tier, _, newOwner) = changes[i];
                    if (newOwner == Neutral) continue; // A zone going neutral ends the PRECEDING stint - no row of its own.

                    double to = (i + 1 < changes.Count) ? changes[i + 1].T : matchLength;
                    string howEnded = (i + 1 < changes.Count)
                        ? (changes[i + 1].New == Neutral ? "decayed" : "captured")
                        : "matchEnd";

                    // Clip this stint to the requested window, splitting it at the phase boundary too when it straddles one
                    // (even on a whole-match build - see ClipAndTagPhase).
                    foreach (var piece in ClipAndTagPhase(t, to, window, tPhase2))
                    {
                        outStints.Add(new OwnershipRow
                        {
                            Zone = zone,
                            Tier = tier,
                            Team = newOwner,
                            From = piece.From,
                            To = piece.To,
                            Duration = piece.To - piece.From,
                            HowEnded = piece.ReachedRealEnd ? howEnded : "phaseBoundary",
                            Phase = piece.Phase,
                        });
                    }
                }
            }
        }

        private static int OwnerAtTime(Dictionary<int, List<(double T, int New)>> rawChangesByZone, int zone, double time)
        {
            if (zone < 0 || !rawChangesByZone.TryGetValue(zone, out var changes)) return Neutral;
            int owner = Neutral;
            foreach (var (t, newOwner) in changes)
            {
                if (t > time) break;
                owner = newOwner;
            }
            return owner;
        }

        // ==================================================================== captures

        /// <summary>ONE time-ordered pass over `capture` lines and deduped `ownership` changes together, so a zone re-captured,
        /// drained and captured again in one match yields several rows (a two-pass version collapsed every start/pause/resume
        /// cycle for a zone into one mutable attempt). Ownership items sort BEFORE a same-instant capture item, so a completion
        /// recorded in the same instant as a stray same-tick capture line closes the right attempt first.
        ///
        /// The state machine builds the RAW (unwindowed) attempts into a local list; the final clip-and-tag pass (like
        /// BuildOwnership's) scopes them to the window and phase-splits a straddling attempt.</summary>
        private static void BuildCaptures(TelemetryLog log, Dictionary<int, List<(double T, int New)>> rawChangesByZone,
                                           Dictionary<int, int> zoneTier, double matchLength, ReportHeader header,
                                           List<CaptureRow> outCaptures, TimeWindow window, double? tPhase2)
        {
            var items = new List<(double T, bool IsOwnership, int Zone, string State, int Team, int Players, int NewOwner)>();

            // Ownership items first: OrderBy below is stable, so for two items sharing the exact same
            // t, whichever was appended here first keeps that relative order.
            foreach (var kv in rawChangesByZone)
                foreach (var (t, newOwner) in kv.Value)
                    items.Add((t, true, kv.Key, "", -1, 0, newOwner));

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Capture) continue;
                items.Add((e.T, false,
                    ReadInt(e.Data, TelemetryKeys.Zone, -1),
                    e.Data[TelemetryKeys.State]?.ToString() ?? "",
                    ReadInt(e.Data, TelemetryKeys.Team, -1),
                    ReadInt(e.Data, TelemetryKeys.Players, 0),
                    0));
            }

            items = items.OrderBy(i => i.T).ToList();

            var open = new Dictionary<int, CaptureAttempt>();
            var rawCaptures = new List<CaptureRow>();

            foreach (var item in items)
            {
                if (item.IsOwnership)
                {
                    if (!open.TryGetValue(item.Zone, out CaptureAttempt attempt)) continue;

                    bool closesAsCompleted = !attempt.IsDrain && item.NewOwner == attempt.Team;
                    bool closesAsNeutralised = attempt.IsDrain && item.NewOwner == Neutral;
                    if (!closesAsCompleted && !closesAsNeutralised) continue;

                    rawCaptures.Add(new CaptureRow
                    {
                        Zone = item.Zone,
                        Tier = zoneTier.GetValueOrDefault(item.Zone, 0),
                        Team = attempt.Team,
                        Start = attempt.Start,
                        End = item.T,
                        Duration = item.T - attempt.Start,
                        Outcome = closesAsCompleted ? "completed" : "neutralised",
                        Players = attempt.LastPlayers,
                    });
                    open.Remove(item.Zone);
                    continue;
                }

                if (!CaptureKnownStates.Contains(item.State))
                {
                    header.UnknownCaptureStateCount++;
                    continue;
                }

                if (CaptureOpenStates.Contains(item.State))
                {
                    bool isDrain = item.State == "drainStarted" || item.State == "drainResumed";
                    bool isFreshStart = CaptureFreshStartStates.Contains(item.State);

                    if (open.TryGetValue(item.Zone, out CaptureAttempt existing))
                    {
                        if (isFreshStart)
                        {
                            // A brand new start/drain-start while something is still open for this
                            // zone: that previous attempt's story ends here, unresolved.
                            rawCaptures.Add(new CaptureRow
                            {
                                Zone = item.Zone,
                                Tier = zoneTier.GetValueOrDefault(item.Zone, 0),
                                Team = existing.Team,
                                Start = existing.Start,
                                End = item.T,
                                Duration = item.T - existing.Start,
                                Outcome = "abandoned",
                                Players = existing.LastPlayers,
                            });
                            open[item.Zone] = new CaptureAttempt { Team = item.Team, Start = item.T, IsDrain = isDrain, LastPlayers = item.Players };
                        }
                        else
                        {
                            // resumed/drainResumed: continues the SAME open attempt.
                            existing.LastPlayers = item.Players;
                        }
                    }
                    else
                    {
                        open[item.Zone] = new CaptureAttempt { Team = item.Team, Start = item.T, IsDrain = isDrain, LastPlayers = item.Players };
                    }
                }
                else if (CapturePauseStates.Contains(item.State))
                {
                    if (open.TryGetValue(item.Zone, out CaptureAttempt attempt))
                        attempt.LastPlayers = item.Players;
                }
                // "completed"/"neutralised" (older logs only) are known but never drive the state machine; the matching
                // ownership item in this merged pass is what closes the attempt.
            }

            foreach (var kv in open.OrderBy(kv => kv.Key))
            {
                CaptureAttempt attempt = kv.Value;
                rawCaptures.Add(new CaptureRow
                {
                    Zone = kv.Key,
                    Tier = zoneTier.GetValueOrDefault(kv.Key, 0),
                    Team = attempt.Team,
                    Start = attempt.Start,
                    End = matchLength,
                    Duration = matchLength - attempt.Start,
                    Outcome = "abandoned",
                    Players = attempt.LastPlayers,
                });
            }

            // Clip + phase-tag every raw attempt against the requested window, like BuildOwnership's stints (ClipAndTagPhase).
            foreach (CaptureRow raw in rawCaptures)
            {
                foreach (var piece in ClipAndTagPhase(raw.Start, raw.End, window, tPhase2))
                {
                    outCaptures.Add(new CaptureRow
                    {
                        Zone = raw.Zone,
                        Tier = raw.Tier,
                        Team = raw.Team,
                        Start = piece.From,
                        End = piece.To,
                        Duration = piece.To - piece.From,
                        Outcome = piece.ReachedRealEnd ? raw.Outcome : "phaseBoundary",
                        Players = raw.Players,
                        Phase = piece.Phase,
                    });
                }
            }
        }

        // ==================================================================== purchases / shop blocked / hits

        private static void BuildPurchasesAndBlocked(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, TelemetrySession> sessionByActor, Dictionary<int, int> effectiveTeam, ReportTables tables,
            TimeWindow window, double? tPhase2)
        {
            foreach (TelemetryEvent e in log.Events)
            {
                if (!window.Contains(e.T)) continue;

                int actor = ActorOf(e, fileActor);
                TelemetrySession session = sessionByActor.GetValueOrDefault(actor);
                int team = effectiveTeam.GetValueOrDefault(actor, -1);
                string nick = session?.Nick ?? "";
                int phase = PhaseOf(e.T, tPhase2);

                if (e.Name == TelemetryKeys.Purchase)
                {
                    tables.Purchases.Add(new PurchaseRow
                    {
                        T = e.T, Actor = actor, Nick = nick, Team = team, Kind = "purchase",
                        Category = CategoryName(e.Data[TelemetryKeys.Category]?.ToString()),
                        // Item stays an opaque int: 100+level (absorb) / 200+level (recharge) for armor, a real weapon/ability
                        // id otherwise. Never decoded - see TelemetryKeys.ItemId.
                        ItemId = ReadInt(e.Data, TelemetryKeys.ItemId, -1),
                        Amount = ReadInt(e.Data, TelemetryKeys.Price, 0),
                        BalanceAfter = ReadInt(e.Data, TelemetryKeys.BalanceAfter, 0),
                        Zone = ReadInt(e.Data, TelemetryKeys.Zone, -1),
                        Free = e.Data[TelemetryKeys.Free]?.ToObject<bool?>() ?? false,
                        Phase = phase,
                    });
                }
                else if (e.Name == TelemetryKeys.Refund)
                {
                    tables.Purchases.Add(new PurchaseRow
                    {
                        T = e.T, Actor = actor, Nick = nick, Team = team, Kind = "refund",
                        Category = CategoryName(e.Data[TelemetryKeys.Category]?.ToString()),
                        ItemId = -1,
                        Amount = ReadInt(e.Data, TelemetryKeys.Amount, 0),
                        BalanceAfter = ReadInt(e.Data, TelemetryKeys.BalanceAfter, 0),
                        Zone = ReadInt(e.Data, TelemetryKeys.Zone, -1),
                        Phase = phase,
                    });
                }
                else if (e.Name == TelemetryKeys.ShopBlocked)
                {
                    tables.ShopBlocked.Add(new ShopBlockedRow
                    {
                        T = e.T, Actor = actor, Nick = nick,
                        ItemId = ReadInt(e.Data, TelemetryKeys.ItemId, -1),
                        Price = ReadInt(e.Data, TelemetryKeys.Price, 0),
                        Reason = e.Data[TelemetryKeys.Reason]?.ToString() ?? "",
                        Shortfall = ReadInt(e.Data, TelemetryKeys.Shortfall, 0),
                        Zone = ReadInt(e.Data, TelemetryKeys.Zone, -1),
                        Phase = phase,
                    });
                }
            }
        }

        /// <summary>hits.csv/deaths.csv take the `at`/`vt`/`at` (killer) team field from the event line, but a late joiner's
        /// early lines can carry tm:-1 before the room's player-properties echo arrives; a raw -1 falls back to the actor's
        /// effectiveTeam (as every other team-keyed table does). A genuinely unresolvable actor (id -1, e.g. a dummy) still
        /// reads -1.</summary>
        private static int ResolveTeam(int rawTeam, int actor, Dictionary<int, int> effectiveTeam)
        {
            if (rawTeam >= 0) return rawTeam;
            return effectiveTeam.GetValueOrDefault(actor, -1);
        }

        private static void BuildHits(TelemetryLog log, Dictionary<int, int> effectiveTeam, List<HitRow> outHits,
            TimeWindow window, double? tPhase2)
        {
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Hit) continue;
                if (!window.Contains(e.T)) continue;

                JToken dToken = e.Data[TelemetryKeys.Distance];
                float? distance = (dToken != null && dToken.Type != JTokenType.Null) ? dToken.ToObject<float?>() : null;

                int attacker = ReadInt(e.Data, TelemetryKeys.Attacker, -1);
                int victim = ReadInt(e.Data, TelemetryKeys.Victim, -1);

                outHits.Add(new HitRow
                {
                    T = e.T,
                    Attacker = attacker,
                    AttackerTeam = ResolveTeam(ReadInt(e.Data, TelemetryKeys.AttackerTeam, -1), attacker, effectiveTeam),
                    Victim = victim,
                    VictimTeam = ResolveTeam(ReadInt(e.Data, TelemetryKeys.VictimTeam, -1), victim, effectiveTeam),
                    Weapon = ReadInt(e.Data, TelemetryKeys.Weapon, -1),
                    Ability = ReadInt(e.Data, TelemetryKeys.AbilityId, -1),
                    Source = e.Data[TelemetryKeys.Source]?.ToString() ?? "",
                    Raw = ReadFloat(e.Data, TelemetryKeys.Raw),
                    Armor = ReadFloat(e.Data, TelemetryKeys.ArmorAbsorbed),
                    // New logs use "hpLost"; older ones wrote the same value as "hp".
                    HealthLost = ReadFloat(e.Data, TelemetryKeys.HealthLost, "hp"),
                    Lethal = e.Data[TelemetryKeys.Lethal]?.ToObject<bool?>() ?? false,
                    Distance = distance,
                    Vulnerable = ReadFloat(e.Data, TelemetryKeys.Vulnerable),
                    Overpower = e.Data[TelemetryKeys.OverpowerActive]?.ToObject<bool?>() ?? false,
                    // Absent key (a non-marking hit, or a hit logged before the field existed) reads 0, like every other "only
                    // when non-zero" field.
                    Mark = ReadInt(e.Data, TelemetryKeys.Mark, 0),
                    Phase = PhaseOf(e.T, tPhase2),
                });
            }
        }

        /// <summary>A lethal Burn tick is logged as both a flushed `dot` (its accumulated bucket) AND a `hit` (so every kill
        /// keeps a row - see PlayerTelemetry.RouteHit). That hit's damage is already counted via the `dot`, so damage sums must
        /// skip it. It stays in hits.csv, and kills never read a hit row (they come from `death`).</summary>
        private static bool CountsTowardDamageSums(HitRow h) => h.Source != "Burn";

        // ==================================================================== gold timeline / economy by minute

        /// <summary>The one table not windowed as cleanly as the rest. gold_timeline.csv's EarnedSoFar/SpentSoFar stay
        /// whole-match running totals ON PURPOSE even on a phase-scoped build (a Phase 2 row showing a Phase-1-inclusive
        /// running total beats a reset to zero); only which SAMPLE ROWS are emitted is scoped. economy_by_minute.csv scopes
        /// each event's contribution to the window, and only buckets overlapping the window survive, but the minute INDEX stays
        /// the absolute match minute: the "zones held per tier" and "gold gap to richest" passes below depend on absolute
        /// minute math (matchLength, OwnerAtTime at an absolute midpoint, a stale-sample cutoff from an absolute bucket end). A
        /// known simplification.</summary>
        private static void BuildGoldTimelineAndEconomy(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, TelemetrySession> sessionByActor, Dictionary<int, int> effectiveTeam, Dictionary<int, int> zoneTier,
            Dictionary<int, List<(double T, int New)>> rawChangesByZone, double matchLength, ReportTables tables,
            TimeWindow window, double? tPhase2, int? forcedPhase)
        {
            var earnedRunning = new Dictionary<int, int>();
            var spentRunning = new Dictionary<int, int>();

            // Ceiling, not floor+1: an event at exactly t == matchLength (a multiple of 60, e.g. a
            // 180s match's final samples) must land in the LAST occupied minute, not spill into an
            // empty one after it - see the minute-index clamp below.
            int numMinutes = Math.Max(1, (int)Math.Ceiling(matchLength / 60.0));
            List<int> teams = effectiveTeam.Values.Distinct().Where(t => t >= 0).OrderBy(t => t).ToList();
            var economyRows = new Dictionary<(int Minute, int Team), EconomyByMinuteRow>();
            for (int m = 0; m < numMinutes; m++)
                foreach (int team in teams)
                    economyRows[(m, team)] = new EconomyByMinuteRow { Minute = m, Team = team };

            foreach (TelemetryEvent e in log.Events)
            {
                int actor = ActorOf(e, fileActor);
                int team = effectiveTeam.GetValueOrDefault(actor, -1);

                if (e.Name == TelemetryKeys.GoldEarned)
                {
                    if (IsJunkGoldEarned(e.Data)) continue;

                    int total = SumGoldEarnedTotal(e.Data);
                    // Unwindowed on purpose - see this method's summary.
                    earnedRunning[actor] = earnedRunning.GetValueOrDefault(actor) + total;

                    if (window.Contains(e.T))
                    {
                        int minute = Math.Min((int)Math.Floor(e.T / 60.0), numMinutes - 1);
                        if (minute < 0) minute = 0;
                        if (team >= 0 && economyRows.TryGetValue((minute, team), out EconomyByMinuteRow row))
                        {
                            // Rescale this line's zones[] to sum to its terr (authoritative) before accumulating - see
                            // ScaledZones.
                            double[] scaledZones = ScaledZones(e.Data);
                            for (int z = 0; z < scaledZones.Length; z++)
                            {
                                if (!zoneTier.TryGetValue(z, out int tier)) continue;
                                if (tier < 1 || tier > row.IncomeByTier.Length) { tables.Header.InvalidTierCount++; continue; }
                                row.IncomeByTier[tier - 1] += scaledZones[z];
                            }
                            // terr > 0 with no zones[] to rescale by (empty or all zero): kept, not dropped - see
                            // UnattributedIncome.
                            row.UnattributedIncome += UnattributedTerritoryGold(e.Data, scaledZones);
                            // goldEarned.bounty is the OWNER's credited total, not the master's per-payout `bounty` line - safe
                            // to sum without double counting.
                            row.Bounty += ReadInt(e.Data, TelemetryKeys.Bounty, 0);
                            row.Refund += ReadInt(e.Data, TelemetryKeys.Refund, 0);
                        }
                    }
                }
                else if (e.Name == TelemetryKeys.Purchase)
                {
                    int price = ReadInt(e.Data, TelemetryKeys.Price, 0);
                    // Unwindowed on purpose - see this method's summary.
                    spentRunning[actor] = spentRunning.GetValueOrDefault(actor) + price;

                    if (window.Contains(e.T))
                    {
                        int minute = Math.Min((int)Math.Floor(e.T / 60.0), numMinutes - 1);
                        if (minute < 0) minute = 0;
                        if (team >= 0 && economyRows.TryGetValue((minute, team), out EconomyByMinuteRow row))
                            row.Spent += price;
                    }
                }
                else if (e.Name == TelemetryKeys.Sample)
                {
                    if (!window.Contains(e.T)) continue; // only this window's own samples.
                    tables.GoldTimeline.Add(new GoldTimelineRow
                    {
                        T = e.T,
                        Actor = actor,
                        Nick = sessionByActor.GetValueOrDefault(actor)?.Nick ?? "",
                        Team = team,
                        Balance = ReadInt(e.Data, TelemetryKeys.Balance, 0),
                        EarnedSoFar = earnedRunning.GetValueOrDefault(actor),
                        SpentSoFar = spentRunning.GetValueOrDefault(actor),
                        Phase = PhaseOf(e.T, tPhase2),
                    });
                }
            }

            // A zone's tier (from zoneTier, set once per zone by BuildOwnership) never changes across this sweep, only its
            // OWNER does; checking validity inside the per-minute loop counted the same bad zone once per minute. Computed
            // once, outside, so Header.InvalidTierCount counts distinct bad zones, not zone-minutes.
            var invalidTierZones = new HashSet<int>();
            foreach (var kv in zoneTier)
                if (kv.Value < 1 || kv.Value > 4)
                    invalidTierZones.Add(kv.Key);
            tables.Header.InvalidTierCount += invalidTierZones.Count;

            // Zones held per tier, sampled at each minute's midpoint.
            for (int m = 0; m < numMinutes; m++)
            {
                double midpoint = Math.Min(m * 60.0 + 30.0, matchLength);
                foreach (int zone in zoneTier.Keys)
                {
                    if (invalidTierZones.Contains(zone)) continue;
                    int tier = zoneTier[zone];
                    int owner = OwnerAtTime(rawChangesByZone, zone, midpoint);
                    if (owner < 0) continue;
                    if (economyRows.TryGetValue((m, owner), out EconomyByMinuteRow row))
                        row.ZonesHeldByTier[tier - 1]++;
                }
            }

            // Gold gap to the richest team, from each actor's last sample at or before the minute's end; a sample older than
            // (bucket end - 60s) is stale (the player likely left) and ignored rather than keeping their last balance forever.
            var samplesByActor = new Dictionary<int, List<(double T, int Balance)>>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Sample) continue;
                int actor = ActorOf(e, fileActor);
                if (!samplesByActor.TryGetValue(actor, out var list))
                    samplesByActor[actor] = list = new List<(double, int)>();
                list.Add((e.T, ReadInt(e.Data, TelemetryKeys.Balance, 0)));
            }

            const double StaleSampleWindowSeconds = 60.0;
            for (int m = 0; m < numMinutes; m++)
            {
                double cutoff = Math.Min((m + 1) * 60.0, matchLength);
                double staleBefore = cutoff - StaleSampleWindowSeconds;
                var teamBalance = new Dictionary<int, int>();
                foreach (int actor in effectiveTeam.Keys)
                {
                    int team = effectiveTeam[actor];
                    if (team < 0 || !samplesByActor.TryGetValue(actor, out var list)) continue;

                    int? balance = null;
                    double bestT = double.NegativeInfinity;
                    foreach (var (t, bal) in list)
                        if (t <= cutoff && t >= bestT) { bestT = t; balance = bal; }

                    if (balance.HasValue && bestT >= staleBefore)
                        teamBalance[team] = teamBalance.GetValueOrDefault(team) + balance.Value;
                }

                int richest = teamBalance.Values.DefaultIfEmpty(0).Max();
                foreach (int team in teams)
                    if (economyRows.TryGetValue((m, team), out EconomyByMinuteRow row))
                        row.GoldGapToRichest = richest - teamBalance.GetValueOrDefault(team);
            }

            // Keep only the minute buckets overlapping this window (the INDEX stays absolute, see this method's summary). A
            // Phase 1- or Phase 2-SCOPED build (forcedPhase set) tags every surviving row with that one phase: a bucket
            // straddling the transition can overlap a single-phase window while its START reads as the other phase (minute 1,
            // 60->120, overlaps Phase 2 [90,180] but starts at 60). Only the whole-match build (forcedPhase null) tags a
            // straddling bucket by its absolute start - the table's one simplification.
            tables.EconomyByMinute = economyRows.Values
                .Where(r => BucketOverlapsWindow(r.Minute, matchLength, window))
                .OrderBy(r => r.Minute).ThenBy(r => r.Team)
                .ToList();
            foreach (EconomyByMinuteRow row in tables.EconomyByMinute)
                row.Phase = forcedPhase ?? PhaseOf(row.Minute * 60.0, tPhase2);
        }

        private static bool BucketOverlapsWindow(int minute, double matchLength, TimeWindow window)
        {
            double bucketStart = minute * 60.0;
            double bucketEnd = Math.Min((minute + 1) * 60.0, matchLength);
            if (bucketEnd <= window.Start) return false;
            return window.EndInclusive ? bucketStart <= window.End : bucketStart < window.End;
        }

        // ==================================================================== zone income

        private static void BuildZoneIncome(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, int> effectiveTeam, Dictionary<int, int> zoneTier,
            List<OwnershipRow> stints, List<ZoneIncomeRow> outRows, TimeWindow window)
        {
            // `stints` is ALREADY window-clipped (BuildOwnership ran first), so summing its Duration is correctly scoped.
            var secondsHeld = new Dictionary<(int Zone, int Team), double>();
            foreach (OwnershipRow stint in stints)
                secondsHeld[(stint.Zone, stint.Team)] = secondsHeld.GetValueOrDefault((stint.Zone, stint.Team)) + stint.Duration;

            var goldGenerated = new Dictionary<(int Zone, int Team), double>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.GoldEarned || IsJunkGoldEarned(e.Data)) continue;
                if (!window.Contains(e.T)) continue;

                int actor = ActorOf(e, fileActor);
                int team = effectiveTeam.GetValueOrDefault(actor, -1);
                if (team < 0) continue;

                double[] scaledZones = ScaledZones(e.Data);
                for (int z = 0; z < scaledZones.Length; z++)
                {
                    if (scaledZones[z] == 0) continue;
                    goldGenerated[(z, team)] = goldGenerated.GetValueOrDefault((z, team)) + scaledZones[z];
                }

                // terr > 0 with no zones[] to rescale by: a synthetic "Unattributed" zone (UnattributedZone) keeps this gold
                // visible per team instead of vanishing from zone_income.csv while players.csv's running total still includes
                // it.
                double unattributed = UnattributedTerritoryGold(e.Data, scaledZones);
                if (unattributed > 0)
                    goldGenerated[(UnattributedZone, team)] = goldGenerated.GetValueOrDefault((UnattributedZone, team)) + unattributed;
            }

            var keys = secondsHeld.Keys.Union(goldGenerated.Keys).OrderBy(k => k.Zone).ThenBy(k => k.Team);
            foreach (var k in keys)
            {
                outRows.Add(new ZoneIncomeRow
                {
                    Zone = k.Zone,
                    Team = k.Team,
                    Tier = zoneTier.GetValueOrDefault(k.Zone, 0),
                    SecondsHeld = secondsHeld.GetValueOrDefault(k),
                    GoldGenerated = goldGenerated.GetValueOrDefault(k),
                });
            }
        }

        /// <summary>`goldEarned.zones[]` is rounded to whole gold PER ZONE at the source (PlayerTelemetry.RoundedZoneArray), so
        /// summing many lines drifts from the line's authoritative `terr` (a real log: Sigma-terr 254 vs Sigma-zones 244, 4%
        /// loss). Rescaling each line's zones to sum to its terr before accumulating removes that; the fraction is kept
        /// (callers accumulate into a double). A line with terr == 0 or zones summing to 0 is left as-is (no ratio to scale
        /// by).</summary>
        private static double[] ScaledZones(JObject data)
        {
            var zones = data[TelemetryKeys.Zones] as JArray;
            if (zones == null) return Array.Empty<double>();

            var raw = new double[zones.Count];
            double sum = 0;
            for (int i = 0; i < zones.Count; i++)
            {
                raw[i] = zones[i].Type == JTokenType.Null ? 0 : zones[i].ToObject<double>();
                sum += raw[i];
            }

            int terr = ReadInt(data, TelemetryKeys.Territory, 0);
            if (sum > 0.0001 && terr > 0 && Math.Abs(sum - terr) > 0.0001)
            {
                double factor = terr / sum;
                for (int i = 0; i < raw.Length; i++)
                    raw[i] *= factor;
            }
            return raw;
        }

        /// <summary>ScaledZones has no ratio to rescale by when a line's `zones[]` sums to ~0 (empty, or every entry 0); if its
        /// `terr` is still > 0 that gold has nowhere to go in a zone-keyed table (players.csv sums `terr` directly, so it stays
        /// correct). Returns the leftover to bucket into an "Unattributed" row, or 0 when the zones already accounted for
        /// it.</summary>
        private static double UnattributedTerritoryGold(JObject data, double[] scaledZones)
        {
            int terr = ReadInt(data, TelemetryKeys.Territory, 0);
            if (terr <= 0) return 0;

            double sum = 0;
            for (int i = 0; i < scaledZones.Length; i++) sum += scaledZones[i];
            return sum <= 0.0001 ? terr : 0;
        }

        // ==================================================================== sample-derived stats (shared sweep)

        private sealed class SampleDerivedStats
        {
            public readonly Dictionary<int, double> EquippedSecondsByWeapon = new Dictionary<int, double>();
            public readonly Dictionary<int, double> OwnZoneSecondsByActor = new Dictionary<int, double>();
            public readonly Dictionary<int, double> EnemyZoneSecondsByActor = new Dictionary<int, double>();
            public readonly Dictionary<int, double> NeutralZoneSecondsByActor = new Dictionary<int, double>();
        }

        /// <summary>One sweep over every `sample` event (already t-sorted) computing, from the same consecutive-sample
        /// intervals, how long each weapon was equipped (globally, weapons.csv) and how long each player stood in their own/an
        /// enemy's/a neutral zone (players.csv). Each interval is attributed using the state at its START (the convention used
        /// throughout, e.g. ownership's "from").
        ///
        /// - a sample with t == -1 is ignored (never a real state to start or end an interval);
        /// - an interval starting on a DEAD sample (alive:false) contributes no equipped/zone time;
        /// - a gap between consecutive samples is capped at 2x the sample interval, so a disconnected client's silent gap
        ///   doesn't keep accruing;
        /// - the LAST sample of each actor's stream gets one trailing interval (capped at the sample interval, never past that
        ///   actor's last covered instant);
        /// - every interval is CLIPPED to the window via TimeWindow.Clip, so one crossing a phase boundary contributes its
        ///   partial share to each phase's build.</summary>
        private static SampleDerivedStats BuildSampleDerivedStats(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, int> effectiveTeam, Dictionary<int, List<(double T, int New)>> rawChangesByZone,
            Dictionary<int, (double First, double Last)> coverageByActor, double sampleInterval, TimeWindow window)
        {
            var stats = new SampleDerivedStats();
            var lastT = new Dictionary<int, double>();
            var lastWeapon = new Dictionary<int, int>();
            var lastZone = new Dictionary<int, int>();
            var lastTeam = new Dictionary<int, int>();
            var lastAlive = new Dictionary<int, bool>();
            double gapCap = sampleInterval * 2.0;

            void Accumulate(int actor, double dur, int weapon, int zone, int team)
            {
                if (dur <= 0) return;
                if (weapon >= 0)
                    stats.EquippedSecondsByWeapon[weapon] = stats.EquippedSecondsByWeapon.GetValueOrDefault(weapon) + dur;

                int owner = zone < 0 ? Neutral : OwnerAtTime(rawChangesByZone, zone, lastT.GetValueOrDefault(actor));
                if (zone < 0 || owner == Neutral)
                    stats.NeutralZoneSecondsByActor[actor] = stats.NeutralZoneSecondsByActor.GetValueOrDefault(actor) + dur;
                else if (owner == team)
                    stats.OwnZoneSecondsByActor[actor] = stats.OwnZoneSecondsByActor.GetValueOrDefault(actor) + dur;
                else
                    stats.EnemyZoneSecondsByActor[actor] = stats.EnemyZoneSecondsByActor.GetValueOrDefault(actor) + dur;
            }

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Sample) continue;
                if (e.T < 0) continue; // t == -1 sample is ignored

                int actor = ActorOf(e, fileActor);
                double t = e.T;
                int weapon = ReadInt(e.Data, TelemetryKeys.Weapon, -1);
                int zone = ReadInt(e.Data, TelemetryKeys.Zone, -1);
                int team = effectiveTeam.GetValueOrDefault(actor, -1);
                bool alive = e.Data[TelemetryKeys.Alive]?.ToObject<bool?>() ?? true;

                if (lastT.TryGetValue(actor, out double prevT) && t > prevT)
                {
                    bool prevAlive = lastAlive.GetValueOrDefault(actor, true);
                    if (prevAlive) // a dead sample starts no counted interval
                    {
                        double intervalEnd = prevT + Math.Min(t - prevT, gapCap); // cap a disconnect gap
                        // Clip [prevT, intervalEnd) to the requested window.
                        if (window.Clip(prevT, intervalEnd, out double clipFrom, out double clipTo))
                            Accumulate(actor, clipTo - clipFrom, lastWeapon[actor], lastZone[actor], lastTeam[actor]);
                    }
                }

                lastT[actor] = t;
                lastWeapon[actor] = weapon;
                lastZone[actor] = zone;
                lastTeam[actor] = team;
                lastAlive[actor] = alive;
            }

            // Trailing interval for each actor's LAST sample, bounded by that actor's covered range - never invented time past
            // what we have for them; clipped to the window like every other interval.
            foreach (int actor in lastT.Keys)
            {
                if (!lastAlive.GetValueOrDefault(actor, true)) continue;
                double last = lastT[actor];
                double coverageEnd = coverageByActor.TryGetValue(actor, out var range) ? range.Last : last;
                double tailEnd = last + Math.Min(sampleInterval, Math.Max(0, coverageEnd - last));
                if (window.Clip(last, tailEnd, out double clipFrom, out double clipTo))
                    Accumulate(actor, clipTo - clipFrom, lastWeapon[actor], lastZone[actor], lastTeam[actor]);
            }

            return stats;
        }

        // ==================================================================== weapons / abilities

        private static void BuildWeaponsAndAbilities(TelemetryLog log, List<HitRow> hits,
            Dictionary<int, double> equippedSecondsByWeapon, ReportTables tables, TimeWindow window)
        {
            List<int> weaponIds = WeaponIdUniverse(log);

            var pullsByWeapon = new Dictionary<int, int>();
            var projectilesByWeapon = new Dictionary<int, int>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Shots) continue;
                if (!window.Contains(e.T)) continue;
                int w = ReadInt(e.Data, TelemetryKeys.Weapon, -1);
                pullsByWeapon[w] = pullsByWeapon.GetValueOrDefault(w) + ReadInt(e.Data, TelemetryKeys.Pulls, 0);
                projectilesByWeapon[w] = projectilesByWeapon.GetValueOrDefault(w) + ReadInt(e.Data, TelemetryKeys.Projectiles, 0);
            }

            var killsByWeapon = new Dictionary<int, int>();
            var killsByAbility = new Dictionary<int, int>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Death) continue;
                if (!window.Contains(e.T)) continue;
                int w = ReadInt(e.Data, TelemetryKeys.Weapon, -1);
                int ab = ReadInt(e.Data, TelemetryKeys.AbilityId, -1);
                if (w >= 0) killsByWeapon[w] = killsByWeapon.GetValueOrDefault(w) + 1;
                if (ab >= 0) killsByAbility[ab] = killsByAbility.GetValueOrDefault(ab) + 1;
            }

            // Accuracy is Projectile hits / projectiles ONLY: Splash hits (one rocket's splash landing on several targets) are
            // counted separately so they can't push accuracy over 100%. Distance stays Projectile-only for the same reason.
            // Damage sums exclude any Burn-source hit row (already counted via its `dot`), from BOTH weapon and ability totals.
            // `hits` (tables.Hits) is already window-filtered by BuildHits.
            var hitCountByWeapon = new Dictionary<int, int>();
            var splashCountByWeapon = new Dictionary<int, int>();
            var distancesByWeapon = new Dictionary<int, List<double>>();
            var damageRawByWeapon = new Dictionary<int, float>();
            var armorByWeapon = new Dictionary<int, float>();
            var healthByWeapon = new Dictionary<int, float>();
            var damageRawByAbility = new Dictionary<int, float>();
            // Counted regardless of Source (Projectile/Splash both apply as long as the firing weapon marks): Decision 6 keys
            // the mark on the WEAPON's stat block, not on how the damage arrived.
            var marksPlacedByWeapon = new Dictionary<int, int>();
            var marksCashedByWeapon = new Dictionary<int, int>();

            foreach (HitRow h in hits)
            {
                if (h.Weapon >= 0)
                {
                    if (h.Source == "Projectile")
                    {
                        hitCountByWeapon[h.Weapon] = hitCountByWeapon.GetValueOrDefault(h.Weapon) + 1;
                        if (h.Distance.HasValue)
                        {
                            if (!distancesByWeapon.TryGetValue(h.Weapon, out var list))
                                distancesByWeapon[h.Weapon] = list = new List<double>();
                            list.Add(h.Distance.Value);
                        }
                    }
                    else if (h.Source == "Splash")
                    {
                        splashCountByWeapon[h.Weapon] = splashCountByWeapon.GetValueOrDefault(h.Weapon) + 1;
                    }

                    if (CountsTowardDamageSums(h))
                    {
                        damageRawByWeapon[h.Weapon] = damageRawByWeapon.GetValueOrDefault(h.Weapon) + h.Raw;
                        armorByWeapon[h.Weapon] = armorByWeapon.GetValueOrDefault(h.Weapon) + h.Armor;
                        healthByWeapon[h.Weapon] = healthByWeapon.GetValueOrDefault(h.Weapon) + h.HealthLost;
                    }

                    // 1 == MarkOutcome.Applied, 2 == MarkOutcome.Cashed (HitRow.Mark); 0 (no key on the line) is neither and is
                    // never counted.
                    if (h.Mark == 1)
                        marksPlacedByWeapon[h.Weapon] = marksPlacedByWeapon.GetValueOrDefault(h.Weapon) + 1;
                    else if (h.Mark == 2)
                        marksCashedByWeapon[h.Weapon] = marksCashedByWeapon.GetValueOrDefault(h.Weapon) + 1;
                }
                if (h.Ability >= 0 && CountsTowardDamageSums(h))
                    damageRawByAbility[h.Ability] = damageRawByAbility.GetValueOrDefault(h.Ability) + h.Raw;
            }

            var castsByAbility = new Dictionary<int, int>();
            var statusCountByAbility = new Dictionary<int, int>();
            var statusSecondsByAbility = new Dictionary<int, double>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (!window.Contains(e.T)) continue;
                if (e.Name == TelemetryKeys.Cast)
                {
                    // A follow-up (the AoE Zone's throw) is not a fresh cast of the ability.
                    if (ReadInt(e.Data, TelemetryKeys.FollowUp, 0) == 1) continue;
                    int ab = ReadInt(e.Data, TelemetryKeys.AbilityId, -1);
                    if (ab >= 0) castsByAbility[ab] = castsByAbility.GetValueOrDefault(ab) + 1;
                }
                else if (e.Name == TelemetryKeys.Status)
                {
                    int ab = ReadInt(e.Data, TelemetryKeys.AbilityId, -1);
                    if (ab < 0) continue;
                    statusCountByAbility[ab] = statusCountByAbility.GetValueOrDefault(ab) + 1;
                    statusSecondsByAbility[ab] = statusSecondsByAbility.GetValueOrDefault(ab) + ReadFloat(e.Data, TelemetryKeys.Duration);
                }
                else if (e.Name == TelemetryKeys.Dot)
                {
                    int ab = ReadInt(e.Data, TelemetryKeys.AbilityId, -1);
                    if (ab >= 0)
                        damageRawByAbility[ab] = damageRawByAbility.GetValueOrDefault(ab) + ReadFloat(e.Data, TelemetryKeys.Raw);
                }
            }
            // dot lines also carry a weapon id (FireField-launched burns) - fold into the same weapon totals.
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Dot) continue;
                if (!window.Contains(e.T)) continue;
                int w = ReadInt(e.Data, TelemetryKeys.Weapon, -1);
                if (w < 0) continue;
                damageRawByWeapon[w] = damageRawByWeapon.GetValueOrDefault(w) + ReadFloat(e.Data, TelemetryKeys.Raw);
                armorByWeapon[w] = armorByWeapon.GetValueOrDefault(w) + ReadFloat(e.Data, TelemetryKeys.ArmorAbsorbed);
                healthByWeapon[w] = healthByWeapon.GetValueOrDefault(w) + ReadFloat(e.Data, TelemetryKeys.HealthLost, "hpLost");
            }

            foreach (int weaponId in weaponIds)
            {
                double equippedSeconds = equippedSecondsByWeapon.GetValueOrDefault(weaponId);
                int hitsCount = hitCountByWeapon.GetValueOrDefault(weaponId);
                int projectiles = projectilesByWeapon.GetValueOrDefault(weaponId);
                float damage = damageRawByWeapon.GetValueOrDefault(weaponId);
                List<double> distances = distancesByWeapon.GetValueOrDefault(weaponId);

                tables.Weapons.Add(new WeaponRow
                {
                    WeaponId = weaponId,
                    TimeEquippedSeconds = equippedSeconds,
                    Pulls = pullsByWeapon.GetValueOrDefault(weaponId),
                    Projectiles = projectiles,
                    Hits = hitsCount,
                    SplashHits = splashCountByWeapon.GetValueOrDefault(weaponId),
                    Accuracy = projectiles > 0 ? (double)hitsCount / projectiles : 0,
                    DamageRaw = damage,
                    ArmorDamage = armorByWeapon.GetValueOrDefault(weaponId),
                    HealthDamage = healthByWeapon.GetValueOrDefault(weaponId),
                    DamagePerEquippedMinute = equippedSeconds > 0 ? damage / (equippedSeconds / 60.0) : 0,
                    Kills = killsByWeapon.GetValueOrDefault(weaponId),
                    MeanDistance = (distances != null && distances.Count > 0) ? distances.Average() : (double?)null,
                    MedianDistance = Median(distances),
                    MarksPlaced = marksPlacedByWeapon.GetValueOrDefault(weaponId),
                    MarksCashed = marksCashedByWeapon.GetValueOrDefault(weaponId),
                });
            }

            var abilityIds = castsByAbility.Keys
                .Union(damageRawByAbility.Keys).Union(killsByAbility.Keys)
                .Union(statusCountByAbility.Keys).Distinct().OrderBy(a => a);
            foreach (int abilityId in abilityIds)
            {
                tables.Abilities.Add(new AbilityRow
                {
                    AbilityId = abilityId,
                    Casts = castsByAbility.GetValueOrDefault(abilityId),
                    DamageRaw = damageRawByAbility.GetValueOrDefault(abilityId),
                    Kills = killsByAbility.GetValueOrDefault(abilityId),
                    StatusCount = statusCountByAbility.GetValueOrDefault(abilityId),
                    StatusSeconds = statusSecondsByAbility.GetValueOrDefault(abilityId),
                });
            }
        }

        private static List<int> WeaponIdUniverse(TelemetryLog log)
        {
            foreach (TelemetrySession s in log.Sessions)
            {
                JArray weapons = s.Tuning?["weapons"] as JArray;
                if (weapons == null || weapons.Count == 0) continue;
                var ids = weapons.Select(w => w["id"]?.ToObject<int?>() ?? -1).Where(id => id >= 0).Distinct().OrderBy(id => id).ToList();
                if (ids.Count > 0) return ids;
            }

            // No tuning snapshot available (e.g. a hand-built log with no session) - fall back to
            // whatever weapon ids actually appear, so the table is never simply empty.
            var seen = new HashSet<int>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name == TelemetryKeys.Shots || e.Name == TelemetryKeys.Hit || e.Name == TelemetryKeys.Death)
                {
                    int w = ReadInt(e.Data, TelemetryKeys.Weapon, -1);
                    if (w >= 0) seen.Add(w);
                }
            }
            return seen.OrderBy(w => w).ToList();
        }

        private static double? Median(List<double> values)
        {
            if (values == null || values.Count == 0) return null;
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return (sorted.Count % 2 == 1) ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        // ==================================================================== players / deaths

        private static void BuildPlayersAndDeaths(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, TelemetrySession> sessionByActor, Dictionary<int, int> effectiveTeam,
            Dictionary<int, (double First, double Last)> coverageByActor, SampleDerivedStats sampleStats, ReportTables tables,
            TimeWindow window, double? tPhase2)
        {
            // Deaths first - "victim" has no field of its own on a `death` line; it is the file owner.
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Death) continue;
                if (!window.Contains(e.T)) continue; // this death didn't happen in this window.

                int victim = ActorOf(e, fileActor);
                TelemetrySession victimSession = sessionByActor.GetValueOrDefault(victim);

                // A null inside `assists` (a malformed line, or a future schema) would throw on ToObject<int>(): skipped
                // instead.
                var assists = (e.Data[TelemetryKeys.Assists] as JArray)
                    ?.Where(t => t.Type != JTokenType.Null)
                    .Select(t => t.ToObject<int>())
                    .ToArray() ?? Array.Empty<int>();

                tables.Deaths.Add(new DeathRow
                {
                    T = e.T,
                    Victim = victim,
                    VictimNick = victimSession?.Nick ?? "",
                    VictimTeam = effectiveTeam.GetValueOrDefault(victim, -1),
                    Killer = ReadInt(e.Data, TelemetryKeys.Killer, -1),
                    KillerTeam = ResolveTeam(ReadInt(e.Data, TelemetryKeys.KillerTeam, -1), ReadInt(e.Data, TelemetryKeys.Killer, -1), effectiveTeam),
                    Assists = assists,
                    Weapon = ReadInt(e.Data, TelemetryKeys.Weapon, -1),
                    Ability = ReadInt(e.Data, TelemetryKeys.AbilityId, -1),
                    X = ReadFloat(e.Data, TelemetryKeys.X),
                    Z = ReadFloat(e.Data, TelemetryKeys.Z),
                    UnspentGold = ReadInt(e.Data, TelemetryKeys.UnspentGold, 0),
                    LoadoutWeapon = ReadInt(e.Data, TelemetryKeys.LoadoutWeapon, -1),
                    LoadoutAttachment = ReadInt(e.Data, TelemetryKeys.LoadoutAttachment, -1),
                    LoadoutMobility = ReadInt(e.Data, TelemetryKeys.LoadoutMobility, -1),
                    LoadoutUltimate = ReadInt(e.Data, TelemetryKeys.LoadoutUltimate, -1),
                    AbsorbLevel = ReadInt(e.Data, TelemetryKeys.AbsorbLevel, 0),
                    RechargeLevel = ReadInt(e.Data, TelemetryKeys.RechargeLevel, 0),
                    Phase = PhaseOf(e.T, tPhase2),
                });
            }

            var killsByActor = new Dictionary<int, int>();
            var assistsByActor = new Dictionary<int, int>();
            var respawnsByActor = new Dictionary<int, List<double>>();
            foreach (DeathRow d in tables.Deaths) // already window-filtered
            {
                if (d.Killer >= 0) killsByActor[d.Killer] = killsByActor.GetValueOrDefault(d.Killer) + 1;
                foreach (int assister in d.Assists)
                    assistsByActor[assister] = assistsByActor.GetValueOrDefault(assister) + 1;
            }
            // Respawns stay UNWINDOWED: the time-alive tail below needs the TRUE respawn history to find the first respawn
            // after the true last death, even when that respawn (or the death before it) falls outside this window.
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Respawn) continue;
                int actor = ActorOf(e, fileActor);
                if (!respawnsByActor.TryGetValue(actor, out var list))
                    respawnsByActor[actor] = list = new List<double>();
                list.Add(e.T);
            }

            // Burn-source hit rows are excluded (already counted via their `dot`).
            var damageDealtByActor = new Dictionary<int, float>();
            var damageTakenByActor = new Dictionary<int, float>();
            foreach (HitRow h in tables.Hits) // already window-filtered
            {
                if (!CountsTowardDamageSums(h)) continue;
                if (h.Attacker >= 0) damageDealtByActor[h.Attacker] = damageDealtByActor.GetValueOrDefault(h.Attacker) + h.Raw;
                if (h.Victim >= 0) damageTakenByActor[h.Victim] = damageTakenByActor.GetValueOrDefault(h.Victim) + h.Raw;
            }
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Dot) continue;
                if (!window.Contains(e.T)) continue;
                int a = ReadInt(e.Data, TelemetryKeys.Attacker, -1);
                int v = ReadInt(e.Data, TelemetryKeys.Victim, -1);
                float raw = ReadFloat(e.Data, TelemetryKeys.Raw);
                if (a >= 0) damageDealtByActor[a] = damageDealtByActor.GetValueOrDefault(a) + raw;
                if (v >= 0) damageTakenByActor[v] = damageTakenByActor.GetValueOrDefault(v) + raw;
            }

            var goldByActor = new Dictionary<int, (int Terr, int Bounty, int Refund, int Debug, int Other)>();
            var spentByActor = new Dictionary<int, int>();
            var healByActor = new Dictionary<int, float>();
            foreach (TelemetryEvent e in log.Events)
            {
                int actor = ActorOf(e, fileActor);
                if (e.Name == TelemetryKeys.GoldEarned && !IsJunkGoldEarned(e.Data))
                {
                    if (!window.Contains(e.T)) continue;
                    var prev = goldByActor.GetValueOrDefault(actor);
                    goldByActor[actor] = (
                        prev.Terr + ReadInt(e.Data, TelemetryKeys.Territory, 0),
                        prev.Bounty + ReadInt(e.Data, TelemetryKeys.Bounty, 0),
                        prev.Refund + ReadInt(e.Data, TelemetryKeys.Refund, 0),
                        prev.Debug + ReadInt(e.Data, TelemetryKeys.Debug, 0),
                        prev.Other + ReadInt(e.Data, TelemetryKeys.Other, 0));
                }
                else if (e.Name == TelemetryKeys.Purchase)
                {
                    if (!window.Contains(e.T)) continue;
                    spentByActor[actor] = spentByActor.GetValueOrDefault(actor) + ReadInt(e.Data, TelemetryKeys.Price, 0);
                }
                else if (e.Name == TelemetryKeys.Heal)
                {
                    if (!window.Contains(e.T)) continue;
                    var tiers = e.Data[TelemetryKeys.HealTiers] as JArray;
                    float sum = 0;
                    if (tiers != null) foreach (JToken v in tiers) sum += v.Type == JTokenType.Null ? 0f : v.ToObject<float>();
                    healByActor[actor] = healByActor.GetValueOrDefault(actor) + sum;
                }
            }

            foreach (var kv in sessionByActor)
            {
                int actor = kv.Key;
                TelemetrySession session = kv.Value;
                if (session.Spectator) continue; // a spectator host's file feeds the timeline, not a player row
                var gold = goldByActor.GetValueOrDefault(actor);
                (double First, double Last) coverage = coverageByActor.TryGetValue(actor, out var c) ? c : (0, 0);

                // A completed life is a continuous span [deathT - timeAlive, deathT), not an instant: CLIPPED to the window
                // like every other integral, rather than crediting the WHOLE life to the phase the death instant falls in
                // (which could report more alive time in a phase than the phase lasted - a 125s life dying at t=125 credited to
                // a 90s Phase 2). Clipping keeps Phase 1 + Phase 2 summing to exactly the whole match.
                double timeAlive = 0;
                double? lastDeathT = null;
                foreach (TelemetryEvent e in log.Events)
                {
                    if (e.Name != TelemetryKeys.Death) continue;
                    if (ActorOf(e, fileActor) != actor) continue;
                    if (!lastDeathT.HasValue || e.T > lastDeathT.Value) lastDeathT = e.T; // unwindowed - the TRUE last death

                    // Plain window.Clip treats 0/matchLength as hard walls, which truncated a life that started before the
                    // match clock (timeAlive > deathT) at 0 and dropped a death logged at the t == -1 sentinel
                    // (window.Clip(-1-timeAlive, -1, ...) never overlaps a window starting at 0). OverlapWithUnboundedEdges
                    // treats the first/last window's edge as unbounded, matching how Contains(-1) counts a t == -1 death. The
                    // death's own t maps negative-to-0 first before being the span's upper end.
                    float lifeTimeAlive = ReadFloat(e.Data, TelemetryKeys.TimeAlive);
                    double deathTForSpan = e.T < 0 ? 0 : e.T;
                    timeAlive += window.OverlapWithUnboundedEdges(deathTForSpan - lifeTimeAlive, deathTForSpan);
                }

                double tailStart;
                if (lastDeathT.HasValue)
                {
                    double? firstAfter = respawnsByActor.TryGetValue(actor, out var respawns)
                        ? respawns.Where(t => t > lastDeathT.Value).OrderBy(t => t).Cast<double?>().FirstOrDefault()
                        : (double?)null;
                    tailStart = firstAfter ?? double.PositiveInfinity; // never respawned again - no tail at all
                }
                else
                {
                    tailStart = coverage.First; // never died - "alive" for this player's own covered span
                }

                if (window.Clip(tailStart, coverage.Last, out double clipFrom, out double clipTo))
                    timeAlive += clipTo - clipFrom;

                tables.Players.Add(new PlayerRow
                {
                    Actor = actor,
                    Nick = session.Nick,
                    Team = effectiveTeam.GetValueOrDefault(actor, session.Team),
                    Kills = killsByActor.GetValueOrDefault(actor),
                    Deaths = tables.Deaths.Count(d => d.Victim == actor),
                    Assists = assistsByActor.GetValueOrDefault(actor),
                    DamageDealt = damageDealtByActor.GetValueOrDefault(actor),
                    DamageTaken = damageTakenByActor.GetValueOrDefault(actor),
                    GoldTerritory = gold.Terr,
                    GoldBounty = gold.Bounty,
                    GoldRefund = gold.Refund,
                    GoldDebug = gold.Debug,
                    GoldOther = gold.Other,
                    GoldSpent = spentByActor.GetValueOrDefault(actor),
                    TimeAlive = timeAlive,
                    TimeOwnZone = sampleStats.OwnZoneSecondsByActor.GetValueOrDefault(actor),
                    TimeEnemyZone = sampleStats.EnemyZoneSecondsByActor.GetValueOrDefault(actor),
                    TimeNeutralZone = sampleStats.NeutralZoneSecondsByActor.GetValueOrDefault(actor),
                    Healing = healByActor.GetValueOrDefault(actor),
                });
            }
        }

        // ==================================================================== shared helpers

        private static int ActorOf(TelemetryEvent e, Dictionary<string, int> fileActor) =>
            fileActor.TryGetValue(e.File, out int a) ? a : -1;

        /// <summary>The repeated `data[key]?.ToObject&lt;int?&gt;() ?? fallback` read, in one place.</summary>
        /// <summary>Purchase/refund category as the report shows it. Logs written before the right-click
        /// slot was renamed say "equipment"; they read as "attachment" so old and new matches line up.</summary>
        public static string CategoryName(string logged)
        {
            if (string.IsNullOrEmpty(logged)) return "";
            return logged == "equipment" ? "attachment" : logged;
        }

        private static int ReadInt(JObject data, string key, int fallback)
        {
            JToken token = data[key];
            return (token != null && token.Type != JTokenType.Null) ? token.ToObject<int>() : fallback;
        }

        private static int SumGoldEarnedTotal(JObject data) =>
            ReadInt(data, TelemetryKeys.Territory, 0)
            + ReadInt(data, TelemetryKeys.Bounty, 0)
            + ReadInt(data, TelemetryKeys.Refund, 0)
            + ReadInt(data, TelemetryKeys.Debug, 0)
            + ReadInt(data, TelemetryKeys.Other, 0);

        /// <summary>A `goldEarned` line where every source is 0 and `zones` is empty or all-zero is junk from a remote copy's
        /// teardown (or the owner's closing flush): skipped everywhere this aggregator reads goldEarned. Kept for old logs; new
        /// clients no longer write these.</summary>
        private static bool IsJunkGoldEarned(JObject data)
        {
            if (SumGoldEarnedTotal(data) != 0) return false;
            var zones = data[TelemetryKeys.Zones] as JArray;
            if (zones == null || zones.Count == 0) return true;
            foreach (JToken z in zones)
                if ((z.Type == JTokenType.Null ? 0 : z.ToObject<int>()) != 0) return false;
            return true;
        }

        /// <summary>A `console` line's FirstT/LastT are only written when its fold count is > 1 (TelemetryKeys.RepeatCount);
        /// otherwise the line's own `t` IS both, hence the fallback being that line's T rather than a constant.</summary>
        private static double ReadDoubleOrDefault(JObject data, string key, double fallback)
        {
            JToken token = data[key];
            return (token != null && token.Type != JTokenType.Null) ? token.ToObject<double>() : fallback;
        }

        private static float ReadFloat(JObject data, string key, string fallbackKey = null)
        {
            JToken token = data[key];
            if (token != null && token.Type != JTokenType.Null) return token.ToObject<float>();
            if (fallbackKey != null)
            {
                JToken fallback = data[fallbackKey];
                if (fallback != null && fallback.Type != JTokenType.Null) return fallback.ToObject<float>();
            }
            return 0f;
        }
    }
}
