using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Which Cover wall a ricocheting shot bounces off (BounceRules.BouncesOffCover): its placer's own, nobody else's.</summary>
    public class BounceRulesTests
    {
        private const int Shooter = 7;
        private const int SomeoneElse = 12;

        [Test]
        public void AShotBouncesOffTheCoverWallItsShooterPlaced()
        {
            Assert.IsTrue(BounceRules.BouncesOffCover(Shooter, Shooter));
        }

        [Test]
        public void AShotStopsOnACoverWallSomeoneElsePlaced()
        {
            Assert.IsFalse(BounceRules.BouncesOffCover(Shooter, SomeoneElse));
        }

        [Test]
        public void ATeammatesWallStopsTheShotBecauseOnlyThePlacerCounts()
        {
            // A teammate is just a different actor number: the rule never sees teams, so a friend's wall is blocked exactly like an enemy's.
            Assert.IsFalse(BounceRules.BouncesOffCover(SomeoneElse, Shooter));
        }

        [Test]
        public void AWallWhosePlacerIsUnknownIsNobodysOwn()
        {
            Assert.IsFalse(BounceRules.BouncesOffCover(Shooter, -1));
        }

        [Test]
        public void AShotWhoseShooterIsUnknownBouncesOffNothing()
        {
            Assert.IsFalse(BounceRules.BouncesOffCover(-1, Shooter));
        }

        [Test]
        public void TwoUnknownActorsAreNotTheSamePlayer()
        {
            Assert.IsFalse(BounceRules.BouncesOffCover(-1, -1));
        }

        [Test]
        public void ActorZeroIsNotARealPlayerEither()
        {
            Assert.IsFalse(BounceRules.BouncesOffCover(0, 0));
        }
    }
}
