using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Task 5b-2: shop tooltip timing (D5), armour limit wording (D5), tree arrows (D6),
    /// and the combat clock after death/respawn.</summary>
    public class ShopTooltipArmourArrowTests
    {
        // ---- tooltip delay ----

        [Test]
        public void TooltipIsHiddenBeforeTheDelay()
        {
            var t = new HoverTooltipTimer();
            t.Enter("w:1");
            t.Tick(0.5f);
            Assert.IsFalse(t.IsShown(1f));
        }

        [Test]
        public void TooltipShowsOnceTheDelayHasPassed()
        {
            var t = new HoverTooltipTimer();
            t.Enter("w:1");
            t.Tick(0.6f);
            t.Tick(0.5f);
            Assert.IsTrue(t.IsShown(1f));
        }

        [Test]
        public void MovingToAnotherItemRestartsTheWait()
        {
            var t = new HoverTooltipTimer();
            t.Enter("w:1");
            t.Tick(0.9f);
            t.Exit("w:1");
            t.Enter("w:2");
            t.Tick(0.9f);
            Assert.IsFalse(t.IsShown(1f));
            t.Tick(0.2f);
            Assert.IsTrue(t.IsShown(1f));
        }

        [Test]
        public void MovingOffHidesAtOnce()
        {
            var t = new HoverTooltipTimer();
            t.Enter("w:1");
            t.Tick(2f);
            Assert.IsTrue(t.IsShown(1f));
            t.Exit("w:1");
            Assert.IsFalse(t.IsShown(1f));
        }

        [Test]
        public void ALateExitFromTheOldItemDoesNotHideTheNewOne()
        {
            var t = new HoverTooltipTimer();
            t.Enter("w:1");
            t.Enter("w:2");
            t.Exit("w:1");
            t.Tick(1.5f);
            Assert.IsTrue(t.IsShown(1f));
        }

        [Test]
        public void ReEnteringTheSameItemDoesNotRestartTheWait()
        {
            var t = new HoverTooltipTimer();
            t.Enter("w:1");
            t.Tick(0.8f);
            t.Enter("w:1");
            t.Tick(0.3f);
            Assert.IsTrue(t.IsShown(1f));
        }

        [Test]
        public void NothingHoveredShowsNothingEvenAtDelayZero()
        {
            var t = new HoverTooltipTimer();
            t.Tick(5f);
            Assert.IsFalse(t.IsShown(0f));
        }

        // ---- armour label ----

        [Test]
        public void ArmourLabelNamesTheNextUpgradeOutOfTheLimit()
        {
            Assert.AreEqual("Upgrade 1 of 2", ArmorLimitLabel.Text(0, 2));
            Assert.AreEqual("Upgrade 2 of 2", ArmorLimitLabel.Text(1, 2));
        }

        [Test]
        public void ArmourLabelSaysMaxAtTheLimit()
        {
            Assert.AreEqual("2 of 2 (max)", ArmorLimitLabel.Text(2, 2));
        }

        [Test]
        public void ArmourLabelFollowsTheConfiguredLimit()
        {
            Assert.AreEqual("Upgrade 1 of 3", ArmorLimitLabel.Text(0, 3));
            Assert.AreEqual("3 of 3 (max)", ArmorLimitLabel.Text(3, 3));
        }

        // ---- tree arrows ----

        [Test]
        public void OneArrowPerParentChildPairAndNoneForTheRoot()
        {
            var tree = new WeaponUpgradeTree(new List<(int, int)>
            {
                (1, -1), (2, 1), (3, 2), (4, 2), (5, 1), (6, 5), (7, 5),
            });
            IReadOnlyList<(int parent, int child)> edges = tree.Edges();
            Assert.AreEqual(6, edges.Count);
            CollectionAssert.AreEquivalent(new[] { (1, 2), (2, 3), (2, 4), (1, 5), (5, 6), (5, 7) }, edges);
            foreach (var e in edges) Assert.AreNotEqual(1, e.child, "the starting weapon is never an arrow's target");
        }

        [Test]
        public void ANewWeaponGetsItsArrowAutomatically()
        {
            var tree = new WeaponUpgradeTree(new List<(int, int)> { (1, -1), (2, 1), (3, 2) });
            CollectionAssert.Contains(tree.Edges(), (2, 3));
        }

        [Test]
        public void ARealSizedTreeHasTwelveArrows()
        {
            var tree = new WeaponUpgradeTree(new List<(int, int)>
            {
                (1, -1), (2, 1), (3, 2), (4, 2), (5, 1), (6, 5), (7, 5),
                (8, 1), (9, 8), (10, 8), (11, 1), (12, 11), (13, 11),
            });
            Assert.AreEqual(12, tree.Edges().Count);
        }

        // ---- combat clock after death / respawn ----

        [Test]
        public void OutOfCombatValueClearsEveryReadersThreshold()
        {
            float clock = CombatClockRule.OutOfCombatValue(6f, 5f, 4f);
            Assert.GreaterOrEqual(clock, 6f);
            Assert.GreaterOrEqual(clock, 5f);
            Assert.GreaterOrEqual(clock, 4f);
        }

        [Test]
        public void OutOfCombatValueUsesTheLargestThreshold()
        {
            Assert.AreEqual(8f, CombatClockRule.OutOfCombatValue(6f, 8f, 5f));
            Assert.AreEqual(0f, CombatClockRule.OutOfCombatValue());
        }
    }
}
