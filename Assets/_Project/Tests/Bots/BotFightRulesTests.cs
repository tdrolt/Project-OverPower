using NUnit.Framework;
using Overpower.Bots;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// How a bot fights: whom it shoots, how it aims, where it stands, when it pulls the trigger, vents, backs off or takes a pack.
    /// Literal numbers made up for the test; Tudor's numbers live in BotConfig.
    /// </summary>
    public class BotFightRulesTests
    {
        [Test]
        public void TheClosestSeenEnemyIsTheTarget()
        {
            Assert.AreEqual(7, BotTargetRules.Pick(new[] { (5, 12f), (7, 6f), (9, 20f) }, -1, 3f));
            Assert.AreEqual(-1, BotTargetRules.Pick(new (int, float)[0], 5, 3f));
        }

        [Test]
        public void TheTargetIsKeptUnlessAnotherIsClearlyCloser()
        {
            Assert.AreEqual(5, BotTargetRules.Pick(new[] { (5, 10f), (7, 8f) }, 5, 3f), "only 2 m closer");
            Assert.AreEqual(7, BotTargetRules.Pick(new[] { (5, 10f), (7, 6.5f) }, 5, 3f), "3.5 m closer");
            Assert.AreEqual(7, BotTargetRules.Pick(new[] { (7, 9f) }, 5, 3f), "the old target is out of sight");
        }

        [Test]
        public void AShieldedOrDeadEnemyIsNoTarget()
        {
            Assert.IsTrue(BotTargetRules.Targetable(true, false));
            Assert.IsFalse(BotTargetRules.Targetable(true, true), "its respawn shield stops every shot: a player would not waste time on it");
            Assert.IsFalse(BotTargetRules.Targetable(false, false));
        }

        [Test]
        public void TheAimSettlesFromTheFirstErrorToTheSettledOne()
        {
            Assert.AreEqual(2f, BotAimRules.ErrorMetres(0f, 2f, 0.5f, 3f), 1e-4f);
            Assert.AreEqual(1.25f, BotAimRules.ErrorMetres(1.5f, 2f, 0.5f, 3f), 1e-4f);
            Assert.AreEqual(0.5f, BotAimRules.ErrorMetres(10f, 2f, 0.5f, 3f), 1e-4f);
        }

        [Test]
        public void ASlowShotLeadsAMovingTargetByTheFraction()
        {
            Vector3 aim = BotAimRules.Lead(new Vector3(10, 0, 0), new Vector3(0, 0, 4), 10f, 50f, 0.5f);
            Assert.AreEqual(10f, aim.x, 1e-4f);
            Assert.AreEqual(0.4f, aim.z, 1e-4f, "0.2 s flight, 0.8 m of travel, half of it");
            Assert.AreEqual(new Vector3(10, 0, 0), BotAimRules.Lead(new Vector3(10, 0, 0), new Vector3(0, 0, 4), 10f, 0f, 0.5f), "an instant beam needs no lead");
        }

        [Test]
        public void TheAimTurnsNoFasterThanAllowed()
        {
            Vector3 turned = BotAimRules.TurnToward(Vector3.forward, Vector3.right, 30f);
            Assert.AreEqual(30f, Vector3.Angle(Vector3.forward, turned), 0.01f);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.right, BotAimRules.TurnToward(Vector3.forward, Vector3.right, 120f)), 0.01f);
        }

        [Test]
        public void TheAimIsOnTargetWithinTheTolerance()
        {
            Assert.IsTrue(BotAimRules.OnTarget(Vector3.forward, Quaternion.Euler(0, 8, 0) * Vector3.forward, 10f));
            Assert.IsFalse(BotAimRules.OnTarget(Vector3.forward, Quaternion.Euler(0, 15, 0) * Vector3.forward, 10f));
        }

        [TestCase(3f, RangeMove.BackOff)]
        [TestCase(10f, RangeMove.Hold)]
        [TestCase(20f, RangeMove.Approach)]
        public void ItKeepsItsBuildsRange(float distance, RangeMove expected)
        {
            Assert.AreEqual(expected, BotRangeRules.Decide(distance, 8f, 14f, RangeMove.Hold, 1f));
        }

        [Test]
        public void OnceItMovesInOrOutItKeepsGoingUntilWellInsideItsRange()
        {
            Assert.AreEqual(RangeMove.BackOff, BotRangeRules.Decide(5.8f, 5f, 11f, RangeMove.BackOff, 1.5f), "still inside the dead band");
            Assert.AreEqual(RangeMove.Hold, BotRangeRules.Decide(5.8f, 5f, 11f, RangeMove.Hold, 1.5f), "holding: no step back just inside the edge");
            Assert.AreEqual(RangeMove.Hold, BotRangeRules.Decide(6.7f, 5f, 11f, RangeMove.BackOff, 1.5f), "far enough in");
            Assert.AreEqual(RangeMove.Approach, BotRangeRules.Decide(10.2f, 5f, 11f, RangeMove.Approach, 1.5f));
            Assert.AreEqual(RangeMove.Hold, BotRangeRules.Decide(9.3f, 5f, 11f, RangeMove.Approach, 1.5f));
        }

        [Test]
        public void ItShootsOnlyOnceReactedAimedInRangeAndAble()
        {
            Assert.IsTrue(BotFireRules.ShouldFire(true, true, true, 10f, 18f, 0.2f, 0.8f, 1f, 0.25f, true));
            Assert.IsFalse(BotFireRules.ShouldFire(false, true, true, 10f, 18f, 0.2f, 0.8f, 1f, 0.25f, true), "not seen");
            Assert.IsFalse(BotFireRules.ShouldFire(true, false, true, 10f, 18f, 0.2f, 0.8f, 1f, 0.25f, true), "still reacting");
            Assert.IsFalse(BotFireRules.ShouldFire(true, true, false, 10f, 18f, 0.2f, 0.8f, 1f, 0.25f, true), "not aimed");
            Assert.IsFalse(BotFireRules.ShouldFire(true, true, true, 20f, 18f, 0.2f, 0.8f, 1f, 0.25f, true), "out of range");
            Assert.IsFalse(BotFireRules.ShouldFire(true, true, true, 10f, 18f, 0.2f, 0.8f, 1f, 0.25f, false), "silenced, stunned or dead");
        }

        [Test]
        public void ItStopsShootingWhenHotUnlessTheTargetIsAlmostDead()
        {
            Assert.IsFalse(BotFireRules.ShouldFire(true, true, true, 10f, 18f, 0.85f, 0.8f, 0.6f, 0.25f, true));
            Assert.IsTrue(BotFireRules.ShouldFire(true, true, true, 10f, 18f, 0.85f, 0.8f, 0.2f, 0.25f, true));
        }

        [Test]
        public void ItReactsAfterTheDelay()
        {
            Assert.IsFalse(BotFireRules.Reacted(0.3f, 0.5f));
            Assert.IsTrue(BotFireRules.Reacted(0.5f, 0.5f));
        }

        [Test]
        public void ItVentsOnlyInTheWindowAndOnlyWhenItDecidedToHitIt()
        {
            Assert.IsTrue(BotVentRules.PressNow(true, true, true, false));
            Assert.IsFalse(BotVentRules.PressNow(true, false, true, false), "window not open yet");
            Assert.IsFalse(BotVentRules.PressNow(true, true, false, false), "this lockout it misses");
            Assert.IsFalse(BotVentRules.PressNow(true, true, true, true), "one try per lockout");
            Assert.IsFalse(BotVentRules.PressNow(false, true, true, false), "not locked out");
        }

        [Test]
        public void ItBacksOffWhenLowAndComesBackWhenHealed()
        {
            Assert.AreEqual(BotStance.Retreat, BotHealthRules.Stance(BotStance.Fight, 0.3f, 0.35f, 0.7f, false));
            Assert.AreEqual(BotStance.Fight, BotHealthRules.Stance(BotStance.Fight, 0.5f, 0.35f, 0.7f, false));
            Assert.AreEqual(BotStance.Retreat, BotHealthRules.Stance(BotStance.Retreat, 0.5f, 0.35f, 0.7f, false), "half way back is not enough");
            Assert.AreEqual(BotStance.Fight, BotHealthRules.Stance(BotStance.Retreat, 0.75f, 0.35f, 0.7f, false));
        }

        [Test]
        public void ACorneredBotFightsBack()
        {
            Assert.AreEqual(BotStance.Fight, BotHealthRules.Stance(BotStance.Retreat, 0.2f, 0.35f, 0.7f, true));
            Assert.AreEqual(BotStance.Fight, BotHealthRules.Stance(BotStance.Fight, 0.2f, 0.35f, 0.7f, true));
        }

        [Test]
        public void ItTakesAHealthPackOnlyWhenHurtQuietAndClose()
        {
            Assert.IsTrue(BotPackRules.WantsHealthPack(0.4f, 0.5f, false, 20f, 25f));
            Assert.IsFalse(BotPackRules.WantsHealthPack(0.6f, 0.5f, false, 20f, 25f), "not hurt enough");
            Assert.IsFalse(BotPackRules.WantsHealthPack(0.4f, 0.5f, true, 20f, 25f), "an enemy in sight");
            Assert.IsFalse(BotPackRules.WantsHealthPack(0.4f, 0.5f, false, 30f, 25f), "too far");
        }

        [Test]
        public void ItTakesAnUltimatePackOnlyWhenItsMeterIsNotFullAndThePackIsClose()
        {
            Assert.IsTrue(BotPackRules.WantsUltimatePack(false, 10f, 12f));
            Assert.IsFalse(BotPackRules.WantsUltimatePack(true, 10f, 12f));
            Assert.IsFalse(BotPackRules.WantsUltimatePack(false, 13f, 12f));
        }

        [Test]
        public void StrafingSwitchesSideAfterItsTime()
        {
            Assert.AreEqual(1, BotRangeRules.StrafeSide(1, 2.0f, 2.5f));
            Assert.AreEqual(-1, BotRangeRules.StrafeSide(1, 2.6f, 2.5f));
        }
    }
}
