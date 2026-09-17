using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>A9 (Tudor 2026-09-17 evening): mines can be placed anywhere within 2 m of the player, not only at
    /// their feet - the aim point is clamped to Placement Range. See MinePlacementRule.</summary>
    public class MinePlacementRuleTests
    {
        [Test]
        public void AnAimPointInsideRangeIsUnchanged()
        {
            Vector3 result = MinePlacementRule.ClampToRange(Vector3.zero, new Vector3(1f, 0f, 0f), range: 2f);

            Assert.AreEqual(new Vector3(1f, 0f, 0f), result);
        }

        [Test]
        public void AnAimPointBeyondRangeClampsToRangeAlongTheSameDirection()
        {
            Vector3 result = MinePlacementRule.ClampToRange(Vector3.zero, new Vector3(5f, 0f, 0f), range: 2f);

            Assert.AreEqual(new Vector3(2f, 0f, 0f), result);
        }

        [Test]
        public void AnAimPointExactlyOnTheRangeBoundaryIsUnchanged()
        {
            Vector3 result = MinePlacementRule.ClampToRange(Vector3.zero, new Vector3(2f, 0f, 0f), range: 2f);

            Assert.AreEqual(new Vector3(2f, 0f, 0f), result);
        }

        [Test]
        public void AZeroLengthAimDropsAtThePlayersOwnPosition()
        {
            // Aiming at your own feet has no direction to clamp along - the same degenerate case
            // TeleportAbility.ClampToRange already handles identically.
            Vector3 player = new Vector3(3f, 0.5f, 4f);

            Vector3 result = MinePlacementRule.ClampToRange(player, player, range: 2f);

            Assert.AreEqual(player, result);
        }

        [Test]
        public void ClampingPreservesTheAimPointsOwnHeight()
        {
            // Height is resolved by the caller afterwards (GroundSnap) - this rule only ever touches XZ.
            Vector3 result = MinePlacementRule.ClampToRange(Vector3.zero, new Vector3(5f, 1.5f, 0f), range: 2f);

            Assert.AreEqual(1.5f, result.y, 1e-5f);
        }

        [Test]
        public void ClampingWorksAroundAnOffCentrePlayer()
        {
            Vector3 player = new Vector3(10f, 0f, 10f);
            Vector3 aim = new Vector3(10f, 0f, 20f); // 10 m north, beyond a 2 m range

            Vector3 result = MinePlacementRule.ClampToRange(player, aim, range: 2f);

            Assert.AreEqual(new Vector3(10f, 0f, 12f), result);
        }
    }
}
