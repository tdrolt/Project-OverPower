using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Walking away from the aim (Tudor, 29 Oct, from a friend's test): straight back (a 45-degree slice) is 10% slower, a back
    /// diagonal 5%; shooting while stepping back or back-diagonally adds 10%; strafing left or right is never slowed.
    /// </summary>
    public class BackpedalRuleTests
    {
        private static readonly Vector3 Aim = Vector3.forward;

        private static BackpedalRule.Settings S => new BackpedalRule.Settings
        {
            backSlow = 0.1f, backDiagonalSlow = 0.05f, shootingAwaySlow = 0.1f, backSliceDegrees = 45f, strafeSliceDegrees = 45f,
        };

        private static Vector3 At(float degreesFromAim) =>
            Quaternion.Euler(0f, degreesFromAim, 0f) * Aim;

        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(-60f)]
        public void StepsTowardTheAimAreForward(float degrees) =>
            Assert.AreEqual(MoveHeading.Forward, BackpedalRule.HeadingOf(At(degrees), Aim, 45f, 45f));

        [TestCase(90f)]
        [TestCase(-90f)]
        [TestCase(70f)]
        [TestCase(110f)]
        public void SidewaysStepsAreStrafes(float degrees) =>
            Assert.AreEqual(MoveHeading.Strafe, BackpedalRule.HeadingOf(At(degrees), Aim, 45f, 45f));

        [TestCase(135f)]
        [TestCase(-135f)]
        [TestCase(115f)]
        [TestCase(155f)]
        public void TheBackDiagonalsAreBackDiagonals(float degrees) =>
            Assert.AreEqual(MoveHeading.BackDiagonal, BackpedalRule.HeadingOf(At(degrees), Aim, 45f, 45f));

        [TestCase(180f)]
        [TestCase(160f)]
        [TestCase(-160f)]
        public void WithinTheBackSliceIsBack(float degrees) =>
            Assert.AreEqual(MoveHeading.Back, BackpedalRule.HeadingOf(At(degrees), Aim, 45f, 45f));

        [Test]
        public void HeightIsIgnored() =>
            Assert.AreEqual(MoveHeading.Back, BackpedalRule.HeadingOf(new Vector3(0f, 3f, -1f), new Vector3(0f, -2f, 1f), 45f, 45f));

        [Test]
        public void StandingStillOrNoAimIsNeverSlowed()
        {
            Assert.AreEqual(MoveHeading.Forward, BackpedalRule.HeadingOf(Vector3.zero, Aim, 45f, 45f));
            Assert.AreEqual(MoveHeading.Forward, BackpedalRule.HeadingOf(Vector3.back, Vector3.zero, 45f, 45f));
        }

        [Test]
        public void TheSlicesFollowTheirSettings()
        {
            Assert.AreEqual(MoveHeading.Back, BackpedalRule.HeadingOf(At(135f), Aim, 90f, 45f));
            Assert.AreEqual(MoveHeading.Strafe, BackpedalRule.HeadingOf(At(130f), Aim, 45f, 90f));
        }

        [TestCase(MoveHeading.Forward, false, 1f)]
        [TestCase(MoveHeading.Strafe, false, 1f)]
        [TestCase(MoveHeading.BackDiagonal, false, 0.95f)]
        [TestCase(MoveHeading.Back, false, 0.9f)]
        [TestCase(MoveHeading.Forward, true, 1f)]
        [TestCase(MoveHeading.Strafe, true, 1f)]
        [TestCase(MoveHeading.BackDiagonal, true, 0.85f)]
        [TestCase(MoveHeading.Back, true, 0.8f)]
        public void TheSlowsAddUp(MoveHeading heading, bool shooting, float kept) =>
            Assert.AreEqual(kept, BackpedalRule.SpeedMultiplier(heading, shooting, S), 1e-5f);

        [Test]
        public void SpeedNeverGoesBelowZero()
        {
            BackpedalRule.Settings s = S;
            s.backSlow = 0.8f;
            s.shootingAwaySlow = 0.5f;
            Assert.AreEqual(0f, BackpedalRule.SpeedMultiplier(MoveHeading.Back, true, s), 1e-5f);
        }

        [TestCase(0f, 0.1f, 0.05f, true)]
        [TestCase(0.12f, 0.1f, 0.05f, true)]
        [TestCase(0.2f, 0.1f, 0.05f, false)]
        [TestCase(1.4f, 1.5f, 0f, true)]
        [TestCase(float.PositiveInfinity, 0.1f, 0.05f, false)]
        public void TheGunCountsAsShootingForOneShotsGap(float since, float interval, float grace, bool shooting) =>
            Assert.AreEqual(shooting, BackpedalRule.IsShooting(since, interval, grace));

        [Test]
        public void TheMotorAsksTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerMotor), "UpdateBackpedal", typeof(BackpedalRule).GetMethod(nameof(BackpedalRule.HeadingOf))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerMotor), "UpdateBackpedal", typeof(BackpedalRule).GetMethod(nameof(BackpedalRule.IsShooting))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerMotor), "UpdateBackpedal", typeof(BackpedalRule).GetMethod(nameof(BackpedalRule.SpeedMultiplier))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerMotor), "UpdateBackpedal", typeof(PlayerMotor).GetMethod(nameof(PlayerMotor.AddSpeedMultiplier))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerMotor), "Update", typeof(PlayerMotor).GetMethod("UpdateBackpedal",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)), "every frame, for the owner");
        }
    }
}
