using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Dominion;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 1: what the shop opens each round. Tables are literal test values, never the asset.</summary>
    public class DominionShopRulesTests
    {
        private static readonly int[] Depth = { 0, 1, 2 };
        private static readonly int[] Armor = { 0, 1, 2 };

        [Test] public void RoundOneAllowsOnlyTheBaselineAndNoArmorUpgrade()
        {
            Assert.AreEqual(0, DominionShopRules.MaxWeaponDepth(1, Depth));
            Assert.AreEqual(0, DominionShopRules.ArmorUpgradesAllowed(1, Armor));
            Assert.IsTrue(DominionShopRules.WeaponAllowed(0, 1, Depth));
            Assert.IsFalse(DominionShopRules.WeaponAllowed(1, 1, Depth));
        }

        [Test] public void RoundTwoAllowsAFamilyAndOneUpgrade()
        {
            Assert.AreEqual(1, DominionShopRules.MaxWeaponDepth(2, Depth));
            Assert.AreEqual(1, DominionShopRules.ArmorUpgradesAllowed(2, Armor));
            Assert.IsTrue(DominionShopRules.WeaponAllowed(1, 2, Depth));
            Assert.IsFalse(DominionShopRules.WeaponAllowed(2, 2, Depth));
        }

        [Test] public void RoundThreeAllowsAFamilysUpgradeAndTwoUpgrades()
        {
            Assert.AreEqual(2, DominionShopRules.MaxWeaponDepth(3, Depth));
            Assert.AreEqual(2, DominionShopRules.ArmorUpgradesAllowed(3, Armor));
            Assert.IsTrue(DominionShopRules.WeaponAllowed(2, 3, Depth));
        }

        [Test] public void ARoundPastTheTableUsesTheLastEntryAndRoundBelowOneTheFirst()
        {
            Assert.AreEqual(2, DominionShopRules.MaxWeaponDepth(9, Depth));
            Assert.AreEqual(2, DominionShopRules.ArmorUpgradesAllowed(9, Armor));
            Assert.AreEqual(0, DominionShopRules.MaxWeaponDepth(0, Depth));
            Assert.AreEqual(0, DominionShopRules.ArmorUpgradesAllowed(-3, Armor));
        }

        [Test] public void AnEmptyTableAllowsNothingBeyondTheBaseline()
        {
            Assert.AreEqual(0, DominionShopRules.MaxWeaponDepth(2, null));
            Assert.AreEqual(0, DominionShopRules.ArmorUpgradesAllowed(2, new int[0]));
        }

        [Test] public void PickingIsAllowedInTheBreakOnlyPlusALateJoinersFirstPick()
        {
            Assert.IsTrue(DominionShopRules.MayPick(DominionStage.Break, false));
            Assert.IsFalse(DominionShopRules.MayPick(DominionStage.Round, false));
            Assert.IsFalse(DominionShopRules.MayPick(DominionStage.SuddenDeath, false));
            Assert.IsFalse(DominionShopRules.MayPick(DominionStage.Over, false));
            Assert.IsFalse(DominionShopRules.MayPick(DominionStage.None, false));
            Assert.IsTrue(DominionShopRules.MayPick(DominionStage.Round, true));
        }

        [Test] public void TheLockedLabelNamesTheFirstRoundThatOpensTheDepth()
        {
            Assert.AreEqual(1, DominionShopRules.FirstRoundAllowingDepth(0, Depth));
            Assert.AreEqual(2, DominionShopRules.FirstRoundAllowingDepth(1, Depth));
            Assert.AreEqual(3, DominionShopRules.FirstRoundAllowingDepth(2, Depth));
            Assert.AreEqual(-1, DominionShopRules.FirstRoundAllowingDepth(3, Depth));
            Assert.AreEqual("Round 2", DominionShopRules.LockedLabel("Round {0}", 2));
            Assert.AreEqual("Round 3", DominionShopRules.LockedLabel("Round {0}", 3));
            Assert.AreEqual("", DominionShopRules.LockedLabel("Round {0}", -1));
        }

        [Test] public void TheBaselineIsDepthZeroAFamilyOneAndItsUpgradeTwo()
        {
            var parents = new Dictionary<int, int> { { 10, -1 }, { 11, 10 }, { 12, 11 }, { 20, 10 } };
            System.Func<int, int> parentOf = id => parents.TryGetValue(id, out int p) ? p : -2;
            Assert.AreEqual(0, DominionShopRules.WeaponDepth(10, parentOf));
            Assert.AreEqual(1, DominionShopRules.WeaponDepth(11, parentOf));
            Assert.AreEqual(2, DominionShopRules.WeaponDepth(12, parentOf));
            Assert.AreEqual(1, DominionShopRules.WeaponDepth(20, parentOf));
            Assert.AreEqual(-1, DominionShopRules.WeaponDepth(99, parentOf), "an unknown weapon has no depth");
        }

        [Test] public void AParentLoopGivesNoDepthInsteadOfHanging()
        {
            var parents = new Dictionary<int, int> { { 1, 2 }, { 2, 1 } };
            Assert.AreEqual(-1, DominionShopRules.WeaponDepth(1, id => parents[id]));
        }

        [Test] public void DepthAgreesWithTheWeaponUpgradeTree()
        {
            var tree = new WeaponUpgradeTree(new[] { (1, -1), (2, 1), (3, 2) });
            var parents = new Dictionary<int, int> { { 1, -1 }, { 2, 1 }, { 3, 2 } };
            Assert.AreEqual(tree.RootId, 1);
            Assert.AreEqual(2, DominionShopRules.WeaponDepth(3, id => parents[id]));
        }
    }
}
