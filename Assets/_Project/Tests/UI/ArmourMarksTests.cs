using System.Reflection;
using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Which armour-upgrade marks a plate shows for the two armour paths' levels, and that every plate asks this rule.</summary>
    public class ArmourMarksTests
    {
        [Test]
        public void NoUpgradeShowsNoMark()
        {
            ArmourMarks.Marks m = ArmourMarks.Compose(0, 0);
            Assert.AreEqual(0, m.cells);
            Assert.AreEqual(0, m.squares);
            Assert.AreEqual(0, m.chevrons);
        }

        [Test]
        public void EachAbsorbLevelIsASquareSideBySide()
        {
            ArmourMarks.Marks one = ArmourMarks.Compose(1, 0);
            Assert.AreEqual(1, one.cells);
            Assert.AreEqual(1, one.squares);
            Assert.AreEqual(0, one.chevrons);
            ArmourMarks.Marks two = ArmourMarks.Compose(2, 0);
            Assert.AreEqual(2, two.cells);
            Assert.AreEqual(2, two.squares);
        }

        [Test]
        public void EachRechargeLevelIsAVStackedInOneMark()
        {
            ArmourMarks.Marks one = ArmourMarks.Compose(0, 1);
            Assert.AreEqual(1, one.cells);
            Assert.AreEqual(0, one.squares);
            Assert.AreEqual(1, one.chevrons);
            Assert.IsFalse(one.chevronsInSquare);
            ArmourMarks.Marks two = ArmourMarks.Compose(0, 2);
            Assert.AreEqual(1, two.cells, "the second v stacks on the first, not beside it");
            Assert.AreEqual(2, two.chevrons);
        }

        [Test]
        public void BothPathsPutTheVInsideTheSquare()
        {
            ArmourMarks.Marks m = ArmourMarks.Compose(1, 1);
            Assert.AreEqual(1, m.cells);
            Assert.AreEqual(1, m.squares);
            Assert.AreEqual(1, m.chevrons);
            Assert.IsTrue(m.chevronsInSquare);
        }

        [Test]
        public void TwoAbsorbAndOneRechargeAreTwoSquaresOneWithTheV()
        {
            ArmourMarks.Marks m = ArmourMarks.Compose(2, 1);
            Assert.AreEqual(2, m.cells);
            Assert.AreEqual(2, m.squares);
            Assert.AreEqual(1, m.chevrons);
            Assert.IsTrue(m.chevronsInSquare);
        }

        [Test]
        public void OneAbsorbAndTwoRechargeAreOneSquareHoldingBothVs()
        {
            ArmourMarks.Marks m = ArmourMarks.Compose(1, 2);
            Assert.AreEqual(1, m.cells);
            Assert.AreEqual(1, m.squares);
            Assert.AreEqual(2, m.chevrons);
            Assert.IsTrue(m.chevronsInSquare);
        }

        [Test]
        public void ABrokenLevelShowsNothing()
        {
            ArmourMarks.Marks m = ArmourMarks.Compose(-1, -2);
            Assert.AreEqual(0, m.cells);
            Assert.AreEqual(0, m.squares);
            Assert.AreEqual(0, m.chevrons);
            Assert.IsFalse(m.chevronsInSquare);
        }

        private static MethodInfo M(System.Type type, string name) => type.GetMethod(name);

        [Test]
        public void ThePlateComposesItsMarksFromTheRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(NameplateView), nameof(NameplateView.ShowArmourMarks), M(typeof(ArmourMarks), nameof(ArmourMarks.Compose))));

        [Test]
        public void EveryCopyOfAPlayerHandsItsArmourLevelsToThePlate() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), "UpdateOverheadBar", M(typeof(NameplateView), nameof(NameplateView.ShowArmourMarks))));
    }
}
