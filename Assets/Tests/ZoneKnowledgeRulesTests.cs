using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Match;
using Overpower.Vision;

namespace Overpower.Tests
{
    /// <summary>
    /// Vision Task 9a (Tudor 2026-09-30): with the switch off, a team only learns about a zone while it sees it, owns
    /// it (or is capturing/draining it), or the centre scan passes over it. Team 0 is "me"; zone 0 is my capital,
    /// zone 1 the enemy's (team 1), zone 2 neutral. Task 9b adds the live list the component feeds it, and "seen".
    /// </summary>
    public class ZoneKnowledgeRulesTests
    {
        private const int Me = 0;
        private const int Enemy = 1;
        private static readonly int[] None = new int[0];

        private static ZoneView Owned(int team) =>
            new ZoneView(team, new CaptureRingState(CaptureRingPhase.Idle, 0f, -1, team, -1, false), 100);

        // A team taking a neutral zone (owner -1) or an enemy draining an owned one: the ring the displays draw.
        private static ZoneView Capturing(int owner, int by, float progress) => owner < 0
            ? new ZoneView(owner, new CaptureRingState(CaptureRingPhase.Capturing, progress, by, owner, -1, false), 100)
            : new ZoneView(owner, new CaptureRingState(CaptureRingPhase.Draining, progress, owner, owner, by, false), 100);

        private static ZoneView Attacked(int owner) =>
            new ZoneView(owner, new CaptureRingState(CaptureRingPhase.Idle, 0f, -1, owner, -1, true), 100);

        private static bool SeeNothing(int zone) => false;

        private static ZoneKnowledgeStore Started(bool switchOn)
        {
            var k = new ZoneKnowledgeStore();
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
            Assert.AreEqual(2, k.Displayed(1).Ring.DrainerTeam);
            Assert.AreEqual(0.5f, k.Displayed(1).Ring.Fill01, 1e-5f);
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
            Assert.AreEqual(Enemy, k.Displayed(0).Ring.DrainerTeam);
            Assert.AreEqual(0.4f, k.Displayed(0).Ring.Fill01, 1e-5f);
        }

        [Test]
        public void MyOwnZone_UnderAttackShows()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Attacked(Me), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
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
            Assert.AreEqual(0.3f, k.Displayed(1).Ring.Fill01, 1e-5f);
            Assert.AreEqual(2, k.Displayed(1).Ring.DrainerTeam);
        }

