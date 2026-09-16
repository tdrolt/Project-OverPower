using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T5: aggregator totals against a hand-written fixture (Assets/Tests/TelemetryFixtures/match_a),
    /// two files/players over a 3-minute match, with every expected number computed by hand in the
    /// comment next to its assertion - see the fixture files themselves for the raw lines these
    /// numbers come from.</summary>
    public class TelemetryAggregatorTests
    {
        private static string FixturePath => Path.Combine(Application.dataPath, "Tests/TelemetryFixtures/match_a");

        private static ReportTables BuildFixtureTables() => TelemetryAggregator.Build(TelemetryLog.Load(FixturePath));

        // ---------------------------------------------------------------- header

        [Test]
        public void HeaderReportsMatchLengthPlayersPerTeamAndCounts()
        {
            var log = TelemetryLog.Load(FixturePath);
            var tables = TelemetryAggregator.Build(log);

            // Latest real (t >= 0) event in either file is the t=180 sample.
            Assert.AreEqual(180.0, tables.Header.MatchLengthSeconds, 1e-9);
            // Both sessions' tuning.territory.playersPerTeam = 1 (a 2-player, 1-per-team fixture).
            Assert.AreEqual(1, tables.Header.PlayersPerTeam);
            Assert.IsFalse(tables.Header.FreeLoadoutUsed);
            Assert.IsFalse(tables.Header.DebugGoldUsed); // every goldEarned line has debug = 0
            Assert.AreEqual(1, log.MalformedLineCount);   // the truncated `{"e":"sample","t":178,"bal` line
            Assert.AreEqual(1, log.UnknownEventCount);    // "thisEventNameDoesNotExist"
            Assert.AreEqual(1, tables.Header.UnknownCaptureStateCount); // zone 1's "mystery" capture state
            Assert.AreEqual(1, tables.Header.Markers.Count);
            Assert.AreEqual("check economy", tables.Header.Markers[0].Note);
            Assert.IsNull(log.OtherMatchId);      // both files share one match id - nothing else to report
            Assert.AreEqual(0, log.OtherMatchFileCount);
        }

        // ---------------------------------------------------------------- ownership (dedupe)

        [Test]
        public void DuplicatedOwnershipEventDedupesToOneStintPerZone()
        {
            var tables = BuildFixtureTables();
            Assert.AreEqual(2, tables.Ownership.Count); // zone 0 and zone 1 - not 3, despite the duplicate

            // zone 0: file 1 logs it at t=20, file 2 logs the SAME change (zone 0, new 0, since 1000)
            // at t=20.1 - the aggregator's dedupe key (zone, new owner, since) collapses these to one.
            var zone0 = tables.Ownership.Single(o => o.Zone == 0);
            Assert.AreEqual(2, zone0.Tier);
            Assert.AreEqual(0, zone0.Team);
            Assert.AreEqual(20.0, zone0.From, 1e-9);   // the earlier of the two timestamps wins
            Assert.AreEqual(180.0, zone0.To, 1e-9);    // never changed hands again - held to match end
            Assert.AreEqual(160.0, zone0.Duration, 1e-9);
            Assert.AreEqual("matchEnd", zone0.HowEnded);

            var zone1 = tables.Ownership.Single(o => o.Zone == 1);
            Assert.AreEqual(3, zone1.Tier);
            Assert.AreEqual(1, zone1.Team);
            Assert.AreEqual(40.0, zone1.From, 1e-9);
            Assert.AreEqual(140.0, zone1.Duration, 1e-9);
            Assert.AreEqual("matchEnd", zone1.HowEnded);
        }

        // ---------------------------------------------------------------- captures (ownership-driven closing)

        [Test]
        public void CaptureAttemptClosesOnTheOwnershipChangeNotOnTheCaptureLinesOwnState()
        {
            var tables = BuildFixtureTables();

            // Zone 1 only ever sees an unrecognised capture state ("mystery") - no attempt is ever
            // opened for it, so its ownership change (team 1, t=40) has nothing to close and produces
            // no captures.csv row.
            Assert.AreEqual(1, tables.Captures.Count);

            var capture = tables.Captures[0];
            Assert.AreEqual(0, capture.Zone);
            // T6 review fix: captures now carry the zone's tier (from the same ownership-derived
            // zoneTier map) - zone 0 is tier 2 (see zone0.Tier above).
            Assert.AreEqual(2, capture.Tier);
            Assert.AreEqual(0, capture.Team);
            Assert.AreEqual(15.0, capture.Start, 1e-9);
            // Closed by zone 0's ownership event at t=20 (new owner 0 matches the open attempt's team) -
            // the capture line's own "completed" state at t=20 is informational only per the T4-review
            // note (capture outcomes come only from `ownership`).
            Assert.AreEqual(20.0, capture.End, 1e-9);
            Assert.AreEqual(5.0, capture.Duration, 1e-9);
            Assert.AreEqual("completed", capture.Outcome);
            Assert.AreEqual(1, capture.Players);
        }

        // ---------------------------------------------------------------- weapons (hits, accuracy)

        [Test]
        public void WeaponHitsAndAccuracy()
        {
            var tables = BuildFixtureTables();
            Assert.AreEqual(13, tables.Weapons.Count); // "all 13" - every id from the tuning snapshot

            var w1 = tables.Weapons.Single(w => w.WeaponId == 1);
            Assert.AreEqual(3, w1.Pulls);
            Assert.AreEqual(3, w1.Projectiles);
            Assert.AreEqual(3, w1.Hits);                // 3 Projectile hits with w == 1
            Assert.AreEqual(0, w1.SplashHits);          // no Splash-source hits in this fixture
            Assert.AreEqual(1.0, w1.Accuracy, 1e-9);     // 3 hits / 3 projectiles
            Assert.AreEqual(60f, w1.DamageRaw, 1e-6);    // 3 x raw 20
            Assert.AreEqual(15f, w1.ArmorDamage, 1e-6);  // 3 x arm 5
            Assert.AreEqual(45f, w1.HealthDamage, 1e-6); // 3 x hpLost 15
            Assert.AreEqual(1, w1.Kills);                 // the one death has w == 1
            Assert.AreEqual(12.0, w1.MeanDistance.Value, 1e-6);   // (10+12+14)/3
            Assert.AreEqual(12.0, w1.MedianDistance.Value, 1e-6);
            // Equipped time (opus review items 3/11): player2 holds weapon 1 the WHOLE match (170s
            // across consecutive-sample intervals) + player1's own first interval (60s, before
            // switching to weapon 2 at t=65) = 230s. The fixture declares its own tuning
            // sampleIntervalSeconds=60 (matching its actual ~60s sample spacing), so neither the
            // trailing-interval addition nor the 2x-interval gap cap change this from the pre-review
            // number - see the fixture's own tuning line.
            Assert.AreEqual(230.0, w1.TimeEquippedSeconds, 1e-6);
            Assert.AreEqual(60f * 60.0 / 230.0, w1.DamagePerEquippedMinute, 1e-2); // 60 raw / (230s/60) ~= 15.65/min

            var w2 = tables.Weapons.Single(w => w.WeaponId == 2);
            Assert.AreEqual(3, w2.Hits);
            Assert.AreEqual(0, w2.SplashHits);
            Assert.AreEqual(90f, w2.DamageRaw, 1e-6);    // 3 x raw 30
            Assert.AreEqual(0, w2.Kills);
            Assert.AreEqual(22.0, w2.MeanDistance.Value, 1e-6); // (20+22+24)/3
            Assert.AreEqual(115.0, w2.TimeEquippedSeconds, 1e-6); // player1 only: 60+55
            Assert.AreEqual(90f * 60.0 / 115.0, w2.DamagePerEquippedMinute, 1e-2); // 90 raw / (115s/60) ~= 46.96/min

            var w3 = tables.Weapons.Single(w => w.WeaponId == 3); // never fired at all
            Assert.AreEqual(0, w3.Hits);
            Assert.AreEqual(0, w3.SplashHits);
            Assert.AreEqual(0f, w3.DamageRaw);
            Assert.IsNull(w3.MeanDistance);
            Assert.AreEqual(0.0, w3.Accuracy);
        }

        // ---------------------------------------------------------------- abilities (dot damage folded in)

        [Test]
        public void AbilityDamageIncludesTheDotLine()
        {
            var tables = BuildFixtureTables();
            Assert.AreEqual(1, tables.Abilities.Count); // only ability 27 ever appears; ab == -1 means "no ability"

            var ability = tables.Abilities[0];
            Assert.AreEqual(27, ability.AbilityId);
            Assert.AreEqual(1, ability.Casts);
            Assert.AreEqual(15f, ability.DamageRaw, 1e-6); // the dot's raw sum - no `hit` line uses ab 27
            Assert.AreEqual(0, ability.Kills);              // the death's own ab is -1, not 27
            Assert.AreEqual(1, ability.StatusCount);
            Assert.AreEqual(5.0, ability.StatusSeconds, 1e-6);
        }

        // ---------------------------------------------------------------- players (symmetry + zone time)

        [Test]
        public void PlayerDamageDealtEqualsTheOtherPlayersDamageTaken()
        {
            var tables = BuildFixtureTables();
            Assert.AreEqual(2, tables.Players.Count);

            var p1 = tables.Players.Single(p => p.Actor == 1);
            var p2 = tables.Players.Single(p => p.Actor == 2);

            // Opus review item 2: p2's OWN session line carries tm:-1 (a late joiner, before the
            // room's player-properties echo arrived) - the effective team instead comes from p2's own
            // first `sample` (tm:1), so team-keyed totals below (goldEarned, zone income, gold gap)
            // aren't silently dropped for this player. Confirmed here directly on the player row.
            Assert.AreEqual(1, p2.Team);

            // p1 dealt: the 3 weapon-1 hits he lands on p2 (raw 20 x3 = 60) plus the dot he applies (raw 15) = 75.
            Assert.AreEqual(75f, p1.DamageDealt, 1e-6);
            // p2 dealt: the 3 weapon-2 hits he lands on p1 (raw 30 x3 = 90).
            Assert.AreEqual(90f, p2.DamageDealt, 1e-6);
            // p1 took: the 3 weapon-2 hits landed on him (90).
            Assert.AreEqual(90f, p1.DamageTaken, 1e-6);
            // p2 took: the 3 weapon-1 hits (60) + the dot (15) = 75.
            Assert.AreEqual(75f, p2.DamageTaken, 1e-6);
            // The symmetry the task asks for: one player's dealt equals the other's taken.
            Assert.AreEqual(p1.DamageDealt, p2.DamageTaken, 1e-6);
            Assert.AreEqual(p2.DamageDealt, p1.DamageTaken, 1e-6);

            Assert.AreEqual(1, p1.Kills);
            Assert.AreEqual(0, p1.Deaths);
            Assert.AreEqual(0, p2.Kills);
            Assert.AreEqual(1, p2.Deaths);
            // The death's assist credits actor 99, who has no session line - nobody's row gets it.
            Assert.AreEqual(0, p1.Assists);
            Assert.AreEqual(0, p2.Assists);

            Assert.AreEqual(50, p1.GoldTerritory); // 25 + 25 across p1's two goldEarned lines
            Assert.AreEqual(400, p1.GoldRefund);
            Assert.AreEqual(1200, p1.GoldSpent);

            Assert.AreEqual(8, p2.GoldTerritory);  // 0 + 8 (the all-zero junk line at t=178 is in p1's file, adds nothing anyway)
            Assert.AreEqual(20f, p2.Healing, 1e-6); // the one heal line's tiers sum (0+0+20+0+0)

            // Never dies - alive for p1's own covered range: session's own t (0.02, the first real
            // event in file 1) to the last sample (180) - opus review item 3, bounded by the PLAYER'S
            // OWN coverage rather than the whole match length.
            Assert.AreEqual(179.98, p1.TimeAlive, 1e-6);
            // p2: first life 0 -> 60.05 (the death's own timeAlive) + the tail after respawn, 65.05 -> 180.
            Assert.AreEqual(60.05 + (180.0 - 65.05), p2.TimeAlive, 1e-6);

            // Zone-standing time from consecutive sample intervals (each interval attributed to the
            // zone/owner read at the START of that interval):
            //   p1: [5,65) zone 0, still neutral (captured at t=20 but classification uses START) -> neutral
            //       [65,125) zone 0, now team 0's own -> own
            //       [125,180) zone 1, owned by team 1 -> enemy
            Assert.AreEqual(60.0, p1.TimeNeutralZone, 1e-6);
            Assert.AreEqual(60.0, p1.TimeOwnZone, 1e-6);
            Assert.AreEqual(55.0, p1.TimeEnemyZone, 1e-6);
            //   p2: [10,70) zone -1 (no zone) -> neutral; [70,130) & [130,180) zone 1, team 1's own -> own
            Assert.AreEqual(60.0, p2.TimeNeutralZone, 1e-6);
            Assert.AreEqual(110.0, p2.TimeOwnZone, 1e-6);
            Assert.AreEqual(0.0, p2.TimeEnemyZone, 1e-6);
        }

        // ---------------------------------------------------------------- zone income

        [Test]
        public void ZoneIncomeSumsGoldEarnedZoneArraysByTeam()
        {
            var tables = BuildFixtureTables();
            Assert.AreEqual(2, tables.ZoneIncome.Count);

            var z0 = tables.ZoneIncome.Single(z => z.Zone == 0 && z.Team == 0);
            Assert.AreEqual(2, z0.Tier);
            Assert.AreEqual(160.0, z0.SecondsHeld, 1e-9);
            Assert.AreEqual(50, z0.GoldGenerated); // p1's zones[0]: 25 + 25

            var z1 = tables.ZoneIncome.Single(z => z.Zone == 1 && z.Team == 1);
            Assert.AreEqual(3, z1.Tier);
            Assert.AreEqual(140.0, z1.SecondsHeld, 1e-9);
            Assert.AreEqual(8, z1.GoldGenerated); // p2's zones[1]: 0 + 8
        }

        // ---------------------------------------------------------------- economy by minute

        [Test]
        public void EconomyByMinuteBucketsIncomeZonesHeldAndGoldGap()
        {
            var tables = BuildFixtureTables();
            Assert.AreEqual(6, tables.EconomyByMinute.Count); // 3 minutes (0,1,2) x 2 teams

            var t0team0 = tables.EconomyByMinute.Single(r => r.Minute == 0 && r.Team == 0);
            Assert.AreEqual(25, t0team0.IncomeByTier[1]);   // tier 2 (index 1): zone 0's 25 at t=25
            Assert.AreEqual(1200, t0team0.Spent);            // p1's t=25 purchase
            Assert.AreEqual(1, t0team0.ZonesHeldByTier[1]);  // zone 0 (tier 2), captured by t=20, owned at the t=30 midpoint
            Assert.AreEqual(0, t0team0.GoldGapToRichest);    // team 0's own last sample (<=60: t=5, bal 1500) is the richest

            var t0team1 = tables.EconomyByMinute.Single(r => r.Minute == 0 && r.Team == 1);
            Assert.AreEqual(700, t0team1.GoldGapToRichest);  // 1500 - 800 (team 1's last sample <=60: t=10)

            var t1team0 = tables.EconomyByMinute.Single(r => r.Minute == 1 && r.Team == 0);
            Assert.AreEqual(25, t1team0.IncomeByTier[1]);    // p1's t=100 goldEarned zones[0]
            Assert.AreEqual(400, t1team0.Refund);
            Assert.AreEqual(275, t1team0.GoldGapToRichest);  // richest (600, team1 last<=120 @ t=70) - team0's 325 (@ t=65)

            var t1team1 = tables.EconomyByMinute.Single(r => r.Minute == 1 && r.Team == 1);
            Assert.AreEqual(1, t1team1.ZonesHeldByTier[2]);  // zone 1 (tier 3), owned by team 1 at the t=90 midpoint
            Assert.AreEqual(0, t1team1.GoldGapToRichest);

            var t2team0 = tables.EconomyByMinute.Single(r => r.Minute == 2 && r.Team == 0);
            Assert.AreEqual(0, t2team0.GoldGapToRichest);    // richest (1350, team0's own last sample @ t=180)

            var t2team1 = tables.EconomyByMinute.Single(r => r.Minute == 2 && r.Team == 1);
            Assert.AreEqual(150, t2team1.GoldGapToRichest);  // 1350 - 1200
        }

        // ---------------------------------------------------------------- straight-row tables

        [Test]
        public void PurchasesShopBlockedHitsAndDeathsRowCounts()
        {
            var tables = BuildFixtureTables();

            Assert.AreEqual(2, tables.Purchases.Count); // 1 purchase + 1 refund
            Assert.IsTrue(tables.Purchases.Any(p => p.Kind == "purchase" && p.Category == "weapon" && p.Amount == 1200));
            Assert.IsTrue(tables.Purchases.Any(p => p.Kind == "refund" && p.Category == "equipment" && p.Amount == 400));

            Assert.AreEqual(1, tables.ShopBlocked.Count);
            Assert.AreEqual("gold", tables.ShopBlocked[0].Reason);
            Assert.AreEqual(200, tables.ShopBlocked[0].Shortfall);

            Assert.AreEqual(6, tables.Hits.Count); // 3 + 3, dot excluded (dot has no row of its own in hits.csv)

            Assert.AreEqual(1, tables.Deaths.Count);
            var death = tables.Deaths[0];
            Assert.AreEqual(2, death.Victim);   // file 2 owns this Died event
            Assert.AreEqual(1, death.Killer);
            Assert.AreEqual(0, death.KillerTeam);
            Assert.AreEqual(1, death.Weapon);
            CollectionAssert.AreEqual(new[] { 99 }, death.Assists);
            Assert.AreEqual(250, death.UnspentGold);
            Assert.AreEqual(1, death.LoadoutWeapon);
            Assert.AreEqual(15, death.LoadoutMobility);
        }

        // ---------------------------------------------------------------- gold timeline (opus review item 12)

        [Test]
        public void GoldTimelineRows()
        {
            var tables = BuildFixtureTables();
            // 4 samples for p1 (t=5,65,125,180) + 4 samples for p2 (t=10,70,130,180) = 8 rows.
            Assert.AreEqual(8, tables.GoldTimeline.Count);

            // p1 @ t=65: only the t=25 goldEarned (terr 25) and t=25 purchase (price 1200) have
            // happened by then - the t=100 goldEarned/refund haven't yet.
            var p1At65 = tables.GoldTimeline.Single(r => r.Actor == 1 && Math.Abs(r.T - 65.0) < 1e-9);
            Assert.AreEqual(0, p1At65.Team);
            Assert.AreEqual(325, p1At65.Balance);
            Assert.AreEqual(25, p1At65.EarnedSoFar);
            Assert.AreEqual(1200, p1At65.SpentSoFar);

            // p1 @ t=180 (last sample): both goldEarned lines have landed (t=25: terr 25 + refund 0;
            // t=100: terr 25 + refund 400 - EarnedSoFar sums EVERY source, not just territory) plus
            // the one purchase; the t=178 junk zero line adds nothing.
            var p1At180 = tables.GoldTimeline.Single(r => r.Actor == 1 && Math.Abs(r.T - 180.0) < 1e-9);
            Assert.AreEqual(1350, p1At180.Balance);
            Assert.AreEqual(25 + 25 + 400, p1At180.EarnedSoFar);
            Assert.AreEqual(1200, p1At180.SpentSoFar);

            // p2 @ t=10 (first sample): nothing has happened yet.
            var p2At10 = tables.GoldTimeline.Single(r => r.Actor == 2 && Math.Abs(r.T - 10.0) < 1e-9);
            Assert.AreEqual(800, p2At10.Balance);
            Assert.AreEqual(0, p2At10.EarnedSoFar);
            Assert.AreEqual(0, p2At10.SpentSoFar);
            Assert.AreEqual(1, p2At10.Team); // effective team (fallback from tm:-1 session), not raw sample tm
        }

        // ---------------------------------------------------------------- multi-match-id merge key

        [Test]
        public void MostFilesWinsAndTheOtherMatchIdIsReportedNotMerged()
        {
            string temp = Path.Combine(Path.GetTempPath(), "TelemetryLogTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                // Match X: 2 files. Match Y: 1 file. X must win (most files); Y is reported, not merged.
                File.WriteAllText(Path.Combine(temp, "1_X.jsonl"),
                    "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"X\",\"a\":1,\"nick\":\"A\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n" +
                    "{\"e\":\"marker\",\"t\":1,\"a\":1,\"note\":\"from-x\"}\n");
                File.WriteAllText(Path.Combine(temp, "2_X.jsonl"),
                    "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"X\",\"a\":2,\"nick\":\"B\",\"tm\":1,\"master\":false,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n");
                File.WriteAllText(Path.Combine(temp, "3_Y.jsonl"),
                    "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"Y\",\"a\":3,\"nick\":\"C\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n" +
                    "{\"e\":\"marker\",\"t\":1,\"a\":3,\"note\":\"from-y\"}\n");

                var log = TelemetryLog.Load(temp);

                Assert.AreEqual("X", log.MatchId);
                Assert.AreEqual("Y", log.OtherMatchId);
                Assert.AreEqual(1, log.OtherMatchFileCount);
                Assert.AreEqual(2, log.Sessions.Count); // only X's two sessions are built

                var tables = TelemetryAggregator.Build(log);
                Assert.IsFalse(tables.Header.Markers.Any(m => m.Note == "from-y")); // Y's own line never merged in
                Assert.IsTrue(tables.Header.Markers.Any(m => m.Note == "from-x"));
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
