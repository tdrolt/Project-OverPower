using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Bots;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Where a bot goes when nothing is shooting at it: Conquest's five rules and Dominion's, written once from the mode's zone list.
    /// </summary>
    public class BotGoalRulesTests
    {
        private static ConquestZone Zone(int id, float x, int tier = 2, bool may = true, bool bounty = false) =>
            new ConquestZone { Id = id, Centre = new Vector3(x, 0, 0), Tier = tier, MayCapture = may, Bounty = bounty };

        private static readonly ConquestGoalRules.Settings Rules = new ConquestGoalRules.Settings { TierThreeBonusMetres = 10f, BountyBonusMetres = 10f, PushCapitalTeammates = 2 };

        [Test]
        public void AThreatenedCapitalComesFirst()
        {
            var zones = new List<ConquestZone> { Zone(1, 5f), new ConquestZone { Id = 9, Centre = new Vector3(50, 0, 0), IsOwnCapital = true, OwnCapitalThreatened = true } };
            Assert.AreEqual(9, ConquestGoalRules.Choose(zones, Vector3.zero, false, Rules));
        }

        [Test]
        public void TheClosestCapturableZoneWinsWithTierThreeAndBountyCountedCloser()
        {
            var zones = new List<ConquestZone> { Zone(1, 12f), Zone(2, 18f, tier: 3), Zone(3, 6f, may: false) };
            Assert.AreEqual(2, ConquestGoalRules.Choose(zones, Vector3.zero, false, Rules), "18 m less 10 for Tier III beats 12 m");
            zones.Add(Zone(4, 15f, bounty: true));
            Assert.AreEqual(4, ConquestGoalRules.Choose(zones, Vector3.zero, false, Rules), "15 m less 10 for the bounty beats 8 m");
        }

        [Test]
        public void AnEnemyCapitalIsPushedOnlyWithEnoughOfTheTeamNear()
        {
            var capital = new ConquestZone { Id = 7, Centre = new Vector3(40, 0, 0), IsEnemyCapital = true, MayCapture = true, TeammatesNear = 1 };
            var zones = new List<ConquestZone> { Zone(1, 30f), capital };
            Assert.AreEqual(1, ConquestGoalRules.Choose(zones, Vector3.zero, false, Rules));
            capital.TeammatesNear = 2;
            zones[1] = capital;
            Assert.AreEqual(7, ConquestGoalRules.Choose(zones, Vector3.zero, false, Rules));
        }

        [Test]
        public void TheCentreComesBeforeTheScanAfterTheShrink()
        {
            var zones = new List<ConquestZone> { Zone(1, 5f), new ConquestZone { Id = 6, Centre = new Vector3(60, 0, 0), IsCentre = true, MayCapture = false } };
            Assert.AreEqual(1, ConquestGoalRules.Choose(zones, Vector3.zero, false, Rules));
            Assert.AreEqual(6, ConquestGoalRules.Choose(zones, Vector3.zero, true, Rules));
        }

        [Test]
        public void NothingToDoGivesNoZone()
        {
            Assert.AreEqual(-1, ConquestGoalRules.Choose(new List<ConquestZone> { Zone(1, 5f, may: false) }, Vector3.zero, false, Rules));
        }

        private static DominionZone DZone(int id, float x, float pps, bool ours = false, bool losing = false, bool centre = false) =>
            new DominionZone { Id = id, Centre = new Vector3(x, 0, 0), PointsPerSecond = pps, Ours = ours, BeingTakenFromUs = losing, IsCentre = centre, Capturable = true };

        [Test]
        public void DominionDefendsAZoneBeingTakenFirst()
        {
            var zones = new List<DominionZone> { DZone(1, 5f, 5f), DZone(2, 30f, 5f, ours: true, losing: true) };
            Assert.AreEqual(2, DominionGoalRules.Choose(zones, Vector3.zero, false));
        }

        [Test]
        public void DominionTakesTheBestPayingZoneItDoesNotHoldClosestFirst()
        {
            var zones = new List<DominionZone> { DZone(1, 5f, 3f), DZone(2, 30f, 5f), DZone(3, 20f, 5f), DZone(4, 2f, 5f, ours: true) };
            Assert.AreEqual(3, DominionGoalRules.Choose(zones, Vector3.zero, false));
        }

        [Test]
        public void DominionHoldsItsBestZoneWhenItHoldsThemAll()
        {
            var zones = new List<DominionZone> { DZone(1, 5f, 3f, ours: true), DZone(2, 30f, 5f, ours: true) };
            Assert.AreEqual(2, DominionGoalRules.Choose(zones, Vector3.zero, false));
        }

        [Test]
        public void DominionGoesForTheCentreBeforeItsPayout()
        {
            var zones = new List<DominionZone> { DZone(1, 5f, 5f), DZone(9, 40f, 0f, centre: true) };
            Assert.AreEqual(1, DominionGoalRules.Choose(zones, Vector3.zero, false));
            Assert.AreEqual(9, DominionGoalRules.Choose(zones, Vector3.zero, true));
        }

        [Test]
        public void InsideTheCircleWithAMarginStaysPut()
        {
            Assert.IsTrue(DominionGoalRules.InsideCircle(new Vector2(3, 0), Vector2.zero, 6f, 2f));
            Assert.IsFalse(DominionGoalRules.InsideCircle(new Vector2(5, 0), Vector2.zero, 6f, 2f));
        }
    }
}
