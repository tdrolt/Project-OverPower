using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T7: the phases fixture (Assets/Tests/TelemetryFixtures/match_phases, two files)
    /// has a `phase` 2 event at t=90, an ownership stint (zone 5) spanning 60-&gt;120 across the
    /// boundary, a weapon-1 sample interval (actor 2, t=80-&gt;100) spanning it too, and a third actor
    /// (3) who only ever appears as a `hit` attacker - no file of their own.</summary>
    public class TelemetryPhaseSplitTests
    {
        private static string FixturePath => Path.Combine(Application.dataPath, "Tests/TelemetryFixtures/match_phases");

        private static ReportSet BuildFixtureSet() => TelemetryAggregator.BuildSet(TelemetryLog.Load(FixturePath));

        // ---------------------------------------------------------------- the invariant: Phase 1 + Phase 2 == whole match

        [Test]
        public void Phase1PlusPhase2AdditiveTotalsEqualTheWholeMatch()
        {
            const double Tolerance = 0.01;
            ReportSet set = BuildFixtureSet();
            Assert.IsNotNull(set.Phase2, "the fixture's phase 2 event must produce a Phase 2 scope");

            double wholeDamage = set.WholeMatch.Players.Sum(p => p.DamageDealt);
            double p1Damage = set.Phase1.Players.Sum(p => p.DamageDealt);
            double p2Damage = set.Phase2.Players.Sum(p => p.DamageDealt);
            Assert.AreEqual(wholeDamage, p1Damage + p2Damage, Tolerance, "damage");

            double wholeGoldTerr = set.WholeMatch.Players.Sum(p => p.GoldTerritory);
            double p1GoldTerr = set.Phase1.Players.Sum(p => p.GoldTerritory);
            double p2GoldTerr = set.Phase2.Players.Sum(p => p.GoldTerritory);
            Assert.AreEqual(wholeGoldTerr, p1GoldTerr + p2GoldTerr, Tolerance, "gold by source (territory)");

            double wholeGoldOther = set.WholeMatch.Players.Sum(p => p.GoldBounty + p.GoldRefund + p.GoldDebug + p.GoldOther);
            double p1GoldOther = set.Phase1.Players.Sum(p => p.GoldBounty + p.GoldRefund + p.GoldDebug + p.GoldOther);
            double p2GoldOther = set.Phase2.Players.Sum(p => p.GoldBounty + p.GoldRefund + p.GoldDebug + p.GoldOther);
            Assert.AreEqual(wholeGoldOther, p1GoldOther + p2GoldOther, Tolerance, "gold by source (other)");

            double wholeSpent = set.WholeMatch.Players.Sum(p => p.GoldSpent);
            double p1Spent = set.Phase1.Players.Sum(p => p.GoldSpent);
            double p2Spent = set.Phase2.Players.Sum(p => p.GoldSpent);
            Assert.AreEqual(wholeSpent, p1Spent + p2Spent, Tolerance, "spent");

            double wholeKills = set.WholeMatch.Players.Sum(p => p.Kills);
            double p1Kills = set.Phase1.Players.Sum(p => p.Kills);
            double p2Kills = set.Phase2.Players.Sum(p => p.Kills);
            Assert.AreEqual(wholeKills, p1Kills + p2Kills, Tolerance, "kills");

            double wholeZoneGold = set.WholeMatch.ZoneIncome.Sum(z => z.GoldGenerated);
            double p1ZoneGold = set.Phase1.ZoneIncome.Sum(z => z.GoldGenerated);
            double p2ZoneGold = set.Phase2.ZoneIncome.Sum(z => z.GoldGenerated);
            Assert.AreEqual(wholeZoneGold, p1ZoneGold + p2ZoneGold, Tolerance, "zone gold");

            double wholeEquipped = set.WholeMatch.Weapons.Sum(w => w.TimeEquippedSeconds);
            double p1Equipped = set.Phase1.Weapons.Sum(w => w.TimeEquippedSeconds);
            double p2Equipped = set.Phase2.Weapons.Sum(w => w.TimeEquippedSeconds);
            Assert.AreEqual(wholeEquipped, p1Equipped + p2Equipped, Tolerance, "equipped time");

            double wholeAlive = set.WholeMatch.Players.Sum(p => p.TimeAlive);
            double p1Alive = set.Phase1.Players.Sum(p => p.TimeAlive);
            double p2Alive = set.Phase2.Players.Sum(p => p.TimeAlive);
            Assert.AreEqual(wholeAlive, p1Alive + p2Alive, Tolerance, "alive time (total)");

            // Review fix: alive time PER PLAYER, not just the aggregate sum - a per-life
            // credit-to-one-phase bug could still cancel out in the total while being wrong for
            // any one player.
            foreach (var actor in new[] { 1, 2 })
            {
                double whole = set.WholeMatch.Players.Single(p => p.Actor == actor).TimeAlive;
                double p1 = set.Phase1.Players.Single(p => p.Actor == actor).TimeAlive;
                double p2 = set.Phase2.Players.Single(p => p.Actor == actor).TimeAlive;
                Assert.AreEqual(whole, p1 + p2, Tolerance, $"alive time (actor {actor})");
            }

            // Review fix: deaths, hits, ownership seconds and the zone-time columns too.
            Assert.AreEqual(set.WholeMatch.Deaths.Count, set.Phase1.Deaths.Count + set.Phase2.Deaths.Count, "death count");
            Assert.AreEqual(set.WholeMatch.Hits.Count, set.Phase1.Hits.Count + set.Phase2.Hits.Count, "hit count");
            Assert.AreEqual(set.WholeMatch.Hits.Sum(h => h.Raw), set.Phase1.Hits.Sum(h => h.Raw) + set.Phase2.Hits.Sum(h => h.Raw), Tolerance, "hit raw damage");

            double wholeOwnershipSeconds = set.WholeMatch.Ownership.Sum(o => o.Duration);
            double p1OwnershipSeconds = set.Phase1.Ownership.Sum(o => o.Duration);
            double p2OwnershipSeconds = set.Phase2.Ownership.Sum(o => o.Duration);
            Assert.AreEqual(wholeOwnershipSeconds, p1OwnershipSeconds + p2OwnershipSeconds, Tolerance, "ownership seconds");

            foreach (var actor in new[] { 1, 2 })
            {
                var wholeRow = set.WholeMatch.Players.Single(p => p.Actor == actor);
                var p1Row = set.Phase1.Players.Single(p => p.Actor == actor);
                var p2Row = set.Phase2.Players.Single(p => p.Actor == actor);
                Assert.AreEqual(wholeRow.TimeOwnZone, p1Row.TimeOwnZone + p2Row.TimeOwnZone, Tolerance, $"time own zone (actor {actor})");
                Assert.AreEqual(wholeRow.TimeEnemyZone, p1Row.TimeEnemyZone + p2Row.TimeEnemyZone, Tolerance, $"time enemy zone (actor {actor})");
                Assert.AreEqual(wholeRow.TimeNeutralZone, p1Row.TimeNeutralZone + p2Row.TimeNeutralZone, Tolerance, $"time neutral zone (actor {actor})");
            }
        }

        // ---------------------------------------------------------------- review fix: alive time is a proper per-life split

        [Test]
        public void AliveTimeIsProperlySplitByLifeSpanNotCreditedWhollyToTheDeathsPhase()
        {
            ReportSet set = BuildFixtureSet();

            // Actor 1: life [0, 45) (dies t=45, timeAlive=45) then a tail [50 (respawn), 180]
            // (their own coverage end). Phase 1 [0,90): life fully inside (45) + tail clipped to
            // [50,90) (40) = 85. Phase 2 [90,180]: life clipped away (0) + tail clipped to
            // [90,180] (90) = 90.
            Assert.AreEqual(85.0, set.Phase1.Players.Single(p => p.Actor == 1).TimeAlive, 1e-6);
            Assert.AreEqual(90.0, set.Phase2.Players.Single(p => p.Actor == 1).TimeAlive, 1e-6);

            // Actor 2: life [0, 125) (dies t=125, timeAlive=125) then a tail [130, 180]. Phase 1
            // [0,90): life clipped to [0,90) (90) + tail clipped away (0) = 90. Phase 2 [90,180]:
            // life clipped to [90,125) (35) + tail clipped to [130,180] (50) = 85.
            //
            // Before this fix, the whole 125s life was credited to Phase 2 (the death's own
            // phase) instead of being split - reporting more alive time in Phase 2 (125 + the
            // 50s tail = 175s) than Phase 2 itself lasted (90s). Neither actor's own per-phase
            // alive time may now exceed that phase's own length.
            Assert.AreEqual(90.0, set.Phase1.Players.Single(p => p.Actor == 2).TimeAlive, 1e-6);
            Assert.AreEqual(85.0, set.Phase2.Players.Single(p => p.Actor == 2).TimeAlive, 1e-6);

            double phase1Length = set.Phase1.Header.MatchLengthSeconds;
            double phase2Length = set.Phase2.Header.MatchLengthSeconds;
            foreach (var p in set.Phase1.Players) Assert.LessOrEqual(p.TimeAlive, phase1Length + 1e-6, $"actor {p.Actor} Phase 1");
            foreach (var p in set.Phase2.Players) Assert.LessOrEqual(p.TimeAlive, phase2Length + 1e-6, $"actor {p.Actor} Phase 2");
        }

        // ---------------------------------------------------------------- review fix: minute buckets tag their OWN scope's phase

        [Test]
        public void MinuteBucketsInAPhaseScopedBuildAreTaggedWithThatPhaseNotTheirOwnBucketStart()
        {
            ReportSet set = BuildFixtureSet();

            // A Phase-2-scoped build only ever contains buckets belonging to Phase 2 - even one
            // (minute 1, 60->120) whose own START falls before the t=90 transition, because it
            // still overlaps the Phase 2 window. Before this fix, that bucket kept reading Phase 1
            // (derived from its own absolute start time) even inside the Phase 2 build.
            Assert.IsTrue(set.Phase2.EconomyByMinute.Count > 0);
            Assert.IsTrue(set.Phase2.EconomyByMinute.All(r => r.Phase == 2), "every row in a Phase-2-scoped build must read Phase 2");
            Assert.IsTrue(set.Phase1.EconomyByMinute.All(r => r.Phase == 1), "every row in a Phase-1-scoped build must read Phase 1");

            // The whole-match build still tags a straddling bucket by its own absolute start (a
            // documented simplification of economy_by_minute's own windowing, not a bug) - minute
            // 1 (60->120) starts before the transition, so it reads Phase 1 there.
            var wholeMinute1 = set.WholeMatch.EconomyByMinute.Where(r => r.Minute == 1).ToList();
            if (wholeMinute1.Count > 0)
                Assert.AreEqual(1, wholeMinute1[0].Phase);
        }

        // ---------------------------------------------------------------- ownership stint split at the boundary

        [Test]
        public void AnOwnershipStintCrossingTheBoundarySplitsIntoTwoPhaseTaggedRowsEvenOnTheWholeMatchBuild()
        {
            ReportSet set = BuildFixtureSet();

            // Zone 5: team 0 holds it continuously from t=60 (captured) to t=120 (lost to team 1) -
            // one continuous 60s stint in the raw log, split by the whole-match build itself because
            // it straddles the t=90 transition (design doc, Part 3: "a stint crossing the boundary is
            // split into two rows").
            var zone5Rows = set.WholeMatch.Ownership.Where(o => o.Zone == 5 && o.Team == 0).OrderBy(o => o.From).ToList();
            Assert.AreEqual(2, zone5Rows.Count, "zone 5's team-0 stint must split into exactly two rows");

            Assert.AreEqual(60.0, zone5Rows[0].From, 1e-9);
            Assert.AreEqual(90.0, zone5Rows[0].To, 1e-9);
            Assert.AreEqual(30.0, zone5Rows[0].Duration, 1e-9);
            Assert.AreEqual("phaseBoundary", zone5Rows[0].HowEnded);
            Assert.AreEqual(1, zone5Rows[0].Phase);

            Assert.AreEqual(90.0, zone5Rows[1].From, 1e-9);
            Assert.AreEqual(120.0, zone5Rows[1].To, 1e-9);
            Assert.AreEqual(30.0, zone5Rows[1].Duration, 1e-9);
            Assert.AreEqual("captured", zone5Rows[1].HowEnded); // the REAL reason - team 1 captured it at 120.
            Assert.AreEqual(2, zone5Rows[1].Phase);

            // Phase 1's own build only ever sees its own half.
            var phase1Zone5 = set.Phase1.Ownership.Where(o => o.Zone == 5).ToList();
            Assert.AreEqual(1, phase1Zone5.Count);
            Assert.AreEqual(60.0, phase1Zone5[0].From, 1e-9);
            Assert.AreEqual(90.0, phase1Zone5[0].To, 1e-9);

            // Phase 2's own build sees BOTH of zone 5's post-boundary stints (team 0's tail, then
            // team 1's, once team 1 actually captures it).
            var phase2Zone5 = set.Phase2.Ownership.Where(o => o.Zone == 5).OrderBy(o => o.From).ToList();
            Assert.AreEqual(2, phase2Zone5.Count);
            Assert.AreEqual(0, phase2Zone5[0].Team);
            Assert.AreEqual(90.0, phase2Zone5[0].From, 1e-9);
            Assert.AreEqual(120.0, phase2Zone5[0].To, 1e-9);
            Assert.AreEqual(1, phase2Zone5[1].Team);
            Assert.AreEqual(120.0, phase2Zone5[1].From, 1e-9);
            Assert.AreEqual(180.0, phase2Zone5[1].To, 1e-9);
        }

        // ---------------------------------------------------------------- a sample interval crossing the boundary

        [Test]
        public void ASampleIntervalCrossingTheBoundaryIsClippedProportionally()
        {
            ReportSet set = BuildFixtureSet();

            // Weapon 1 is equipped ONLY by actor 2, across samples at t=80, 100, 150, 180 - the
            // (80 -> 100) interval straddles t=90 and must split into a 10s Phase-1 slice and a 10s
            // Phase-2 slice; the other two intervals (100->150 = 50s, 150->180 = 30s) land entirely in
            // Phase 2. Whole match: 20 + 50 + 30 = 100s. Phase 1: 10s. Phase 2: 90s.
            double WeaponOneSeconds(ReportTables t) => t.Weapons.Single(w => w.WeaponId == 1).TimeEquippedSeconds;

            Assert.AreEqual(100.0, WeaponOneSeconds(set.WholeMatch), 1e-6);
            Assert.AreEqual(10.0, WeaponOneSeconds(set.Phase1), 1e-6);
            Assert.AreEqual(90.0, WeaponOneSeconds(set.Phase2), 1e-6);
        }

        // ---------------------------------------------------------------- review fix: phase/elimination are known events

        [Test]
        public void PhaseAndEliminationEventsAreNotCountedAsUnknown()
        {
            // The fixture's own phase (x2) and elimination (x1) lines must not inflate
            // UnknownEventCount - before the review fix, KnownEventNames lacked both names, so
            // every report (even one with no elimination at all, since MatchTelemetry's own
            // phase-1 anchor is unconditional) showed a false "unknown event(s) were skipped"
            // warning.
            var log = TelemetryLog.Load(FixturePath);
            Assert.AreEqual(0, log.UnknownEventCount);
        }

        // ---------------------------------------------------------------- review fix (item 8): zero-length stints survive

        [Test]
        public void ZeroLengthOwnershipStintSurvivesWindowClippingOnTheWholeMatchBuild()
        {
            // Not the shared match_phases fixture - a small, isolated temp log (same pattern as
            // TelemetryAggregatorReviewFixesTests), since this is testing TimeWindow.Clip's own
            // edge case (see TimeWindowTests for the unit-level version) end to end through the
            // aggregator, not anything about the phase split itself.
            string temp = Path.Combine(Path.GetTempPath(), "TelemetryZeroLengthStint_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string lines =
                    "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1,\"nick\":\"n1\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n" +
                    // Zone 0 captured by team 0 at t=30, then captured away by team 1 at the exact
                    // same instant - a real, zero-length stint for team 0 that every pre-T7,
                    // unwindowed table already kept.
                    "{\"e\":\"ownership\",\"t\":30,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":0,\"since\":1000}\n" +
                    "{\"e\":\"ownership\",\"t\":30,\"zone\":0,\"tier\":2,\"old\":0,\"new\":1,\"since\":1001}\n" +
                    "{\"e\":\"sample\",\"t\":60,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));
                var zeroLength = tables.Ownership.SingleOrDefault(o => o.Zone == 0 && o.Team == 0);
                Assert.IsNotNull(zeroLength, "the zero-length team-0 stint must still appear, not silently vanish");
                Assert.AreEqual(0.0, zeroLength.Duration, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- review fix (item 12): the boundary instant itself

        [Test]
        public void AnEventAtExactlyTheTransitionInstantLandsInPhase2OnlyNeverPhase1()
        {
            string temp = NewIsolatedTempFolder();
            Directory.CreateDirectory(temp);
            try
            {
                string lines =
                    "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1,\"nick\":\"n1\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n" +
                    "{\"e\":\"phase\",\"t\":90,\"num\":2,\"remain\":[0,1]}\n" +
                    // A hit logged at EXACTLY t=90 - the transition instant itself. Phase 1 is
                    // [0, 90) (half-open, excludes 90); Phase 2 is [90, end] (closed) - so this
                    // must land in Phase 2 only, never Phase 1, and never in both.
                    "{\"e\":\"hit\",\"t\":90,\"a\":1,\"at\":0,\"v\":1,\"vt\":1,\"w\":1,\"ab\":-1,\"src\":\"Projectile\",\"raw\":10,\"arm\":0,\"hpLost\":10,\"lethal\":false,\"d\":5,\"vul\":0,\"op\":false}\n" +
                    "{\"e\":\"sample\",\"t\":120,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var set = TelemetryAggregator.BuildSet(TelemetryLog.Load(temp));
                Assert.AreEqual(0, set.Phase1.Hits.Count, "the boundary instant belongs to Phase 2, not Phase 1");
                Assert.AreEqual(1, set.Phase2.Hits.Count);
                Assert.AreEqual(90.0, set.Phase2.Hits[0].T, 1e-9);
                Assert.AreEqual(2, set.Phase2.Hits[0].Phase);

                // The same hit, tagged Phase 2, on the whole-match build too.
                Assert.AreEqual(1, set.WholeMatch.Hits.Count);
                Assert.AreEqual(2, set.WholeMatch.Hits[0].Phase);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- log coverage: a missing actor seen only via `hit`

        [Test]
        public void LogCoverageListsATeamThreeActorSeenOnlyInHitLinesAsMissing()
        {
            var log = TelemetryLog.Load(FixturePath);
            var tables = TelemetryAggregator.Build(log); // whole match - log coverage doesn't depend on the window.

            var actor3 = tables.Header.LogCoverage.SingleOrDefault(r => r.Actor == 3);
            Assert.IsNotNull(actor3, "actor 3 (seen only as a `hit` attacker) must appear in log coverage");
            Assert.IsFalse(actor3.FilePresent, "actor 3 never wrote their own log file in this fixture");

            // Actors 1 and 2 DO have their own files.
            Assert.IsTrue(tables.Header.LogCoverage.Single(r => r.Actor == 1).FilePresent);
            Assert.IsTrue(tables.Header.LogCoverage.Single(r => r.Actor == 2).FilePresent);
        }

        // Review fix (item 10): actor 3 in the shared fixture is seen ONLY via a `hit` line (no
        // join/leave at all) - First/Last t must read null (the HTML/CSV render "-"), not 0/0.
        [Test]
        public void LogCoverageShowsNullFirstLastTForAnActorWithNoJoinOrLeaveEventAtAll()
        {
            var log = TelemetryLog.Load(FixturePath);
            var tables = TelemetryAggregator.Build(log);

            var actor3 = tables.Header.LogCoverage.Single(r => r.Actor == 3);
            Assert.IsNull(actor3.FirstT);
            Assert.IsNull(actor3.LastT);
            Assert.IsFalse(actor3.JoinedAndLeftBeforeLoggingStarted);
            Assert.AreEqual("", actor3.Nick, "no join line ever named actor 3 in this fixture");
        }

        private static string NewIsolatedTempFolder() =>
            Path.Combine(Path.GetTempPath(), "TelemetryLogCoverageTests_" + System.Guid.NewGuid().ToString("N"));

        [Test]
        public void LogCoverageUsesTheJoinLinesNickForAMissingActor()
        {
            string temp = NewIsolatedTempFolder();
            Directory.CreateDirectory(temp);
            try
            {
                string lines =
                    "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1,\"nick\":\"Editor\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n" +
                    "{\"e\":\"join\",\"t\":-1,\"a\":1,\"tm\":0}\n" +
                    // Actor 2 has no file of their own - only OTHER clients' join/hit lines mention them.
                    "{\"e\":\"join\",\"t\":-1,\"a\":2,\"tm\":1,\"nick\":\"Ghost\"}\n" +
                    "{\"e\":\"hit\",\"t\":10,\"a\":2,\"at\":1,\"v\":1,\"vt\":0,\"w\":1,\"ab\":-1,\"src\":\"Projectile\",\"raw\":10,\"arm\":0,\"hpLost\":10,\"lethal\":false,\"d\":5,\"vul\":0,\"op\":false}\n" +
                    "{\"e\":\"sample\",\"t\":30,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));
                var missing = tables.Header.LogCoverage.Single(r => r.Actor == 2);
                Assert.IsFalse(missing.FilePresent);
                Assert.AreEqual("Ghost", missing.Nick);
                Assert.AreEqual(-1.0, missing.FirstT.Value, 1e-9); // their own earliest join, t=-1
                Assert.IsNull(missing.LastT); // no leave line at all
                Assert.IsFalse(missing.JoinedAndLeftBeforeLoggingStarted);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void LogCoverageFlagsAnActorWhoJoinedAndLeftBeforeLoggingStarted()
        {
            string temp = NewIsolatedTempFolder();
            Directory.CreateDirectory(temp);
            try
            {
                string lines =
                    "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1,\"nick\":\"Editor\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n" +
                    "{\"e\":\"join\",\"t\":-1,\"a\":1,\"tm\":0}\n" +
                    "{\"e\":\"join\",\"t\":5,\"a\":2,\"tm\":1,\"nick\":\"Ghost\"}\n" +
                    "{\"e\":\"leave\",\"t\":8,\"a\":2,\"tm\":1}\n" +
                    "{\"e\":\"sample\",\"t\":30,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));
                var missing = tables.Header.LogCoverage.Single(r => r.Actor == 2);
                Assert.IsFalse(missing.FilePresent);
                Assert.AreEqual("Ghost", missing.Nick);
                Assert.AreEqual(5.0, missing.FirstT.Value, 1e-9);
                Assert.AreEqual(8.0, missing.LastT.Value, 1e-9);
                Assert.IsTrue(missing.JoinedAndLeftBeforeLoggingStarted, "joined at 5 and left at 8 - gone before their own file could ever open");
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
