using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The stop rule behind every displacement (movement step 2). "East" is the move; a wall to the east reports a
    /// normal pointing west, and physics would push a capsule inside it back west.
    /// </summary>
    public class DisplacementSweepRuleTests
    {
        private static readonly Vector3 East = Vector3.right;
        private static readonly Vector3 West = Vector3.left;

        private static float Allowed(float requested, Vector3 move, out int blocker, params SweepContact[] contacts) =>
            DisplacementSweepRule.AllowedTravel(requested, new List<SweepContact>(contacts), move, out blocker);

        [Test]
        public void NothingInTheWayAllowsTheWholeStep()
        {
            Assert.AreEqual(0.36f, Allowed(0.36f, East, out int blocker), 1e-5f);
            Assert.AreEqual(-1, blocker);
        }

        [Test]
        public void AWallAheadStopsASkinWidthShortOfIt()
        {
            Assert.AreEqual(0.95f, Allowed(2f, East, out int blocker, SweepContact.Hit(1f, West)), 1e-5f);
            Assert.AreEqual(0, blocker);
        }

        [Test]
        public void AWallCloserThanTheSkinAllowsNoTravelAtAll()
        {
            Assert.AreEqual(0f, Allowed(0.36f, East, out int blocker, SweepContact.Hit(0.03f, West)), 1e-5f);
            Assert.AreEqual(0, blocker);
        }

        [Test]
        public void AWallBeyondTheStepDoesNotShortenIt()
        {
            Assert.AreEqual(0.36f, Allowed(0.36f, East, out int blocker, SweepContact.Hit(0.5f, West)), 1e-5f);
            Assert.AreEqual(-1, blocker);
        }

        [Test]
        public void GroundUnderfootNeverBlocks()
        {
            var slope = new Vector3(-0.43f, 0.9f, 0f).normalized;
            Assert.AreEqual(0.36f, Allowed(0.36f, East, out int blocker, SweepContact.Hit(0.1f, slope)), 1e-5f);
            Assert.AreEqual(-1, blocker);
        }

        [Test]
        public void GroundInFrontDoesNotHideAWallBehindIt()
        {
            float allowed = Allowed(2f, East, out int blocker, SweepContact.Hit(0.2f, Vector3.up), SweepContact.Hit(0.6f, West));
            Assert.AreEqual(0.55f, allowed, 1e-5f);
            Assert.AreEqual(1, blocker);
        }

        [Test]
        public void TheNearestWallWinsWhateverOrderTheyArriveIn()
        {
            float allowed = Allowed(3f, East, out int blocker, SweepContact.Hit(1.5f, West), SweepContact.Hit(0.8f, West));
            Assert.AreEqual(0.75f, allowed, 1e-5f);
            Assert.AreEqual(1, blocker);
        }

        [Test]
        public void StartingInsideAWallAndMovingDeeperStopsWhereYouStand()
        {
            // Walking presses a player about 0.1 m into a wall; the dash that follows must go nowhere.
            Assert.AreEqual(0f, Allowed(0.36f, East, out int blocker, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(0, blocker);
        }

        [Test]
        public void StartingInsideAWallAndMovingAwayOrAlongIsNotBlocked()
        {
            Assert.AreEqual(0.36f, Allowed(0.36f, West, out int away, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(-1, away);
            Assert.AreEqual(0.36f, Allowed(0.36f, Vector3.forward, out int along, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(-1, along);
        }

        [Test]
        public void ShallowIntoTheWallIsStillAlongIt()
        {
            // About 3 degrees into a wall you are touching still counts as running along it; 17 degrees does not.
            Vector3 shallow = new Vector3(0.05f, 0f, 1f).normalized;
            Vector3 steeper = new Vector3(0.3f, 0f, 1f).normalized;
            Assert.AreEqual(0.36f, Allowed(0.36f, shallow, out int soft, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(-1, soft);
            Assert.AreEqual(0f, Allowed(0.36f, steeper, out int hard, SweepContact.Inside(West)), 1e-5f);
            Assert.AreEqual(0, hard);
        }

        [Test]
        public void AnOverlapPhysicsCantExplainNeverBlocks()
        {
            // ComputePenetration found nothing at the probe pose: no push-out, so no reason to stop.
            Assert.AreEqual(0.36f, Allowed(0.36f, East, out int blocker, SweepContact.InsideWithoutPushOut()), 1e-5f);
            Assert.AreEqual(-1, blocker);
        }

        [Test]
        public void AMoveThatCoversLessThanACentimetreIsNotWorthStarting()
        {
            Assert.IsFalse(DisplacementSweepRule.IsUsefulTravel(0f));
            Assert.IsFalse(DisplacementSweepRule.IsUsefulTravel(0.009f));
            Assert.IsTrue(DisplacementSweepRule.IsUsefulTravel(0.01f));
        }

        [Test]
        public void OnlyStaticGeometryCanBlockAsStartInside()
        {
            // Controller decision (movement step 2 opus review): a living player - or anything else physics-driven -
            // has a Rigidbody; a wall or a non-convex mesh does not. Standing flush against an enemy must not refuse
            // a dash the way standing flush against a wall does.
            Assert.IsTrue(DisplacementSweepRule.CanBlockAsStartInside(false));
            Assert.IsFalse(DisplacementSweepRule.CanBlockAsStartInside(true));
        }
    }
}
