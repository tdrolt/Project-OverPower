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

        private static readonly float[] Kept = { 0.75f, 0.5f };

        [Test]
        public void EachBounceReadsItsOwnEntryOfTheList()
        {
            Assert.AreEqual(0.75f, BounceRules.RangeKept(Kept, 1), 0.0001f);
            Assert.AreEqual(0.5f, BounceRules.RangeKept(Kept, 2), 0.0001f);
        }

        [Test]
        public void ABounceBeyondTheListCostsNoRange()
        {
            Assert.AreEqual(1f, BounceRules.RangeKept(Kept, 3), 0.0001f);
            Assert.AreEqual(1f, BounceRules.RangeKept(Kept, 100), 0.0001f);
        }

        [Test]
        public void NoListOrAnEmptyListNeverShortensTheShot()
        {
            Assert.AreEqual(1f, BounceRules.RangeKept(null, 1), 0.0001f);
            Assert.AreEqual(1f, BounceRules.RangeKept(new float[0], 1), 0.0001f);
        }

        [Test]
        public void ANumberBelowTheFirstBounceCostsNoRange()
        {
            Assert.AreEqual(1f, BounceRules.RangeKept(Kept, 0), 0.0001f);
            Assert.AreEqual(1f, BounceRules.RangeKept(Kept, -1), 0.0001f);
        }

        [Test]
        public void ActorZeroIsNotARealPlayerEither()
        {
            Assert.IsFalse(BounceRules.BouncesOffCover(0, 0));
        }
    }
}
