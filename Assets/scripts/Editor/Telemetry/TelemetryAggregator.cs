using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T5 step 3: pure log -> tables. No IO of its own - TelemetryLog already did the
    /// reading, CsvReportWriter (and later HtmlReportWriter) only format what this produces. Edit-mode
    /// tested against a hand-written fixture with hand-computed totals (TelemetryAggregatorTests) plus
    /// a set of focused fixtures for the opus review fixes below (TelemetryAggregatorReviewFixesTests).
    ///
    /// Task T7 (3-team / 2-team phase split): <see cref="Build(TelemetryLog, TimeWindow)"/> is the same
    /// single pipeline as always - it is called up to three times (see <see cref="BuildSet"/>), once
    /// per scope (whole match, Phase 1, Phase 2), each time with a different <see cref="TimeWindow"/>.
    /// No table builder below is duplicated for phases: a DISCRETE event (a hit, a purchase, a death...)
    /// is simply skipped when its own t falls outside the requested window; a CONTINUOUS integral (a
    /// sample interval, an ownership stint, a capture attempt, the alive-time tail) is CLIPPED to the
    /// window's own bounds instead, via TimeWindow.Clip. Several row types (ownership, captures, gold
    /// timeline, economy-by-minute, hits, deaths, purchases, shop-blocked) also carry a `Phase` field,
    /// tagged against the match's own phase-2 transition instant (from PhaseTimeline) REGARDLESS of
    /// which window this particular build is scoped to - so even the whole-match tables show which
    /// phase each row happened in, and a stint/attempt that straddles the transition is split into two
    /// rows in every scope that can see both halves (see ClipAndTagPhase).
    ///
    /// T4-review corrections (first pass, kept):
    /// 2. `bounty` (master, per-player-per-payout) is NOT added into any team/player income total -
    ///    the owner's own `goldEarned.bounty` field already reflects the credit that event describes.
    /// 3. A `goldEarned` line that is entirely zero (every source 0 and `zones` empty or all-zero) is
    ///    junk from a remote copy's teardown or the owner's own closing flush - skipped everywhere
    ///    (kept for old logs; new clients no longer write these at all per the T4 fix).
    /// 4. `underAttack` isn't consumed by any T5 table (none of the 12 CSVs need it) - left for a
    ///    later task.
    /// 5. An armor `purchase`'s `item` id (100+level absorb, 200+level recharge as of the T4 fix) is
    ///    treated as a plain opaque int, never decoded.
    ///
    /// Opus T5 review (second pass), by item number:
    /// 1. Captures are rebuilt as ONE time-ordered pass over `capture` lines and deduped `ownership`
    ///    changes together - see BuildCaptures. An `ownership` change is what closes an attempt
    ///    (completed/neutralised); a fresh `started`/`drainStarted` while one is still open abandons
    ///    it; anything still open at match end is abandoned. A `capture` line's own state is read
    ///    defensively (unrecognised states counted, never fatal) but never trusted for the outcome -
    ///    the T4 fix made `capture` stateless (a "paused" at progress 1 looks identical whether it
    ///    completed or was merely interrupted; only `ownership` tells them apart).
    /// 2. A player's team comes from their own first `sample` with tm >= 0, falling back to the
    ///    session line - see EffectiveTeamByActor. A late joiner's session can carry tm:-1 before the
    ///    room's player-properties echo arrives, which used to drop their whole income/spend/zone
    ///    slice from every team-keyed table.
    /// 3. Time alive and every sample-derived integral (equipped time, zone time) are bounded by the
    ///    PLAYER'S OWN covered range (their file's first/last real event), not the whole match length -
    ///    a joiner at t=600 no longer shows 1200s alive in a 1200s match. Dead samples (alive:false)
    ///    no longer accrue equipped/zone time for the interval they start.
    /// 4. A `goldEarned` line's `zones` array is rounded to whole gold per zone at the source, so
    ///    summing many already-rounded lines drifts from the authoritative `terr` total. Each line is
    ///    rescaled to sum to its own `terr` before accumulating (ScaledZones), keeping the fraction.
    /// 5. An out-of-range tier (an `ownership` line can carry tier 0 for an unregistered zone) no
    ///    longer indexes IncomeByTier/ZonesHeldByTier out of range - skipped and counted
    ///    (Header.InvalidTierCount).
    /// 6. Accuracy is Projectile-hits / projectiles only; Splash hits (a rocket's own splash falloff
    ///    landing on several targets from one projectile) are counted separately (WeaponRow.SplashHits)
    ///    instead of inflating both the numerator and looking like more than 100% accuracy.
    /// 7. The gold gap ignores a balance sample older than (bucket end - 60s) - a leaver's stale last
    ///    balance no longer keeps counting for their team forever.
    /// 8. A lethal Burn tick is logged as BOTH a `dot` (flushed just before) and a `hit` (so every kill
    ///    keeps a row) - `hit` rows with src:"Burn" are excluded from every damage sum (already counted
    ///    via the `dot`), but kept in hits.csv and for kill attribution (kills come from `death`, never
    ///    from a hit row, so this was never at risk).
    /// 9. Header.FreeLoadoutUsed is the tuning snapshot's flag OR'd with any `purchase.free:true` -
    ///    a mid-match toggle (or a per-purchase free flag with no matching tuning flag) is no longer missed.
    /// 10. Robustness: a non-numeric `t`, a null inside `assists`, and an unreadable file no longer
    ///     throw or vanish silently (see TelemetryLog); a newer `schema` is counted, not ignored.
    /// 11. Integral edges: the last sample's own trailing interval (capped at the sample interval) is
    ///     now included; t == -1 samples are ignored; any gap between two samples is capped at 2x the
    ///     sample interval (a disconnected client doesn't keep accruing).
    /// 13. CsvReportWriter now writes UTF-8 WITH a BOM (see its own comment).</summary>
    public static class TelemetryAggregator
    {
        private const int Neutral = -1; // TerritoryMap.Neutral's own value - a zone with no owner.
        // T5 re-review item 9: same sentinel value as Neutral (both mean "no real zone id"), under
        // its own name so a reader of zone_income.csv/economy_by_minute.csv isn't confused about
        // which concept a -1 zone/tier means in that context.
        private const int UnattributedZone = -1;
        private const double DefaultSampleIntervalSeconds = 5.0;

        private static readonly HashSet<string> CaptureOpenStates =
            new HashSet<string> { "started", "resumed", "drainStarted", "drainResumed" };
        private static readonly HashSet<string> CaptureFreshStartStates = new HashSet<string> { "started", "drainStarted" };
        private static readonly HashSet<string> CapturePauseStates = new HashSet<string> { "paused", "drainPaused" };
        // "completed"/"neutralised" are known (older, pre-T4-fix logs may still carry them) but never
        // drive the state machine - see the class comment's item 1. Anything outside this whole set
        // is unknown (counted, not fatal).
        private static readonly HashSet<string> CaptureKnownStates = new HashSet<string>
            { "started", "resumed", "drainStarted", "drainResumed", "paused", "drainPaused", "completed", "neutralised" };

        private sealed class CaptureAttempt
        {
            public int Team;
            public double Start;
            public bool IsDrain;
            public int LastPlayers;
        }

        /// <summary>The original, pre-T7 entry point - the whole match, exactly as before. Equivalent
        /// to <c>Build(log, PhaseTimeline.From(log).WholeMatch)</c>.</summary>
        public static ReportTables Build(TelemetryLog log) => Build(log, null);

        /// <summary>Task T7: builds one scope. <paramref name="window"/> null means the whole match
        /// (every event, every integral in full) - the same result <c>Build(log)</c> always produced.
        /// A real window (Phase 1 or Phase 2, from PhaseTimeline) filters every discrete event to it
        /// and clips every continuous integral to it - see the class comment.</summary>
        public static ReportTables Build(TelemetryLog log, TimeWindow window)
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

            // Task T7: the phase timeline gives both this build's own default (whole-match) window
            // AND the phase-2 transition instant - every phase-taggable row below is split/tagged
            // against the transition regardless of which window THIS call is scoped to (see
            // ClipAndTagPhase's own comment on why the whole-match build also splits at it).
            PhaseTimeline timeline = PhaseTimeline.From(log);
            double matchLength = timeline.MatchLength;
            double? tPhase2 = timeline.TransitionSeconds;
            TimeWindow effectiveWindow = window ?? timeline.WholeMatch;

            // Review fix: when this build's own window lies entirely within ONE phase (a Phase
            // 1- or Phase 2-scoped build), every surviving row belongs to that phase - null when
            // the window spans both (the whole-match build), which is the only case where a
            // straddling minute bucket's own absolute start time is still the right way to tag it
            // (see BuildGoldTimelineAndEconomy's own comment).
            int? forcedPhase = null;
            if (tPhase2.HasValue)
            {
                if (effectiveWindow.End <= tPhase2.Value) forcedPhase = 1;
                else if (effectiveWindow.Start >= tPhase2.Value) forcedPhase = 2;
            }

            // Opus review item 2: team from the actor's own first sample with tm >= 0, falling back
            // to the session - see the method's own comment.
            Dictionary<int, int> effectiveTeam = BuildEffectiveTeam(log, fileActor, sessionByActor);

            // Opus review item 3: every player's own covered range (their file's first/last real
            // event) - time-alive and every sample integral are bounded by THIS, not matchLength.
            Dictionary<int, (double First, double Last)> coverageByActor = BuildCoverageByActor(log, fileActor);

            double sampleInterval = ResolveSampleIntervalSeconds(log);

            var zoneTier = new Dictionary<int, int>();
            // Every raw ownership change per zone, IN TIME ORDER, including transitions to neutral -
            // built from the WHOLE log regardless of window (Task T7): a sample inside a phase-scoped
            // window still needs the true owner-at-time, even when the change that established it
            // happened before this window started. The ownership.csv STINT ROWS (below) are what
            // actually gets clipped/filtered to the window - this raw history never is.
            var rawChangesByZone = new Dictionary<int, List<(double T, int New)>>();
            BuildOwnership(log, tables.Ownership, zoneTier, rawChangesByZone, matchLength, effectiveWindow, tPhase2);

            BuildHeader(tables.Header, log, sessionByActor, coverageByActor, effectiveWindow);
            tables.Header.EliminationFallbackUsed = timeline.UsedEliminationFallback; // review fix item 9
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

        /// <summary>Task T7: builds all three scopes from one log - see ReportSet's own comment. This
        /// IS the "no second copy of the table logic" design in one call: the same Build(log, window)
        /// runs up to three times, parameterized only by which window to clip/filter against.</summary>
        public static ReportSet BuildSet(TelemetryLog log)
        {
            PhaseTimeline timeline = PhaseTimeline.From(log);
            return new ReportSet
            {
                WholeMatch = Build(log, timeline.WholeMatch),
                Phase1 = Build(log, timeline.Phase1),
                Phase2 = timeline.HasPhase2 ? Build(log, timeline.Phase2) : null,
            };
        }

        // ==================================================================== phase helpers (Task T7)

        /// <summary>1 or 2, from a discrete event's own t vs the match's phase-2 transition (null when
        /// the match never had one - everything is Phase 1).</summary>
        private static int PhaseOf(double t, double? tPhase2) => (tPhase2.HasValue && t >= tPhase2.Value) ? 2 : 1;

        /// <summary>Clips a real [realFrom, realTo) span to the requested window, AND splits it at the
        /// phase-2 transition when the (already window-clipped) span straddles it. Used by both
        /// ownership stints and capture attempts - the two tables whose rows can genuinely span more
        /// than one phase.
        ///
        /// Yields nothing when the span has no overlap with the window at all; yields ONE piece when
        /// it doesn't straddle tPhase2; yields TWO when it does (Phase 1's [from, tPhase2) and Phase
        /// 2's [tPhase2, to)). This single mechanism is what makes "the whole-match build's rows are
        /// ALSO phase-split" and "a Phase-1/Phase-2-scoped build only ever sees its own half" the same
        /// code path: a phase-scoped window's own End IS tPhase2 (Phase 1) or Start IS tPhase2 (Phase
        /// 2), so the window-clip alone already produces exactly one correctly-bounded piece for
        /// those; only the whole-match window (whose End is well past tPhase2) ever needs the second
        /// yield.</summary>
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
            Dictionary<int, (double First, double Last)> coverageByActor, TimeWindow window)
        {
            header.MatchId = log.MatchId;
            // Task T7: THIS window's own duration - the whole match's length when window is the
            // whole-match window (unchanged from before T7), or a phase's own duration on a
            // phase-scoped build, which is what lets the HTML compare it against BalanceTargets'
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
                // Task T7: markers and the free-loadout/debug-gold warnings are scoped to THIS
                // window - a marker dropped in Phase 2 has no business on the Phase 1 tab.
                if (!window.Contains(e.T)) continue;

                if (e.Name == TelemetryKeys.Marker)
                {
                    header.Markers.Add(new MarkerRow
                    {
                        T = e.T,
                        Actor = ReadInt(e.Data, TelemetryKeys.Actor, -1),
                        Note = e.Data[TelemetryKeys.Note]?.ToString() ?? "",
                    });
                }
                else if (e.Name == TelemetryKeys.GoldEarned && !IsJunkGoldEarned(e.Data))
                {
                    if (ReadInt(e.Data, TelemetryKeys.Debug, 0) > 0)
                        header.DebugGoldUsed = true;
                }
                // Opus review item 9: OR the tuning flag with any purchase actually marked free -
                // a mid-match Free Loadout toggle (or a free purchase with no matching tuning read)
                // must still surface the warning.
                else if (e.Name == TelemetryKeys.Purchase && (e.Data[TelemetryKeys.Free]?.ToObject<bool?>() ?? false))
                {
                    header.FreeLoadoutUsed = true;
                }
            }

            // Player coverage: each FILE's own first/last real event - a fact about the file, not
            // about any one phase window, so this stays unwindowed even on a Phase 1/Phase
            // 2-scoped build (Task T7).
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
                header.Coverage.Add(new PlayerCoverageRow
                {
                    Actor = s.Actor,
                    Nick = s.Nick,
                    FirstT = firstT.TryGetValue(s.File, out double f) ? f : 0,
                    LastT = lastT.TryGetValue(s.File, out double l) ? l : 0,
                });
            }

            // Task T7: log coverage - every actor seen ANYWHERE (not just those with their own
            // file), always whole-match regardless of this build's own window - see
            // LogCoverageRow's own comment.
            BuildLogCoverage(log, sessionByActor, coverageByActor, header.LogCoverage);
        }

        /// <summary>Task T7: every actor seen anywhere in the match - joins, sessions, `hit`
        /// attackers/victims, `death` killers/assists - against which files are actually present, so
        /// the report can say "no log from actor N" instead of silently under-counting their damage,
        /// gold and purchases.</summary>
        private static void BuildLogCoverage(TelemetryLog log, Dictionary<int, TelemetrySession> sessionByActor,
            Dictionary<int, (double First, double Last)> coverageByActor, List<LogCoverageRow> outRows)
        {
            var seen = new HashSet<int>(sessionByActor.Keys);
            // Review fix (item 10): a MISSING actor's own nick/first-seen/last-seen now come from
            // whoever else logged their `join`/`leave` (every client logs every OTHER player's join
            // and leave, even one whose own file never opened).
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
                    joinedAndLeft = hasJoin && hasLeave && leaveT >= joinT;
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

        /// <summary>Opus review item 2: a late joiner's `session` line can be written before the room's
        /// player-properties echo carries their team (tm:-1), which used to drop their whole slice from
        /// every team-keyed table (economy, zone income, gold gap, players.csv). Each actor's team is
        /// instead read from their own first `sample` that reports tm >= 0 - `sample` is written by the
        /// owner every interval for the rest of the match, so it catches up moments later - falling back
        /// to the session's own team only if no sample ever does.</summary>
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
                if (e.T < 0) continue; // Opus review item 11: t == -1 never counts as covered time.
                int actor = ActorOf(e, fileActor);
                if (!firstT.TryGetValue(actor, out double f) || e.T < f) firstT[actor] = e.T;
                if (!lastT.TryGetValue(actor, out double l) || e.T > l) lastT[actor] = e.T;
            }
            var result = new Dictionary<int, (double, double)>();
            foreach (int actor in firstT.Keys)
                result[actor] = (firstT[actor], lastT[actor]);
            return result;
        }

        /// <summary>The sample interval used to bound the trailing interval and cap gaps (opus review
        /// item 11). Step 0b added TelemetryConfig to the tuning snapshot (TuningSnapshot.Json), so a
        /// session line from a real match now carries `telemetry.sampleIntervalSeconds`; older logs
        /// captured before that change don't, so this still falls back to the shipped default (5s)
        /// when the field is absent.</summary>
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

                    // Task T7: clip this stint to the requested window, splitting it at the phase
                    // boundary too when it straddles one (even on a whole-match build - see
                    // ClipAndTagPhase's own comment).
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

        /// <summary>Opus review item 1 (HIGH): rebuilt as ONE time-ordered pass over `capture` lines and
        /// deduped `ownership` changes together - the old two-pass version consumed every capture line
        /// first (collapsing every start/pause/resume cycle for a zone into a single mutable attempt
        /// object) and only then walked ownership changes to close whatever was still open, so a zone
        /// re-captured, drained, and captured again within one match produced at most one row instead
        /// of several. Ownership items sort BEFORE a same-instant capture item (see the merge below),
        /// so a completion recorded in the same instant as a stray same-tick capture line always closes
        /// the right attempt first.
        ///
        /// Task T7: the state machine below builds the RAW (unwindowed) attempts exactly as before,
        /// into a local list; only the final clip-and-tag pass (mirroring BuildOwnership's own) scopes
        /// them to the requested window and phase-splits a straddling attempt.</summary>
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
                // "completed"/"neutralised" (pre-T4-fix logs only) - known, but never drives the state
                // machine; the matching ownership item (in this same merged pass) is what actually closes it.
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

            // Task T7: clip + phase-tag every raw attempt against the requested window - same
            // mechanism as BuildOwnership's own stints (see ClipAndTagPhase).
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
                        Category = e.Data[TelemetryKeys.Category]?.ToString() ?? "",
                        // Opus review item 5 (armor encoding): item stays a plain opaque int here -
                        // 100+level (absorb) / 200+level (recharge) as of the T4 fix, a real weapon/
                        // ability id otherwise. Never decoded - see TelemetryKeys.ItemId's own comment.
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
                        Category = e.Data[TelemetryKeys.Category]?.ToString() ?? "",
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

        /// <summary>Opus review item 7 (T5 re-review): hits.csv/deaths.csv used to read the raw
        /// `at`/`vt`/`at` (killer) team field straight off the event line, bypassing the same
        /// late-joiner fallback every other team-keyed table already gets via effectiveTeam (a late
        /// joiner's own early lines can carry tm:-1 before the room's player-properties echo
        /// arrives). A raw value of -1 now falls back to the actor's resolved effective team; a
        /// genuinely unresolvable actor (id -1, e.g. a dummy) still reads -1.</summary>
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
                    // T3 review: new logs use "hpLost"; older ones (pre-review) wrote the same value as "hp".
                    HealthLost = ReadFloat(e.Data, TelemetryKeys.HealthLost, "hp"),
                    Lethal = e.Data[TelemetryKeys.Lethal]?.ToObject<bool?>() ?? false,
                    Distance = distance,
                    Vulnerable = ReadFloat(e.Data, TelemetryKeys.Vulnerable),
                    Overpower = e.Data[TelemetryKeys.OverpowerActive]?.ToObject<bool?>() ?? false,
                    Phase = PhaseOf(e.T, tPhase2),
                });
            }
        }

        /// <summary>Opus review item 8: a lethal Burn tick is logged as both a flushed `dot` (its
        /// accumulated bucket) AND a `hit` (so every kill keeps a row - see PlayerTelemetry.RouteHit).
        /// That `hit` row's own damage is already counted via the `dot`; summing it again here would
        /// double it. Kept in hits.csv itself, and kills never read a hit row at all (they come from
        /// `death`), so only damage sums need this guard.</summary>
        private static bool CountsTowardDamageSums(HitRow h) => h.Source != "Burn";

        // ==================================================================== gold timeline / economy by minute

        /// <summary>Task T7: the one table this feature does NOT window as cleanly as the other
        /// eleven. `gold_timeline.csv`'s own EarnedSoFar/SpentSoFar stay whole-match running totals on
        /// PURPOSE even on a phase-scoped build (they keep their literal meaning, "earned/spent so far
        /// in the match" - a Phase 2 row showing a Phase-1-inclusive running total is more useful than
        /// a confusing reset-to-zero); only which SAMPLE ROWS are emitted is scoped to the window.
        /// `economy_by_minute.csv` goes further: each event's own contribution to a minute bucket IS
        /// scoped to the window (so a Phase 1/Phase 2 build's numbers are correct), and only buckets
        /// that overlap the window survive into the output - but the minute INDEX itself stays the
        /// absolute match minute rather than being renumbered relative to the phase's own start,
        /// because the "zones held per tier" and "gold gap to richest" passes below both already
        /// depend on absolute minute math (matchLength, OwnerAtTime at an absolute midpoint, a stale-
        /// sample cutoff measured from an absolute bucket end) that would need its own separate
        /// re-derivation to renumber cleanly. Reported to Tudor as a known simplification rather than
        /// silently pretending it's exactly as clean as the rest.</summary>
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
                    // Task T7: unwindowed on purpose - see this method's own class comment.
                    earnedRunning[actor] = earnedRunning.GetValueOrDefault(actor) + total;

                    if (window.Contains(e.T))
                    {
                        int minute = Math.Min((int)Math.Floor(e.T / 60.0), numMinutes - 1);
                        if (minute < 0) minute = 0;
                        if (team >= 0 && economyRows.TryGetValue((minute, team), out EconomyByMinuteRow row))
                        {
                            // Opus review item 4: rescale this line's own zones[] to sum to its own terr
                            // (authoritative) before accumulating - see ScaledZones.
                            double[] scaledZones = ScaledZones(e.Data);
                            for (int z = 0; z < scaledZones.Length; z++)
                            {
                                if (!zoneTier.TryGetValue(z, out int tier)) continue;
                                if (tier < 1 || tier > row.IncomeByTier.Length) { tables.Header.InvalidTierCount++; continue; } // item 5
                                row.IncomeByTier[tier - 1] += scaledZones[z];
                            }
                            // T5 re-review item 9: terr > 0 with no zones[] to rescale by (empty or all
                            // zero) - kept here rather than silently dropped, see UnattributedIncome.
                            row.UnattributedIncome += UnattributedTerritoryGold(e.Data, scaledZones);
                            // Point 2 (first-pass review): goldEarned.bounty is the OWNER's own credited
                            // total, not the master's per-payout `bounty` line - safe to sum without double counting.
                            row.Bounty += ReadInt(e.Data, TelemetryKeys.Bounty, 0);
                            row.Refund += ReadInt(e.Data, TelemetryKeys.Refund, 0);
                        }
                    }
                }
                else if (e.Name == TelemetryKeys.Purchase)
                {
                    int price = ReadInt(e.Data, TelemetryKeys.Price, 0);
                    // Task T7: unwindowed on purpose - see this method's own class comment.
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
                    if (!window.Contains(e.T)) continue; // Task T7: only this window's own samples.
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

            // T5 re-review item 10: a zone's own tier (from zoneTier, set once per zone by
            // BuildOwnership) never changes across this sweep - only its OWNER does per minute - so
            // checking validity inside the "for each minute" loop counted the SAME bad zone once per
            // minute (a 20-minute match inflated one bad zone to 20). Computed once, outside the
            // minute loop, so Header.InvalidTierCount counts distinct bad zones, not zone-minutes.
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

            // Gold gap to the richest team, from each actor's last sample at or before the minute's
            // end - opus review item 7: a sample older than (bucket end - 60s) is stale (the player
            // likely left) and is ignored rather than keeping their last known balance forever.
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

            // Task T7: keep only the minute buckets that actually overlap this window (see this
            // method's own class comment on why the minute INDEX itself stays absolute). Review
            // fix: a Phase 1- or Phase 2-SCOPED build (forcedPhase set) tags every surviving row
            // with that one phase - a bucket straddling the transition can otherwise overlap a
            // single-phase window while its own START time still reads as the OTHER phase (minute
            // 1, 60->120, overlaps the Phase 2 window [90,180] but starts at 60). Only the
            // whole-match build (forcedPhase null) still tags a straddling bucket by its own
            // absolute start - documented above as this table's one simplification.
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
            // Task T7: `stints` is ALREADY window-clipped (BuildOwnership ran first) - summing its
            // Duration here needs no extra work at all to be correctly scoped.
            var secondsHeld = new Dictionary<(int Zone, int Team), double>();
            foreach (OwnershipRow stint in stints)
                secondsHeld[(stint.Zone, stint.Team)] = secondsHeld.GetValueOrDefault((stint.Zone, stint.Team)) + stint.Duration;

            var goldGenerated = new Dictionary<(int Zone, int Team), double>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.GoldEarned || IsJunkGoldEarned(e.Data)) continue;
                if (!window.Contains(e.T)) continue; // Task T7

                int actor = ActorOf(e, fileActor);
                int team = effectiveTeam.GetValueOrDefault(actor, -1);
                if (team < 0) continue;

                double[] scaledZones = ScaledZones(e.Data); // opus review item 4
                for (int z = 0; z < scaledZones.Length; z++)
                {
                    if (scaledZones[z] == 0) continue;
                    goldGenerated[(z, team)] = goldGenerated.GetValueOrDefault((z, team)) + scaledZones[z];
                }

                // T5 re-review item 9: terr > 0 with no zones[] to rescale by - a synthetic
                // "Unattributed" zone (id UnattributedZone) keeps this gold visible per team instead
                // of vanishing from zone_income.csv while players.csv's own running total still
                // includes it.
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

        /// <summary>Opus review item 4: `goldEarned.zones[]` is rounded to whole gold PER ZONE at the
        /// source (PlayerTelemetry.RoundedZoneArray), so summing many already-rounded lines drifts
        /// noticeably from the line's own authoritative `terr` total (a real log measured Sigma-terr 254
        /// vs Sigma-zones 244, a 4% loss). Rescaling each line's own zones to sum to its own terr before
        /// accumulating removes that drift; the fraction is kept (callers accumulate into a double),
        /// only rounded for display far downstream if at all. A line with terr == 0 or zones summing to
        /// 0 is left as-is (no ratio to scale by).</summary>
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

        /// <summary>T5 re-review item 9: ScaledZones has no ratio to rescale a line's `zones[]` by
        /// when they sum to ~0 (empty array, or every entry 0) - if that same line's own `terr` is
        /// still > 0, that gold has nowhere to go in a zone-keyed table and used to just vanish from
        /// zone_income.csv/economy_by_minute.csv while players.csv's running total (summed straight
        /// from `terr`, never from `zones`) stayed correct. Returns the leftover amount to bucket
        /// into an "Unattributed" row instead, or 0 when the zones already accounted for it (or
        /// there was no territory income on this line at all).</summary>
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

        /// <summary>One sweep over every `sample` event (already t-sorted) computing two things at
        /// once from the same consecutive-sample intervals: how long each weapon was equipped
        /// (globally, for weapons.csv) and how long each player stood in their own/an enemy's/a
        /// neutral zone (for players.csv). Each interval is attributed using the state read at the
        /// START of that interval - the same convention used throughout (e.g. ownership's own "from").
        ///
        /// Opus review items 3 and 11:
        /// - a sample with t == -1 is ignored entirely (never a real state to start or end an interval);
        /// - an interval starting on a DEAD sample (alive:false) contributes no equipped/zone time -
        ///   a corpse doesn't hold a weapon or stand in anyone's territory;
        /// - a gap between two consecutive samples is capped at 2x the sample interval - a disconnected
        ///   client's silent gap doesn't keep accruing whatever it was doing when it dropped;
        /// - the LAST sample of each actor's own stream gets one trailing interval too (capped at the
        ///   sample interval, and never past that actor's own last covered instant), so the tail of the
        ///   match isn't simply uncounted.
        ///
        /// Task T7: every interval (the main sweep's and the trailing one) is CLIPPED to the requested
        /// window via TimeWindow.Clip before being accumulated - so a sample interval crossing a phase
        /// boundary contributes its correct partial share to each phase's own build.</summary>
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
                if (e.T < 0) continue; // item 11

                int actor = ActorOf(e, fileActor);
                double t = e.T;
                int weapon = ReadInt(e.Data, TelemetryKeys.Weapon, -1);
                int zone = ReadInt(e.Data, TelemetryKeys.Zone, -1);
                int team = effectiveTeam.GetValueOrDefault(actor, -1);
                bool alive = e.Data[TelemetryKeys.Alive]?.ToObject<bool?>() ?? true;

                if (lastT.TryGetValue(actor, out double prevT) && t > prevT)
                {
                    bool prevAlive = lastAlive.GetValueOrDefault(actor, true);
                    if (prevAlive) // item 3: a dead sample starts no counted interval
                    {
                        double intervalEnd = prevT + Math.Min(t - prevT, gapCap); // item 11: cap a disconnect gap
                        // Task T7: clip [prevT, intervalEnd) to the requested window.
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

            // Trailing interval for each actor's own LAST sample (item 11), bounded by that actor's own
            // covered range (item 3) - never invented time past what we actually have for them. Task
            // T7: clipped to the window the same way as every other interval above.
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

            // Opus review item 6: accuracy is Projectile-hits / projectiles ONLY - Splash hits (one
            // rocket's own splash falloff landing on several targets from one projectile) are counted
            // separately so they can no longer push accuracy over 100%. Distance stays Projectile-only
            // too, for the same "matches what Hits counts" reason. Damage sums exclude any Burn-source
            // hit row (item 8 - already counted via its `dot`), from BOTH weapon and ability totals.
            // `hits` (tables.Hits) is already window-filtered by BuildHits - nothing extra to do here.
            var hitCountByWeapon = new Dictionary<int, int>();
            var splashCountByWeapon = new Dictionary<int, int>();
            var distancesByWeapon = new Dictionary<int, List<double>>();
            var damageRawByWeapon = new Dictionary<int, float>();
            var armorByWeapon = new Dictionary<int, float>();
            var healthByWeapon = new Dictionary<int, float>();
            var damageRawByAbility = new Dictionary<int, float>();

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
                if (!window.Contains(e.T)) continue; // Task T7: this death didn't happen in this window.

                int victim = ActorOf(e, fileActor);
                TelemetrySession victimSession = sessionByActor.GetValueOrDefault(victim);

                // Opus review item 10: a null inside `assists` (a malformed line, or a future schema
                // that can write one) used to throw on ToObject<int>() - skipped instead.
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
                    LoadoutEquipment = ReadInt(e.Data, TelemetryKeys.LoadoutEquipment, -1),
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
            // Task T7: respawns stay UNWINDOWED - the time-alive tail below needs the TRUE respawn
            // history to find "the first respawn after the true last death", even when that respawn
            // (or the death before it) falls outside this window.
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Respawn) continue;
                int actor = ActorOf(e, fileActor);
                if (!respawnsByActor.TryGetValue(actor, out var list))
                    respawnsByActor[actor] = list = new List<double>();
                list.Add(e.T);
            }

            // Opus review item 8: Burn-source hit rows are excluded (already counted via their `dot`).
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
                var gold = goldByActor.GetValueOrDefault(actor);
                (double First, double Last) coverage = coverageByActor.TryGetValue(actor, out var c) ? c : (0, 0);

                // Opus review item 3 (Task T7), CORRECTED by the T7 review: a completed life is a
                // continuous span [deathT - timeAlive, deathT), not an instant - CLIPPED to the
                // window like every other integral here, rather than crediting the WHOLE life to
                // whichever phase the death instant itself falls in. Crediting the whole life to the
                // death's phase could report more alive time in a phase than the phase itself lasted
                // (a life of 125s dying at t=125, entirely credited to a 90s-long Phase 2). Clipping
                // the life's own span the same way the still-alive tail already is keeps Phase 1 +
                // Phase 2 summing to exactly the whole match, this time without ever exceeding either
                // phase's own length.
                double timeAlive = 0;
                double? lastDeathT = null;
                foreach (TelemetryEvent e in log.Events)
                {
                    if (e.Name != TelemetryKeys.Death) continue;
                    if (ActorOf(e, fileActor) != actor) continue;
                    if (!lastDeathT.HasValue || e.T > lastDeathT.Value) lastDeathT = e.T; // unwindowed - the TRUE last death

                    float lifeTimeAlive = ReadFloat(e.Data, TelemetryKeys.TimeAlive);
                    if (window.Clip(e.T - lifeTimeAlive, e.T, out double lifeFrom, out double lifeTo))
                        timeAlive += lifeTo - lifeFrom;
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

        /// <summary>Point 3 (first-pass review): a `goldEarned` line where every source is 0 and
        /// `zones` is empty or all-zero is junk from a remote copy's teardown (or the owner's own
        /// closing flush) - skipped everywhere this aggregator reads goldEarned. Kept for old logs;
        /// the T4 fix stopped writing these at the source, but a log recorded before it still can.</summary>
        private static bool IsJunkGoldEarned(JObject data)
        {
            if (SumGoldEarnedTotal(data) != 0) return false;
            var zones = data[TelemetryKeys.Zones] as JArray;
            if (zones == null || zones.Count == 0) return true;
            foreach (JToken z in zones)
                if ((z.Type == JTokenType.Null ? 0 : z.ToObject<int>()) != 0) return false;
            return true;
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