        [Test]
        public void ASwitchTurnedOnLater_ShowsTheLiveStateAtOnce()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral }, SeeNothing, None);
            k.Update(true, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(2, k.Displayed(1).OwnerTeam);
        }

        // ---- 9a review fixes

        [Test]
        public void ANeutralZoneMyTeamIsCapturing_IsLiveEvenUnseen()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(Enemy), Capturing(-1, Me, 0.3f) }, SeeNothing, None);
            Assert.AreEqual(0.3f, k.Displayed(2).Ring.Fill01, 1e-5f, "a teammate capturing it away from the tower: I see my own progress");
        }

        [Test]
        public void AnEnemyZoneMyTeamIsDraining_IsLiveEvenUnseen()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Capturing(Enemy, Me, 0.6f), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(Me, k.Displayed(1).Ring.DrainerTeam);
            Assert.AreEqual(0.6f, k.Displayed(1).Ring.Fill01, 1e-5f);
        }

        [Test]
        public void ANeutralZoneAnotherTeamIsCapturing_StaysNeutralWhenUnseen()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(Enemy), Capturing(-1, 2, 0.5f) }, SeeNothing, None);
            Assert.AreEqual(CaptureRingPhase.Idle, k.Displayed(2).Ring.Phase);
            Assert.AreEqual(0f, k.Displayed(2).Ring.Fill01);
        }

        [Test]
        public void WithNoTeamYet_NothingCountsAsMine_SoUnseenZonesStayFrozen()
        {
            var k = new ZoneKnowledgeStore();
            k.Update(false, -1, new[] { Owned(Me), Owned(Enemy), ZoneView.Neutral }, SeeNothing, None);
            k.Update(false, -1, new[] { Owned(Me), Owned(Enemy), Owned(2) }, SeeNothing, None);
            Assert.AreEqual(-1, k.Displayed(2).OwnerTeam, "a neutral zone must not read as mine for team -1");
        }

        [Test]
        public void ACountChange_CopiesTheLiveStateAgain()
        {
            var k = Started(false);
            k.Update(false, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral, Owned(Enemy) }, SeeNothing, None);
            Assert.AreEqual(2, k.Displayed(1).OwnerTeam);
            Assert.AreEqual(Enemy, k.Displayed(3).OwnerTeam);
        }

        [Test]
        public void AfterAReset_TheNextUpdateCopiesLive_EvenWithTheSwitchOff()
        {
            var k = Started(false);
            k.Reset();
            k.Update(false, Me, new[] { Owned(Me), Owned(2), ZoneView.Neutral }, SeeNothing, None);
            Assert.AreEqual(2, k.Displayed(1).OwnerTeam);
        }

        [Test]
        public void ADisplayedZoneBeforeAnyUpdate_IsNeutral()
        {
            Assert.AreEqual(-1, new ZoneKnowledgeStore().Displayed(0).OwnerTeam);
        }

        // ---- the live list, built from the room's plain values

        private static CaptureProgress NoProgress(int zone) => CaptureProgress.Idle;
        private static bool NotAttacked(int zone) => false;

        [Test]
        public void TheLiveList_HasTheOwnerAndHeldSinceOfEveryZone()
        {
            TerritorySnapshot snapshot = TerritorySnapshot.Starting(3, new[] { (0, 0), (1, 1) }, null, 500)
                .WithCapture(2, 2, 900, 0);
            var live = new List<ZoneView>();
            ZoneViews.BuildLive(snapshot, NoProgress, NotAttacked, 1000, live);
            Assert.AreEqual(3, live.Count);
            Assert.AreEqual(0, live[0].OwnerTeam);
            Assert.AreEqual(1, live[1].OwnerTeam);
            Assert.AreEqual(2, live[2].OwnerTeam);
            Assert.AreEqual(900, live[2].HeldSinceMs);
        }

        [Test]
        public void TheLiveList_EvaluatesAProgressAtNow()
        {
            TerritorySnapshot snapshot = TerritorySnapshot.Starting(3, new[] { (0, 0) }, null, 500);
            var capture = new CaptureProgress(1, 0.2f, 0.1f, 1000);
            var live = new List<ZoneView>();
            ZoneViews.BuildLive(snapshot, z => z == 2 ? capture : CaptureProgress.Idle, NotAttacked, 3000, live);
            Assert.AreEqual(CaptureRingPhase.Capturing, live[2].Ring.Phase);
            Assert.AreEqual(1, live[2].Ring.ArcTeam);
            Assert.AreEqual(0.4f, live[2].Ring.Fill01, 1e-4f, "0.2 + 0.1/s over two seconds");
        }

        [Test]
        public void TheLiveList_MarksAnOwnedZoneUnderAttack_ButNotANeutralOne()
        {
            TerritorySnapshot snapshot = TerritorySnapshot.Starting(3, new[] { (0, 0) }, null, 500);
            var live = new List<ZoneView>();
            ZoneViews.BuildLive(snapshot, NoProgress, z => true, 1000, live);
            Assert.IsTrue(live[0].UnderAttack);
            Assert.IsFalse(live[1].UnderAttack);
        }

        [Test]
        public void TheLiveList_IsRebuiltInPlace_NotAppended()
        {
            TerritorySnapshot snapshot = TerritorySnapshot.Starting(3, new[] { (0, 0) }, null, 500);
            var live = new List<ZoneView>();
            ZoneViews.BuildLive(snapshot, NoProgress, NotAttacked, 1000, live);
            ZoneViews.BuildLive(snapshot, NoProgress, NotAttacked, 2000, live);
            Assert.AreEqual(3, live.Count);
        }

        // ---- "seen"

        // The tower is a solid (Building layer) capsule 2.6 m wide, so a sight line to its centre always stops at its own
        // surface; the tower is seen when a spot just outside that capsule is.
        [Test]
        public void EveryTowerSightPoint_IsTheSameDistanceFromTheCentre_OnTheGround()
        {
            var centre = new Vector3(40f, 0.3f, -12f);
            for (int i = 0; i < ZoneViews.TowerSightPointCount; i++)
            {
                Vector3 p = ZoneViews.TowerSightPoint(centre, i);
                Assert.AreEqual(centre.y, p.y, 1e-5f);
                Assert.AreEqual(ZoneViews.TowerSightRadius, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(centre.x, centre.z)), 1e-4f);
            }
        }

        [Test]
        public void TheTowerSightPoints_AreAllDifferent_AndSitOutsideTheTowerCapsule()
        {
            Assert.Greater(ZoneViews.TowerSightRadius, 2.6f, "the Tower Look capsule is 2.6 m in radius");
            var seen = new System.Collections.Generic.HashSet<Vector3>();
            for (int i = 0; i < ZoneViews.TowerSightPointCount; i++)
                seen.Add(ZoneViews.TowerSightPoint(Vector3.zero, i));
            Assert.AreEqual(ZoneViews.TowerSightPointCount, seen.Count);
        }

        [Test] public void WithNoSightObject_ATowerCountsAsSeen() => Assert.IsTrue(ZoneViews.IsSeen(false, false));
        [Test] public void ATowerOutOfSight_IsNotSeen() => Assert.IsFalse(ZoneViews.IsSeen(true, false));
        [Test] public void ATowerInSight_IsSeen() => Assert.IsTrue(ZoneViews.IsSeen(true, true));
    }
}
