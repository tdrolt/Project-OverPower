using NUnit.Framework;
using UnityEngine;
using Overpower.Arena;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Arena amendment 1, step 4a: pins BarrierCrossingRule.ExitPoints against one shared footprint - a
    /// 10 x 0.6 m barrier run, centred on the origin, its long axis along world +X - and BoxFootprint.OverlapsCircle
    /// against the same footprint's corner. A capsule radius of 0.7 m and DisplacementSweepRule's own skin (0.05 m)
    /// match the real player and the real sweep, so 1.05 m (0.3 + 0.7 + 0.05) recurs throughout.</summary>
    public class BarrierCrossingRuleTests
    {
        private const float Radius = 0.7f;
        private static readonly BoxFootprint Barrier = new BoxFootprint(Vector2.zero, Vector2.right, 5f, 0.3f);

        [Test]
        public void PastTheMiddleTheCapsuleLeavesByTheFarFace()
        {
            // 0.1 m past the middle line, on the +Across (+Z) side.
            BarrierCrossingRule.ExitPoints(new Vector2(0f, 0.1f), Vector2.right, Radius, Barrier, out Vector2 first, out Vector2 other);

            Assert.AreEqual(0f, first.x, 0.0001f, "the along position must not shift");
            Assert.AreEqual(1.05f, first.y, 0.0001f, "past the middle, the first candidate finishes the crossing (the far face)");
            Assert.AreEqual(-1.05f, other.y, 0.0001f, "the other candidate is the mirror, back the way it came");
        }

        [Test]
        public void ShortOfTheMiddleItGoesBackOutTheWayItCame()
        {
            // 0.1 m short of the middle line, on the -Across (-Z) side.
            BarrierCrossingRule.ExitPoints(new Vector2(2f, -0.1f), Vector2.right, Radius, Barrier, out Vector2 first, out Vector2 other);

            Assert.AreEqual(2f, first.x, 0.0001f, "the along position must not shift");
            Assert.AreEqual(-1.05f, first.y, 0.0001f, "short of the middle, the first candidate drops back the way it came");
            Assert.AreEqual(1.05f, other.y, 0.0001f, "the other candidate is the mirror, finishing the crossing");
        }

        [Test]
        public void ExactlyOnTheMiddleTheWayTheMoveWasHeadingWins()
        {
            BarrierCrossingRule.ExitPoints(new Vector2(-1f, 0f), new Vector2(0f, 1f), Radius, Barrier, out Vector2 firstUp, out _);
            Assert.AreEqual(1.05f, firstUp.y, 0.0001f, "heading +Across on the middle line exits on the +Across side");

            BarrierCrossingRule.ExitPoints(new Vector2(-1f, 0f), new Vector2(0f, -1f), Radius, Barrier, out Vector2 firstDown, out _);
            Assert.AreEqual(-1.05f, firstDown.y, 0.0001f, "heading -Across on the middle line exits on the -Across side");
        }

        [Test]
        public void TheExitClearsByRadiusPlusSkinAndKeepsTheAlongPosition()
        {
            BarrierCrossingRule.ExitPoints(new Vector2(3f, 0.05f), Vector2.right, Radius, Barrier, out Vector2 first, out Vector2 other);

            float expectedClearance = Barrier.HalfWidth + Radius + DisplacementSweepRule.SkinMetres;
            Assert.AreEqual(1.05f, expectedClearance, 0.0001f, "sanity: 0.3 + 0.7 + 0.05");
            Assert.AreEqual(expectedClearance, Mathf.Abs(first.y), 0.0001f);
            Assert.AreEqual(expectedClearance, Mathf.Abs(other.y), 0.0001f);
            Assert.AreEqual(3f, first.x, 0.0001f);
            Assert.AreEqual(3f, other.x, 0.0001f);
        }

        [Test]
        public void AShallowMoveStillLeavesStraightOutNotAlongTheBarrier()
        {
            // A 10-degree move: mostly along the barrier (+X), only slightly across (+Z) - on the middle line, so
            // the shallow across component alone must decide the side, and the exit must still be a pure Across
            // offset (never blended with the along direction the move was actually heading).
            Vector2 shallowMove = new Vector2(Mathf.Cos(10f * Mathf.Deg2Rad), Mathf.Sin(10f * Mathf.Deg2Rad));
            BarrierCrossingRule.ExitPoints(new Vector2(1.5f, 0f), shallowMove, Radius, Barrier, out Vector2 first, out _);

            Assert.AreEqual(1.5f, first.x, 0.0001f, "the exit is straight out (Across only), never along the barrier");
            Assert.AreEqual(1.05f, first.y, 0.0001f, "the shallow +Across component still picks the +Across side");
        }

        [Test]
        public void OverlapIsTheCapsuleCircleAgainstTheFootprint()
        {
            Vector2 corner = new Vector2(Barrier.HalfLength, Barrier.HalfWidth);
            Vector2 circleCentre = corner + new Vector2(0.5f, 0.5f);
            float distance = Vector2.Distance(corner, circleCentre);

            Assert.IsTrue(Barrier.OverlapsCircle(circleCentre, distance + 0.001f), "a corner within the radius overlaps");
            Assert.IsFalse(Barrier.OverlapsCircle(circleCentre, distance - 0.001f), "1 mm short of the corner is clear");
        }
    }
}
