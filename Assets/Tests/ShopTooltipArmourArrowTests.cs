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

        private static string L(int bought, int max) =>
            ArmorLimitLabel.Text(bought, max, ArmorLimitLabel.DefaultUpgradeFormat, ArmorLimitLabel.DefaultMaxFormat);

        [Test]
        public void ArmourLabelUsesTheGivenFormats()
        {
            Assert.AreEqual("#1/2", ArmorLimitLabel.Text(0, 2, "#{0}/{1}", "full {0}"));
            Assert.AreEqual("full 2", ArmorLimitLabel.Text(2, 2, "#{0}/{1}", "full {0}"));
        }

        [Test]
        public void ArmourLabelNamesTheNextUpgradeOutOfTheLimit()
        {
            Assert.AreEqual("Upgrade 1 of 2", L(0, 2));
            Assert.AreEqual("Upgrade 2 of 2", L(1, 2));
        }

        [Test]
        public void ArmourLabelSaysMaxAtTheLimit()
        {
            Assert.AreEqual("2 of 2 (max)", L(2, 2));
        }

        [Test]
        public void ArmourLabelFollowsTheConfiguredLimit()
        {
            Assert.AreEqual("Upgrade 1 of 3", L(0, 3));
            Assert.AreEqual("3 of 3 (max)", L(3, 3));
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

        [Test]
        public void DeathAndRespawnPutTheClockAtTheLargestGate()
        {
            Assert.AreEqual(6f, CombatClockRule.AfterDeath(6f, 5f, 4f));
            Assert.AreEqual(6f, CombatClockRule.AfterRespawn(6f, 5f, 4f));
            Assert.AreEqual(7.5f, CombatClockRule.AfterDeath(6f, 5f, 7.5f), "a long armour delay must be cleared too");
            Assert.AreEqual(7.5f, CombatClockRule.AfterRespawn(6f, 5f, 7.5f));
        }

        [Test]
        public void DealingDamageWhileAliveRestartsTheClock()
        {
            Assert.AreEqual(0f, CombatClockRule.AfterDealtDamage(false, 12f));
        }

        [Test]
        public void DealingDamageWhileDeadLeavesTheClockAlone()
        {
            Assert.AreEqual(6f, CombatClockRule.AfterDealtDamage(true, 6f));
        }

        [Test]
        public void TheLongestRechargeDelayOverAllLevelsIsUsed()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<Overpower.Data.ArmorConfig>();
            try
            {
                typeof(Overpower.Data.ArmorConfig).GetField("rechargeSeconds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(config, new[] { 6f, 4f, 9f, 3f });
                Assert.AreEqual(9f, config.MaxRechargeSeconds);
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

        // ---- arrow shape by geometry ----

        private static TreeNodeBox Box(float cx, float top) => new TreeNodeBox(cx - 65f, cx + 65f, top - 64f, top);

        [Test]
        public void AnotherColumnIsAnSCurve()
        {
            Assert.AreEqual(TreeArrowShape.SCurve, TreeArrowShapeRule.Choose(Box(0, 0), Box(200, -100), new TreeNodeBox[0]));
        }

        [Test]
        public void DirectlyBelowWithNothingBetweenIsStraight()
        {
            Assert.AreEqual(TreeArrowShape.Straight, TreeArrowShapeRule.Choose(Box(0, 0), Box(0, -100), new TreeNodeBox[0]));
        }

        [Test]
        public void ANodeBetweenSendsTheArrowDownTheSide()
        {
            Assert.AreEqual(TreeArrowShape.SideLane, TreeArrowShapeRule.Choose(Box(0, 0), Box(0, -200), new[] { Box(0, -100) }));
        }

        [Test]
        public void ANodeInAnotherColumnDoesNotForceTheSide()
        {
            Assert.AreEqual(TreeArrowShape.Straight, TreeArrowShapeRule.Choose(Box(0, 0), Box(0, -200), new[] { Box(160, -100) }));
        }

        [Test]
        public void TheMiddleOfThreeChildrenSideBySideGetsAStraightArrowNotACurve()
        {
            var left = Box(-160, -100); var mid = Box(0, -100); var right = Box(160, -100);
            var parent = Box(0, 0);
            Assert.AreEqual(TreeArrowShape.SCurve, TreeArrowShapeRule.Choose(parent, left, new[] { mid, right }));
            Assert.AreEqual(TreeArrowShape.Straight, TreeArrowShapeRule.Choose(parent, mid, new[] { left, right }));
        }
    }
}
