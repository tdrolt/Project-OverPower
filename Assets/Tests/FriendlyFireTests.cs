using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Covers the shared no-friendly-fire rule pulled out of ProjectileMotor.FliesThrough,
    /// ExplodeOnImpact.IsFriendly and BeamResolver.PassesThrough during the laser code review (see
    /// FriendlyFire.IsSelfOrTeammate). BeamResolverTests exercises the same rule indirectly through
    /// BeamResolver.Resolve; these tests check it directly, plain ints in and out.</summary>
    public class FriendlyFireTests
    {
        private const int ShooterActor = 1;
        private const int ShooterTeam = 0;

        [Test]
        public void TheShooterItselfIsATeammate()
        {
            Assert.IsTrue(FriendlyFire.IsSelfOrTeammate(ShooterActor, ShooterActor, ShooterTeam, ShooterTeam));
        }

        [Test]
        public void ADifferentActorOnTheSameTeamIsATeammate()
        {
            Assert.IsTrue(FriendlyFire.IsSelfOrTeammate(ShooterActor, 2, ShooterTeam, ShooterTeam));
        }

        [Test]
        public void ADifferentActorOnADifferentTeamIsNotATeammate()
        {
            Assert.IsFalse(FriendlyFire.IsSelfOrTeammate(ShooterActor, 2, ShooterTeam, ShooterTeam + 1));
        }

        [Test]
        public void AnUnknownShooterTeamFailsOpenSoAMatchingTargetTeamIsNotATeammate()
        {
            // Matches Teams.AreSameTeam and BeamResolver's own version of this test: an unknown
            // team must never silently make someone invulnerable, so a negative shooterTeamId never
            // counts as a match even against a target reporting the same negative number.
            Assert.IsFalse(FriendlyFire.IsSelfOrTeammate(ShooterActor, 2, -1, -1));
        }

        [Test]
        public void AnUnknownShooterTeamStillRecognisesTheShooterItself()
        {
            Assert.IsTrue(FriendlyFire.IsSelfOrTeammate(ShooterActor, ShooterActor, -1, -1));
        }
    }
}
