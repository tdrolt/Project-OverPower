using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Vision;

namespace Overpower.Tests
{
    /// <summary>
    /// Vision Task 9a (Tudor 2026-09-30): with the switch off, a team only learns about a zone while it sees it, owns
    /// it, or the centre scan passes over it. Team 0 is "me"; zone 0 is my capital, zone 1 the enemy's (team 1),
    /// zone 2 neutral.
    /// </summary>
    public class ZoneKnowledgeRulesTests
    {
        private const int Me = 0;
        private const int Enemy = 1;
        private static readonly int[] None = new int[0];

        private static ZoneView Owned(int team) => new ZoneView(team, -1, 0f, false, 100);
        private static ZoneView Capturing(int owner, int by, float progress) => new ZoneView(owner, by, progress, false, 100);
        private static bool SeeNothing(int zone) => false;

        private static ZoneKnowledge Started(bool switchOn)
        {
            var k = new ZoneKnowledge();
            k.Update(switchOn, Me, new[] { Owned(Me), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
            return k;
        }

        [Test]
        public void TheStartingState_IsTheLiveStateAtTheFirstRead_EvenWithTheSwitchOff()
        {
            var k = Started(false);
            Assert.AreEqual(Me, k.Displayed(0).OwnerTeam);
            Assert.AreEqual(Enemy, k.Displayed(1).OwnerTeam);
            Assert.AreEqual(-1, k.Displayed(2).OwnerTeam);
        }

        [Test]
        public void WithTheSwitchOn_EverythingIsLive()
        {
            var k = Started(true);
            k.Update(true, Me, new[] { Owned(Me), Capturing(Enemy, 2, 0.5f), Owned(2) }, SeeNothing, None);
            Assert.AreEqual(2, k.Displayed(2).OwnerTeam);
            Assert.AreEqual(2, k.Displayed(1).CapturingTeam);
            Assert.AreEqual(0.5f, k.Displayed(1).Progress01, 1e-5f);
        }

        [Test]
        public void AnUnseenEnemyZone_KeepsItsOldOwner_AfterALiveCapture()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(Enemy, k.Displayed(1).OwnerTeam);
        }

        [Test]
        public void AZone_UpdatesTheFrameItIsSeen()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral }, z => z == 1, None);
            Assert.AreEqual(2, k.Displayed(1).OwnerTeam);
        }

        [Test]
        public void AZoneOnceSeen_FreezesAgain_WhenItLeavesSight()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral }, z => z == 1, None);
            k.Update(false, Me, new[] { Owned(Me), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(2, k.Displayed(1).OwnerTeam);
        }

        [Test]
        public void MyOwnZone_IsAlwaysLive_AnEnemyDrainingItShows()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Capturing(Me, Enemy, 0.4f), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(Enemy, k.Displayed(0).CapturingTeam);
            Assert.AreEqual(0.4f, k.Displayed(0).Progress01, 1e-5f);
        }

        [Test]
        public void MyOwnZone_UnderAttackShows()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { new ZoneView(Me, Enemy, 0f, true, 100), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
            Assert.IsTrue(k.Displayed(0).UnderAttack);
        }

        [Test]
        public void AZoneILoseWhileUnseen_ShowsTheLoss()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Enemy), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(Enemy, k.Displayed(0).OwnerTeam);
        }

        [Test]
        public void AfterTheLoss_TheZoneIsNoLongerMine_SoItFreezesAgain()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Enemy), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
            k.Update(false, Me, new[] { Owned(2), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(Enemy, k.Displayed(0).OwnerTeam);
        }

        [Test]
        public void AScannedZone_TakesTheLiveState_ThenFreezesAgain()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral }, SeeNothing, new[] { 1 });
            Assert.AreEqual(2, k.Displayed(1).OwnerTeam);
            k.Update(false, Me, new[] { Owned(Me), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(2, k.Displayed(1).OwnerTeam);
        }

        [Test]
        public void AZoneNotScanned_IsLeftAlone()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(2), Owned(2) }, SeeNothing, new[] { 2 });
            Assert.AreEqual(Enemy, k.Displayed(1).OwnerTeam);
        }

        [Test]
        public void UnseenProgress_DoesNotKeepAdvancing()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Capturing(Enemy, 2, 0.3f), ZoneView.Neutral }, z => z == 1, None);
            k.Update(false, Me, new[] { Owned(Me), Capturing(Enemy, 2, 0.8f), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(0.3f, k.Displayed(1).Progress01, 1e-5f);
            Assert.AreEqual(2, k.Displayed(1).CapturingTeam);
        }

        [Test]
        public void ASwitchTurnedOnLater_ShowsTheLiveStateAtOnce()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral }, SeeNothing, None);
            k.Update(true, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(2, k.Displayed(1).OwnerTeam);
        }
    }
}
