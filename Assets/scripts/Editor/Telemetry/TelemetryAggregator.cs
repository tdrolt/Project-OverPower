using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overpower.Telemetry;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T5 step 3: pure log -> tables. No IO of its own - TelemetryLog already did the
    /// reading, CsvReportWriter (and later HtmlReportWriter) only format what this produces. Edit-mode
    /// tested against a hand-written fixture with hand-computed totals (TelemetryAggregatorTests).
    ///
    /// T4-review corrections this was built against (see the coordinator's own note, applied here
    /// rather than re-litigated):
    /// 1. Capture outcomes come ONLY from `ownership` lines, never from a `capture` line's own state
    ///    string - see BuildCaptures. `capture` lines are read defensively: an unrecognised state is
    ///    counted (Header.UnknownCaptureStateCount) and otherwise ignored, never a crash.
    /// 2. `bounty` (master, per-player-per-payout) is NOT added into any team/player income total -
    ///    the owner's own `goldEarned.bounty` field already reflects the credit that event describes.
    ///    `bounty` lines are parsed by TelemetryLog (a known event name) but this aggregator does not
    ///    consume them into any T5 table.
    /// 3. A `goldEarned` line that is entirely zero (every source 0 and `zones` empty or all-zero) is
    ///    junk from a remote copy's teardown or the owner's own closing flush - skipped everywhere.
    /// 4. `underAttack` isn't consumed by any T5 table (none of the 12 CSVs need it) - left for a
    ///    later task.
    /// 5. An armor `purchase`'s `item` id is treated as a plain opaque int, never decoded.</summary>
    public static class TelemetryAggregator
    {
        private const int Neutral = -1; // TerritoryMap.Neutral's own value - a zone with no owner.

        private static readonly HashSet<string> DiscreteHitSources = new HashSet<string> { "Projectile", "Splash" };

        private static readonly HashSet<string> CaptureOpenStates =
            new HashSet<string> { "started", "resumed", "drainStarted", "drainResumed" };
        private static readonly HashSet<string> CapturePauseStates = new HashSet<string> { "paused", "drainPaused" };
        // "completed"/"neutralised" are known (older logs may still carry them) but never drive the
        // state machine - see the class comment's point 1. Anything outside this whole set is unknown.
        private static readonly HashSet<string> CaptureKnownStates = new HashSet<string>
            { "started", "resumed", "drainStarted", "drainResumed", "paused", "drainPaused", "completed", "neutralised" };

        private sealed class CaptureAttempt
        {
            public int Team;
            public double Start;
            public bool Paused;
            public bool IsDrain;
            public int LastPlayers;
        }

        public static ReportTables Build(TelemetryLog log)
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

            double matchLength = 0;
            foreach (TelemetryEvent e in log.Events)
                if (e.T > matchLength) matchLength = e.T;

            var zoneTier = new Dictionary<int, int>();
            // Every raw ownership change per zone, IN TIME ORDER, including transitions to neutral -
            // the ownership.csv stint list (below) deliberately omits neutral rows, but capture-closing
            // and owner-at-time queries both need the full history.
            var rawChangesByZone = new Dictionary<int, List<(double T, int New)>>();
            BuildOwnership(log, tables.Ownership, zoneTier, rawChangesByZone, matchLength);

            BuildHeader(tables.Header, log, sessionByActor, matchLength);
            BuildCaptures(log, rawChangesByZone, tables.Header, tables.Captures);
            BuildPurchasesAndBlocked(log, fileActor, sessionByActor, tables);
            BuildHits(log, tables.Hits);
            BuildGoldTimelineAndEconomy(log, fileActor, sessionByActor, zoneTier, rawChangesByZone, matchLength, tables);
            BuildZoneIncome(log, fileActor, sessionByActor, zoneTier, tables.Ownership, tables.ZoneIncome);

            var sampleStats = BuildSampleDerivedStats(log, fileActor, sessionByActor, rawChangesByZone);
            BuildWeaponsAndAbilities(log, tables.Hits, sampleStats.EquippedSecondsByWeapon, tables);
            BuildPlayersAndDeaths(log, fileActor, sessionByActor, sampleStats, matchLength, tables);

            return tables;
        }

        // ==================================================================== header

        private static void BuildHeader(ReportHeader header, TelemetryLog log, Dictionary<int, TelemetrySession> sessionByActor, double matchLength)
        {
            header.MatchId = log.MatchId;
            header.MatchLengthSeconds = matchLength;
            header.MalformedLineCount = log.MalformedLineCount;
            header.UnknownEventCount = log.UnknownEventCount;
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
                if (e.Name == TelemetryKeys.Marker)
                {
                    header.Markers.Add(new MarkerRow
                    {
                        T = e.T,
                        Actor = e.Data[TelemetryKeys.Actor]?.ToObject<int?>() ?? -1,
                        Note = e.Data[TelemetryKeys.Note]?.ToString() ?? "",
                    });
                }
                else if (e.Name == TelemetryKeys.GoldEarned && !IsJunkGoldEarned(e.Data))
                {
                    if ((e.Data[TelemetryKeys.Debug]?.ToObject<int?>() ?? 0) > 0)
                        header.DebugGoldUsed = true;
                }
            }

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
        }

        // ==================================================================== ownership

        private static void BuildOwnership(TelemetryLog log, List<OwnershipRow> outStints, Dictionary<int, int> zoneTier,
                                            Dictionary<int, List<(double T, int New)>> rawChangesByZone, double matchLength)
        {
            var dedupe = new HashSet<(int Zone, int New, int Since)>();
            var changesByZone = new Dictionary<int, List<(double T, int Tier, int Old, int New)>>();

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Ownership) continue;

                int zone = e.Data[TelemetryKeys.Zone]?.ToObject<int?>() ?? -1;
                int tier = e.Data[TelemetryKeys.Tier]?.ToObject<int?>() ?? 0;
                int oldOwner = e.Data[TelemetryKeys.OldOwner]?.ToObject<int?>() ?? Neutral;
                int newOwner = e.Data[TelemetryKeys.NewOwner]?.ToObject<int?>() ?? Neutral;
                int since = e.Data[TelemetryKeys.HeldSince]?.ToObject<int?>() ?? 0;

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

                    outStints.Add(new OwnershipRow
                    {
                        Zone = zone,
                        Tier = tier,
                        Team = newOwner,
                        From = t,
                        To = to,
                        Duration = to - t,
                        HowEnded = howEnded,
                    });
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

        private static void BuildCaptures(TelemetryLog log, Dictionary<int, List<(double T, int New)>> rawChangesByZone,
                                           ReportHeader header, List<CaptureRow> outCaptures)
        {
            var open = new Dictionary<int, CaptureAttempt>();

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Capture) continue;

                int zone = e.Data[TelemetryKeys.Zone]?.ToObject<int?>() ?? -1;
                string state = e.Data[TelemetryKeys.State]?.ToString() ?? "";
                int team = e.Data[TelemetryKeys.Team]?.ToObject<int?>() ?? -1;
                int players = e.Data[TelemetryKeys.Players]?.ToObject<int?>() ?? 0;

                if (!CaptureKnownStates.Contains(state))
                {
                    header.UnknownCaptureStateCount++;
                    continue;
                }

                if (CaptureOpenStates.Contains(state))
                {
                    if (!open.TryGetValue(zone, out CaptureAttempt attempt))
                    {
                        open[zone] = new CaptureAttempt
                        {
                            Team = team,
                            Start = e.T,
                            IsDrain = state == "drainStarted" || state == "drainResumed",
                            LastPlayers = players,
                        };
                    }
                    else
                    {
                        attempt.Paused = false;
                        attempt.LastPlayers = players;
                    }
                }
                else if (CapturePauseStates.Contains(state))
                {
                    if (open.TryGetValue(zone, out CaptureAttempt attempt))
                    {
                        attempt.Paused = true;
                        attempt.LastPlayers = players;
                    }
                }
                else
                {
                    // "completed"/"neutralised" - informational only (point 1); keep players fresh in
                    // case an ownership change closes this same instant.
                    if (open.TryGetValue(zone, out CaptureAttempt attempt))
                        attempt.LastPlayers = players;
                }
            }

            // Close attempts using ownership changes, not the capture line's own state - see point 1.
            var allChanges = rawChangesByZone
                .SelectMany(kv => kv.Value.Select(c => (Zone: kv.Key, c.T, c.New)))
                .OrderBy(c => c.T)
                .ToList();

            foreach (var change in allChanges)
            {
                if (!open.TryGetValue(change.Zone, out CaptureAttempt attempt)) continue;

                bool closesAsCompleted = !attempt.IsDrain && change.New == attempt.Team;
                bool closesAsNeutralised = attempt.IsDrain && change.New == Neutral;
                if (!closesAsCompleted && !closesAsNeutralised) continue;

                outCaptures.Add(new CaptureRow
                {
                    Zone = change.Zone,
                    Team = attempt.Team,
                    Start = attempt.Start,
                    End = change.T,
                    Duration = change.T - attempt.Start,
                    Outcome = closesAsCompleted ? "completed" : "neutralised",
                    Players = attempt.LastPlayers,
                });
                open.Remove(change.Zone);
            }

            double matchLength = header.MatchLengthSeconds;
            foreach (var kv in open.OrderBy(kv => kv.Key))
            {
                CaptureAttempt attempt = kv.Value;
                outCaptures.Add(new CaptureRow
                {
                    Zone = kv.Key,
                    Team = attempt.Team,
                    Start = attempt.Start,
                    End = matchLength,
                    Duration = matchLength - attempt.Start,
                    Outcome = attempt.Paused ? "abandoned" : "interrupted",
                    Players = attempt.LastPlayers,
                });
            }
        }

        // ==================================================================== purchases / shop blocked / hits

        private static void BuildPurchasesAndBlocked(TelemetryLog log, Dictionary<string, int> fileActor,
                                                      Dictionary<int, TelemetrySession> sessionByActor, ReportTables tables)
        {
            foreach (TelemetryEvent e in log.Events)
            {
                int actor = ActorOf(e, fileActor);
                TelemetrySession session = sessionByActor.GetValueOrDefault(actor);
                int team = session?.Team ?? -1;
                string nick = session?.Nick ?? "";

                if (e.Name == TelemetryKeys.Purchase)
                {
                    tables.Purchases.Add(new PurchaseRow
                    {
                        T = e.T, Actor = actor, Nick = nick, Team = team, Kind = "purchase",
                        Category = e.Data[TelemetryKeys.Category]?.ToString() ?? "",
                        ItemId = e.Data[TelemetryKeys.ItemId]?.ToObject<int?>() ?? -1,
                        Amount = e.Data[TelemetryKeys.Price]?.ToObject<int?>() ?? 0,
                        BalanceAfter = e.Data[TelemetryKeys.BalanceAfter]?.ToObject<int?>() ?? 0,
                        Zone = e.Data[TelemetryKeys.Zone]?.ToObject<int?>() ?? -1,
                        Free = e.Data[TelemetryKeys.Free]?.ToObject<bool?>() ?? false,
                    });
                }
                else if (e.Name == TelemetryKeys.Refund)
                {
                    tables.Purchases.Add(new PurchaseRow
                    {
                        T = e.T, Actor = actor, Nick = nick, Team = team, Kind = "refund",
                        Category = e.Data[TelemetryKeys.Category]?.ToString() ?? "",
                        ItemId = -1,
                        Amount = e.Data[TelemetryKeys.Amount]?.ToObject<int?>() ?? 0,
                        BalanceAfter = e.Data[TelemetryKeys.BalanceAfter]?.ToObject<int?>() ?? 0,
                        Zone = e.Data[TelemetryKeys.Zone]?.ToObject<int?>() ?? -1,
                    });
                }
                else if (e.Name == TelemetryKeys.ShopBlocked)
                {
                    tables.ShopBlocked.Add(new ShopBlockedRow
                    {
                        T = e.T, Actor = actor, Nick = nick,
                        ItemId = e.Data[TelemetryKeys.ItemId]?.ToObject<int?>() ?? -1,
                        Price = e.Data[TelemetryKeys.Price]?.ToObject<int?>() ?? 0,
                        Reason = e.Data[TelemetryKeys.Reason]?.ToString() ?? "",
                        Shortfall = e.Data[TelemetryKeys.Shortfall]?.ToObject<int?>() ?? 0,
                        Zone = e.Data[TelemetryKeys.Zone]?.ToObject<int?>() ?? -1,
                    });
                }
            }
        }

        private static void BuildHits(TelemetryLog log, List<HitRow> outHits)
        {
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Hit) continue;

                JToken dToken = e.Data[TelemetryKeys.Distance];
                float? distance = (dToken != null && dToken.Type != JTokenType.Null) ? dToken.ToObject<float?>() : null;

                outHits.Add(new HitRow
                {
                    T = e.T,
                    Attacker = e.Data[TelemetryKeys.Attacker]?.ToObject<int?>() ?? -1,
                    AttackerTeam = e.Data[TelemetryKeys.AttackerTeam]?.ToObject<int?>() ?? -1,
                    Victim = e.Data[TelemetryKeys.Victim]?.ToObject<int?>() ?? -1,
                    VictimTeam = e.Data[TelemetryKeys.VictimTeam]?.ToObject<int?>() ?? -1,
                    Weapon = e.Data[TelemetryKeys.Weapon]?.ToObject<int?>() ?? -1,
                    Ability = e.Data[TelemetryKeys.AbilityId]?.ToObject<int?>() ?? -1,
                    Source = e.Data[TelemetryKeys.Source]?.ToString() ?? "",
                    Raw = ReadFloat(e.Data, TelemetryKeys.Raw),
                    Armor = ReadFloat(e.Data, TelemetryKeys.ArmorAbsorbed),
                    // T3 review: new logs use "hpLost"; older ones (pre-review) wrote the same value as "hp".
                    HealthLost = ReadFloat(e.Data, TelemetryKeys.HealthLost, "hp"),
                    Lethal = e.Data[TelemetryKeys.Lethal]?.ToObject<bool?>() ?? false,
                    Distance = distance,
                    Vulnerable = ReadFloat(e.Data, TelemetryKeys.Vulnerable),
                    Overpower = e.Data[TelemetryKeys.OverpowerActive]?.ToObject<bool?>() ?? false,
                });
            }
        }

        // ==================================================================== gold timeline / economy by minute

        private static void BuildGoldTimelineAndEconomy(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, TelemetrySession> sessionByActor, Dictionary<int, int> zoneTier,
            Dictionary<int, List<(double T, int New)>> rawChangesByZone, double matchLength, ReportTables tables)
        {
            var earnedRunning = new Dictionary<int, int>();
            var spentRunning = new Dictionary<int, int>();

            // Ceiling, not floor+1: an event at exactly t == matchLength (a multiple of 60, e.g. a
            // 180s match's final samples) must land in the LAST occupied minute, not spill into an
            // empty one after it - see the minute-index clamp below.
            int numMinutes = Math.Max(1, (int)Math.Ceiling(matchLength / 60.0));
            List<int> teams = sessionByActor.Values.Select(s => s.Team).Distinct().OrderBy(t => t).ToList();
            var economyRows = new Dictionary<(int Minute, int Team), EconomyByMinuteRow>();
            for (int m = 0; m < numMinutes; m++)
                foreach (int team in teams)
                    economyRows[(m, team)] = new EconomyByMinuteRow { Minute = m, Team = team };

            foreach (TelemetryEvent e in log.Events)
            {
                int actor = ActorOf(e, fileActor);
                TelemetrySession session = sessionByActor.GetValueOrDefault(actor);
                int team = session?.Team ?? -1;

                if (e.Name == TelemetryKeys.GoldEarned)
                {
                    if (IsJunkGoldEarned(e.Data)) continue;

                    int total = SumGoldEarnedTotal(e.Data);
                    earnedRunning[actor] = earnedRunning.GetValueOrDefault(actor) + total;

                    int minute = Math.Min((int)Math.Floor(e.T / 60.0), numMinutes - 1);
                    if (minute < 0) minute = 0;
                    if (team >= 0 && economyRows.TryGetValue((minute, team), out EconomyByMinuteRow row))
                    {
                        var zones = e.Data[TelemetryKeys.Zones] as JArray;
                        if (zones != null)
                        {
                            for (int z = 0; z < zones.Count; z++)
                            {
                                if (!zoneTier.TryGetValue(z, out int tier)) continue;
                                int amount = zones[z].ToObject<int?>() ?? 0;
                                row.IncomeByTier[tier - 1] += amount;
                            }
                        }
                        // Point 2: goldEarned.bounty is the OWNER's own credited total, not the
                        // master's per-payout `bounty` line - safe to sum here without double counting.
                        row.Bounty += e.Data[TelemetryKeys.Bounty]?.ToObject<int?>() ?? 0;
                        row.Refund += e.Data[TelemetryKeys.Refund]?.ToObject<int?>() ?? 0;
                    }
                }
                else if (e.Name == TelemetryKeys.Purchase)
                {
                    int price = e.Data[TelemetryKeys.Price]?.ToObject<int?>() ?? 0;
                    spentRunning[actor] = spentRunning.GetValueOrDefault(actor) + price;

                    int minute = Math.Min((int)Math.Floor(e.T / 60.0), numMinutes - 1);
                    if (minute < 0) minute = 0;
                    if (team >= 0 && economyRows.TryGetValue((minute, team), out EconomyByMinuteRow row))
                        row.Spent += price;
                }
                else if (e.Name == TelemetryKeys.Sample)
                {
                    tables.GoldTimeline.Add(new GoldTimelineRow
                    {
                        T = e.T,
                        Actor = actor,
                        Nick = session?.Nick ?? "",
                        Team = e.Data[TelemetryKeys.Team]?.ToObject<int?>() ?? team,
                        Balance = e.Data[TelemetryKeys.Balance]?.ToObject<int?>() ?? 0,
                        EarnedSoFar = earnedRunning.GetValueOrDefault(actor),
                        SpentSoFar = spentRunning.GetValueOrDefault(actor),
                    });
                }
            }

            // Zones held per tier, sampled at each minute's midpoint.
            for (int m = 0; m < numMinutes; m++)
            {
                double midpoint = Math.Min(m * 60.0 + 30.0, matchLength);
                foreach (int zone in zoneTier.Keys)
                {
                    int owner = OwnerAtTime(rawChangesByZone, zone, midpoint);
                    if (owner < 0) continue;
                    if (economyRows.TryGetValue((m, owner), out EconomyByMinuteRow row))
                        row.ZonesHeldByTier[zoneTier[zone] - 1]++;
                }
            }

            // Gold gap to the richest team, from each actor's last sample at or before the minute's end.
            var samplesByActor = new Dictionary<int, List<(double T, int Balance)>>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Sample) continue;
                int actor = ActorOf(e, fileActor);
                if (!samplesByActor.TryGetValue(actor, out var list))
                    samplesByActor[actor] = list = new List<(double, int)>();
                list.Add((e.T, e.Data[TelemetryKeys.Balance]?.ToObject<int?>() ?? 0));
            }

            for (int m = 0; m < numMinutes; m++)
            {
                double cutoff = Math.Min((m + 1) * 60.0, matchLength);
                var teamBalance = new Dictionary<int, int>();
                foreach (var kv in sessionByActor)
                {
                    int actor = kv.Key;
                    int team = kv.Value.Team;
                    if (team < 0 || !samplesByActor.TryGetValue(actor, out var list)) continue;

                    int? balance = null;
                    double bestT = double.NegativeInfinity;
                    foreach (var (t, bal) in list)
                        if (t <= cutoff && t >= bestT) { bestT = t; balance = bal; }

                    if (balance.HasValue)
                        teamBalance[team] = teamBalance.GetValueOrDefault(team) + balance.Value;
                }

                int richest = teamBalance.Values.DefaultIfEmpty(0).Max();
                foreach (int team in teams)
                    if (economyRows.TryGetValue((m, team), out EconomyByMinuteRow row))
                        row.GoldGapToRichest = richest - teamBalance.GetValueOrDefault(team);
            }

            tables.EconomyByMinute = economyRows.Values.OrderBy(r => r.Minute).ThenBy(r => r.Team).ToList();
        }

        // ==================================================================== zone income

        private static void BuildZoneIncome(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, TelemetrySession> sessionByActor, Dictionary<int, int> zoneTier,
            List<OwnershipRow> stints, List<ZoneIncomeRow> outRows)
        {
            var secondsHeld = new Dictionary<(int Zone, int Team), double>();
            foreach (OwnershipRow stint in stints)
                secondsHeld[(stint.Zone, stint.Team)] = secondsHeld.GetValueOrDefault((stint.Zone, stint.Team)) + stint.Duration;

            var goldGenerated = new Dictionary<(int Zone, int Team), int>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.GoldEarned || IsJunkGoldEarned(e.Data)) continue;

                int actor = ActorOf(e, fileActor);
                int team = sessionByActor.GetValueOrDefault(actor)?.Team ?? -1;
                if (team < 0) continue;

                var zones = e.Data[TelemetryKeys.Zones] as JArray;
                if (zones == null) continue;
                for (int z = 0; z < zones.Count; z++)
                {
                    int amount = zones[z].ToObject<int?>() ?? 0;
                    if (amount == 0) continue;
                    goldGenerated[(z, team)] = goldGenerated.GetValueOrDefault((z, team)) + amount;
                }
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
        /// START of that interval - the same convention used throughout (e.g. ownership's own "from").</summary>
        private static SampleDerivedStats BuildSampleDerivedStats(TelemetryLog log, Dictionary<string, int> fileActor,
            Dictionary<int, TelemetrySession> sessionByActor, Dictionary<int, List<(double T, int New)>> rawChangesByZone)
        {
            var stats = new SampleDerivedStats();
            var lastT = new Dictionary<int, double>();
            var lastWeapon = new Dictionary<int, int>();
            var lastZone = new Dictionary<int, int>();
            var lastTeam = new Dictionary<int, int>();

            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Sample) continue;
                int actor = ActorOf(e, fileActor);
                double t = e.T;
                int weapon = e.Data[TelemetryKeys.Weapon]?.ToObject<int?>() ?? -1;
                int zone = e.Data[TelemetryKeys.Zone]?.ToObject<int?>() ?? -1;
                int team = e.Data[TelemetryKeys.Team]?.ToObject<int?>() ?? sessionByActor.GetValueOrDefault(actor)?.Team ?? -1;

                if (lastT.TryGetValue(actor, out double prevT) && t > prevT)
                {
                    double dur = t - prevT;
                    int prevWeapon = lastWeapon[actor];
                    if (prevWeapon >= 0)
                        stats.EquippedSecondsByWeapon[prevWeapon] = stats.EquippedSecondsByWeapon.GetValueOrDefault(prevWeapon) + dur;

                    int prevZone = lastZone[actor];
                    int prevTeam = lastTeam[actor];
                    int owner = prevZone < 0 ? Neutral : OwnerAtTime(rawChangesByZone, prevZone, prevT);

                    if (prevZone < 0 || owner == Neutral)
                        stats.NeutralZoneSecondsByActor[actor] = stats.NeutralZoneSecondsByActor.GetValueOrDefault(actor) + dur;
                    else if (owner == prevTeam)
                        stats.OwnZoneSecondsByActor[actor] = stats.OwnZoneSecondsByActor.GetValueOrDefault(actor) + dur;
                    else
                        stats.EnemyZoneSecondsByActor[actor] = stats.EnemyZoneSecondsByActor.GetValueOrDefault(actor) + dur;
                }

                lastT[actor] = t;
                lastWeapon[actor] = weapon;
                lastZone[actor] = zone;
                lastTeam[actor] = team;
            }

            return stats;
        }

        // ==================================================================== weapons / abilities

        private static void BuildWeaponsAndAbilities(TelemetryLog log, List<HitRow> hits,
            Dictionary<int, double> equippedSecondsByWeapon, ReportTables tables)
        {
            List<int> weaponIds = WeaponIdUniverse(log);

            var pullsByWeapon = new Dictionary<int, int>();
            var projectilesByWeapon = new Dictionary<int, int>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Shots) continue;
                int w = e.Data[TelemetryKeys.Weapon]?.ToObject<int?>() ?? -1;
                pullsByWeapon[w] = pullsByWeapon.GetValueOrDefault(w) + (e.Data[TelemetryKeys.Pulls]?.ToObject<int?>() ?? 0);
                projectilesByWeapon[w] = projectilesByWeapon.GetValueOrDefault(w) + (e.Data[TelemetryKeys.Projectiles]?.ToObject<int?>() ?? 0);
            }

            var killsByWeapon = new Dictionary<int, int>();
            var killsByAbility = new Dictionary<int, int>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Death) continue;
                int w = e.Data[TelemetryKeys.Weapon]?.ToObject<int?>() ?? -1;
                int ab = e.Data[TelemetryKeys.AbilityId]?.ToObject<int?>() ?? -1;
                if (w >= 0) killsByWeapon[w] = killsByWeapon.GetValueOrDefault(w) + 1;
                if (ab >= 0) killsByAbility[ab] = killsByAbility.GetValueOrDefault(ab) + 1;
            }

            // Discrete hits (Projectile/Splash only) drive hit-count/accuracy/distance; hit + dot raw
            // together drive damage (the T3-review global rule).
            var hitCountByWeapon = new Dictionary<int, int>();
            var distancesByWeapon = new Dictionary<int, List<double>>();
            var damageRawByWeapon = new Dictionary<int, float>();
            var armorByWeapon = new Dictionary<int, float>();
            var healthByWeapon = new Dictionary<int, float>();
            var damageRawByAbility = new Dictionary<int, float>();

            foreach (HitRow h in hits)
            {
                bool discrete = DiscreteHitSources.Contains(h.Source);
                if (h.Weapon >= 0)
                {
                    if (discrete)
                    {
                        hitCountByWeapon[h.Weapon] = hitCountByWeapon.GetValueOrDefault(h.Weapon) + 1;
                        if (h.Distance.HasValue)
                        {
                            if (!distancesByWeapon.TryGetValue(h.Weapon, out var list))
                                distancesByWeapon[h.Weapon] = list = new List<double>();
                            list.Add(h.Distance.Value);
                        }
                    }
                    damageRawByWeapon[h.Weapon] = damageRawByWeapon.GetValueOrDefault(h.Weapon) + h.Raw;
                    armorByWeapon[h.Weapon] = armorByWeapon.GetValueOrDefault(h.Weapon) + h.Armor;
                    healthByWeapon[h.Weapon] = healthByWeapon.GetValueOrDefault(h.Weapon) + h.HealthLost;
                }
                if (h.Ability >= 0)
                    damageRawByAbility[h.Ability] = damageRawByAbility.GetValueOrDefault(h.Ability) + h.Raw;
            }

            var castsByAbility = new Dictionary<int, int>();
            var statusCountByAbility = new Dictionary<int, int>();
            var statusSecondsByAbility = new Dictionary<int, double>();
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name == TelemetryKeys.Cast)
                {
                    int ab = e.Data[TelemetryKeys.AbilityId]?.ToObject<int?>() ?? -1;
                    if (ab >= 0) castsByAbility[ab] = castsByAbility.GetValueOrDefault(ab) + 1;
                }
                else if (e.Name == TelemetryKeys.Status)
                {
                    int ab = e.Data[TelemetryKeys.AbilityId]?.ToObject<int?>() ?? -1;
                    if (ab < 0) continue;
                    statusCountByAbility[ab] = statusCountByAbility.GetValueOrDefault(ab) + 1;
                    statusSecondsByAbility[ab] = statusSecondsByAbility.GetValueOrDefault(ab) + ReadFloat(e.Data, TelemetryKeys.Duration);
                }
                else if (e.Name == TelemetryKeys.Dot)
                {
                    int ab = e.Data[TelemetryKeys.AbilityId]?.ToObject<int?>() ?? -1;
                    if (ab >= 0)
                        damageRawByAbility[ab] = damageRawByAbility.GetValueOrDefault(ab) + ReadFloat(e.Data, TelemetryKeys.Raw);
                }
            }
            // dot lines also carry a weapon id (FireField-launched burns) - fold into the same weapon totals.
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Dot) continue;
                int w = e.Data[TelemetryKeys.Weapon]?.ToObject<int?>() ?? -1;
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
                    int w = e.Data[TelemetryKeys.Weapon]?.ToObject<int?>() ?? -1;
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
            Dictionary<int, TelemetrySession> sessionByActor, SampleDerivedStats sampleStats, double matchLength, ReportTables tables)
        {
            // Deaths first - "victim" has no field of its own on a `death` line; it is the file owner.
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Death) continue;
                int victim = ActorOf(e, fileActor);
                TelemetrySession victimSession = sessionByActor.GetValueOrDefault(victim);

                var assists = (e.Data[TelemetryKeys.Assists] as JArray)?.Select(t => t.ToObject<int>()).ToArray() ?? Array.Empty<int>();

                tables.Deaths.Add(new DeathRow
                {
                    T = e.T,
                    Victim = victim,
                    VictimNick = victimSession?.Nick ?? "",
                    VictimTeam = victimSession?.Team ?? -1,
                    Killer = e.Data[TelemetryKeys.Killer]?.ToObject<int?>() ?? -1,
                    KillerTeam = e.Data[TelemetryKeys.KillerTeam]?.ToObject<int?>() ?? -1,
                    Assists = assists,
                    Weapon = e.Data[TelemetryKeys.Weapon]?.ToObject<int?>() ?? -1,
                    Ability = e.Data[TelemetryKeys.AbilityId]?.ToObject<int?>() ?? -1,
                    X = ReadFloat(e.Data, TelemetryKeys.X),
                    Z = ReadFloat(e.Data, TelemetryKeys.Z),
                    UnspentGold = e.Data[TelemetryKeys.UnspentGold]?.ToObject<int?>() ?? 0,
                    LoadoutWeapon = e.Data[TelemetryKeys.LoadoutWeapon]?.ToObject<int?>() ?? -1,
                    LoadoutEquipment = e.Data[TelemetryKeys.LoadoutEquipment]?.ToObject<int?>() ?? -1,
                    LoadoutMobility = e.Data[TelemetryKeys.LoadoutMobility]?.ToObject<int?>() ?? -1,
                    LoadoutUltimate = e.Data[TelemetryKeys.LoadoutUltimate]?.ToObject<int?>() ?? -1,
                    AbsorbLevel = e.Data[TelemetryKeys.AbsorbLevel]?.ToObject<int?>() ?? 0,
                    RechargeLevel = e.Data[TelemetryKeys.RechargeLevel]?.ToObject<int?>() ?? 0,
                });
            }

            var killsByActor = new Dictionary<int, int>();
            var assistsByActor = new Dictionary<int, int>();
            var respawnsByActor = new Dictionary<int, List<double>>();
            foreach (DeathRow d in tables.Deaths)
            {
                if (d.Killer >= 0) killsByActor[d.Killer] = killsByActor.GetValueOrDefault(d.Killer) + 1;
                foreach (int assister in d.Assists)
                    assistsByActor[assister] = assistsByActor.GetValueOrDefault(assister) + 1;
            }
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Respawn) continue;
                int actor = ActorOf(e, fileActor);
                if (!respawnsByActor.TryGetValue(actor, out var list))
                    respawnsByActor[actor] = list = new List<double>();
                list.Add(e.T);
            }

            var damageDealtByActor = new Dictionary<int, float>();
            var damageTakenByActor = new Dictionary<int, float>();
            foreach (HitRow h in tables.Hits)
            {
                if (h.Attacker >= 0) damageDealtByActor[h.Attacker] = damageDealtByActor.GetValueOrDefault(h.Attacker) + h.Raw;
                if (h.Victim >= 0) damageTakenByActor[h.Victim] = damageTakenByActor.GetValueOrDefault(h.Victim) + h.Raw;
            }
            foreach (TelemetryEvent e in log.Events)
            {
                if (e.Name != TelemetryKeys.Dot) continue;
                int a = e.Data[TelemetryKeys.Attacker]?.ToObject<int?>() ?? -1;
                int v = e.Data[TelemetryKeys.Victim]?.ToObject<int?>() ?? -1;
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
                    var prev = goldByActor.GetValueOrDefault(actor);
                    goldByActor[actor] = (
                        prev.Terr + (e.Data[TelemetryKeys.Territory]?.ToObject<int?>() ?? 0),
                        prev.Bounty + (e.Data[TelemetryKeys.Bounty]?.ToObject<int?>() ?? 0),
                        prev.Refund + (e.Data[TelemetryKeys.Refund]?.ToObject<int?>() ?? 0),
                        prev.Debug + (e.Data[TelemetryKeys.Debug]?.ToObject<int?>() ?? 0),
                        prev.Other + (e.Data[TelemetryKeys.Other]?.ToObject<int?>() ?? 0));
                }
                else if (e.Name == TelemetryKeys.Purchase)
                {
                    spentByActor[actor] = spentByActor.GetValueOrDefault(actor) + (e.Data[TelemetryKeys.Price]?.ToObject<int?>() ?? 0);
                }
                else if (e.Name == TelemetryKeys.Heal)
                {
                    var tiers = e.Data[TelemetryKeys.HealTiers] as JArray;
                    float sum = 0;
                    if (tiers != null) foreach (JToken v in tiers) sum += v.ToObject<float?>() ?? 0f;
                    healByActor[actor] = healByActor.GetValueOrDefault(actor) + sum;
                }
            }

            foreach (var kv in sessionByActor)
            {
                int actor = kv.Key;
                TelemetrySession session = kv.Value;
                var gold = goldByActor.GetValueOrDefault(actor);

                // Sum of every completed life's own `timeAlive` field (how long that life lasted, not
                // when it ended), plus - if the player is still alive after their last death - the
                // tail from their next respawn to the match's end (see below).
                double timeAlive = 0;
                double? lastDeathT = null;
                foreach (TelemetryEvent e in log.Events)
                {
                    if (e.Name != TelemetryKeys.Death) continue;
                    if (ActorOf(e, fileActor) != actor) continue;
                    timeAlive += ReadFloat(e.Data, TelemetryKeys.TimeAlive);
                    lastDeathT = e.T;
                }

                if (lastDeathT.HasValue)
                {
                    // Still-ongoing life after the last death: from the first respawn after it to the
                    // match's end. No respawn after the last death (log ends while still dead) adds nothing.
                    if (respawnsByActor.TryGetValue(actor, out var respawns))
                    {
                        double? firstAfter = respawns.Where(t => t > lastDeathT.Value).OrderBy(t => t).Cast<double?>().FirstOrDefault();
                        if (firstAfter.HasValue)
                            timeAlive += matchLength - firstAfter.Value;
                    }
                }
                else
                {
                    timeAlive = matchLength; // Never died - alive for the whole match.
                }

                tables.Players.Add(new PlayerRow
                {
                    Actor = actor,
                    Nick = session.Nick,
                    Team = session.Team,
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

        private static int SumGoldEarnedTotal(JObject data) =>
            (data[TelemetryKeys.Territory]?.ToObject<int?>() ?? 0)
            + (data[TelemetryKeys.Bounty]?.ToObject<int?>() ?? 0)
            + (data[TelemetryKeys.Refund]?.ToObject<int?>() ?? 0)
            + (data[TelemetryKeys.Debug]?.ToObject<int?>() ?? 0)
            + (data[TelemetryKeys.Other]?.ToObject<int?>() ?? 0);

        /// <summary>Point 3: a `goldEarned` line where every source is 0 and `zones` is empty or all
        /// zero is junk from a remote copy's teardown (or the owner's own closing flush) - skipped
        /// everywhere this aggregator reads goldEarned.</summary>
        private static bool IsJunkGoldEarned(JObject data)
        {
            if (SumGoldEarnedTotal(data) != 0) return false;
            var zones = data[TelemetryKeys.Zones] as JArray;
            if (zones == null || zones.Count == 0) return true;
            foreach (JToken z in zones)
                if ((z.ToObject<int?>() ?? 0) != 0) return false;
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
