using NUnit.Framework;
using Overpower.Abilities;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 2026-09-21: "make the dash go towards the player movement direction (like using wasd
    /// and where the player is currently moving to dash that way) and keep the blink at cursor
    /// location" - reversing his own 2026-09-13 call, pinned until today by DashAbility's class
    /// comment, that a dash always aims at the cursor regardless of WASD. DashDirectionRule.Choose is
    /// the pulled-out pure decision (same reasoning as every other *Rule class - OutOfArenaRule,
    /// DisplacementSweepRule, ...): tested here without a scene, called from DashAbility.TryBuildCast.
    ///
    /// No existing test pinned "the dash ignores WASD" as a design pin (grepped the whole repo for
    /// "WASD"/"ignores" before writing this file) - the only place that decision lived was
    /// DashAbility's own class/method comments, both rewritten alongside this change, so there was
    /// nothing else to update.
    /// </summary>
    public class DashDirectionRuleTests
    {
        // Arbitrary but realistic thresholds for these tests - not the production constants
        // themselves (DashAbility keeps its own), just values that let "above" and "below" mean
        // something concrete below.
        private const float MovementThreshold = 0.1f;
        private const float CursorOnSelfThreshold = 0.1f;

        private static Vector3 Choose(Vector3 moveDirection, Vector3 towardCursor, Vector3 aimDirection) =>
            DashDirectionRule.Choose(moveDirection, towardCursor, aimDirection, MovementThreshold, CursorOnSelfThreshold);

        [Test]
        public void MovingGoesAlongTheMoveDirectionNotTheCursor()
        {
            // Moving "right" (camera-relative +X) while the cursor sits off to the left and behind -
            // the dash must go right, never toward the cursor.
            Vector3 result = Choose(
                moveDirection: new Vector3(1f, 0f, 0f),
                towardCursor: new Vector3(-5f, 0f, -3f),
                aimDirection: new Vector3(0f, 0f, -1f));

            Assert.AreEqual(new Vector3(1f, 0f, 0f), result);
        }

        [Test]
        public void AnyMoveLengthAboveTheThresholdNormalisesToTheSameDirection()
        {
            // A diagonal, partially-held stick (length well under 1) still picks a full unit vector,
            // in the same direction - CastContext.MoveDirection is already clamped to length <= 1,
            // but Choose must not scale the dash's speed by how hard the stick was pushed.
            Vector3 result = Choose(
                moveDirection: new Vector3(0.3f, 0f, 0.3f),
                towardCursor: new Vector3(10f, 0f, 0f),
                aimDirection: Vector3.forward);

            Vector3 expected = new Vector3(0.3f, 0f, 0.3f).normalized;
            Assert.AreEqual(expected.x, result.x, 1e-5f);
            Assert.AreEqual(expected.z, result.z, 1e-5f);
            Assert.AreEqual(1f, result.magnitude, 1e-5f);
        }

        [Test]
        public void MoveDirectionIsFlattenedBeforeDeciding()
        {
            // A stray Y component (there should never be one, but the rule must not trust that)
            // is dropped, exactly like TryBuildCast used to flatten towardCursor by hand.
            Vector3 result = Choose(
                moveDirection: new Vector3(0f, 5f, 1f),
                towardCursor: new Vector3(-1f, 0f, 0f),
                aimDirection: Vector3.forward);

            Assert.AreEqual(new Vector3(0f, 0f, 1f), result);
        }

        [Test]
        public void StandingStillGoesTowardTheCursor()
        {
            Vector3 result = Choose(
                moveDirection: Vector3.zero,
                towardCursor: new Vector3(3f, 0f, 4f),
                aimDirection: new Vector3(-1f, 0f, 0f));

            Assert.AreEqual(new Vector3(0.6f, 0f, 0.8f), result);
        }

        [Test]
        public void StandingStillWithTheCursorOnSelfFallsBackToTheAimDirection()
        {
            // towardCursor's length is inside CursorOnSelfThreshold - "toward it" would not mean
            // anything, so the dash faces the way the player is already aiming instead.
            Vector3 result = Choose(
                moveDirection: Vector3.zero,
                towardCursor: new Vector3(0.01f, 0f, 0.02f),
                aimDirection: new Vector3(0f, 0f, -1f));

            Assert.AreEqual(new Vector3(0f, 0f, -1f), result);
        }

        [Test]
        public void ATinyStickDriftBelowTheThresholdCountsAsStandingStill()
        {
            // Below MovementThreshold - floating-point noise from a key released this same frame, or
            // a controller's dead-zone leak - must read exactly like Vector3.zero: toward the cursor,
            // never off in the drift's own near-arbitrary direction.
            Vector3 result = Choose(
                moveDirection: new Vector3(0.02f, 0f, 0.01f),
                towardCursor: new Vector3(5f, 0f, 0f),
                aimDirection: Vector3.forward);

            Assert.AreEqual(new Vector3(1f, 0f, 0f), result);
        }

        [Test]
        public void MovingWinsEvenWithTheCursorOnSelf()
        {
            // Moving always wins outright - the cursor-on-self fallback only matters while standing
            // still, so a move held at the same time the cursor happens to sit on the player must
            // still dash along the move, not fall through to the aim direction.
            Vector3 result = Choose(
                moveDirection: new Vector3(0f, 0f, -1f),
                towardCursor: new Vector3(0.01f, 0f, 0f),
                aimDirection: new Vector3(1f, 0f, 0f));

            Assert.AreEqual(new Vector3(0f, 0f, -1f), result);
        }
    }
}
