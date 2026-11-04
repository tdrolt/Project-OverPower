using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Bots;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The Laser bot as supporting fire: whom it pairs with, where it stands behind its partner, waiting after a respawn to walk out with a
    /// teammate, and how often it stops to scout. Literal numbers made up for the test; the real ones live in BotConfig.
    /// </summary>
    public class BotSupportRulesTests
    {
        private static void Near(Vector3 expected, Vector3 actual, string message = null)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-3f, message);
            Assert.AreEqual(expected.y, actual.y, 1e-3f, message);
            Assert.AreEqual(expected.z, actual.z, 1e-3f, message);
        }

        [Test]
        public void ItPairsWithTheNearestHumanFirst()
        {
            var mates = new List<(int, float, bool)> { (7, 5f, false), (8, 12f, true), (9, 9f, true) };
            Assert.AreEqual(9, BotSupportRules.PickPartner(-1, mates));
        }

        [Test]
        public void ItKeepsItsPartnerWhileThatOneIsStillThere()
        {
            var mates = new List<(int, float, bool)> { (7, 5f, false), (9, 9f, true) };
            Assert.AreEqual(7, BotSupportRules.PickPartner(7, mates));
            Assert.AreEqual(9, BotSupportRules.PickPartner(4, mates), "the old partner is gone");
        }

        [Test]
        public void WithNoHumanItPairsWithTheNearestTeammate()
        {
            var mates = new List<(int, float, bool)> { (7, 5f, false), (6, 3f, false) };
            Assert.AreEqual(6, BotSupportRules.PickPartner(-1, mates));
            Assert.AreEqual(-1, BotSupportRules.PickPartner(-1, new List<(int, float, bool)>()));
        }

        [Test]
        public void ItFollowsBehindAMovingPartnerWithinItsDistances()
        {
            Vector3 north = new Vector3(0, 0, 1);
            Near(new Vector3(0, 0, -11), BotSupportRules.FollowPoint(Vector3.zero, north, new Vector3(7, 0, 0), 11f, 16f), "too close: back to the nearest");
            Near(new Vector3(0, 0, -16), BotSupportRules.FollowPoint(Vector3.zero, north, new Vector3(0, 0, -40), 11f, 16f), "too far: up to the furthest");
            Near(new Vector3(0, 0, -13), BotSupportRules.FollowPoint(Vector3.zero, north, new Vector3(0, 0, 13), 11f, 16f), "ahead of it: round behind");
        }

        [Test]
        public void ByAStandingPartnerItKeepsItsOwnSide()
        {
            Near(new Vector3(13, 0, 0), BotSupportRules.FollowPoint(Vector3.zero, Vector3.zero, new Vector3(13, 0, 0), 11f, 16f), "already in place");
            Near(new Vector3(11, 0, 0), BotSupportRules.FollowPoint(Vector3.zero, Vector3.zero, new Vector3(4, 0, 0), 11f, 16f));
        }

        [Test]
        public void TheSpotIsFlatAtThePartnersHeight() =>
            Assert.AreEqual(2f, BotSupportRules.FollowPoint(new Vector3(0, 2, 0), new Vector3(0, 5, 1), new Vector3(0, 0, -12), 11f, 16f).y, 1e-3f);

        [Test]
        public void AfterARespawnItWaitsOnlyForATeammateBackWithinTheWait()
        {
            Assert.IsTrue(BotSupportRules.WaitForTeammate(1f, 4f, 2f), "back in 2 s, 3 s of the wait left");
            Assert.IsFalse(BotSupportRules.WaitForTeammate(1f, 4f, 3.5f), "back too late");
            Assert.IsFalse(BotSupportRules.WaitForTeammate(4.5f, 4f, 0.1f), "waited long enough");
            Assert.IsFalse(BotSupportRules.WaitForTeammate(0f, 4f, float.MaxValue), "nobody is coming back");
        }

        [Test]
        public void ItStopsToScoutAtMostOnceInTheGap()
        {
            Assert.IsTrue(BotAbilityRules.ScoutDue(26f, 25f));
            Assert.IsTrue(BotAbilityRules.ScoutDue(25f, 25f));
            Assert.IsFalse(BotAbilityRules.ScoutDue(10f, 25f));
        }
    }
}
