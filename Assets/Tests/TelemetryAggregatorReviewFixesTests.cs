using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Task T5, opus review (second pass): one focused fixture per fix, each built from a
    /// small hand-written temp-directory log (not the shared match_a fixture - see
    /// TelemetryAggregatorTests for that one), so a change to one scenario can't perturb another's
    /// numbers. See TelemetryAggregator's own class comment for the numbered list these correspond to.</summary>
    public class TelemetryAggregatorReviewFixesTests
    {
        private static string NewTempFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "TelemetryReviewFixTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return temp;
        }

        private static string Session(int actor, int team, bool master, string tuningJson) =>
            "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":" + actor + ",\"nick\":\"n" + actor +
            "\",\"tm\":" + team + ",\"master\":" + (master ? "true" : "false") +
            ",\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":" + tuningJson + "}\n";

        // ---------------------------------------------------------------- lobby Task 6 review: a spectator host's log

        [Test]
        public void ASpectatorHostsLogGivesTheTimelineButNoPlayerRow()
        {
            string temp = NewTempFolder();
            try
            {
                string specSession = Session(1, -1, true, "{}").Replace("\"master\":true", "\"master\":true,\"spec\":true");
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), specSession +
                    "{\"e\":\"ownership\",\"t\":30,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":0,\"since\":1000}\n");
                File.WriteAllText(Path.Combine(temp, "2.jsonl"), Session(2, 0, false, "{}"));

                var log = TelemetryLog.Load(temp);
                var tables = TelemetryAggregator.Build(log);

                Assert.IsTrue(log.Sessions.Single(s => s.Actor == 1).Spectator);
                Assert.IsFalse(log.Sessions.Single(s => s.Actor == 2).Spectator);
                Assert.AreEqual(1, tables.Ownership.Count, "the host's territory line still reaches the report");
                CollectionAssert.AreEqual(new[] { 2 }, tables.Players.Select(p => p.Actor).ToArray(), "only the player is a player row");
            }
            finally { Directory.Delete(temp, true); }
        }

        // ---------------------------------------------------------------- lobby Task 13 (Task 8 review): spectators in the report

        private static string SpectatorHostSession(int actor) =>
            Session(actor, -1, true, "{}").Replace("\"master\":true", "\"master\":true,\"spec\":true");

        private const string JoinOfThree = "{\"e\":\"join\",\"t\":1,\"a\":3,\"tm\":-1,\"nick\":\"Sam\"}\n";

        [Test]
        public void ANonHostSpectatorHasNoRowInTheLogCoverageTable()
        {
            string temp = NewTempFolder();
            try
            {
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), Session(1, 0, true, "{}") + JoinOfThree +
                    "{\"e\":\"marker\",\"t\":2,\"a\":1,\"note\":\"" + Overpower.Telemetry.LobbyMarkerNotes.SpectatorSeen(3) + "\"}\n");
                File.WriteAllText(Path.Combine(temp, "2.jsonl"), Session(2, 1, false, "{}"));

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                CollectionAssert.AreEqual(new[] { 1, 2 }, tables.Header.LogCoverage.Select(r => r.Actor).ToArray(), "the spectator (actor 3, no file) is not 'a player with no log'");
            }
            finally { Directory.Delete(temp, true); }
        }

        [Test]
        public void ASeenPlayerWithNoFileIsStillFlaggedWhenNoSpectatorNoteNamesThem()
        {
            string temp = NewTempFolder();
            try
            {
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), Session(1, 0, true, "{}") + JoinOfThree);
                File.WriteAllText(Path.Combine(temp, "2.jsonl"), Session(2, 1, false, "{}"));

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                var missing = tables.Header.LogCoverage.Single(r => r.Actor == 3);
                Assert.IsFalse(missing.FilePresent, "a player whose file never arrived still reads 'no log from actor 3'");
            }
            finally { Directory.Delete(temp, true); }
        }

        [Test]
        public void ASpectatorHostHasNoRowInTheLogCoverageTableEither()
        {
            string temp = NewTempFolder();
            try
            {
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), SpectatorHostSession(1));
                File.WriteAllText(Path.Combine(temp, "2.jsonl"), Session(2, 0, false, "{}"));

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                CollectionAssert.AreEqual(new[] { 2 }, tables.Header.LogCoverage.Select(r => r.Actor).ToArray());
            }
            finally { Directory.Delete(temp, true); }
        }

        [Test]
        public void ASpectatorNoteIsBookkeepingNotAMarkerRow()
        {
            string temp = NewTempFolder();
            try
            {
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), Session(1, 0, true, "{}") +
                    "{\"e\":\"marker\",\"t\":2,\"a\":1,\"note\":\"" + Overpower.Telemetry.LobbyMarkerNotes.SpectatorSeen(3) + "\"}\n" +
                    "{\"e\":\"marker\",\"t\":3,\"a\":1,\"note\":\"countdown start\"}\n");

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp), null, includeWarmupMarkers: true);

                CollectionAssert.AreEqual(new[] { "countdown start" }, tables.Header.Markers.Select(m => m.Note).ToArray());
            }
            finally { Directory.Delete(temp, true); }
        }

        [Test]
        public void ASpectatorHostsCaptureCountsInEveryScopeOfTheReport()
        {
            string temp = NewTempFolder();
            try
            {
                // The host is a spectator (team -1) and the only one logging; a team takes zone 4 in phase one. Nothing keys the table by the
                // writer's own team.
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), SpectatorHostSession(1) +
                    "{\"e\":\"phase\",\"t\":5,\"num\":1,\"remain\":[0,1,2]}\n" +
                    "{\"e\":\"capture\",\"t\":10,\"zone\":4,\"tm\":2,\"state\":\"started\",\"progress\":0.01,\"players\":1}\n" +
                    "{\"e\":\"ownership\",\"t\":30,\"zone\":4,\"tier\":1,\"old\":-1,\"new\":2,\"since\":1000}\n" +
                    "{\"e\":\"phase\",\"t\":90,\"num\":3,\"remain\":[2]}\n");
                File.WriteAllText(Path.Combine(temp, "2.jsonl"), Session(2, 2, false, "{}"));

                var set = TelemetryAggregator.BuildSet(TelemetryLog.Load(temp));

                Assert.AreEqual(1, set.WholeMatch.Ownership.Count, "whole match");
                Assert.AreEqual(2, set.WholeMatch.Ownership[0].Team);
                Assert.AreEqual(1, set.Phase1.Ownership.Count, "phase one");
                Assert.AreEqual(1, set.WholeMatch.Captures.Count);
            }
            finally { Directory.Delete(temp, true); }
        }

        // ---------------------------------------------------------------- item 1 (HIGH): captures rewrite

        [Test]
        public void CapturesHandleMultipleCycles_TeamACapturesThenTeamBDrainsThenTeamBCaptures()
        {
            string temp = NewTempFolder();
            try
            {
                string lines =
                    Session(1, 0, true, "{}") +
                    // Team A (0) captures zone 0: started 10 -> owns it at 30.
                    "{\"e\":\"capture\",\"t\":10,\"zone\":0,\"tm\":0,\"state\":\"started\",\"progress\":0.01,\"players\":1}\n" +
                    "{\"e\":\"ownership\",\"t\":30,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":0,\"since\":1000}\n" +
                    // Team B (1) drains it: drainStarted, reaching neutral at 220.
                    "{\"e\":\"capture\",\"t\":150,\"zone\":0,\"tm\":1,\"state\":\"drainStarted\",\"progress\":-0.01,\"players\":1}\n" +
                    "{\"e\":\"ownership\",\"t\":220,\"zone\":0,\"tier\":2,\"old\":0,\"new\":-1,\"since\":2000}\n" +
                    // Team B captures the now-neutral zone fresh: started 225 -> owns it at 245.
                    "{\"e\":\"capture\",\"t\":225,\"zone\":0,\"tm\":1,\"state\":\"started\",\"progress\":0.01,\"players\":1}\n" +
                    "{\"e\":\"ownership\",\"t\":245,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":1,\"since\":3000}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                Assert.AreEqual(3, tables.Captures.Count);

                var row1 = tables.Captures.Single(c => Math.Abs(c.Start - 10.0) < 1e-9);
                Assert.AreEqual(0, row1.Team);
                Assert.AreEqual(30.0, row1.End, 1e-9);
                Assert.AreEqual("completed", row1.Outcome);
                Assert.AreEqual(20.0, row1.Duration, 1e-9);

                var row2 = tables.Captures.Single(c => Math.Abs(c.Start - 150.0) < 1e-9);
                Assert.AreEqual(1, row2.Team);
                Assert.AreEqual(220.0, row2.End, 1e-9);
                Assert.AreEqual("neutralised", row2.Outcome);

                var row3 = tables.Captures.Single(c => Math.Abs(c.Start - 225.0) < 1e-9);
                Assert.AreEqual(1, row3.Team);
                Assert.AreEqual(245.0, row3.End, 1e-9);
                Assert.AreEqual("completed", row3.Outcome);
                Assert.AreEqual(20.0, row3.Duration, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 8 (T5 re-review): a same-timestamp tie between a post-T4 "paused@1" capture line and the closing ownership line

        [Test]
        public void APausedAtFullProgressCaptureLineAtTheSameTimestampAsOwnershipStillClosesExactlyOnce()
        {
            string temp = NewTempFolder();
            try
            {
                // Unlike the recapture test above (row3), whose closing ownership line has no
                // matching capture-state line at all at t=245, this one DOES: a post-T4 "paused"
                // state at progress 1.0 (T4's own stateless design - "paused" reads the same whether
                // a capture merely stopped or actually finished) landing at the EXACT same t as the
                // ownership change that actually closes it. Ownership items are appended to the
                // merged, stably-sorted list before capture-state items (see BuildCaptures' own
                // comment), so the tie must resolve to ownership closing the attempt first, and the
                // redundant same-t "paused" line finding nothing left open for that zone.
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"capture\",\"t\":10,\"zone\":0,\"tm\":0,\"state\":\"started\",\"progress\":0.01,\"players\":1}\n" +
                    "{\"e\":\"capture\",\"t\":30,\"zone\":0,\"tm\":0,\"state\":\"paused\",\"progress\":1.0,\"players\":1}\n" +
                    "{\"e\":\"ownership\",\"t\":30,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":0,\"since\":1000}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                Assert.AreEqual(1, tables.Captures.Count); // exactly once, not skipped and not duplicated
                var capture = tables.Captures.Single();
                Assert.AreEqual(0, capture.Team);
                Assert.AreEqual(10.0, capture.Start, 1e-9);
                Assert.AreEqual(30.0, capture.End, 1e-9);
                Assert.AreEqual(20.0, capture.Duration, 1e-9);
                Assert.AreEqual("completed", capture.Outcome);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 3: dead samples excluded

        [Test]
        public void DeadSamplesDoNotAccrueEquippedOrZoneTime()
        {
            string temp = NewTempFolder();
            try
            {
                string tuning = "{\"telemetry\":{\"sampleIntervalSeconds\":10},\"weapons\":[{\"id\":1}]}";
                string lines = Session(1, 0, true, tuning) +
                    "{\"e\":\"sample\",\"t\":0,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    // Died sometime during [0,10) - this sample is DEAD, so [10,20) must not count.
                    "{\"e\":\"sample\",\"t\":10,\"bal\":0,\"x\":0,\"z\":0,\"alive\":false,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":0,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    "{\"e\":\"sample\",\"t\":20,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    "{\"e\":\"sample\",\"t\":30,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                var w1 = tables.Weapons.Single(w => w.WeaponId == 1);
                // [0,10) alive -> 10s; [10,20) starts DEAD -> skipped; [20,30) alive -> 10s.
                // Trailing interval after t=30 (coverage ends at 30 too) adds 0.
                Assert.AreEqual(20.0, w1.TimeEquippedSeconds, 1e-6);

                var p1 = tables.Players.Single(p => p.Actor == 1);
                Assert.AreEqual(20.0, p1.TimeNeutralZone, 1e-6); // same intervals, zone -1 throughout
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 11 (T5 re-review): a non-zero trailing interval

        [Test]
        public void TheFinalSamplesOwnTrailingIntervalCountsWhenCoverageExtendsPastIt()
        {
            string temp = NewTempFolder();
            try
            {
                // The item-3 test above always ends coverage exactly ON the last sample, so its own
                // trailing interval is 0 (see its comment) - never exercising the non-zero case.
                string tuning = "{\"telemetry\":{\"sampleIntervalSeconds\":10},\"weapons\":[{\"id\":1}]}";
                string lines = Session(1, 0, true, tuning) +
                    "{\"e\":\"sample\",\"t\":0,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    "{\"e\":\"sample\",\"t\":20,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    // A later event (a marker) extends this player's own covered range 5s past their
                    // last sample - the trailing interval [20,25) must count too (capped at the 10s
                    // sample interval, but 5 < 10 so nothing is actually capped away here).
                    "{\"e\":\"marker\",\"t\":25,\"a\":1,\"note\":\"end\"}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                var w1 = tables.Weapons.Single(w => w.WeaponId == 1);
                Assert.AreEqual(25.0, w1.TimeEquippedSeconds, 1e-6); // [0,20) 20s + trailing [20,25) 5s
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 11 (T5 re-review): a gap over 2x the sample interval is capped

        [Test]
        public void AGapBetweenSamplesOverTwiceTheIntervalIsCapped()
        {
            string temp = NewTempFolder();
            try
            {
                string tuning = "{\"telemetry\":{\"sampleIntervalSeconds\":10},\"weapons\":[{\"id\":1}]}";
                string lines = Session(1, 0, true, tuning) +
                    "{\"e\":\"sample\",\"t\":0,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    // A 50s gap to the next sample (e.g. a disconnect) - far more than 2x the 10s
                    // interval (a 20s cap) - must not accrue the full 50s as equipped time.
                    "{\"e\":\"sample\",\"t\":50,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                var w1 = tables.Weapons.Single(w => w.WeaponId == 1);
                // [0,50) capped to 20s (2x the 10s interval); coverage ends exactly at the last
                // sample (t=50), so the trailing interval adds 0.
                Assert.AreEqual(20.0, w1.TimeEquippedSeconds, 1e-6);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 7 (T5 re-review): hits/deaths late-joiner team fallback

        [Test]
        public void HitAndDeathRowsFallBackToEffectiveTeamWhenTheRawTeamFieldIsUnresolved()
        {
            string temp = NewTempFolder();
            try
            {
                // Actor 1 is the victim (file owner of the death line - "victim has no field of its
                // own on a death line"). Actor 2 is a late joiner: its session's own team is still
                // unresolved (-1) at join time, so the hit/death lines logged before its first real
                // sample echo carry the killer's raw team as -1 too - exactly the gap effectiveTeam
                // (first sample with tm >= 0) already exists to paper over everywhere else.
                string victimFile = Session(1, 0, true, "{}") +
                    "{\"e\":\"death\",\"t\":20,\"a\":2,\"at\":-1,\"w\":1,\"ab\":-1,\"assists\":[],\"x\":0,\"z\":0,\"timeAlive\":20,\"gold\":0,\"lw\":1,\"leq\":-1,\"lmob\":-1,\"lult\":-1,\"abl\":0,\"rcl\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), victimFile);

                string killerFile = Session(2, -1, false, "{}") +
                    "{\"e\":\"hit\",\"t\":10,\"a\":2,\"at\":-1,\"v\":1,\"vt\":0,\"w\":1,\"ab\":-1,\"src\":\"Projectile\",\"raw\":20,\"arm\":5,\"hpLost\":15,\"lethal\":false,\"d\":10,\"vul\":0,\"op\":false}\n" +
                    "{\"e\":\"sample\",\"t\":15,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":1,\"w\":-1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n";
                File.WriteAllText(Path.Combine(temp, "2.jsonl"), killerFile);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                var hit = tables.Hits.Single();
                Assert.AreEqual(1, hit.AttackerTeam); // resolved via actor 2's effective team, not the raw -1
                Assert.AreEqual(0, hit.VictimTeam); // victim's raw vt:0 was already valid - unaffected

                var death = tables.Deaths.Single();
                Assert.AreEqual(1, death.KillerTeam); // same fallback applied to the killer's team on a death line
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- opus review item 10 (first pass): a null inside assists is skipped, not thrown

        [Test]
        public void ANullInsideAssistsIsSkippedNotThrown()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"death\",\"t\":10,\"a\":2,\"at\":1,\"w\":1,\"ab\":-1,\"assists\":[3,null,4],\"x\":0,\"z\":0,\"timeAlive\":10,\"gold\":0,\"lw\":1,\"leq\":-1,\"lmob\":-1,\"lult\":-1,\"abl\":0,\"rcl\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                ReportTables tables = null;
                Assert.DoesNotThrow(() => tables = TelemetryAggregator.Build(TelemetryLog.Load(temp)));

                var death = tables.Deaths.Single();
                CollectionAssert.AreEqual(new[] { 3, 4 }, death.Assists); // the null in the middle is dropped, not thrown on
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 5: tier bounds don't crash

        [Test]
        public void InvalidTierValuesAreSkippedNotCrashed()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"ownership\",\"t\":5,\"zone\":0,\"tier\":0,\"old\":-1,\"new\":0,\"since\":1}\n" + // tier 0: out of range
                    "{\"e\":\"ownership\",\"t\":5,\"zone\":1,\"tier\":5,\"old\":-1,\"new\":0,\"since\":2}\n" + // tier 5: out of range
                    "{\"e\":\"sample\",\"t\":10,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":0,\"tm\":0,\"w\":-1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    "{\"e\":\"goldEarned\",\"t\":10,\"terr\":10,\"zones\":[10,0],\"bounty\":0,\"refund\":0,\"debug\":0,\"other\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                ReportTables tables = null;
                Assert.DoesNotThrow(() => tables = TelemetryAggregator.Build(TelemetryLog.Load(temp)));

                // 2 invalid zones (the "zones held" sweep counts each distinct bad zone once - T5
                // re-review item 10 - which happens to equal "2 zones x 1 minute" here since this
                // fixture is only 1 minute long; see the dedicated multi-minute test below for the
                // actual regression) + 2 invalid zones in the one goldEarned line's own zones[] (the
                // income accumulation) = 4.
                Assert.AreEqual(4, tables.Header.InvalidTierCount);
                Assert.AreEqual(1, tables.EconomyByMinute.Count); // still builds a normal row otherwise
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 10 (T5 re-review): invalid tier count is per zone, not per zone-minute

        [Test]
        public void InvalidTierCountIsPerDistinctZoneNotPerMinuteSwept()
        {
            string temp = NewTempFolder();
            try
            {
                // A match long enough to sweep 3 minutes (0, 1, 2 - matchLength 125s), with one zone
                // whose tier is out of range (0). Before the fix, the "zones held" sweep re-counted
                // this same zone once per minute it swept (3); the fix counts each distinct bad zone
                // exactly once regardless of how many minutes the sweep covers.
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"ownership\",\"t\":5,\"zone\":9,\"tier\":0,\"old\":-1,\"new\":0,\"since\":1}\n" +
                    "{\"e\":\"sample\",\"t\":125,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":-1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                Assert.AreEqual(3, tables.EconomyByMinute.Select(r => r.Minute).Distinct().Count()); // the sweep did cover 3 minutes
                Assert.AreEqual(1, tables.Header.InvalidTierCount); // one bad zone, not one per minute it was swept in
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 4: zone income rescaling

        [Test]
        public void GoldEarnedZonesAreRescaledToSumToTerr()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"ownership\",\"t\":2,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":0,\"since\":1}\n" +
                    "{\"e\":\"sample\",\"t\":1,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":0,\"tm\":0,\"w\":-1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    // zones[0]=9 but terr=10 - a real rounding mismatch (each zone rounded to whole gold at the source).
                    "{\"e\":\"goldEarned\",\"t\":5,\"terr\":10,\"zones\":[9,0],\"bounty\":0,\"refund\":0,\"debug\":0,\"other\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                var zoneRow = tables.ZoneIncome.Single(z => z.Zone == 0 && z.Team == 0);
                // Rescaled: 9 * (10/9) = 10, not the raw unscaled 9.
                Assert.AreEqual(10.0, zoneRow.GoldGenerated, 1e-6);

                var econRow = tables.EconomyByMinute.Single(r => r.Minute == 0 && r.Team == 0);
                Assert.AreEqual(10.0, econRow.IncomeByTier[1], 1e-6); // tier 2 -> index 1, same rescale applied
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 9 (T5 re-review): terr > 0 with no zones[] to rescale by

        [Test]
        public void TerritoryGoldWithNoZonesBreakdownLandsInAnUnattributedBucketInsteadOfVanishing()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"ownership\",\"t\":2,\"zone\":0,\"tier\":2,\"old\":-1,\"new\":0,\"since\":1}\n" +
                    "{\"e\":\"sample\",\"t\":1,\"bal\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":0,\"tm\":0,\"w\":-1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    // terr:10 but zones is empty - ScaledZones has no ratio to rescale an empty array
                    // by, so this used to just vanish from zone_income.csv/economy_by_minute.csv.
                    "{\"e\":\"goldEarned\",\"t\":5,\"terr\":10,\"zones\":[],\"bounty\":0,\"refund\":0,\"debug\":0,\"other\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                // Not attributed to zone 0 (or any real zone) - IncomeByTier stays at 0 for every tier.
                var econRow = tables.EconomyByMinute.Single(r => r.Minute == 0 && r.Team == 0);
                Assert.AreEqual(0.0, econRow.IncomeByTier[0] + econRow.IncomeByTier[1] + econRow.IncomeByTier[2] + econRow.IncomeByTier[3], 1e-9);
                // ...but kept, not lost, in the new bucket.
                Assert.AreEqual(10.0, econRow.UnattributedIncome, 1e-6);

                var unattributedZoneRow = tables.ZoneIncome.Single(z => z.Zone == -1 && z.Team == 0);
                Assert.AreEqual(10.0, unattributedZoneRow.GoldGenerated, 1e-6);
                Assert.AreEqual(0, unattributedZoneRow.Tier); // not a real zone - no tier
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 6: splash hits separated from accuracy

        [Test]
        public void SplashHitsAreCountedSeparatelyFromAccuracy()
        {
            string temp = NewTempFolder();
            try
            {
                string tuning = "{\"weapons\":[{\"id\":5}]}";
                string lines = Session(1, 0, true, tuning) +
                    "{\"e\":\"shots\",\"t\":1,\"w\":5,\"pulls\":1,\"proj\":1}\n" +
                    "{\"e\":\"hit\",\"t\":1,\"a\":1,\"at\":0,\"v\":2,\"vt\":1,\"w\":5,\"ab\":-1,\"src\":\"Projectile\",\"raw\":10,\"arm\":0,\"hpLost\":10,\"lethal\":false,\"d\":5,\"vul\":0,\"op\":false}\n" +
                    "{\"e\":\"hit\",\"t\":1,\"a\":1,\"at\":0,\"v\":3,\"vt\":1,\"w\":5,\"ab\":-1,\"src\":\"Splash\",\"raw\":4,\"arm\":0,\"hpLost\":4,\"lethal\":false,\"d\":null,\"vul\":0,\"op\":false}\n" +
                    "{\"e\":\"hit\",\"t\":1,\"a\":1,\"at\":0,\"v\":4,\"vt\":1,\"w\":5,\"ab\":-1,\"src\":\"Splash\",\"raw\":4,\"arm\":0,\"hpLost\":4,\"lethal\":false,\"d\":null,\"vul\":0,\"op\":false}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));
                var w5 = tables.Weapons.Single(w => w.WeaponId == 5);

                Assert.AreEqual(1, w5.Hits);           // Projectile only
                Assert.AreEqual(2, w5.SplashHits);     // Splash, counted separately
                Assert.AreEqual(1.0, w5.Accuracy, 1e-9); // 1 hit / 1 projectile - NOT 3/1 = 300%
                Assert.AreEqual(18f, w5.DamageRaw, 1e-6); // damage still includes all 3 (10+4+4)
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- item 12: culture invariance

        [Test]
        public void AggregateAndCsvWriteAreInvariantUnderDutchCulture()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            string temp = NewTempFolder();
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("nl-NL"); // comma decimals, semicolon lists

                string tuning = "{\"weapons\":[{\"id\":1}]}";
                string lines = Session(1, 0, true, tuning) +
                    "{\"e\":\"sample\",\"t\":0,\"bal\":0,\"x\":1.5,\"z\":2.5,\"alive\":true,\"zone\":-1,\"tm\":0,\"w\":1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"abl\":0,\"rcl\":0,\"hp\":100.25,\"armor\":0,\"ultc\":0,\"heat\":0,\"ping\":0}\n" +
                    "{\"e\":\"hit\",\"t\":1,\"a\":1,\"at\":0,\"v\":2,\"vt\":1,\"w\":1,\"ab\":-1,\"src\":\"Projectile\",\"raw\":11.25,\"arm\":0,\"hpLost\":11.25,\"lethal\":false,\"d\":3.5,\"vul\":0,\"op\":false}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));
                CsvReportWriter.Write(tables, temp);

                string hitsCsv = File.ReadAllText(Path.Combine(temp, "csv", "hits.csv"));
                string[] hitsLines = hitsCsv.Replace("﻿", "").Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                Assert.AreEqual(2, hitsLines.Length); // header + 1 row
                int headerColumns = hitsLines[0].Split(',').Length;
                int rowColumns = hitsLines[1].Split(',').Length;
                // If a decimal used a comma under nl-NL, the row would split into MORE fields than
                // the header - invariant culture keeps every number a plain "11.25", never "11,25".
                Assert.AreEqual(headerColumns, rowColumns);
                StringAssert.Contains("11.25", hitsLines[1]);
                StringAssert.DoesNotContain("11,25", hitsLines[1]);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Directory.Delete(temp, true);
            }
        }

        // ---------------------------------------------------------------- 2.7b step 9: the warm-up is left out

        [Test]
        public void AWarmupDeathIsNotCountedInTheMatch()
        {
            string temp = NewTempFolder();
            try
            {
                // Death-line shape matches the fixtures (e.g. TelemetryFixtures/match_phases/1_Editor.jsonl):
                // "a"/"at" are the KILLER's own actor/team (TelemetryKeys.Killer aliases Actor); the victim is
                // the file owner (actor 1, from Session below).
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"phase\",\"t\":-1,\"num\":0,\"remain\":[]}\n" + // the warm-up anchor
                    "{\"e\":\"death\",\"t\":20,\"a\":-1,\"at\":-1,\"w\":-1,\"ab\":-1,\"assists\":[],\"x\":0,\"z\":0,\"timeAlive\":20,\"gold\":0,\"lw\":-1,\"leq\":-1,\"lmob\":-1,\"lult\":-1,\"abl\":0,\"rcl\":0}\n" + // warm-up death - must be left out
                    "{\"e\":\"phase\",\"t\":60,\"num\":1,\"remain\":[0]}\n" + // live at t=60
                    "{\"e\":\"death\",\"t\":80,\"a\":-1,\"at\":-1,\"w\":-1,\"ab\":-1,\"assists\":[],\"x\":0,\"z\":0,\"timeAlive\":20,\"gold\":0,\"lw\":-1,\"leq\":-1,\"lmob\":-1,\"lult\":-1,\"abl\":0,\"rcl\":0}\n" + // a real, in-match death
                    "{\"e\":\"sample\",\"t\":100,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                ReportSet set = TelemetryAggregator.BuildSet(TelemetryLog.Load(temp));

                Assert.AreEqual(1, set.WholeMatch.Deaths.Count, "only the t=80 live death counts - the t=20 warm-up one is left out");
                Assert.AreEqual(80.0, set.WholeMatch.Deaths[0].T, 1e-9);
                Assert.AreEqual(60.0, set.WholeMatch.Header.WarmupSeconds, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        /// <summary>2026-09-27 designer change: a warm-up event must never pollute a STAT (Deaths, here,
        /// same as AWarmupDeathIsNotCountedInTheMatch above) but must stay visible in the sections that
        /// are meant to cover the full log regardless of live - Console, Bug reports and Markers - on
        /// the whole-match tab. One fixture exercises all four at once, so a future change to any of
        /// them is caught by the same test.</summary>
        [Test]
        public void AWarmupEventIsExcludedFromAStatButKeptInConsoleBugAndMarkers()
        {
            string temp = NewTempFolder();
            try
            {
                string lines = Session(1, 0, true, "{}") +
                    "{\"e\":\"phase\",\"t\":-1,\"num\":0,\"remain\":[]}\n" + // the warm-up anchor
                    "{\"e\":\"console\",\"t\":10,\"state\":\"warning\",\"msg\":\"warmup warning\",\"n\":1}\n" + // warm-up console line - must be kept
                    "{\"e\":\"bug\",\"t\":15,\"a\":1,\"tm\":0,\"x\":0,\"z\":0,\"alive\":true,\"zone\":-1,\"w\":-1,\"eq\":-1,\"mob\":-1,\"ult\":-1,\"img\":\"\"}\n" + // warm-up bug mark - must be kept
                    "{\"e\":\"marker\",\"t\":18,\"a\":1,\"note\":\"warmup marker\"}\n" + // warm-up marker - must be kept
                    "{\"e\":\"death\",\"t\":20,\"a\":-1,\"at\":-1,\"w\":-1,\"ab\":-1,\"assists\":[],\"x\":0,\"z\":0,\"timeAlive\":20,\"gold\":0,\"lw\":-1,\"leq\":-1,\"lmob\":-1,\"lult\":-1,\"abl\":0,\"rcl\":0}\n" + // warm-up death - must be EXCLUDED
                    "{\"e\":\"phase\",\"t\":60,\"num\":1,\"remain\":[0]}\n" + // live at t=60
                    "{\"e\":\"death\",\"t\":80,\"a\":-1,\"at\":-1,\"w\":-1,\"ab\":-1,\"assists\":[],\"x\":0,\"z\":0,\"timeAlive\":20,\"gold\":0,\"lw\":-1,\"leq\":-1,\"lmob\":-1,\"lult\":-1,\"abl\":0,\"rcl\":0}\n" + // a real, in-match death
                    "{\"e\":\"sample\",\"t\":100,\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                ReportSet set = TelemetryAggregator.BuildSet(TelemetryLog.Load(temp));

                // The stat: only the live death counts.
                Assert.AreEqual(1, set.WholeMatch.Deaths.Count, "the warm-up death must not count toward the whole-match stat");
                Assert.AreEqual(80.0, set.WholeMatch.Deaths[0].T, 1e-9);

                // Console/Bug reports: unwindowed, always cover the full log.
                Assert.IsTrue(set.WholeMatch.Header.ConsoleByPlayer.Any(r => r.Message == "warmup warning"),
                    "a warm-up console line must still show up in the Console section");
                Assert.AreEqual(1, set.WholeMatch.Header.Bugs.Count, "a warm-up bug mark must still show up in Bug reports");
                Assert.AreEqual(15.0, set.WholeMatch.Header.Bugs[0].T, 1e-9);

                // Markers on the WHOLE-MATCH tab: also cover the full log now (2026-09-27), unlike a
                // stat - a marker dropped during warm-up is exactly the kind of "designer flagged this
                // live" note that must not silently vanish.
                Assert.AreEqual(1, set.WholeMatch.Header.Markers.Count, "a warm-up marker must still show up on the whole-match tab");
                Assert.AreEqual(18.0, set.WholeMatch.Header.Markers[0].T, 1e-9);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
