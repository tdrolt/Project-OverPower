using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>The scoreboard's pure rules (Tudor's D12): what one player's own client counts, the order of the
    /// table, and how often it publishes. No Photon, no scene.</summary>
    public class ScoreboardRulesTests
    {
        // ---- Counting ----

        [Test]
        public void AKillAddsOneKill()
        {
            var t = new ScoreTally();
            t.AddKill();
            Assert.AreEqual(1, t.Kills);
            Assert.AreEqual(0, t.Assists);
        }

        [Test]
        public void AnAssistAddsOneAssist()
        {
            var t = new ScoreTally();
            t.AddAssist();
            Assert.AreEqual(1, t.Assists);
            Assert.AreEqual(0, t.Kills);
        }

        [Test]
        public void ARealPlayersCreditCountsDamageAndItsKillOrAssist()
        {
            var t = new ScoreTally();
            t.AddCredit(30f, 0);
            t.AddCredit(20f, 1);
            t.AddCredit(10f, 2);
            Assert.AreEqual(60, t.DamageRounded);
            Assert.AreEqual(1, t.Kills);
            Assert.AreEqual(1, t.Assists);
        }

        [Test]
        public void ADeathAddsOneDeath()
        {
            var t = new ScoreTally();
            t.AddDeath();
            t.AddDeath();
            Assert.AreEqual(2, t.Deaths);
        }

        [Test]
        public void DamageSumsAndIsPublishedRounded()
        {
            var t = new ScoreTally();
            t.AddDamage(12.5f);
            t.AddDamage(7.4f);
            Assert.AreEqual(20, t.DamageRounded);
        }

        [Test]
        public void ZeroOrNegativeDamageIsIgnored()
        {
            var t = new ScoreTally();
            t.AddDamage(0f);
            t.AddDamage(-5f);
            Assert.AreEqual(0, t.DamageRounded);
        }

        [Test]
        public void ACaptureWhileStandingInTheZoneOnTheFlippingTeamCounts()
        {
            var t = new ScoreTally();
            t.NoteCapture(standingInZone: true, alive: true, zoneTeam: 1, myTeam: 1);
            Assert.AreEqual(1, t.Captures);
        }

        [Test]
        public void StandingInTheZoneOnAnotherTeamCountsNothing()
        {
            var t = new ScoreTally();
            t.NoteCapture(standingInZone: true, alive: true, zoneTeam: 2, myTeam: 1);
            Assert.AreEqual(0, t.Captures);
        }

        [Test]
        public void NotStandingInTheZoneCountsNothing()
        {
            var t = new ScoreTally();
            t.NoteCapture(standingInZone: false, alive: true, zoneTeam: 1, myTeam: 1);
            Assert.AreEqual(0, t.Captures);
        }

        [Test]
        public void ADeadPlayerCountsNoCapture()
        {
            var t = new ScoreTally();
            t.NoteCapture(standingInZone: true, alive: false, zoneTeam: 1, myTeam: 1);
            Assert.AreEqual(0, t.Captures);
        }

        [Test]
        public void AnUnknownTeamCountsNoCapture()
        {
            var t = new ScoreTally();
            t.NoteCapture(standingInZone: true, alive: true, zoneTeam: -1, myTeam: -1);
            Assert.AreEqual(0, t.Captures);
        }

        [Test]
        public void GoLiveResetsEverythingToZero()
        {
            var t = new ScoreTally();
            t.AddKill(); t.AddAssist(); t.AddDeath(); t.AddDamage(50f);
            t.NoteCapture(true, true, 0, 0);
            t.Reset();
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0 }, t.ToArray());
        }

        [Test]
        public void TheArrayLayoutIsKillsDeathsAssistsDamageCaptures()
        {
            var t = new ScoreTally();
            t.AddKill(); t.AddKill(); t.AddDeath(); t.AddAssist(); t.AddAssist(); t.AddAssist();
            t.AddDamage(41f);
            t.NoteCapture(true, true, 2, 2);
            CollectionAssert.AreEqual(new[] { 2, 1, 3, 41, 1 }, t.ToArray());
        }

        [Test]
        public void ARowReadsBackTheArrayAndAMissingOrShortOneReadsAsZeros()
        {
            ScoreRow row = ScoreboardRules.RowFrom(7, 1, "Ana", new[] { 2, 1, 3, 41, 1 });
            Assert.AreEqual(7, row.Actor); Assert.AreEqual(1, row.Team); Assert.AreEqual("Ana", row.Name);
            Assert.AreEqual(2, row.Kills); Assert.AreEqual(1, row.Deaths); Assert.AreEqual(3, row.Assists);
            Assert.AreEqual(41, row.Damage); Assert.AreEqual(1, row.Captures);

            Assert.AreEqual(0, ScoreboardRules.RowFrom(8, 0, "Bo", null).Kills);
            Assert.AreEqual(0, ScoreboardRules.RowFrom(8, 0, "Bo", new[] { 5, 5 }).Kills);
        }

        // ---- Ordering ----

        private static ScoreRow Row(int actor, int team, int kills, int damage) =>
            ScoreboardRules.RowFrom(actor, team, "p" + actor, new[] { kills, 0, 0, damage, 0 });

        [Test]
        public void RowsAreGroupedByTeamInTeamOrder()
        {
            var rows = new List<ScoreRow> { Row(1, 2, 9, 0), Row(2, 0, 0, 0), Row(3, 1, 5, 0), Row(4, 0, 1, 0) };
            ScoreboardRules.Sort(rows);
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 2 }, new[] { rows[0].Team, rows[1].Team, rows[2].Team, rows[3].Team });
        }

        [Test]
        public void WithinATeamMostKillsFirstThenMostDamage()
        {
            var rows = new List<ScoreRow> { Row(1, 0, 2, 100), Row(2, 0, 3, 10), Row(3, 0, 2, 500) };
            ScoreboardRules.Sort(rows);
            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, new[] { rows[0].Actor, rows[1].Actor, rows[2].Actor });
        }

        [Test]
        public void AFullTieFallsBackToTheLowerActorNumber()
        {
            var rows = new List<ScoreRow> { Row(5, 0, 1, 10), Row(3, 0, 1, 10) };
            ScoreboardRules.Sort(rows);
            Assert.AreEqual(3, rows[0].Actor);
        }

        [Test]
        public void APlayerWithNoTeamYetSortsLast()
        {
            var rows = new List<ScoreRow> { Row(1, -1, 99, 0), Row(2, 2, 0, 0) };
            ScoreboardRules.Sort(rows);
            Assert.AreEqual(2, rows[0].Actor);
        }

        // ---- Throttle ----

        [Test]
        public void ManyDamageEventsInATenthOfASecondPublishOnce()
        {
            var th = new ScorePublishThrottle(4f);
            int publishes = 0;
            for (int i = 0; i <= 10; i++)
            {
                float now = i * 0.01f;
                th.NoteChange(urgent: false);
                if (th.ShouldPublish(now)) { th.MarkPublished(now); publishes++; }
            }
            Assert.AreEqual(1, publishes);
        }

        [Test]
        public void TheDamageHeldBackIsPublishedOnceTheIntervalHasPassed()
        {
            var th = new ScorePublishThrottle(4f);
            th.NoteChange(false);
            Assert.IsTrue(th.ShouldPublish(0f)); th.MarkPublished(0f);
            th.NoteChange(false);
            Assert.IsFalse(th.ShouldPublish(0.1f));
            Assert.IsTrue(th.ShouldPublish(0.26f));
        }

        [Test]
        public void AKillDeathOrCapturePublishesAtOnce()
        {
            var th = new ScorePublishThrottle(4f);
            th.NoteChange(false); th.MarkPublished(0f);
            th.NoteChange(urgent: true);
            Assert.IsTrue(th.ShouldPublish(0.01f));
        }

        [Test]
        public void NothingChangedPublishesNothing()
        {
            var th = new ScorePublishThrottle(4f);
            Assert.IsFalse(th.ShouldPublish(100f));
            th.NoteChange(true); th.MarkPublished(100f);
            Assert.IsFalse(th.ShouldPublish(200f));
        }

        [Test]
        public void ARateOfZeroPublishesEveryChange()
        {
            var th = new ScorePublishThrottle(0f);
            th.NoteChange(false); th.MarkPublished(0f);
            th.NoteChange(false);
            Assert.IsTrue(th.ShouldPublish(0f));
        }

        // ---- Input ----

        [Test]
        public void TabOpensTheBoardUnlessYouAreTypingAndDeadPlayersStillSeeIt()
        {
            Assert.IsTrue(ScoreboardRules.MayOpen(typingInChat: false));
            Assert.IsFalse(ScoreboardRules.MayOpen(typingInChat: true));
        }
    }
}
