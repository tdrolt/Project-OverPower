using NUnit.Framework;
using Overpower.Bots;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Two walkable maps: which route a bot walks (the wide one unless it is much longer), and how it takes a tight route's bends (slower,
    /// through the middle of the gap). Literal numbers made up for the test; the real ones live in BotConfig.
    /// </summary>
    public class BotRouteRulesTests
    {
        [Test]
        public void ARoutesLengthIsFlatThroughItsCorners()
        {
            Assert.AreEqual(7f, BotPathRules.RouteMetres(new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 3), new Vector3(4, 0, 3) }), 1e-4f);
            Assert.AreEqual(3f, BotPathRules.RouteMetres(new[] { new Vector3(0, 0, 0), new Vector3(0, 2, 3) }), 1e-4f, "height does not count");
            Assert.AreEqual(0f, BotPathRules.RouteMetres(new[] { new Vector3(5, 0, 5) }), 1e-4f);
            Assert.AreEqual(0f, BotPathRules.RouteMetres(new Vector3[0]), 1e-4f);
        }

        [Test]
        public void TheWideRouteIsTakenUnlessItIsTooMuchLonger()
        {
            Assert.IsTrue(BotPathRules.TakeWide(true, 25f, true, 20f, 1.5f), "25 m is within 1.5 times 20 m");
            Assert.IsTrue(BotPathRules.TakeWide(true, 30f, true, 20f, 1.5f), "exactly the limit");
            Assert.IsFalse(BotPathRules.TakeWide(true, 31f, true, 20f, 1.5f), "too long: the tight route");
            Assert.IsFalse(BotPathRules.TakeWide(false, 0f, true, 20f, 1.5f), "no wide route");
        }

        [Test]
        public void TheWideRouteIsTakenWhenTheTightOneDoesNotReachTheGoal() =>
            Assert.IsTrue(BotPathRules.TakeWide(true, 90f, false, 20f, 1.5f));

        [Test]
        public void AcrossABendIsFlatSquareToTheWayThroughAndToItsRight()
        {
            Vector3 straight = BotPathRules.Across(new Vector3(0, 0, 0), new Vector3(0, 0, 5), new Vector3(0, 0, 10));
            Assert.AreEqual(1f, straight.x, 1e-4f, "walking north, the right is east");
            Assert.AreEqual(0f, straight.z, 1e-4f);

            Vector3 turn = BotPathRules.Across(new Vector3(0, 0, 0), new Vector3(0, 1, 5), new Vector3(5, 0, 5));
            Assert.AreEqual(0f, turn.y, 1e-4f, "height does not count");
            Assert.AreEqual(0.7071f, turn.x, 1e-3f, "in north, out east: the way through is north-east, its right south-east");
            Assert.AreEqual(-0.7071f, turn.z, 1e-3f);
        }

        [Test]
        public void ABendMovesToTheMiddleOfItsGap()
        {
            Vector3 middle = BotPathRules.GapMiddle(new Vector3(2, 0, 2), new Vector3(1, 0, 0), 0.5f, 2.5f);
            Assert.AreEqual(3f, middle.x, 1e-4f, "1 m more open on the right: half of it");
            Assert.AreEqual(2f, middle.z, 1e-4f);
            Assert.AreEqual(new Vector3(2, 0, 2), BotPathRules.GapMiddle(new Vector3(2, 0, 2), new Vector3(1, 0, 0), 1.5f, 1.5f), "already in the middle");
        }

        [Test]
        public void OnATightRouteItSlowsNearABendOnly()
        {
            Assert.AreEqual(0.7f, BotPathRules.CornerSpeed(true, true, 1f, 1.2f, 0.7f), 1e-4f);
            Assert.AreEqual(1f, BotPathRules.CornerSpeed(true, true, 2f, 1.2f, 0.7f), 1e-4f, "still far from the bend");
            Assert.AreEqual(1f, BotPathRules.CornerSpeed(false, true, 1f, 1.2f, 0.7f), 1e-4f, "a wide route");
            Assert.AreEqual(1f, BotPathRules.CornerSpeed(true, false, 1f, 1.2f, 0.7f), 1e-4f, "the route's end is no bend");
        }
    }
}
