using NUnit.Framework;
using Overpower.Bots;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Following a route: the next corner, when it is stuck, when to plan again, and which colliders the walkable map is built from.</summary>
    public class BotPathRulesTests
    {
        private static readonly Vector3[] Route = { new Vector3(0, 0, 0), new Vector3(0, 0, 5), new Vector3(5, 0, 5) };

        [Test]
        public void ItHeadsForTheFirstCornerItHasNotReached()
        {
            Assert.AreEqual(1, BotPathRules.NextCorner(Route, new Vector3(0, 0, 0.2f), 0.6f));
            Assert.AreEqual(2, BotPathRules.NextCorner(Route, new Vector3(0, 0, 4.7f), 0.6f), "close enough to the second, on to the third");
            Assert.AreEqual(-1, BotPathRules.NextCorner(Route, new Vector3(5, 0, 5), 0.6f), "arrived");
            Assert.AreEqual(-1, BotPathRules.NextCorner(new Vector3[0], Vector3.zero, 0.6f));
        }

        [Test]
        public void HeightDoesNotCountWhenReachingACorner()
        {
            Assert.AreEqual(2, BotPathRules.NextCorner(Route, new Vector3(0, 1.4f, 4.8f), 0.6f));
        }

        [Test]
        public void ItIsStuckWhenTryingToMoveButHardlyMoving()
        {
            Assert.IsTrue(BotPathRules.IsStuck(true, 0.2f, 1.2f, 1f));
            Assert.IsFalse(BotPathRules.IsStuck(true, 0.2f, 0.6f, 1f), "not long enough");
            Assert.IsFalse(BotPathRules.IsStuck(true, 2f, 1.2f, 1f), "it moved");
            Assert.IsFalse(BotPathRules.IsStuck(false, 0f, 3f, 1f), "standing still on purpose");
        }

        [Test]
        public void ItPlansAgainOnATimerOrWhenTheGoalMoved()
        {
            Assert.IsTrue(BotPathRules.NeedsRepath(false, 0.1f, 0.5f, 0f));
            Assert.IsTrue(BotPathRules.NeedsRepath(true, 0.6f, 0.5f, 0f));
            Assert.IsTrue(BotPathRules.NeedsRepath(true, 0.1f, 0.5f, 1.5f));
            Assert.IsFalse(BotPathRules.NeedsRepath(true, 0.1f, 0.5f, 0.2f));
        }

        [Test]
        public void TheMapIsBuiltFromSolidStillCollidersOnly()
        {
            Assert.IsTrue(BotPathRules.IsWalkSource(false, false));
            Assert.IsFalse(BotPathRules.IsWalkSource(false, true), "a moving body (a player) is not part of the map");
            Assert.IsFalse(BotPathRules.IsWalkSource(true, false), "a zone's trigger stops nobody");
        }

        [Test]
        public void TheMoveIsFlatAndAtMostOneLong()
        {
            Vector3 move = BotPathRules.MoveToward(new Vector3(0, 0.5f, 0), new Vector3(3, 2f, 4));
            Assert.AreEqual(0f, move.y);
            Assert.AreEqual(1f, move.magnitude, 1e-4f);
            Assert.AreEqual(Vector3.zero, BotPathRules.MoveToward(Vector3.zero, new Vector3(0, 2, 0)));
        }
    }
}
