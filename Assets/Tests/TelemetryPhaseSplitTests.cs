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
            Assert.AreEqual(wholeAlive, p1Alive + p2Alive, Tolerance, "alive time");
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
    }
}
